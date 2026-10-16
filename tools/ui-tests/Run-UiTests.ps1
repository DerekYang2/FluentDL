<#
Runs the UI tests (TestCategory=UI) against a freshly installed "FluentDL (Dev)" package.

By default they run in the Windows Sandbox that WinApp CLI 0.7+ manages (see sandbox\Invoke-SandboxUiTests.ps1),
so the dev app never opens on this desktop and nothing here changes. -Local runs them on this desktop as before:
the tests drive the app through UI Automation, but its window opens on top and a few tests read screen pixels, so a
window covering it can fail them.

  .\tools\ui-tests\Run-UiTests.ps1                          # in the Sandbox: build, install the dev app, compare with the baselines
  .\tools\ui-tests\Run-UiTests.ps1 -Filter PreviewDetails   # only the tests whose name contains this
  .\tools\ui-tests\Run-UiTests.ps1 -BaselineFrom 2cb77c1    # record the baselines from an older commit's app
  .\tools\ui-tests\Run-UiTests.ps1 -Local                   # on this desktop

-BaselineFrom checks the commit out into a temporary worktree, installs the dev app from it, records the UI
baselines with this checkout's test code, then removes the worktree. Locally it also reinstalls the dev app from this
checkout afterwards. Record one test at a time with -Filter when only that test is new, so a flaky service in another
test can't overwrite a good baseline.

-SkipInstall skips the build. Locally it also keeps the installed dev app and its settings; in the Sandbox the dev
app is always registered fresh.
#>
param(
    [string]$BaselineFrom,
    [string]$Filter,
    [switch]$IncludeYoutube,
    [switch]$IncludeSpotifyOfficial,
    [switch]$SkipInstall,
    [switch]$Local
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$install = Join-Path $PSScriptRoot 'Install-DevPackage.ps1'
$sandbox = Join-Path $PSScriptRoot 'sandbox\Invoke-SandboxUiTests.ps1'
$tests = Join-Path $repo 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'
$worktree = $null
# YouTube-category tests only run with -IncludeYoutube: a YouTube link crashes the app while YouTube bot-checks (bug 20).
# Deferred workflows stay in source for follow-up, but do not run until their automation is finished.
$testFilter = 'TestCategory=UI&TestCategory!=Deferred'
if ($Filter) { $testFilter += "&FullyQualifiedName~$Filter" }
if (-not $IncludeYoutube) { $testFilter += '&TestCategory!=YouTube' }
if (-not $IncludeSpotifyOfficial) { $testFilter += '&TestCategory!=SpotifyOfficial' }

function Invoke-UiTests([switch]$Record) {
    $env:FLUENTDL_UI_TESTS = '1'
    if ($Record) { $env:FLUENTDL_RECORD = '1' }
    try {
        dotnet test $tests -p:Platform=x64 --nologo -v q --filter $testFilter --logger 'console;verbosity=normal' | Out-Host
        return $LASTEXITCODE
    }
    finally {
        Remove-Item Env:FLUENTDL_UI_TESTS -ErrorAction SilentlyContinue
        Remove-Item Env:FLUENTDL_RECORD -ErrorAction SilentlyContinue
    }
}

try {
    if ($BaselineFrom) {
        $worktree = Join-Path ([IO.Path]::GetTempPath()) "fluentdl-ui-baseline-$BaselineFrom"
        if (Test-Path $worktree) { git -C $repo worktree remove --force $worktree }
        git -C $repo worktree add --detach $worktree $BaselineFrom | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not check out $BaselineFrom." }

        if ($Local) {
            & $install -RepoRoot $worktree | Out-Null
            $code = Invoke-UiTests -Record
        }
        else {
            & $sandbox -TestFilter $testFilter -AppRepoRoot $worktree -Record
            $code = $LASTEXITCODE
        }
        Write-Host "Recorded UI baselines from $BaselineFrom (exit $code)."
    }
    elseif ($Local) {
        if (-not $SkipInstall) { & $install -RepoRoot $repo | Out-Null }
        $code = Invoke-UiTests
    }
    else {
        & $sandbox -TestFilter $testFilter -SkipBuild:$SkipInstall
        $code = $LASTEXITCODE
    }
}
finally {
    if ($worktree) {
        # Locally, the dev app was registered from the worktree; move it back to this checkout before removing it.
        if ($Local) { & $install -RepoRoot $repo | Out-Null }
        git -C $repo worktree remove --force $worktree
    }
}

exit $code
