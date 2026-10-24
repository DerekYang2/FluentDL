param([switch]$Seed)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This fixture only runs in Windows Sandbox.' }
$package = Get-AppxPackage -Name 'DerekYang2.FluentDL.Dev'
if (-not $package -or -not $package.IsDevelopmentMode) { throw 'The isolated development package is missing.' }
if ($Seed) {
    if (Get-Process FluentDL -ErrorAction SilentlyContinue) { throw 'Close the development test app before seeding settings.' }
    $data = [Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily($package.PackageFamilyName)
    $indexer = [Collections.Generic.IDictionary[string,object]].GetProperty('Item')
    foreach ($pair in @{ auto_play='false'; notify_update='false'; preview_player_volume='1.0' }.GetEnumerator()) {
        $indexer.SetValue($data.LocalSettings.Values,[object]$pair.Value,[object[]]@($pair.Key))
    }
}
Start-Process ('shell:AppsFolder\' + $package.PackageFamilyName + '!App')
$deadline = (Get-Date).AddSeconds(30)
do {
    $app = Get-Process FluentDL -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $package.InstallLocation 'FluentDL.exe') } | Select-Object -First 1
    if ($app) { [pscustomobject]@{ ProcessId=$app.Id; ProcessScope='sandbox'; UiTargetArgs="--on sandbox -a $($app.Id)" } | ConvertTo-Json; exit 0 }
    Start-Sleep -Milliseconds 100
} while ((Get-Date) -lt $deadline)
throw 'The development test app did not start.'
