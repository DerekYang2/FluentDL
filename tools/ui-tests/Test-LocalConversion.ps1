<# Tests the explicit LocalConversionPreview.targets package in Windows Sandbox only. #>
param(
    [int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\local-conversion-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'local-conversion-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
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
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); throw }
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a new screenshot path.' }
    $main = @(Ui @('list-windows','--json') | ConvertFrom-Json) | Where-Object { $_.hwnd -and $_.title -eq 'FluentDL' } | Select-Object -First 1
    if (-not $main) { throw 'No Sandbox fixture window.' }
    Checked @('ui','screenshot','--on','sandbox','-w',"$($main.hwnd)",'--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot not delivered.' }
    $screenshots.Add($path)
}
function Nodes($items) { foreach ($item in $items) { if ($null -ne $item) { $item; Nodes $item.children } } }
function Assert-Rows([int]$Count, [string]$Prefix) {
    $tree = Ui @('inspect','--depth','24','--json') | ConvertFrom-Json
    $all = @(Nodes @($tree.windows | ForEach-Object { $_.elements }))
    $list = @($all | Where-Object { $_.automationId -eq 'ConversionListView' })
    if ($list.Count -ne 1) { throw 'Expected one conversion result list.' }
    $rows = @(Nodes $list[0].children | Where-Object { $_.type -eq 'ListItem' })
    if ($rows.Count -ne $Count) { throw "Expected $Count result rows, found $($rows.Count)." }
    foreach ($row in $rows) {
        $text = @(Nodes @($row) | ForEach-Object { $_.name }) -join ' '
        if ($text -notlike "*$Prefix*") { throw "Unexpected result from an old batch: $text" }
    }
}

try {
    if ($AppPid -eq 0) {
        Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Start-PreviewVolumeFixture.ps1'),'FluentDL/Start-PreviewVolumeFixture.ps1') | Out-Null
        $launch = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1','-Seed') | ConvertFrom-Json
        if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox PID.' }
        $script:AppPid = [int]$launch.ProcessId
    }
    Ui @('wait-for','PrepareConversionFixture','-t','60000') | Out-Null
    Test 'Conversion uses its fixed batch and shows an estimate after completed work' {
        Ui @('invoke','PrepareConversionFixture') | Out-Null
        Ui @('wait-for','ConversionFixtureReady','--value','ready','-t','5000') | Out-Null
        Ui @('invoke','ConvertDialogOpenButton') | Out-Null
        Ui @('wait-for','OutputComboBox','-t','5000') | Out-Null
        Ui @('invoke','OutputComboBox','--action','expand') | Out-Null
        Ui @('send-keys','home','--via','send-input') | Out-Null
        Ui @('send-keys','enter','--via','send-input') | Out-Null
        Ui @('invoke','PrimaryButton') | Out-Null
        Ui @('wait-for','ConversionFixtureStatus','--value','Started 1, active 1, install blocked','-t','5000') | Out-Null
        Ui @('invoke','AdvanceConversionClock') | Out-Null
        Ui @('invoke','FinishConversionFile') | Out-Null
        Ui @('wait-for','ConversionInfobarText','--value','Estimated time:','--contains','-t','5000') | Out-Null
        Ui @('wait-for','ConversionFixtureStatus','--value','Started 2, active 1, install blocked','-t','5000') | Out-Null
        Ui @('invoke','ReplaceConversionInputs') | Out-Null
        Ui @('wait-for','ConversionInfobarText','--value','of 20','--contains','-t','3000') | Out-Null
        Capture '01-conversion-eta.png'
    }
    Test 'Cancel keeps Convert disabled and installation blocked while an encoder is still active' {
        Ui @('invoke','SecondaryButton') | Out-Null
        Ui @('wait-for','ConversionInfobarText','--value','Stopping conversion','--contains','-t','3000') | Out-Null
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False','-t','3000') | Out-Null
        Ui @('wait-for','SecondaryButton','-p','IsEnabled','--value','False','-t','3000') | Out-Null
        Ui @('wait-for','ConversionFixtureStatus','--value','install blocked','--contains','-t','3000') | Out-Null
        Capture '02-conversion-stopping.png'
        Ui @('invoke','FinishConversionFile') | Out-Null
        Ui @('wait-for','ConversionInfobarText','--value','Conversion cancelled. Converted 2 of 20 tracks.','-t','5000') | Out-Null
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True','-t','3000') | Out-Null
        Ui @('wait-for','ConversionFixtureStatus','--value','Started 2, active 0, install allowed','-t','3000') | Out-Null
        Assert-Rows 2 'original'
    }
    Test 'Restart runs only the replacement batch and keeps old callbacks out of its results' {
        Ui @('invoke','FinishConversionRest') | Out-Null
        Ui @('invoke','PrimaryButton') | Out-Null
        Ui @('wait-for','ConversionInfobarText','--value','Conversion complete. Converted 3 of 3 tracks.','-t','5000') | Out-Null
        Ui @('wait-for','ConversionFixtureStatus','--value','Started 5, active 0, install allowed','-t','3000') | Out-Null
        Assert-Rows 3 'replacement'
        Capture '03-conversion-restarted.png'
        Ui @('invoke','CloseButton') | Out-Null
    }
    Test 'ReplayGain analysis displays the shared ETA format' {
        Ui @('invoke','ReplayGainAnalysisEtaFixture') | Out-Null
        Ui @('wait-for','InfoText','--value','Estimated time: 1 min 30 sec','--contains','-t','5000') | Out-Null
        Capture '04-replaygain-analysis-eta.png'
        Ui @('invoke','CloseButton') | Out-Null
    }
    Test 'ReplayGain tag writing has its own ETA phase' {
        Ui @('invoke','ReplayGainWritingEtaFixture') | Out-Null
        Ui @('wait-for','InfoText','--value','Writing tags to','--contains','-t','5000') | Out-Null
        Ui @('wait-for','InfoText','--value','Estimated time: 1 min 30 sec','--contains','-t','5000') | Out-Null
        Capture '05-replaygain-writing-eta.png'
        Ui @('invoke','CloseButton') | Out-Null
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    try { Capture 'failure.png' } catch { $results.Add(@{name='Failure capture';status='FAIL';detail="$_"}) }
}
finally {
    try { Ui @('invoke','CloseButton') | Out-Null } catch { }
    try { Ui @('invoke','CloseConversionFixture') | Out-Null } catch { }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } catch { $results.Add(@{name='Release UI workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = @{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray())}
$file = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding utf8
Write-Host "Passed $($report.passed), failed $failed. Results: $file"
if ($failed) { exit 1 }
