# Installs a FluentDL sideload package, starts it, and checks from its startup log that it runs packaged on the
# expected architecture. The log and a screenshot go to the output folder either way.
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
    Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime', 'Windows Error Reporting', 'Microsoft-Windows-AppModel-Runtime' -and $_.Message -match 'FluentDL' }
$crashes | Format-List TimeCreated, ProviderName, Id, Message | Out-String -Width 300 | Tee-Object (Join-Path $Output 'events.txt') | Write-Host

$problems = @()
if (-not $process) { $problems += "FluentDL wasn't running $Seconds seconds after it started." }
# Serilog writes the architecture in quotes and the flag in lower case: architecture "Arm64", packaged true.
if ($text -notmatch "architecture `"?$Architecture`"?, packaged true") { $problems += "The log doesn't say FluentDL started packaged on $Architecture." }
$process | Stop-Process -Force -ErrorAction SilentlyContinue

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host "FluentDL started packaged on $Architecture and was still running after $Seconds seconds."
