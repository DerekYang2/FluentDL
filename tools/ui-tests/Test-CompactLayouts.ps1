<# Tests CompactLayoutPreview.targets in Windows Sandbox only, with 2,000 generated local tracks. #>
param(
    [int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\compact-layout-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'compact-layout-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$dialogOpen = $false
$contrast = $false
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
function Ui([string[]]$Arguments) {
    $selector = if ($script:mainHwnd) { @('-w',"$script:mainHwnd") } else { @('-a',"$script:AppPid") }
    Checked (@('ui') + $Arguments + @('--on','sandbox') + $selector)
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); throw }
}
function State { (Ui @('get-value','CompactFixtureStatus','--json') | ConvertFrom-Json).text }
function Wait-State([scriptblock]$Predicate, [string]$Description) {
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $value = State
        if (& $Predicate $value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Description. Last state: $value"
}
function Nodes($items) { foreach ($item in $items) { if ($null -ne $item) { $item; Nodes $item.children } } }
function Elements {
    $tree = Ui @('inspect','--depth','24','--json') | ConvertFrom-Json
    @(Nodes @($tree.windows | ForEach-Object { $_.elements }))
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a new screenshot path.' }
    Ui @('hover','CompactFixtureStatus') | Out-Null
    Checked @('ui','screenshot','--on','sandbox','-w',"$script:mainHwnd",'--capture-screen','--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot not delivered.' }
    $screenshots.Add($path)
}
function Start-Fixture([switch]$Seed) {
    $script:mainHwnd = $null
    $arguments = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1')
    if ($Seed) { $arguments += '-Seed' }
    $launch = Checked $arguments | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','PrepareCompactFixture','-t','60000') | Out-Null
    $windows = Ui @('list-windows','--json') | ConvertFrom-Json
    $main = $windows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.title -eq 'FluentDL' } | Select-Object -First 1
    if (-not $main) { throw 'No fixture window.' }
    $script:mainHwnd = $main.hwnd
}
function Contrast([switch]$Restore) {
    $arguments = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Set-CompactContrast.ps1')
    if ($Restore) { $arguments += '-Restore' }
    Checked $arguments | Out-Null
}
function Assert-CompactResults([string[]]$ExpectedTitles = @()) {
    $rows = @(Elements | Where-Object { $_.automationId -eq 'CompactResultRow' -and -not $_.isOffscreen })
    if ($rows.Count -lt 3) { throw 'Too few compact result rows are visible.' }
    if (@($rows | Where-Object { $_.height -gt 64 }).Count -gt 0) { throw 'Result rows grew back to the large card layout.' }
    if ($ExpectedTitles.Count -gt 0) {
        $actual = @($rows | Sort-Object y | ForEach-Object { $_.name })
        if (($actual -join '|') -ne ($ExpectedTitles -join '|')) { throw "Unexpected result order: $($actual -join ', ')" }
    }
}

