# Installs a FluentDL sideload package, checks that its bundled ffmpeg encodes audio, starts FluentDL, and checks from
# its log that it started packaged on the expected architecture, ran its ffmpeg and finished activating its main window.
# The log, ffmpeg's output, crash events and screenshots go to the output folder.
param(
    [Parameter(Mandatory)] [string] $Package,
    [ValidateSet('Arm64', 'X64')] [string] $Architecture = 'Arm64',
    [string] $Output = 'launch-results',
    # Startup waits up to 30 seconds for the music services, so the main window can take a while.
    [int] $Seconds = 90
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Output | Out-Null
$problems = @()

Add-AppxPackage -Path $Package -AllowUnsigned
$installed = Get-AppxPackage -Name DerekYang2.FluentDL
Write-Host "Installed $($installed.PackageFullName)"
$logs = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState\Logs"
function Read-Log { (Get-ChildItem $logs -Filter *.log -ErrorAction SilentlyContinue | Get-Content -Raw) -join "`n" }

# At startup, FluentDL runs "ffmpeg -version" and logs "FFmpeg ready", which the log check below requires. That only
# shows ffmpeg starts, so this also encodes a generated one-second tone with the package's ffmpeg. Windows doesn't let
# other processes run files in an app's install folder, so this runs a copy taken from the MSIX.
$ffmpegLog = Join-Path $Output 'ffmpeg.txt'
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $ffmpeg = Join-Path ([IO.Path]::GetTempPath()) 'fluentdl-ffmpeg\ffmpeg.exe'
    New-Item -ItemType Directory -Force (Split-Path $ffmpeg) | Out-Null
    $msix = [IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $entry = $msix.GetEntry('Assets/ffmpeg/bin/ffmpeg.exe')
        if (-not $entry) { throw "The package has no Assets/ffmpeg/bin/ffmpeg.exe." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $ffmpeg, $true)
    }
    finally { $msix.Dispose() }

    $run = Start-Process $ffmpeg -ArgumentList '-f lavfi -i sine=frequency=1000:duration=1 -c:a flac -f null -' `
        -Wait -PassThru -NoNewWindow -RedirectStandardError $ffmpegLog
    Write-Host "ffmpeg: $(Get-Content $ffmpegLog -TotalCount 1)"
    if ($run.ExitCode -ne 0) { $problems += "The package's ffmpeg.exe exited with code $($run.ExitCode). See ffmpeg.txt." }
}
catch {
    $problems += "The package's ffmpeg.exe didn't run: $($_.Exception.Message)"
}

$started = Get-Date
Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
do {
    Start-Sleep -Seconds 2
    $text = Read-Log
    $running = [bool](Get-Process FluentDL -ErrorAction SilentlyContinue)
    $elapsed = ((Get-Date) - $started).TotalSeconds
    # FluentDL checks ffmpeg in the background, so its result can come before or after the main window.
} until (($text -match 'Main window activated' -and $text -match 'FFmpeg (ready|unavailable)') -or ($elapsed -gt 10 -and -not $running) -or $elapsed -gt $Seconds)

# Give a crash right after activation time to happen.
if ($running) { Start-Sleep -Seconds 5 }
$process = Get-Process FluentDL -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
[System.Drawing.Graphics]::FromImage($bitmap).CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
$bitmap.Save((Join-Path $Output 'screenshot.png'))

# The Windows 11 Arm64 runner shows its first sign-in privacy screen over the desktop
# (https://github.com/actions/runner-images/issues/14069), so screenshot.png may not show FluentDL. PrintWindow draws
# FluentDL's own window even when something covers it.
$handle = ($process | Select-Object -First 1).MainWindowHandle
if ($handle -and $handle -ne [IntPtr]::Zero) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);
}
'@
    $rect = New-Object WindowCapture+RECT
    if ([WindowCapture]::GetWindowRect($handle, [ref]$rect) -and $rect.Right -gt $rect.Left -and $rect.Bottom -gt $rect.Top) {
        $window = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
        $graphics = [System.Drawing.Graphics]::FromImage($window)
        $hdc = $graphics.GetHdc()
        # 2 is PW_RENDERFULLCONTENT, which includes content drawn with DirectComposition, as WinUI draws it.
        [WindowCapture]::PrintWindow($handle, $hdc, 2) | Out-Null
        $graphics.ReleaseHdc($hdc)
        $window.Save((Join-Path $Output 'window.png'))
    }
}

Copy-Item (Join-Path $logs '*') $Output -ErrorAction SilentlyContinue
$text = Read-Log
Write-Host $text

# A native crash doesn't reach FluentDL's log, but Windows records it in the Application event log.
$crashes = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $started } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime', 'Windows Error Reporting' -and $_.Message -match 'FluentDL' }
$crashes | Format-List TimeCreated, ProviderName, Id, Message | Out-String -Width 300 | Tee-Object (Join-Path $Output 'events.txt') | Write-Host
$process | Stop-Process -Force -ErrorAction SilentlyContinue

# Serilog writes the architecture in quotes and the flag in lower case: architecture "Arm64", packaged true.
if ($text -notmatch "architecture `"?$Architecture`"?, packaged true") { $problems += "The log doesn't say FluentDL started packaged on $Architecture." }
if ($text -notmatch 'Main window activated') { $problems += "FluentDL didn't finish activating its main window within $Seconds seconds." }
if ($text -notmatch 'FFmpeg ready') { $problems += "FluentDL didn't log 'FFmpeg ready', so it couldn't run its ffmpeg. Look for 'FFmpeg unavailable' in the log." }
if (-not $process) { $problems += "FluentDL wasn't running at the end of the check. See events.txt for a crash." }
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
Write-Host "FluentDL started packaged on $Architecture, ran its ffmpeg, activated its main window and was still running. The package's ffmpeg also encoded a test tone."
