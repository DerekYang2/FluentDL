param([string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\v4-hardening-ui-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$previous = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-v4-hardening-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()
$AppPid = 0
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $preference = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $output = @(& winapp @Arguments); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $preference }
    if ($code -ne 0) { throw "winapp failed with exit $code. $($Arguments -join ' '). $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$script:AppPid")) }
function Test([string]$Name,[scriptblock]$Body) {
    try { & $Body | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch {
        $results.Add(@{name=$Name;status='FAIL';detail="$_"})
        try { Shot ('failure-' + $results.Count + '.png') } catch { }
    }
}
function Fixture([string]$Mode) {
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\V4Hardening\Set-HardeningFixture.ps1','-Mode',$Mode)
}
function Launch {
    $launch = Checked @('run',(Join-Path $repo 'FluentDL\FluentDL.csproj'),'--on','sandbox','--arch','x64','--no-build','--detach','--json',
        '-p',"CustomAfterMicrosoftCommonTargets=$repo\tools\ui-tests\HardeningPreview.targets",'-p','IntermediateOutputPath=obj\hardening-native\',
        '-p','OutputPath=bin\hardening-native\','-p','AppxSymbolPackageEnabled=false') | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or -not $launch.ProcessId -or $launch.AUMID -notlike 'DerekYang2.FluentDL.V4HardeningTest_*') { throw 'The launch did not return the isolated Sandbox app.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','HardeningStatus','-t','90000') | Out-Null
    $windows = Ui @('list-windows','--json') | ConvertFrom-Json
    $main = $windows | Where-Object { $_.hwnd -and $_.title -ne 'PopupHost' } | Select-Object -First 1
    if (-not $main) { throw 'No main fixture window was found.' }
    $script:Hwnd = [string]$main.hwnd
}
function Close-App {
    $code = '$ErrorActionPreference="Stop"; $p=Get-AppxPackage -Name DerekYang2.FluentDL.V4HardeningTest; $a=Get-Process -Id ' + $script:AppPid + '; if (-not $p -or $a.Path -ne (Join-Path $p.InstallLocation "FluentDL.exe")) { throw "Not the isolated fixture app." }; [void]$a.CloseMainWindow(); if (-not $a.WaitForExit(20000)) { throw "The fixture app did not close normally." }'
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-EncodedCommand',([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code)))) | Out-Null
}
function Capture-State {
    Ui @('invoke','HardeningSnapshot') | Out-Null
    Ui @('wait-for','HardeningStatus','--value','captured','--contains','-t','10000') | Out-Null
    $state = Fixture 'Read' | ConvertFrom-Json
    $state | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory ('state-' + $results.Count + '.json')) -Encoding utf8
    $state
}
function Assert-Restored($State,[int]$Count) {
    if (@($State.State.Rows).Count -ne $Count) { throw "Expected $Count visible rows." }
    $positions = @($State.State.Rows | ForEach-Object QueueCounter)
    if (($positions -join ',') -ne ((0..($Count - 1)) -join ',')) { throw "Rows do not have consecutive positions: $($positions -join ',')." }
    $missing = @($State.State.Rows | Where-Object Source -eq 'local' | Where-Object Id -like 'Z:*')
    if (($missing.Title -join '|') -ne 'Missing-drive first|Missing-drive second|Missing-drive third') { throw 'Missing-drive rows or saved order changed.' }
    if (($missing.QueueCounter -join ',') -ne '0,1,2') { throw 'The missing-drive rows lost their saved positions.' }
    foreach ($row in $missing) {
        $saved = $State.State.Saved.('local' + $row.Id) | ConvertFrom-Json
        if ($saved.QueueCounter -ne $row.QueueCounter -or $saved.Artists -ne 'Saved fixture metadata') {
            throw 'The saved missing-drive row lost its metadata or position.'
        }
    }
    if (-not $State.State.Saved.'corrupt-fixture') { throw 'The snapshot deleted the unreadable saved row.' }
    if ($State.State.ArtworkLength -le 0) { throw 'The snapshot deleted cached artwork.' }
}
function Shot([string]$Name) {
    $file = Join-Path $ArtifactDirectory $Name
    Checked @('ui','screenshot','--output',$file,'--json','--on','sandbox','-w',$script:Hwnd) | Out-Null
    if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $shots.Add($file)
}
try {
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Set-HardeningFixture.ps1'),'V4Hardening\Set-HardeningFixture.ps1') | Out-Null
    $seed = Fixture 'Seed' | ConvertFrom-Json
    Launch
    Test 'Local persistence initializes and restores before a stalled provider' {
        $state = Capture-State
        if (-not $state.Startup.PersistenceReady -or $state.Startup.Restored -ne 4 -or $state.Startup.Saved -ne 5) { throw 'Provider startup ran before local persistence and restoration.' }
        Assert-Restored $state 4
        if ($state.State.ArtworkLength -ne $seed.ArtworkLength -or $state.State.ArtworkHash -ne $seed.ArtworkHash) { throw 'Cached artwork bytes changed.' }
        Shot '01-status-after-provider-timeout.png'
    }
    Ui @('invoke','HardeningQueue') | Out-Null
    Ui @('wait-for','CountText','--value','3 tracks, 1 album','-t','10000') | Out-Null
    Test 'Missing-drive rows stay visible and public markup stays literal' {
        Ui @('wait-for',"Fixture Loaded x</a><a href='x'>y & z",'-t','5000')
        Ui @('wait-for','Missing-drive second','-t','5000')
        Shot '02-restored-missing-drive.png'
    }
    Test 'Provider preview failure displays an error without escaping the UI handler' {
        Ui @('invoke','HardeningPreview')
        Ui @('wait-for','HardeningStatus','--value','preview handled','-t','10000')
        Ui @('wait-for','Synthetic metadata preview failure','-t','10000')
        Shot '03-preview-error.png'
    }
    Test 'Album outcomes keep unreadable and failed inputs in album order' {
        Ui @('invoke','DownloadAllButton')
        Ui @('wait-for','CountText','--value','6 tracks','-t','30000')
        Ui @('wait-for','DownloadAllButton','-p','IsEnabled','--value','True','-t','10000')
        Ui @('wait-for','CloseButton','-t','10000')
        Ui @('invoke','CloseButton')
        Ui @('wait-for','CloseButton','--gone','-t','10000')
        $state = Capture-State
        Assert-Restored $state 6
        $expanded = @($state.State.Rows | Select-Object -Skip 3 -First 3)
        if (($expanded.QueueCounter -join ',') -ne '3,4,5' -or ($expanded.Source -join ',') -ne 'local,deezer,deezer' -or $expanded[1].Id -ne 'two' -or $expanded[2].Id -ne 'three') { throw 'Album input order or retry rows were lost.' }
        Shot '04-mixed-album-outcomes.png'
    }
    Test 'New queue admissions persist after provider startup times out' {
        Ui @('invoke','HardeningAdd')
        Ui @('wait-for','HardeningStatus','--value','admitted','-t','10000')
        Assert-Restored (Capture-State) 7
    }
    Close-App
    Fixture 'Fail' | Out-Null
    Launch
    Ui @('invoke','HardeningQueue') | Out-Null
    Test 'A failed provider and a second restart preserve rows and album track positions' {
        Ui @('wait-for','CountText','--value','7 tracks','-t','10000')
        $state = Capture-State
        Assert-Restored $state 7
        if (-not $state.Startup.PersistenceReady -or $state.Startup.Restored -ne 7) { throw 'Provider failure skipped the restored queue.' }
        $expanded = @($state.State.Rows | Select-Object -Skip 3 -First 3)
        if (($expanded.QueueCounter -join ',') -ne '3,4,5' -or $expanded[1].Id -ne 'two' -or $expanded[2].Id -ne 'three') { throw 'Restart changed the album track order.' }
        Shot '05-restarted-after-provider-failure.png'
    }
    Test 'An update stays blocked while a cancelled caller leaves a file writer running' {
        Ui @('invoke','HardeningWriter')
        Ui @('wait-for','HardeningStatus','--value','writer paused','-t','10000')
        Ui @('invoke','HardeningCancelWriter')
        Ui @('wait-for','HardeningStatus','--value','caller cancelled; writer still active','-t','10000')
        Ui @('invoke','HardeningInstall')
        Ui @('wait-for','HardeningStatus','--value','blocked:','--contains','-t','10000')
        Shot '06-update-blocked-by-writer.png'
        $state = Capture-State
        if (-not $state.State.WriterPending -or $state.State.FakeInstalls -ne 0 -or $state.State.InstallPrepared) { throw 'The paused file writer did not block update preparation and installation.' }
        Ui @('invoke','HardeningReleaseWriter')
        Ui @('wait-for','HardeningStatus','--value','writer finished','-t','10000')
        Ui @('invoke','HardeningInstall')
        Ui @('wait-for','HardeningStatus','--value','fake installation admitted','-t','10000')
        $state = Capture-State
        if ($state.State.WriterPending -or $state.State.FakeInstalls -ne 1 -or -not $state.State.InstallPrepared) { throw 'The completed writer did not release update admission.' }
    }
    Close-App
} catch { $results.Add(@{name='Target setup or execution';status='FAIL';detail="$_"}) }
finally {
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previous }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
$report = @{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($shots.ToArray())}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'results.json') -Encoding utf8
$report | ConvertTo-Json -Depth 6
if ($failed) { exit 1 }
