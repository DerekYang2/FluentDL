<#
.SYNOPSIS
Runs the live characterization tests in parallel processes and prints one summary.

.DESCRIPTION
The source classes keep their state in static fields, so tests can't run in parallel inside one process. Each group
below runs in its own dotnet test process instead. The YouTube-heavy tests share one group, so YouTube sees one
client at a time and is less likely to bot-check this machine.

-Quick skips everything that calls YouTube (category "YouTube"; YouTube is slow and bot-checks heavy use) and the
Spotify official search (category "Slow", bug 17). It takes about 2 minutes. Use it while working on a phase, and run
without it before each phase commit.

.EXAMPLE
tools\live-tests\Run-LiveTests.ps1 -Quick
#>
param(
    [switch]$Quick,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $repo 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'
$logDir = Join-Path ([IO.Path]::GetTempPath()) 'opencode\fluentdl-live-tests'
New-Item -ItemType Directory -Force $logDir | Out-Null

$groups = [ordered]@{
    'deezer-qobuz'     = 'FullyQualifiedName~.DeezerTests.|FullyQualifiedName~.QobuzTests.|TestCategory!=Live'
    'convert-dz-qb'    = 'FullyQualifiedName~ConvertToDeezer|FullyQualifiedName~ConvertToQobuz'
    'spotify-web'      = 'FullyQualifiedName~SpotifyWebPlayerTests|FullyQualifiedName~ConvertToSpotify'
    'spotify-official' = 'FullyQualifiedName~SpotifyOfficialTests|FullyQualifiedName=FluentDL.Characterization.Tests.ConversionTests.ConvertWithSpotifyOfficial'
    'youtube'          = 'FullyQualifiedName~YoutubeTests|FullyQualifiedName~ConvertFromYoutube|FullyQualifiedName~ConvertToYoutube|FullyQualifiedName~ConvertWithSpotifyOfficialToYoutube'
}
if ($Quick) { $groups.Remove('youtube') }

if (-not $NoBuild) {
    dotnet build $project -p:Platform=x64 --nologo -v q | Select-String -Pattern ' error |Error\(s\)' | ForEach-Object { $_.Line.Trim() }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$started = Get-Date
$runs = foreach ($name in $groups.Keys) {
    # UI tests never run here; they drive the desktop.
    $filter = "($($groups[$name]))&TestCategory!=UI&TestCategory!=UIProbe"
    if ($Quick) { $filter += '&TestCategory!=Slow&TestCategory!=YouTube' }
    $log = Join-Path $logDir "$name.log"
    $arguments = "test `"$project`" -p:Platform=x64 --nologo --no-build --filter `"$filter`" --logger `"console;verbosity=normal`""
    $process = Start-Process dotnet -ArgumentList $arguments -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    $null = $process.Handle # keeps the exit code readable after the process ends
    [pscustomobject]@{ Name = $name; Process = $process; Log = $log }
}

$runs | ForEach-Object { $_.Process.WaitForExit() }

$failed = $false
foreach ($run in $runs) {
    $lines = Get-Content $run.Log -Encoding utf8
    $passed = @($lines | Where-Object { $_ -match '^\s+Passed ' }).Count
    $failures = @($lines | Where-Object { $_ -match '^\s+Failed ' })
    $skips = @($lines | Where-Object { $_ -match '^\s+Skipped ' })
    "{0,-17} passed {1,3}  failed {2,2}  skipped {3,2}" -f $run.Name, $passed, $failures.Count, $skips.Count
    foreach ($line in $failures + $skips) { "    " + $line.Trim() }
    foreach ($line in $lines | Where-Object { $_ -match 'First difference|^\s+baseline:|^\s+actual:|Assert.Inconclusive' }) { "      " + $line.Trim() }
    if ($failures.Count -gt 0 -or ($run.Process.ExitCode -ne 0 -and $passed -eq 0)) { $failed = $true }
}
"took {0:mm\:ss}. Logs: {1}" -f ((Get-Date) - $started), $logDir
if ($failed) { exit 1 }
