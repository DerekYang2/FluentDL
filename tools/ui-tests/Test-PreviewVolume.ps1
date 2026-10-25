<#
Run after building PreviewVolumePreview.targets and registering that development package in Sandbox.
Uses the actual cached Search, Queue and Local Explorer pages, native volume sliders and silent WAV fixtures.
No provider requests or host app data are part of this test. The fixture toolbar is absent from normal builds.
#>
param(
    [int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\preview-volume-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)

$ErrorActionPreference = 'Stop'
$workflow = 'preview-volume-' + [guid]::NewGuid().ToString('N')
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = $workflow
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)

function Checked([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $global:LASTEXITCODE = $null
        $output = @(& winapp @Arguments)
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($null -eq $code -or $code -ne 0) { throw "winapp $($Arguments -join ' ') failed, exit $code. $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$script:AppPid")) }
function Nodes($items) { foreach ($item in $items) { if ($null -ne $item) { $item; Nodes $item.children } } }
function Navigate([string]$Name) {
    $tree = Ui @('inspect','--depth','18','--json') | ConvertFrom-Json
    $matches = @(Nodes @($tree.windows | ForEach-Object { $_.elements }) | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Name })
    if ($matches.Count -ne 1) { throw "Expected one navigation item '$Name', found $($matches.Count)." }
    Ui @('invoke',$matches[0].selector,'--action','select') | Out-Null
}
function Load([string]$Kind) {
    Ui @('invoke',"VolumeFixture$Kind") | Out-Null
    Ui @('wait-for','VolumeMuteButton','-t','5000') | Out-Null
}
function Assert-Volume([string]$Expected) {
    Ui @('invoke','VolumeFixtureRead') | Out-Null
    Ui @('wait-for','PreviewVolumeReadback','--value',$Expected,'-t','3000') | Out-Null
    Ui @('wait-for','PreviewVolumeSaved','--value',$Expected,'-t','3000') | Out-Null
}
function Set-Volume([int]$Percent) {
    Ui @('invoke','VolumeMuteButton') | Out-Null
    Ui @('wait-for','VolumeSlider','-t','3000') | Out-Null
    Ui @('set-value','VolumeSlider',"$Percent") | Out-Null
    Ui @('send-keys','escape','--via','send-input') | Out-Null
    Assert-Volume (($Percent / 100.0).ToString('F2',[Globalization.CultureInfo]::InvariantCulture))
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw "Evidence already exists: $path" }
    $windows = @(Ui @('list-windows','--json') | ConvertFrom-Json)
    $main = $windows | Where-Object { $_.hwnd -and $_.title -eq 'FluentDL' } | Select-Object -First 1
    if (-not $main) { throw 'No test app window.' }
    Checked @('ui','screenshot','--on','sandbox','-w',"$($main.hwnd)",'--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{ name=$Name; status='PASS' }) }
    catch { $results.Add(@{ name=$Name; status='FAIL'; detail="$_" }); throw }
}
function Start-Fixture([switch]$Seed) {
    $arguments = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1')
    if ($Seed) { $arguments += '-Seed' }
    $launch = Checked $arguments | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox app PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','VolumeFixtureUri','-t','60000') | Out-Null
}

try {
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Start-PreviewVolumeFixture.ps1'),'FluentDL/Start-PreviewVolumeFixture.ps1') | Out-Null
    if ($AppPid -eq 0) { Start-Fixture -Seed }
    else { Ui @('wait-for','VolumeFixtureUri','-t','60000') | Out-Null }
    # Dismiss a flyout left by an earlier inspection of this same test app.
    Ui @('send-keys','escape','--via','send-input') | Out-Null
    Test 'Prime all cached pages at the saved starting volume' {
        Navigate 'Queue'; Load 'Stream'; Assert-Volume '1.00'
        Navigate 'Local'; Load 'Uri'; Assert-Volume '1.00'
        Navigate 'Search'; Load 'Uri'; Assert-Volume '1.00'
    }
    Test 'Search volume saves through the native slider' {
        Set-Volume 25
        Capture '01-search-25.png'
    }
    Test 'Cached Queue stream preview inherits and updates the shared volume' {
        Navigate 'Queue'; Load 'Stream'; Assert-Volume '0.25'
        Set-Volume 40
        Capture '02-queue-stream-40.png'
    }
    Test 'Local Explorer uses and updates the shared volume' {
        Navigate 'Local'; Load 'Uri'; Assert-Volume '0.40'
        Set-Volume 60
        Capture '03-local-60.png'
    }
    Test 'Returning to cached Search uses the Local Explorer volume' {
        Navigate 'Search'; Load 'Uri'; Assert-Volume '0.60'
    }
    Test 'Queue URI preview also uses the shared volume and saves zero' {
        Navigate 'Queue'; Load 'Uri'; Assert-Volume '0.60'
        Set-Volume 0
    }
    Test 'Restart restores zero on all three pages and both source types' {
        $oldPid = $script:AppPid
        Ui @('invoke','VolumeFixtureClose') | Out-Null
        Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',"`$p = Get-Process -Id $oldPid -ErrorAction SilentlyContinue; if (`$p) { if (-not `$p.WaitForExit(20000)) { throw 'The test app did not close.' } }; exit 0") | Out-Null
        Start-Fixture
        Load 'Uri'; Assert-Volume '0.00'
        Capture '04-restarted-search-zero.png'
        Navigate 'Queue'; Load 'Stream'; Assert-Volume '0.00'
        Navigate 'Local'; Load 'Uri'; Assert-Volume '0.00'
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    try { Capture 'failure.png' } catch { $results.Add(@{name='Failure capture';status='FAIL';detail="$_"}) }
}
finally {
    try { Ui @('invoke','VolumeFixtureClose') | Out-Null } catch { }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null }
    catch { $results.Add(@{name='Release UI workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = @{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray())}
$file = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding utf8
Write-Host "Passed $($report.passed), failed $failed. Results: $file"
if ($failed) { exit 1 }
