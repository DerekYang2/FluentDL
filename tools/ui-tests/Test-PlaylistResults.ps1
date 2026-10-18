param([int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\playlist-results-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$previous = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-playlist-ui'
$results = [Collections.Generic.List[object]]::new()
$shots = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Force $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $output = @(& winapp.exe @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$script:AppPid")) }
function Guest([string]$Code) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('$ErrorActionPreference="Stop"; $ProgressPreference="SilentlyContinue"; ' + $Code + '; exit 0'))
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-EncodedCommand',$encoded)
}
function Test([string]$Name,[scriptblock]$Body) {
    try { & $Body | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Shot([string]$Name) {
    $file = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot','--output',$file,'--json') | Out-Null
    if (-not (Test-Path $file) -or (Get-Item $file).Length -eq 0) { throw 'Screenshot not delivered.' }
    $shots.Add($file)
}
function Choose([string]$Control, [string]$Value) {
    Ui @('invoke',$Control,'--action','expand') | Out-Null
    $items = (Ui @('search',$Value,'--json') | ConvertFrom-Json).matches
    $item = $items | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value } | Select-Object -First 1
    if (-not $item) { throw "Missing option $Value in $Control." }
    Ui @('invoke',$item.selector,'--action','select') | Out-Null
    Ui @('invoke',$Control,'--action','collapse') | Out-Null
    Ui @('wait-for',$Control,'--value',$Value) | Out-Null
}
try {
    if ($script:AppPid -gt 0) {
        Guest "`$p=Get-Process -Id $script:AppPid -ErrorAction SilentlyContinue; if (`$p -and `$p.ProcessName -eq 'FluentDL') { [void]`$p.CloseMainWindow(); if (-not `$p.WaitForExit(15000)) { throw 'App did not close.' } }" | Out-Null
    }
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Seed-PlaylistFixture.ps1'),'PlaylistUi/Seed.ps1') | Out-Null
    $snapshot = Checked @('target','snapshot','sandbox','--json') | ConvertFrom-Json
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',($snapshot.workRoot.TrimEnd('\') + '\PlaylistUi\Seed.ps1'),'-Scenario','MultiSource','-Theme','Dark') | Out-Null
    $launch = Checked @('run',(Join-Path $repo 'FluentDL\FluentDL.csproj'),'--on','sandbox','--arch','x64','--detach','--json',
        '-p',('CustomAfterMicrosoftCommonTargets=' + (Join-Path $PSScriptRoot 'PlaylistDialogPreview.targets')),
        '-p','FluentDLPlaylistUiPreview=true','-p','IntermediateOutputPath=obj\playlist-ui-preview\','-p','OutputPath=bin\playlist-ui-preview\') | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or -not $launch.ProcessId) { throw 'No Sandbox app PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','SearchBox','-t','60000') | Out-Null
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Set-PlaylistTestWindow.ps1'),'PlaylistUi/Window.ps1') | Out-Null
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',($snapshot.workRoot.TrimEnd('\') + '\PlaylistUi\Window.ps1'),'-AppPid',"$script:AppPid",'-Width','1524','-Height','776') | Out-Null
    $found = Ui @('search','Queue','--json') | ConvertFrom-Json
    $queue = $found.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
    Ui @('invoke',$queue.selector) | Out-Null
    Test 'Layout toggle is icon-only and keeps its accessible name' {
        $toggle = (Ui @('search','CompactQueueButton','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        if (-not $toggle -or $toggle.width -gt 40 -or $toggle.width -le 0 -or $toggle.name -ne 'List layout') {
            throw 'Expected an icon-only layout toggle with an accessible name.'
        }
    }
    Test 'Switching sources updates counts and resets visibility to non-public' {
        Ui @('invoke','CreatePlaylistButton')
        Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
        Choose 'PlaylistSource' 'Deezer'
        Ui @('wait-for','PlaylistSummary','--value',"2 Deezer tracks will be saved`n1 album excluded`n0 duplicates excluded")
        Ui @('wait-for','PlaylistVisibility','--value','Private, only you')
        Choose 'PlaylistVisibility' 'Public'
        Choose 'PlaylistSource' 'TIDAL'
        Ui @('wait-for','PlaylistVisibility','--value','Unlisted, anyone with the link')
        Choose 'PlaylistSource' 'Qobuz'
        Ui @('wait-for','PlaylistVisibility','--value','Private, only you')
        Ui @('wait-for','PlaylistSummary','--value',"2 Qobuz tracks will be saved`n1 album excluded`n0 duplicates excluded")
        Shot '01-source-switching.png'
        Ui @('invoke','CloseButton')
    }
    $scenarios = foreach ($source in @('TIDAL','Deezer','Qobuz')) {
        foreach ($partial in @($false,$true)) {
            $prefix = if ($partial) { 'Partial' } else { 'My' }
            @{Source=$source;Name="$prefix $source playlist";Count=$(if ($partial) {1} else {2});Shot="$source-$prefix.png"}
        }
    }
    foreach ($scenario in $scenarios) {
        Test "Result screen: $($scenario.Name)" {
            Ui @('invoke','CreatePlaylistButton')
            Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
            Choose 'PlaylistSource' $scenario.Source
            Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
            Ui @('set-value','PlaylistName',$scenario.Name)
            Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True')
            if ($scenario.Count -eq 2) { Shot "$($scenario.Source)-form.png" }
            Ui @('invoke','PrimaryButton')
            Ui @('wait-for','PlaylistResultName','--value',$scenario.Name,'-t','10000')
            Ui @('wait-for','PlaylistStatusText','--value',"Added $($scenario.Count) of 2 tracks.",'--contains','-t','10000')
            Ui @('wait-for','PrimaryButton','--value','Open playlist')
            Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True')
            Ui @('wait-for','PlaylistCopyLink','--gone')
            Ui @('wait-for','CreatePlaylistDialog','--value','Upload playlist')
            Shot $scenario.Shot
            Guest 'Remove-Item (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -ErrorAction SilentlyContinue' | Out-Null
            Ui @('invoke','PrimaryButton')
            $opened = (Guest 'Get-Content (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -Raw') -join ''
            $expected = switch ($scenario.Source) {
                'Deezer' {'https://www.deezer.com/playlist/123456789'}
                'Qobuz' {'https://play.qobuz.com/playlist/123456789'}
                default {'https://tidal.com/browse/playlist/11111111-2222-3333-4444-555555555555'}
            }
            if ($opened.Trim() -ne $expected) { throw 'Wrong playlist navigation target.' }
            if ($scenario.Source -ne 'TIDAL') {
                $file = 'uploaded-' + $scenario.Source.ToLowerInvariant() + '.json'
                $uploaded = ((Guest "Get-Content (Join-Path `$env:LOCALAPPDATA 'Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\$file') -Raw") -join '') | ConvertFrom-Json
                $ids = if ($scenario.Source -eq 'Deezer') { @('3135556','67238735') } else { @('9140031','8767428') }
                if (($uploaded -join ',') -ne (($ids | Select-Object -First $scenario.Count) -join ',')) { throw 'Wrong source IDs or queue order.' }
            }
            Ui @('wait-for','PlaylistResultName','--value',$scenario.Name)
            Ui @('invoke','CloseButton')
            Ui @('wait-for','PlaylistResultName','--gone')
            Ui @('wait-for','CountText','--value','6 tracks, 3 albums')
        }
    }
    foreach ($source in @('TIDAL','Deezer','Qobuz')) {
        Test "Append to existing $source playlist and skip duplicates" {
            Ui @('invoke','CreatePlaylistButton')
            Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
            Choose 'PlaylistSource' $source
            Ui @('wait-for','PlaylistTarget','-p','IsEnabled','--value','True','-t','10000')
            Ui @('wait-for','PlaylistTarget','--value','Create a new playlist')
            Ui @('wait-for','OpenSelectedPlaylist','--gone')
            Choose 'PlaylistTarget' "Existing $source playlist"
            Ui @('wait-for','PlaylistSummary','--value',"1 $source track will be saved`n1 album excluded`n1 duplicate excluded",'-t','10000')
            Ui @('wait-for','PlaylistName','--gone')
            Ui @('wait-for','PlaylistVisibility','--gone')
            Ui @('wait-for','PrimaryButton','--value','Add to playlist')
            Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True')
            $expected = switch ($source) {
                'Deezer' {'https://www.deezer.com/playlist/987654321'}
                'Qobuz' {'https://play.qobuz.com/playlist/987654321'}
                default {'https://tidal.com/browse/playlist/99999999-2222-3333-4444-555555555555'}
            }
            $open = (Ui @('search','OpenSelectedPlaylist','--json') | ConvertFrom-Json).matches | Select-Object -First 1
            $reload = (Ui @('search','RefreshPlaylists','--json') | ConvertFrom-Json).matches | Select-Object -First 1
            if (-not $open -or -not $reload -or $open.x -ge $reload.x -or [Math]::Abs($open.height-$reload.height) -gt 1 -or [Math]::Abs($open.width-$reload.width) -gt 1) { throw 'Open must sit left of Refresh at the same size.' }
            Guest 'Remove-Item (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -ErrorAction SilentlyContinue' | Out-Null
            Ui @('invoke','OpenSelectedPlaylist')
            $inlineOpened = (Guest 'Get-Content (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -Raw') -join ''
            if ($inlineOpened.Trim() -ne $expected) { throw 'Inline Open used the wrong playlist URI.' }
            Ui @('wait-for','PlaylistResultName','--gone')
            Ui @('wait-for','PrimaryButton','--value','Add to playlist')
            Shot "$source-existing-form.png"
            Ui @('invoke','PrimaryButton')
            Ui @('wait-for','PlaylistResultName','--value',"Existing $source playlist",'-t','10000')
            Ui @('wait-for','PlaylistStatusText','--value','Added 1 of 2 tracks. Skipped 1 duplicate.','--contains','-t','10000')
            Ui @('wait-for','PrimaryButton','--value','Open playlist')
            Ui @('wait-for','CreatePlaylistDialog','--value','Upload playlist')
            Ui @('wait-for','PlaylistResultLabel','--value','Playlist updated:')
            Shot "$source-existing-result.png"
            Guest 'Remove-Item (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -ErrorAction SilentlyContinue' | Out-Null
            Ui @('invoke','PrimaryButton')
            $opened = (Guest 'Get-Content (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\opened-uri.txt") -Raw') -join ''
            if ($opened.Trim() -ne $expected) { throw 'Wrong existing playlist navigation target.' }
            Ui @('invoke','CloseButton')
            Ui @('invoke','CreatePlaylistButton')
            Ui @('wait-for','PlaylistStatusText','--value','Ready.','--contains','-t','10000')
            Choose 'PlaylistSource' $source
            Ui @('wait-for','PlaylistTarget','-p','IsEnabled','--value','True','-t','10000')
            Choose 'PlaylistTarget' "Existing $source playlist"
            Ui @('wait-for','PrimaryButton','-p','IsEnabled','--value','True','-t','10000')
            Ui @('invoke','PrimaryButton')
            Ui @('wait-for','PlaylistStatusText','--value','Added 0 of 2 tracks. Skipped 2 duplicates.','--contains','-t','10000')
            Ui @('invoke','CloseButton')
            Ui @('wait-for','CountText','--value','6 tracks, 3 albums')
        }
    }
    Test 'Toolbar switches every action between labeled and icon-only modes' {
        foreach ($size in @(@{Width=1800;Icon=$false},@{Width=800;Icon=$true})) {
            Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',($snapshot.workRoot.TrimEnd('\') + '\PlaylistUi\Window.ps1'),'-AppPid',"$script:AppPid",'-Width',"$($size.Width)",'-Height','776')
            foreach ($id in @('ConvertDialogOpenButton','DownloadAllButton','CreatePlaylistButton','RestoreQueueButton','ClearButton')) {
                $button = (Ui @('search',$id,'--json') | ConvertFrom-Json).matches | Select-Object -First 1
                if (-not $button -or -not $button.name -or $button.isOffscreen) { throw "Missing accessible action $id." }
                if ($size.Icon -and $button.width -gt 40) { throw "$id still has its label at narrow width." }
                if (-not $size.Icon -and $button.width -lt 60) { throw "$id did not restore its label at wide width." }
            }
            Shot $(if ($size.Icon) {'toolbar-narrow.png'} else {'toolbar-wide.png'})
        }
    }
    Test 'Queue card title is accessible in full and appears in a hover tooltip' {
        $title = (Ui @('search','QueueCardTitle','--json') | ConvertFrom-Json).matches | Where-Object { -not $_.isOffscreen } | Select-Object -First 1
        if (-not $title -or $title.name -ne 'First TIDAL track with a deliberately long title for ellipsis and hover testing') { throw 'Missing full accessible queue title.' }
        Ui @('hover',$title.selector,'--dwell-time','1200')
        Shot 'queue-title-tooltip.png'
        $matches = (Ui @('search',$title.name,'--json') | ConvertFrom-Json).matches
        if (-not ($matches | Where-Object { $_.type -eq 'ToolTip' -or $_.className -eq 'ToolTip' })) { throw 'Full title tooltip did not appear.' }
    }
    Test 'InfoBar stays first, form fields align and refresh matches the picker' {
        Ui @('invoke','CreatePlaylistButton')
        Ui @('wait-for','PlaylistTarget','-p','IsEnabled','--value','True','-t','10000')
        $bar = (Ui @('search','PlaylistStatus','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $source = (Ui @('search','PlaylistSource','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $picker = (Ui @('search','PlaylistTarget','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $refresh = (Ui @('search','RefreshPlaylists','--json') | ConvertFrom-Json).matches | Select-Object -First 1
        if (-not $bar -or -not $source -or $bar.y -ge $source.y) { throw 'The InfoBar is not above the form.' }
        $layout = ((Guest 'Get-Content (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\PlaylistUiPreview\picker-layout.json") -Raw') -join '') | ConvertFrom-Json
        @{xaml=$layout;uiaPickerHeight=$picker.height;uiaRefreshHeight=$refresh.height;infoBarTop=$bar.y;sourceTop=$source.y} |
            ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ArtifactDirectory 'dialog-metrics.json') -Encoding utf8
        if (-not $picker -or -not $refresh -or [Math]::Abs($layout.pickerHeight-$layout.refreshHeight) -gt 1 -or [Math]::Abs($layout.pickerTop-$layout.refreshTop) -gt 1) { throw 'Refresh and playlist input do not share the same XAML height and top edge.' }
        if ($layout.fields.Count -ne 4 -or $layout.contentWidth -lt 180) { throw 'No complete native form measurements.' }
        foreach ($field in $layout.fields) {
            if ([Math]::Abs($field.left-$layout.contentLeft) -gt 0.5 -or [Math]::Abs($field.width-$layout.contentWidth) -gt 0.5) {
                throw "Field $($field.name) does not fill the dialog content width."
            }
        }
        Ui @('wait-for','PlaylistListStatus','--gone')
        Ui @('wait-for','PlaylistPreviewHeader','--value','Preview changes')
        Ui @('wait-for','OpenSelectedPlaylist','--gone')
        Ui @('wait-for','PlaylistTarget','--value','Create a new playlist')
        Shot 'dialog-info-and-stats.png'
        Ui @('invoke','CloseButton')
    }
} catch { $results.Add(@{name='Test setup';status='FAIL';detail="$_"}) }
finally {
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previous }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{target='sandbox';appPid=$script:AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($shots.ToArray());syntheticResponses=$true} |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'test-results.json') -Encoding utf8
"Passed: $($results.Count-$failed), failed: $failed. Results: $ArtifactDirectory"
if ($failed) { exit 1 }