try {
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Start-PreviewVolumeFixture.ps1'),'FluentDL/Start-PreviewVolumeFixture.ps1') | Out-Null
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Set-CompactContrast.ps1'),'FluentDL/Set-CompactContrast.ps1') | Out-Null
    if ($AppPid -eq 0) { Start-Fixture -Seed }
    else {
        $windows = Ui @('list-windows','--json') | ConvertFrom-Json
        $script:mainHwnd = ($windows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' } | Select-Object -First 1).hwnd
    }
    Test 'Both missing preferences default to compact and Local stays virtualized with 2000 tracks' {
        Ui @('invoke','PrepareCompactFixture') | Out-Null
        $value = Wait-State { param($s) $s -like 'local=list; queue=list; count=2000*' -and $s -match 'realized=([1-9][0-9]?)\b' } 'the compact defaults and virtualized Local list'
        Capture '01-local-default-compact.png'
        Ui @('invoke','CompactFixtureNarrow') | Out-Null
        Wait-State { param($s) $s -like '*preview=hidden*' } 'an empty narrow preview to collapse' | Out-Null
        Ui @('invoke','CompactFixtureWide') | Out-Null
    }
    Test 'Switching Local layouts preserves its source, cached artwork, selection and far scroll anchor' {
        Ui @('invoke','SelectCompactSample') | Out-Null
        Ui @('wait-for','PreviewTitleText','--value','Local track 0010','-t','10000') | Out-Null
        Ui @('invoke','ScrollCompactFar') | Out-Null
        Wait-State { param($s) $s -like '*first=1500*' } 'the far scroll anchor' | Out-Null
        Ui @('invoke','CaptureCompactState') | Out-Null
        Ui @('invoke','CompactLocalButton') | Out-Null
        Wait-State { param($s) $s -like 'local=cards; queue=list*' -and $s -like '*first=1500*' -and $s -like '*selected=10*' -and $s -like '*source=same; selection=same; art=same*' } 'the unchanged Local card-layout state' | Out-Null
        Capture '02-local-standard.png'
        Ui @('invoke','CompactLocalButton') | Out-Null
        Wait-State { param($s) $s -like 'local=list; queue=list*' -and $s -like '*first=1500*' -and $s -like '*source=same; selection=same; art=same*' } 'the restored compact layout' | Out-Null
        Ui @('wait-for','Local track 1500','-t','5000') | Out-Null
    }
    Test 'A selected narrow preview stacks below the readable compact list' {
        Ui @('invoke','CompactFixtureNarrow') | Out-Null
        Wait-State { param($s) $s -like '*preview=visible; previewRow=5*' } 'the stacked Local preview' | Out-Null
        $all = Elements
        $list = $all | Where-Object { $_.automationId -eq 'FileListView' } | Select-Object -First 1
        $preview = $all | Where-Object { $_.automationId -eq 'PreviewContent' -and -not $_.isOffscreen } | Select-Object -First 1
        if (-not $preview -or $preview.y -lt $list.y + $list.height - 1) { throw 'The preview did not stack below the list.' }
        Ui @('wait-for','PreviewTitleText','-p','IsOffscreen','--value','False','-t','3000') | Out-Null
        Ui @('wait-for','PreviewTitleText','--value','Local track 0010','-t','3000') | Out-Null
        $title = $all | Where-Object { $_.automationId -eq 'CompactTrackTitle' -and -not $_.isOffscreen } | Select-Object -First 1
        if (-not $title -or $title.width -lt 120) { throw 'The compact title column collapsed.' }
        Capture '03-local-narrow-selected.png'
        Ui @('invoke','CompactFixtureWide') | Out-Null
    }
    Test 'Compact rows remain readable in Dark and Contrast themes' {
        Ui @('invoke','CompactFixtureDark') | Out-Null
        Capture '04-local-dark.png'
        $script:contrast = $true
        Contrast
        Capture '05-local-contrast.png'
        Contrast -Restore
        $script:contrast = $false
        Ui @('invoke','CompactFixtureLight') | Out-Null
    }
    Test 'The compact context menu acts on the requested row' {
        Ui @('invoke','ScrollCompactFar') | Out-Null
        Ui @('wait-for','Local track 1501','-t','5000') | Out-Null
        Ui @('click','Local track 1501','--right') | Out-Null
        Ui @('wait-for','CompactLocalAdd','-t','3000') | Out-Null
        Wait-State { param($s) $s -like '*context=1501*' } 'the requested context-menu row' | Out-Null
        Ui @('invoke','CompactLocalAdd') | Out-Null
    }
    Test 'Local conversion results are compact and retain Open and Failed indicators' {
        Ui @('invoke','CompactLocalResults') | Out-Null
        $script:dialogOpen = $true
        Ui @('wait-for','ConversionListView','-t','5000') | Out-Null
        Ui @('scroll','ConversionListView','--to','bottom') | Out-Null
        Ui @('wait-for','FailedConversion','-t','3000') | Out-Null
        Ui @('wait-for','OpenConversionButton','-t','3000') | Out-Null
        Assert-CompactResults
        Capture '06-local-conversion-results.png'
        Ui @('invoke','CloseButton') | Out-Null
        $script:dialogOpen = $false
    }
    Test 'All three Queue outcome tabs use the same compact result rows' {
        Ui @('invoke','CompactQueueResults') | Out-Null
        $script:dialogOpen = $true
        Ui @('wait-for','ConversionTabView','-t','5000') | Out-Null
        foreach ($tab in @('Success','Warning','Error')) {
            Ui @('invoke',($tab + 'Tab'),'--action','select') | Out-Null
            Ui @('wait-for','12 tracks','-t','3000') | Out-Null
            $expected = switch ($tab) {
                'Success' { @('Fixture album result 1','Local track 0001','Local track 0002','Local track 0004','Local track 0005') }
                'Warning' { @('Fixture album result 2','Local track 0007','Local track 0008','Local track 0009','Local track 0010') }
                'Error' { @('Fixture album result 3','Local track 0012','Local track 0013','Local track 0014','Local track 0015') }
            }
            Assert-CompactResults $expected
            Capture ("07-queue-$($tab.ToLowerInvariant())-results.png")
        }
        Ui @('invoke','CloseButton') | Out-Null
        $script:dialogOpen = $false
        Ui @('wait-for','ConversionTabView','--gone','-t','3000') | Out-Null
        Ui @('wait-for','CompactQueueButton','--value','On','-t','3000') | Out-Null
    }
    Test 'A saved Local card preference survives restart without changing Queue' {
        $all = Elements
        $nav = $all | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Local' } | Select-Object -First 1
        if (-not $nav) { throw 'The Local navigation item is missing.' }
        Ui @('invoke',$nav.selector,'--action','select') | Out-Null
        Ui @('invoke','CompactLocalButton') | Out-Null
        Wait-State { param($s) $s -like 'local=cards; queue=list*' } 'the independent saved card preference' | Out-Null
        $oldPid = $script:AppPid
        Ui @('invoke','CloseCompactFixture') | Out-Null
        Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',"`$p = Get-Process -Id $oldPid -ErrorAction SilentlyContinue; if (`$p -and -not `$p.WaitForExit(20000)) { throw 'The fixture did not close.' }; exit 0") | Out-Null
        Start-Fixture
        Wait-State { param($s) $s -like 'local=cards; queue=list*' } 'the preferences after restart' | Out-Null
        Ui @('invoke','PrepareCompactFixture') | Out-Null
        Wait-State { param($s) $s -like 'local=cards; queue=list; count=2000*' } 'the restored Local card layout' | Out-Null
        Ui @('wait-for','CompactLocalButton','--value','Off','-t','3000') | Out-Null
        Capture '08-local-cards-after-restart.png'
        $nav = Elements | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
        if (-not $nav) { throw 'The Queue navigation item is missing after restart.' }
        Ui @('invoke',$nav.selector,'--action','select') | Out-Null
        Ui @('wait-for','CompactQueueButton','--value','On','-t','3000') | Out-Null
        Capture '08-queue-compact-after-restart.png'
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    if ($script:mainHwnd) { try { Capture 'failure.png' } catch { $results.Add(@{name='Failure capture';status='FAIL';detail="$_"}) } }
}
finally {
    if ($script:contrast) { try { Contrast -Restore } catch { $results.Add(@{name='Restore Contrast';status='FAIL';detail="$_"}) } }
    if ($AppPid -gt 0) {
        if ($script:dialogOpen) { try { Ui @('invoke','CloseButton') | Out-Null } catch { } }
        try { Ui @('invoke','CloseCompactFixture') | Out-Null } catch { }
    }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } catch { $results.Add(@{name='Release UI workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = @{target='sandbox';appPid=$AppPid;passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray())}
$file = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $file -Encoding utf8
Write-Host "Passed $($report.passed), failed $failed. Results: $file"
if ($failed) { exit 1 }
