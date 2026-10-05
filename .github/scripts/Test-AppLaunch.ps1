# Installs a FluentDL sideload package, starts it, and checks from its log that it started packaged on the expected
# architecture and reached activation. The log, crash events and a screenshot go to the output folder.
#
# GitHub-hosted runners can't open a WinUI window: x64 and ARM64 builds both crash in the Windows App SDK when they
# create it, with exception 0xc000027b, although the same builds run on real PCs. So the window isn't required here,
# and a crash after activation is reported as a warning.
param(
    [Parameter(Mandatory)] [string] $Package,
    [ValidateSet('Arm64', 'X64')] [string] $Architecture = 'Arm64',
    [string] $Output = 'launch-results',
    [int] $Seconds = 30
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Output | Out-Null

Add-AppxPackage -Path $Package -AllowUnsigned
$installed = Get-AppxPackage -Name DerekYang2.FluentDL
Write-Host "Installed $($installed.PackageFullName)"
$started = Get-Date
Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
Start-Sleep -Seconds $Seconds

$process = Get-Process FluentDL -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
[System.Drawing.Graphics]::FromImage($bitmap).CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save((Join-Path $Output 'screenshot.png'))

$logs = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState\Logs"
Copy-Item (Join-Path $logs '*') $Output -ErrorAction SilentlyContinue
$text = (Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Get-Content -Raw) -join "`n"
Write-Host $text

# A native crash doesn't reach FluentDL's log, but Windows records it in the Application event log.
$crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime', 'Windows Error Reporting' -and $_.Message -match 'FluentDL' }
$crashes | Format-List TimeCreated, ProviderName, Id, Message | Out-String -Width 300 | Tee-Object (Join-Path $Output 'events.txt') | Write-Host
$process | Stop-Process -Force -ErrorAction SilentlyContinue

$problems = @()
# Serilog writes the architecture in quotes and the flag in lower case: architecture "Arm64", packaged true.
if ($text -notmatch "architecture `"?$Architecture`"?, packaged true") { $problems += "The log doesn't say FluentDL started packaged on $Architecture." }
if ($text -notmatch 'Application activation started') { $problems += "FluentDL stopped before activation." }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}

if ($process) {
    Write-Host "FluentDL started packaged on $Architecture and was still running after $Seconds seconds."
}
else {
    Write-Host "::warning::FluentDL started packaged on $Architecture and reached activation, then stopped when it created its window. Hosted runners can't open WinUI windows; see events.txt."
}
