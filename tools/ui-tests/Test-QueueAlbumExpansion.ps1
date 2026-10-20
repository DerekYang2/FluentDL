param([Parameter(Mandatory)][int]$AppPid,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\album-expansion-ui-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$previous = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-album-expansion-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' '). $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$AppPid")) }
function Test([string]$Name,[scriptblock]$Body) {
    try { & $Body | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Nodes($items) { foreach ($item in $items) { if ($item) { $item; Nodes $item.children } } }
function Button([string]$Title,[string]$Id) {
    $tree = Ui @('inspect','--depth','30','--json') | ConvertFrom-Json
    foreach ($node in (Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.name -eq $Title -or ($_.type -eq 'ListItem' -and $_.name -like "*$Title*") })) {
        $button = Nodes @($node.children) | Where-Object { $_.automationId -eq $Id -and -not $_.isOffscreen } | Select-Object -First 1
        if ($button) { return $button.selector }
    }
    throw "No visible $Id on '$Title'."
}
function Shot([string]$Name) {
    $file = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot','--output',$file,'--json') | Out-Null
    if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $shots.Add($file)
}
function Expand([string]$Title,[string]$Id) {
    Ui @('invoke',(Button $Title $Id)) | Out-Null
    Ui @('wait-for','PrimaryButton','-t','5000') | Out-Null
    Ui @('invoke','PrimaryButton') | Out-Null
    Ui @('wait-for','PrimaryButton','--gone','-t','5000') | Out-Null
}
try {
    $code = '$ErrorActionPreference="Stop"; $ProgressPreference="SilentlyContinue"; if ($env:USERNAME -ne "WDAGUtilityAccount") { throw "Not Windows Sandbox." }; $p=Get-AppxPackage -Name DerekYang2.FluentDL.AlbumExpansionTest; $a=Get-Process -Id ' + $AppPid + '; if (-not $p -or $a.Path -ne (Join-Path $p.InstallLocation "FluentDL.exe")) { throw "Not the isolated album test app." }; if (-not (Test-Path (Join-Path $env:LOCALAPPDATA ("Packages\"+$p.PackageFamilyName+"\LocalState\album-expansion-fixture.marker")))) { throw "The fixture marker is missing." }'
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-EncodedCommand',([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code)))) | Out-Null
    Ui @('wait-for','SearchBox','-t','60000') | Out-Null
    $queue = (Ui @('search','Queue','--json') | ConvertFrom-Json).matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
    if (-not $queue) { throw 'The Queue navigation item is missing.' }
    Ui @('invoke',$queue.selector) | Out-Null
    Ui @('wait-for','CountText','--value','2 tracks, 3 albums','-t','10000') | Out-Null
    Test 'Standard album has an accessible expand button' {
        [void](Button 'Expand regular album' 'QueueExpandAlbum')
        Shot '01-standard-albums.png'
    }
    Test 'Cancel leaves the album and queue count unchanged' {
        Ui @('invoke',(Button 'Expand regular album' 'QueueExpandAlbum'))
        Ui @('wait-for','PrimaryButton','-t','5000')
        Shot '02-confirmation.png'
        Ui @('invoke','CloseButton')
        Ui @('wait-for','PrimaryButton','--gone','-t','5000')
        Ui @('wait-for','CountText','--value','2 tracks, 3 albums')
    }
    Test 'Standard expansion skips the existing track and replaces the album' {
        Expand 'Expand regular album' 'QueueExpandAlbum'
        Ui @('wait-for','CountText','--value','4 tracks, 2 albums','-t','15000')
        Ui @('wait-for','First regular track','-t','5000')
        Ui @('wait-for','Third regular track','-t','5000')
        Shot '03-standard-expanded.png'
    }
    Test 'Compact album expansion uses the same action' {
        Ui @('invoke','CompactQueueButton')
        [void](Button 'Expand compact album' 'CompactQueueExpandAlbum')
        Shot '04-compact-albums.png'
        Expand 'Expand compact album' 'CompactQueueExpandAlbum'
        Ui @('wait-for','CountText','--value','7 tracks, 1 album','-t','15000')
        Shot '05-compact-expanded.png'
    }
    Test 'Incomplete source results preserve the album and queue count' {
        Expand 'Incomplete album' 'CompactQueueExpandAlbum'
        Ui @('wait-for','CountText','--value','7 tracks, 1 album','-t','5000')
        Ui @('wait-for','The source returned 2 of 3 album tracks. The album was left unchanged.','-t','10000')
        [void](Button 'Incomplete album' 'CompactQueueExpandAlbum')
        Shot '06-incomplete-kept.png'
    }
    Test 'Expansion buttons are absent from single-track rows' {
        $tree = Ui @('inspect','--depth','30','--json') | ConvertFrom-Json
        $track = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.name -eq 'First compact track' -and $_.children } | Select-Object -First 1
        if (-not $track) { throw 'The expanded track row is missing.' }
        if (@(Nodes @($track.children) | Where-Object { $_.automationId -eq 'CompactQueueExpandAlbum' -and -not $_.isOffscreen }).Count -gt 0) { throw 'A track has an album-only action.' }
    }
} catch { $results.Add(@{name='Target setup or test execution';status='FAIL';detail="$_"}) }
finally {
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previous }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
$report = @{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($shots.ToArray())}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'results.json') -Encoding utf8
$report | ConvertTo-Json -Depth 6
if ($failed) { exit 1 }
