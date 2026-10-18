param([Parameter(Mandatory)][int]$AppPid,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\playlist-layout-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previous = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-playlist-ui'
$results = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()
$contrastOn = $false
New-Item -ItemType Directory -Force $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $output = @(& winapp.exe @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$AppPid")) }
function Test([string]$Name,[scriptblock]$Body) {
    try { & $Body | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Window([string]$Contrast) {
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',$guestHelper,'-AppPid',"$AppPid",'-Contrast',$Contrast)
}
function Shot([string]$Name) {
    $file = Join-Path $ArtifactDirectory $Name
    if (Test-Path $file) { throw 'Use fresh screenshot paths.' }
    Ui @('screenshot','--output',$file,'--json') | Out-Null
    if (-not (Test-Path $file) -or (Get-Item $file).Length -eq 0) { throw 'Screenshot not delivered.' }
    $shots.Add($file)
}
try {
    $snapshot = Checked @('target','snapshot','sandbox','--json') | ConvertFrom-Json
    if (-not $snapshot.workRoot) { throw 'No running guest.' }
    $guestHelper = $snapshot.workRoot.TrimEnd('\') + '\PlaylistUi\Window.ps1'
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Set-PlaylistTestWindow.ps1'),'PlaylistUi/Window.ps1') | Out-Null
    $tree = Ui @('inspect','--json') | ConvertFrom-Json
    function Has-OpenPlaylistDialog($nodes) {
        foreach ($node in $nodes) {
            if ($null -eq $node) { continue }
            if ($node.automationId -eq 'CreatePlaylistDialog' -and -not $node.isOffscreen) { return $true }
            if (Has-OpenPlaylistDialog $node.children) { return $true }
        }
        return $false
    }
    if (Has-OpenPlaylistDialog @($tree.windows | ForEach-Object { $_.elements })) {
        Ui @('invoke','CloseButton') | Out-Null
        Ui @('wait-for','CreatePlaylistDialog','--gone') | Out-Null
    }
    $found = Ui @('search','Queue','--json') | ConvertFrom-Json
    $queue = $found.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
    Ui @('invoke',$queue.selector) | Out-Null
    Test 'Playlist command remains reachable in a narrow queue' {
        Window 'None'
        Ui @('wait-for','CreatePlaylistButton','-p','IsOffscreen','--value','False')
        Shot '00-narrow-toolbar.png'
    }
    Ui @('invoke','CreatePlaylistButton') | Out-Null
    Test 'Dialog fits an 800x740 window' {
        Window 'None'
        Ui @('wait-for','PlaylistName','-p','IsOffscreen','--value','False')
        Ui @('wait-for','CloseButton','-p','IsOffscreen','--value','False')
        Shot '01-narrow.png'
    }
    Test 'Keyboard Tab reaches the next labeled field' {
        Ui @('focus','PlaylistName')
        Ui @('send-keys','tab','--via','send-input')
        Ui @('wait-for','PlaylistDescription','-p','HasKeyboardFocus','--value','True')
    }
    Test 'Contrast theme renders the dialog' {
        Window 'On'
        $script:contrastOn = $true
        Ui @('wait-for','PlaylistVisibility','-p','IsOffscreen','--value','False')
        Shot '02-contrast.png'
    }
} catch { $results.Add(@{name='Target setup';status='FAIL';detail="$_"}) }
finally {
    try { if ($contrastOn) { Window 'Restore' | Out-Null } }
    catch { $results.Add(@{name='Restore guest contrast setting';status='FAIL';detail="$_"}) }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previous }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($shots.ToArray())} |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'test-results.json') -Encoding utf8
"Passed: $($results.Count-$failed), failed: $failed. Results: $ArtifactDirectory"
if ($failed) { exit 1 }
