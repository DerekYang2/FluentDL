<#
Runs the UI tests inside the Windows Sandbox that WinApp CLI 0.7+ manages, so they never take over this desktop
or change the dev app installed here. Run-UiTests.ps1 calls this unless -Local is given.

Builds on this machine, then pushes the dev package layout, the test binaries and baselines, the Windows App SDK
runtime and the test secrets into the guest, runs Run-InGuest.ps1 there, and pulls the results back to
%TEMP%\opencode\sandbox-ui-results. Baseline mismatches (*.actual.json) are copied next to the baselines here, as
local runs leave them. The secrets are deleted from the guest afterwards.

The first run in a new Sandbox installs the .NET SDK in the guest (a few minutes). Later runs only push what changed.
#>
param(
    [Parameter(Mandatory)][string]$TestFilter,
    # The checkout whose app is tested; the tests always come from this checkout.
    [string]$AppRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [switch]$Record,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$devName = 'DerekYang2.FluentDL.Dev'
$guestRoot = 'FluentDL' # relative to the guest's work root, C:\WinApp\work
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$hostResults = Join-Path $env:TEMP "opencode\sandbox-ui-results\$runName"
$testProject = Join-Path $repo 'FluentDL.Characterization.Tests\FluentDL.Characterization.Tests.csproj'

function Invoke-WinApp([string[]]$Arguments) {
    & winapp.exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) { throw 'WinApp CLI 0.7+ is required (winget install Microsoft.WinAppCli).' }

if (-not $SkipBuild) {
    Write-Host "Building the dev package from $AppRepoRoot..."
    $targets = Join-Path $repo 'tools\ui-tests\DevPackage.targets'
    dotnet build (Join-Path $AppRepoRoot 'FluentDL\FluentDL.csproj') -c Debug -p:Platform=x64 -v q --nologo `
        --disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false `
        "-p:CustomAfterMicrosoftCommonTargets=$targets" '-p:IntermediateOutputPath=obj\uitest\' '-p:OutputPath=bin\uitest\'
    if ($LASTEXITCODE -ne 0) { throw "App build failed (exit $LASTEXITCODE)." }
    Write-Host 'Building the tests...'
    dotnet build $testProject -p:Platform=x64 -v q --nologo --disable-build-servers -nodeReuse:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Test build failed (exit $LASTEXITCODE)." }
}

$manifest = Get-ChildItem (Join-Path $AppRepoRoot 'FluentDL\bin\uitest') -Recurse -Filter AppxManifest.xml |
    Where-Object { $_.DirectoryName -notmatch '\\AppX$' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $manifest) { throw 'No dev package layout. Run without -SkipBuild.' }
if (([xml](Get-Content $manifest.FullName)).Package.Identity.Name -ne $devName) { throw "The built manifest is not $devName." }
$testBin = (Get-ChildItem (Join-Path $repo 'FluentDL.Characterization.Tests\bin\x64\Debug') -Recurse -Filter 'FluentDL.Characterization.Tests.dll' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1).DirectoryName
$sdkVersion = ([xml](Get-Content (Join-Path $AppRepoRoot 'FluentDL\FluentDL.csproj'))).Project.ItemGroup.PackageReference |
    Where-Object { $_.Include -eq 'Microsoft.WindowsAppSDK' } | Select-Object -First 1 -ExpandProperty Version
$runtime = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windowsappsdk.runtime\$sdkVersion\tools\MSIX\win10-x64"
if (-not (Test-Path $runtime)) { throw "No Windows App SDK runtime MSIX files at $runtime. Build the app once to restore them." }
$secrets = if ($env:FLUENTDL_SECRETS) { $env:FLUENTDL_SECRETS } else { Join-Path $env:USERPROFILE '.fluentdl-secrets\hurl.secrets' }
if (-not (Test-Path $secrets)) { throw "No secrets file at $secrets." }

Write-Host 'Copying into the Sandbox (unchanged files are skipped)...'
Invoke-WinApp @('target', 'push', 'sandbox', $manifest.DirectoryName, "$guestRoot/app")
Invoke-WinApp @('target', 'push', 'sandbox', $testBin, "$guestRoot/tests/bin")
Invoke-WinApp @('target', 'push', 'sandbox', (Join-Path $repo 'FluentDL.Characterization.Tests\Baselines'), "$guestRoot/tests/Baselines")
Invoke-WinApp @('target', 'push', 'sandbox', $testProject, "$guestRoot/tests/FluentDL.Characterization.Tests.csproj")
Invoke-WinApp @('target', 'push', 'sandbox', $runtime, "$guestRoot/runtime")
Invoke-WinApp @('target', 'push', 'sandbox', (Join-Path $PSScriptRoot 'Run-InGuest.ps1'), "$guestRoot/Run-InGuest.ps1")
Invoke-WinApp @('target', 'push', 'sandbox', $secrets, "$guestRoot/secrets/hurl.secrets")

$code = 1
try {
    $guestArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "C:\WinApp\work\$guestRoot\Run-InGuest.ps1", '-Filter', $TestFilter)
    if ($Record) { $guestArgs += '-Record' }
    & winapp.exe target exec sandbox -- powershell.exe @guestArgs
    $code = $LASTEXITCODE
}
finally {
    & winapp.exe target exec sandbox -- powershell.exe -NoProfile -Command "Remove-Item -Recurse -Force C:\WinApp\work\$guestRoot\secrets" | Out-Null
}

& winapp.exe target pull sandbox "$guestRoot/results" $hostResults | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Failed to pull Sandbox test results. Leave the guest open to recover the evidence.' }
$baselines = Join-Path $repo 'FluentDL.Characterization.Tests\Baselines'
Get-ChildItem $baselines -Filter '*.actual.json' | Remove-Item
Get-ChildItem (Join-Path $hostResults 'baselines') -Filter '*.json' -ErrorAction SilentlyContinue | Copy-Item -Destination $baselines
Write-Host "Results, screenshots and failure evidence: $hostResults"
exit $code
