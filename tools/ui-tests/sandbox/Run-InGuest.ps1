<#
Runs inside the Windows Sandbox guest as its interactive user. Run-UiTests.ps1 pushes everything first:

  C:\WinApp\work\FluentDL\app       the dev package layout (AppxManifest.xml at its root)
  C:\WinApp\work\FluentDL\tests     the test project file, Baselines and bin output
  C:\WinApp\work\FluentDL\runtime   the Windows App SDK runtime MSIX files the app and test host need
  C:\WinApp\work\FluentDL\secrets   hurl.secrets with the test accounts

The guest keeps what this installs (the .NET SDK, the runtime) until the Sandbox closes, so later runs skip setup.
#>
param(
    [Parameter(Mandatory)][string]$Filter,
    [switch]$Record
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$runStart = Get-Date
$root = 'C:\WinApp\work\FluentDL'
$dotnetRoot = 'C:\WinApp\dotnet'
$results = Join-Path $root 'results'
$devName = 'DerekYang2.FluentDL.Dev'

function Step([string]$message) { Write-Host "[guest] $message" }

# .NET SDK: the test host targets net10.0-windows, and dotnet test needs the SDK's test platform.
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
if (-not (Test-Path $dotnet)) {
    Step 'Installing the .NET 10 SDK (first run in this Sandbox)...'
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & $installer -Channel 10.0 -InstallDir $dotnetRoot -NoPath | Out-Null
    if (-not (Test-Path $dotnet)) { throw '.NET SDK install failed.' }
}
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

# Windows App SDK runtime: the dev package depends on the framework, and the test host bootstraps through Main and
# DDLM. Install framework first; Add-AppxPackage skips a version that is already present.
foreach ($name in 'Microsoft.WindowsAppRuntime.2', 'Microsoft.WindowsAppRuntime.Main.2', 'Microsoft.WindowsAppRuntime.Singleton.2', 'Microsoft.WindowsAppRuntime.DDLM.2') {
    $msix = Join-Path $root "runtime\$name.msix"
    if (Test-Path $msix) {
        try { Add-AppxPackage -Path $msix -ErrorAction Stop }
        catch { if ($_.Exception.HResult -ne -2147009274) { Step "Runtime ${name}: $($_.Exception.Message)" } } # 0x80073D06: a newer version is installed
    }
}

# Fresh dev package each run, like Install-DevPackage.ps1: registration also resets the app's settings and queue.
$manifest = Join-Path $root 'app\AppxManifest.xml'
if (([xml](Get-Content $manifest)).Package.Identity.Name -ne $devName) { throw "Refusing to register a package that is not $devName." }
Get-AppxPackage -Name $devName | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
Step 'Registering the dev package...'
Add-AppxPackage -Register $manifest -ForceApplicationShutdown

$artifacts = Join-Path $env:TEMP 'opencode'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
if (Test-Path $results) { Remove-Item $results -Recurse -Force }
New-Item -ItemType Directory -Force $results | Out-Null
Get-ChildItem (Join-Path $root 'tests\Baselines') -Filter '*.actual.json' -ErrorAction SilentlyContinue | Remove-Item

$env:FLUENTDL_UI_TESTS = '1'
$env:FLUENTDL_SECRETS = Join-Path $root 'secrets\hurl.secrets'
if ($Record) { $env:FLUENTDL_RECORD = '1' }
$dll = Get-ChildItem (Join-Path $root 'tests\bin') -Recurse -Filter 'FluentDL.Characterization.Tests.dll' | Select-Object -First 1
if (-not $dll) { throw 'No test assembly was pushed.' }

Step "Running UI tests ($Filter)..."
& $dotnet test $dll.FullName --filter $Filter --logger 'console;verbosity=normal' --logger "trx;LogFileName=$results\ui-tests.trx" --results-directory $results
$code = $LASTEXITCODE

# Hand back the evidence the tests wrote: screenshots, UI trees and baseline mismatches.
if (Test-Path $artifacts) { Copy-Item $artifacts (Join-Path $results 'artifacts') -Recurse }
$baselines = Join-Path $results 'baselines'
New-Item -ItemType Directory -Force $baselines | Out-Null
Get-ChildItem (Join-Path $root 'tests\Baselines') -Filter '*.json' | Where-Object {
    $_.Name -like '*.actual.json' -or ($Record -and $_.LastWriteTime -gt $runStart)
} | Copy-Item -Destination $baselines
exit $code
