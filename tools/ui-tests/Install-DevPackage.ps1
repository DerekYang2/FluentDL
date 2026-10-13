<#
Builds FluentDL as a separate "FluentDL (Dev)" package (DerekYang2.FluentDL.Dev) and registers it for the current user,
so the UI tests can drive it without touching the installed FluentDL, its settings, queue or backups.

Each run removes the previous dev package first, so the dev app always starts with empty settings.
Requires Developer Mode (Settings > System > For developers), which allows registering an unsigned build folder.

  .\tools\ui-tests\Install-DevPackage.ps1                       # build and install from this checkout
  .\tools\ui-tests\Install-DevPackage.ps1 -RepoRoot D:\other    # from another checkout, such as a master worktree
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    # Native WebView2 startup check without navigating to TIDAL's sign-in page.
    [switch]$BlankTidalLogin
)

$ErrorActionPreference = 'Stop'
$devName = 'DerekYang2.FluentDL.Dev'
$targets = Join-Path $PSScriptRoot 'DevPackage.targets'
$project = Join-Path $RepoRoot 'FluentDL\FluentDL.csproj'
$outDir = Join-Path $RepoRoot 'FluentDL\bin\uitest'

Write-Host "Building $project as $devName..."
$diagnosticArguments = @()
if ($BlankTidalLogin) {
    $source = Get-Content (Join-Path $RepoRoot 'FluentDL\Views\TidalDownloadLoginDialog.xaml.cs') -Raw
    $line = 'LoginWebView.Source = login.Address;'
    if (-not $source.Contains($line)) { throw 'Cannot substitute the diagnostic login address.' }
    $folder = Join-Path $env:TEMP 'opencode\tidal-blank-browser'
    New-Item -ItemType Directory -Force $folder | Out-Null
    $diagnosticSource = Join-Path $folder 'TidalDownloadLoginDialog.xaml.cs'
    [IO.File]::WriteAllText($diagnosticSource, $source.Replace($line, 'LoginWebView.Source = new Uri("about:blank");'))
    $diagnosticArguments = @("-p:FluentDLTidalDiagnosticSource=$diagnosticSource")
}
dotnet build $project -c $Configuration -p:Platform=x64 -v q --nologo `
    "-p:CustomAfterMicrosoftCommonTargets=$targets" `
    '-p:IntermediateOutputPath=obj\uitest\' '-p:OutputPath=bin\uitest\' @diagnosticArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$manifest = Get-ChildItem $outDir -Recurse -Filter AppxManifest.xml |
    Where-Object { $_.DirectoryName -notmatch '\\AppX$' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $manifest) { throw "No AppxManifest.xml under $outDir." }

# Never register anything that could replace the real app.
$identity = ([xml](Get-Content $manifest.FullName)).Package.Identity.Name
if ($identity -ne $devName) { throw "Refusing to register: the built manifest is '$identity', not '$devName'." }

Get-AppxPackage -Name $devName | ForEach-Object {
    Write-Host "Removing the previous dev package $($_.PackageFullName)..."
    Remove-AppxPackage -Package $_.PackageFullName
}

Write-Host "Registering $($manifest.FullName)..."
Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown

$installed = Get-AppxPackage -Name $devName
if (-not $installed) { throw 'The dev package did not register.' }
$aumid = "$($installed.PackageFamilyName)!App"
Write-Host "Installed $($installed.PackageFullName)"
Write-Host "AUMID: $aumid"
$aumid
