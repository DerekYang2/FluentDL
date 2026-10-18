param(
    [int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\playlist-ui-' + [guid]::NewGuid().ToString('N'))),
    [switch]$ProductionSignedOutOnly
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-playlist-ui'
$results = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()
$previewArgs = @(if (-not $ProductionSignedOutOnly) {
    '-p'; ('CustomAfterMicrosoftCommonTargets=' + (Join-Path $PSScriptRoot 'PlaylistDialogPreview.targets'))
    '-p'; 'FluentDLPlaylistUiPreview=true'; '-p'; 'IntermediateOutputPath=obj\playlist-ui-preview\'; '-p'; 'OutputPath=bin\playlist-ui-preview\'
})
New-Item -ItemType Directory -Force $ArtifactDirectory | Out-Null

function Checked([string[]]$Arguments) {
    $output = @(& winapp.exe @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' '). $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on', 'sandbox', '-a', "$script:AppPid")) }
function Guest([string]$Code) {
    $Code = '$ErrorActionPreference="Stop"; $ProgressPreference="SilentlyContinue"; ' + $Code + '; exit 0'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Code))
    Checked @('target', 'exec', 'sandbox', '--', 'powershell.exe', '-NoProfile', '-EncodedCommand', $encoded)
}
function Test([string]$Name, [scriptblock]$Body) {
    try { & $Body | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Screenshot([string]$Name) {
    $file = Join-Path $ArtifactDirectory $Name
    if (Test-Path $file) { throw 'Choose a fresh screenshot path.' }
    Ui @('screenshot', '--output', $file, '--json') | Out-Null
    if (-not (Test-Path $file) -or (Get-Item $file).Length -eq 0) { throw 'Screenshot not delivered to host.' }
    $shots.Add($file)
}
function Scenario([string]$Name, [string]$Theme) {
    if ($script:AppPid -gt 0) {
        Guest "`$p=Get-Process -Id $script:AppPid -ErrorAction SilentlyContinue; if (`$p -and `$p.ProcessName -eq 'FluentDL') { [void]`$p.CloseMainWindow(); if (-not `$p.WaitForExit(15000)) { throw 'App did not close for fixture seeding.' } }" | Out-Null
    }
    $snapshot = Checked @('target','snapshot','sandbox','--json') | ConvertFrom-Json
    if (-not $snapshot.workRoot) { throw 'Missing guest work root.' }
    $fixture = $snapshot.workRoot.TrimEnd('\') + '\PlaylistUi\Seed.ps1'
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',$fixture,'-Scenario',$Name,'-Theme',$Theme) | Out-Null
    $launch = Checked (@('run',(Join-Path $repo 'FluentDL\FluentDL.csproj'),'--on','sandbox','--arch','x64','--no-build','--detach','--json') + $previewArgs) | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or -not $launch.ProcessId -or $launch.UiTargetArgs -ne "--on sandbox -a $($launch.ProcessId)") { throw 'No scoped Sandbox app PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','SearchBox','-t','60000') | Out-Null
    $found = Ui @('search','Queue','--json') | ConvertFrom-Json
    $queue = $found.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
    if (-not $queue) { throw 'Queue navigation item not found.' }
    Ui @('invoke',$queue.selector) | Out-Null
    Ui @('wait-for','CreatePlaylistButton','-t','5000') | Out-Null
    Ui @('invoke','CreatePlaylistButton') | Out-Null
    Ui @('wait-for','PlaylistName','-t','5000') | Out-Null
}
function ChooseSource([string]$Name) {
    Ui @('invoke','PlaylistSource','--action','expand') | Out-Null
    $item = (Ui @('search',$Name,'--json') | ConvertFrom-Json).matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Name } | Select-Object -First 1
    if (-not $item) { throw "Missing source $Name." }
    Ui @('invoke',$item.selector,'--action','select') | Out-Null
    Ui @('invoke','PlaylistSource','--action','collapse') | Out-Null
    Ui @('wait-for','PlaylistSource','--value',$Name) | Out-Null
}

try {
    if (-not $ProductionSignedOutOnly) {
        Checked (@('run',(Join-Path $repo 'FluentDL\FluentDL.csproj'),'--on','sandbox','--arch','x64','--no-launch','--json') + $previewArgs) | Out-Null
    }
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Seed-PlaylistFixture.ps1'),'PlaylistUi/Seed.ps1') | Out-Null
    Test 'Signed-out tracks cannot create a playlist' {
        Scenario 'SignedOut' 'Light'
        Ui @('wait-for','PlaylistSummary','--value',"2 TIDAL tracks will be saved`n1 album excluded`n0 duplicates excluded")
        Ui @('wait-for','PlaylistStatusText','--value','Settings > Authentication > TIDAL','--contains','-t','10000')
        Ui @('set-value','PlaylistName','UI test, not submitted')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False')
        Screenshot '01-signed-out-light.png'
    }
    foreach ($source in @('Deezer','Qobuz')) {
        Test "Signed-out $source cannot upload and defaults to Private" {
            ChooseSource $source
            Ui @('wait-for','PlaylistStatusText','--value',"Settings > Authentication > $source",'--contains','-t','10000')
            Ui @('wait-for','PlaylistVisibility','--value','Private, only you')
            Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False')
            Screenshot "01-signed-out-$source.png"
        }
    }
    if (-not $ProductionSignedOutOnly) { Test 'Signed-in form filters queue and updates validation per keystroke' {
        Scenario 'Ready' 'Dark'
        Ui @('wait-for','PlaylistSummary','--value',"2 TIDAL tracks will be saved`n1 album excluded`n0 duplicates excluded")
        Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
        Ui @('wait-for','PlaylistVisibility','--value','Unlisted, anyone with the link')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False')
        Ui @('set-value','PlaylistName','Queue playlist example')
        Ui @('set-value','PlaylistDescription','Only TIDAL tracks, in queue order.')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True')
        Ui @('set-value','PlaylistName',' ')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False')
        Ui @('set-value','PlaylistName','Queue playlist example')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True')
        Screenshot '02-ready-dark.png'
    } }
    Test 'All custom form controls have automation IDs' {
        foreach ($id in 'PlaylistSource','PlaylistName','PlaylistDescription','PlaylistVisibility','PlaylistSummary','PlaylistStatus') {
            Ui @('wait-for',$id)
        }
    }
    Test 'Compact layout toggle is icon-only and retains its accessible name' {
        $toggle = (Ui @('search','CompactQueueButton','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        if (-not $toggle -or $toggle.width -gt 40 -or $toggle.width -le 0 -or $toggle.name -ne 'List layout') {
            throw 'Expected a compact icon-only toggle with its accessible name.'
        }
    }
    Test 'Cancel leaves the queue unchanged' {
        Ui @('invoke','CloseButton')
        Ui @('wait-for','PlaylistName','--gone')
        Ui @('wait-for','CountText','--value','3 tracks, 1 album')
        Screenshot '03-queue-unchanged.png'
    }
    if (-not $ProductionSignedOutOnly) { Test 'Album-only queue cannot create even while signed in' {
        Scenario 'AlbumsOnly' 'Light'
        Ui @('wait-for','PlaylistSummary','--value',"0 TIDAL tracks will be saved`n1 album excluded`n0 duplicates excluded")
        Ui @('wait-for','PlaylistStatusText','--value','There are no individual TIDAL tracks','--contains','-t','10000')
        Ui @('set-value','PlaylistName','Albums must be excluded')
        Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','False')
        Screenshot '04-albums-only.png'
    } }
} catch { $results.Add(@{name='Test setup';status='FAIL';detail="$_"}) }
finally {
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
$report = @{target='sandbox';appPid=$script:AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($shots.ToArray())}
$file = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 6 | Set-Content $file -Encoding utf8
"Passed: $($report.passed), failed: $failed. Results: $file"
if ($failed) { exit 1 }
