# Installs a FluentDL sideload package, starts it, and checks from its log that it started packaged on the expected
# architecture and finished activating its main window. The log, crash events and a screenshot go to the output folder.
param(
    [Parameter(Mandatory)] [string] $Package,
    [ValidateSet('Arm64', 'X64')] [string] $Architecture = 'Arm64',
    [string] $Output = 'launch-results',
    # Startup waits up to 30 seconds for the music services, so the main window can take a while.
    [int] $Seconds = 90
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Output | Out-Null

Add-AppxPackage -Path $Package -AllowUnsigned
$installed = Get-AppxPackage -Name DerekYang2.FluentDL
Write-Host "Installed $($installed.PackageFullName)"
$logs = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState\Logs"
function Read-Log { (Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Get-Content -Raw) -join "`n" }

$started = Get-Date
Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
do {
    Start-Sleep -Seconds 2
    $text = Read-Log
    $running = [bool](Get-Process FluentDL -ErrorAction SilentlyContinue)
    $elapsed = ((Get-Date) - $started).TotalSeconds
} until ($text -match 'Main window activated' -or ($elapsed -gt 10 -and -not $running) -or $elapsed -gt $Seconds)

# Give a crash right after activation time to happen.
if ($running) { Start-Sleep -Seconds 5 }
$process = Get-Process FluentDL -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
[System.Drawing.Graphics]::FromImage($bitmap).CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save((Join-Path $Output 'screenshot.png'))

Copy-Item (Join-Path $logs '*') $Output -ErrorAction SilentlyContinue
$text = Read-Log
Write-Host $text

# A native crash doesn't reach FluentDL's log, but Windows records it in the Application event log.
$crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime', 'Windows Error Reporting' -and $_.Message -match 'FluentDL' }
$crashes | Format-List TimeCreated, ProviderName, Id, Message | Out-String -Width 300 | Tee-Object (Join-Path $Output 'events.txt') | Write-Host
$process | Stop-Process -Force -ErrorAction SilentlyContinue

$problems = @()
# Serilog writes the architecture in quotes and the flag in lower case: architecture "Arm64", packaged true.
if ($text -notmatch "architecture `"?$Architecture`"?, packaged true") { $problems += "The log doesn't say FluentDL started packaged on $Architecture." }
if ($text -notmatch 'Main window activated') { $problems += "FluentDL didn't finish activating its main window within $Seconds seconds." }
if (-not $process) { $problems += "FluentDL wasn't running at the end of the check. See events.txt for a crash." }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host "FluentDL started packaged on $Architecture, activated its main window and was still running."
