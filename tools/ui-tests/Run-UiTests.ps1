<#
Runs the UI tests (TestCategory=UI) against a freshly installed "FluentDL (Dev)" package.

The tests drive the app only through UI Automation, with no real mouse or keyboard input, so the PC stays usable.
The dev app's window does open on top while they run (about 1 minute).

  .\tools\ui-tests\Run-UiTests.ps1                          # install the dev app from this checkout, compare with the baselines
  .\tools\ui-tests\Run-UiTests.ps1 -BaselineFrom 2cb77c1    # record the baselines from an older commit's app
  .\tools\ui-tests\Run-UiTests.ps1 -Filter PreviewDetails   # only the tests whose name contains this

-BaselineFrom checks the commit out into a temporary worktree, installs the dev app from it, records the UI
baselines with this checkout's test code, then reinstalls the dev app from this checkout and removes the worktree.
Record one test at a time with -Filter when only that test is new, so a flaky service in another test can't
overwrite a good baseline.
#>
param(
    [string]$BaselineFrom,
    [string]$Filter,
    [switch]$IncludeYoutube,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$install = Join-Path $PSScriptRoot 'Install-DevPackage.ps1'
$tests = Join-Path $repo 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'
$worktree = $null
# YouTube-category tests only run with -IncludeYoutube: a YouTube link crashes the app while YouTube bot-checks (bug 20).
$testFilter = 'TestCategory=UI'
if ($Filter) { $testFilter += "&FullyQualifiedName~$Filter" }
if (-not $IncludeYoutube) { $testFilter += '&TestCategory!=YouTube' }

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

        & $install -RepoRoot $worktree | Out-Null
        $code = Invoke-UiTests -Record
        Write-Host "Recorded UI baselines from $BaselineFrom (exit $code)."
    }
    else {
        if (-not $SkipInstall) { & $install -RepoRoot $repo | Out-Null }
        $code = Invoke-UiTests
    }
}
finally {
    if ($worktree) {
        # The dev app was registered from the worktree; move it back to this checkout before removing it.
        & $install -RepoRoot $repo | Out-Null
        git -C $repo worktree remove --force $worktree
    }
}

exit $code
