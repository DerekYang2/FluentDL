<#
.SYNOPSIS
Runs the live characterization tests in parallel processes and prints one summary.

.DESCRIPTION
The source classes keep their state in static fields, so tests can't run in parallel inside one process. Each group
below runs in its own dotnet test process instead.

Two ways to compare:

- Default: against the baselines stored in the repo. Fast, but the live services change their data (a release date,
  a ranking), so a stored baseline can go stale and fail without any code change.
- -Against <commit>: parity with another build on today's data. The runner checks the commit out into a temporary
  worktree, records fresh baselines there with that commit's own tests, then runs this checkout's tests against them
  a minute later. A difference then comes from the code, not the data. Use HEAD while working on a step: every commit
  was compared with the one before it, back to the pre-refactor code.

-Quick skips the Spotify official search (category "Slow", about 2 minutes because of bug 17).
YouTube is slow (about a minute per conversion) and bot-checks heavy use, so its group only runs with -IncludeYoutube.

.EXAMPLE
tools\live-tests\Run-LiveTests.ps1 -Quick -Against HEAD
#>
param(
    [switch]$Quick,
    [string]$Against,
    [switch]$IncludeYoutube,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$logDir = Join-Path ([IO.Path]::GetTempPath()) 'opencode\fluentdl-live-tests'
New-Item -ItemType Directory -Force $logDir | Out-Null

$groups = [ordered]@{
    'deezer-qobuz'     = 'FullyQualifiedName~.DeezerTests.|FullyQualifiedName~.QobuzTests.|TestCategory!=Live'
    'convert-dz-qb'    = 'FullyQualifiedName~ConvertToDeezer|FullyQualifiedName~ConvertToQobuz|FullyQualifiedName~DeezerMatchesByMetadata|FullyQualifiedName~NativeNamesConvertThroughSpotify'
    'spotify-web'      = 'FullyQualifiedName~SpotifyWebPlayerTests|FullyQualifiedName~ConvertToSpotify'
    'spotify-official' = 'FullyQualifiedName~SpotifyOfficialTests|FullyQualifiedName=FluentDL.Characterization.Tests.ConversionTests.ConvertWithSpotifyOfficial'
    'downloads'        = 'FullyQualifiedName~.DownloadTests.|FullyQualifiedName~.DownloadSettingsTests.'
    'youtube'          = 'FullyQualifiedName~YoutubeTests|FullyQualifiedName~YoutubeWithoutMusicTests|FullyQualifiedName~ConvertFromYoutube|FullyQualifiedName~ConvertToYoutube|FullyQualifiedName~ConvertWithSpotifyOfficialToYoutube'
}
if (-not $IncludeYoutube) { $groups.Remove('youtube') }

function Build-Tests([string]$project) {
    dotnet build $project -p:Platform=x64 --nologo -v q | Select-String -Pattern ' error |Error\(s\)' | ForEach-Object { $_.Line.Trim() }
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}

# Starts one dotnet test process per group and waits for all of them. Returns the runs with their log paths.
function Invoke-Groups([string]$project, [string]$label, [hashtable]$environment) {
    $saved = @{}
    foreach ($key in $environment.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, $environment[$key]) }
    try {
        $runs = foreach ($name in $groups.Keys) {
            # UI tests never run here; they drive the desktop. YouTube-category tests outside the youtube group are skipped with it.
            $filter = "($($groups[$name]))&TestCategory!=UI&TestCategory!=UIProbe"
            if ($Quick) { $filter += '&TestCategory!=Slow' }
            if (-not $IncludeYoutube) { $filter += '&TestCategory!=YouTube' }
            $log = Join-Path $logDir "$label-$name.log"
            $arguments = "test `"$project`" -p:Platform=x64 --nologo --no-build --filter `"$filter`" --logger `"console;verbosity=normal`""
            $process = Start-Process dotnet -ArgumentList $arguments -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
            $null = $process.Handle # keeps the exit code readable after the process ends
            [pscustomobject]@{ Name = $name; Process = $process; Log = $log }
        }
        $runs | ForEach-Object { $_.Process.WaitForExit() }
        return $runs
    }
    finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
}

$started = Get-Date
$project = Join-Path $repo 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'
$worktree = $null
$environment = @{}

try {
    if ($Against) {
        $sha = (git -C $repo rev-parse --short $Against).Trim()
        $worktree = Join-Path ([IO.Path]::GetTempPath()) "fluentdl-parity-$sha"
        if (Test-Path $worktree) { git -C $repo worktree remove --force $worktree }
        git -C $repo worktree add --quiet --detach $worktree $sha | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not check out $Against." }

        $referenceProject = Join-Path $worktree 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'
        "Recording reference baselines from $sha ..."
        Build-Tests $referenceProject
        if (-not $NoBuild) { Build-Tests $project }
        $reference = Invoke-Groups $referenceProject "ref-$sha" @{ FLUENTDL_RECORD = '1' }
        foreach ($run in $reference) {
            $errors = @(Get-Content $run.Log -Encoding utf8 | Where-Object { $_ -match '^\s+Failed ' })
            foreach ($line in $errors) { "  reference $($run.Name): " + $line.Trim() }
        }
        $environment = @{ FLUENTDL_BASELINES = (Join-Path $worktree 'FluentDL.Characterization.Tests\Baselines') }
        "Comparing this checkout with $sha ..."
    }
    elseif (-not $NoBuild) {
        Build-Tests $project
    }

    $runs = Invoke-Groups $project 'current' $environment

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
}
finally {
    if ($worktree) { git -C $repo worktree remove --force $worktree }
}

"took {0:mm\:ss}. Logs: {1}" -f ((Get-Date) - $started), $logDir
if ($failed) { exit 1 }
