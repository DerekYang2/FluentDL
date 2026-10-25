<# Synthetic cross-provider queue additions and loopback artwork. Windows Sandbox only. #>
param([string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\queue-load-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'queue-load-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$observations = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $global:LASTEXITCODE = $null; $output = @(& winapp @Arguments); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($null -eq $code -or $code -ne 0) { throw "winapp $($Arguments -join ' ') failed, exit $code. $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) {
    $selector = if ($script:hwnd) { @('-w',"$script:hwnd") } else { @('-a',"$script:AppPid") }
    Checked (@('ui') + $Arguments + @('--on','sandbox') + $selector)
}
function State { (Ui @('get-value','QueueLoadStatus','--json') | ConvertFrom-Json).text }
function Number([string]$State, [string]$Name) {
    if ($State -notmatch "\b$Name=(-?[0-9]+)") { throw "No $Name in $State" }
    [long]$Matches[1]
}
function Wait([scriptblock]$Predicate, [string]$Description, [switch]$Storage) {
    $deadline = (Get-Date).AddSeconds(45)
    do {
        if ($Storage) { Ui @('invoke','QueueLoadInspect') | Out-Null }
        $state = State
        if (& $Predicate $state) { $observations.Add($state); return $state }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Description. $state"
}
function Wait-SearchIdle {
    $deadline = (Get-Date).AddSeconds(20)
    $quietSince = $null
    $lastRequests = -1
    do {
        $state = State
        $requests = Number $state 'requests'
        if ($state -like 'phase=prepared*' -and (Number $state 'active') -eq 0 -and $requests -eq $lastRequests) {
            if ($null -eq $quietSince) { $quietSince = Get-Date }
            if (((Get-Date) - $quietSince).TotalSeconds -ge 1) { return }
        } else { $quietSince = $null }
        $lastRequests = $requests
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Search artwork did not settle before queue measurement. $state"
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); throw }
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a fresh evidence directory.' }
    Ui @('hover','QueueLoadStatus') | Out-Null
    Ui @('screenshot','--capture-screen','--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Launch {
    $script:hwnd = $null
    $launch = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1','-Seed') | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Ui @('wait-for','QueueLoadPrepare','-t','60000') | Out-Null
    $windows = Ui @('list-windows','--json') | ConvertFrom-Json
    $script:hwnd = ($windows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.title -eq 'FluentDL' } | Select-Object -First 1).hwnd
    if (-not $script:hwnd) { throw 'No fixture window.' }
}
function Close {
    $oldPid = $script:AppPid
    Ui @('invoke','QueueLoadClose') | Out-Null
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',"`$p=Get-Process -Id $oldPid -ErrorAction SilentlyContinue; if (`$p -and -not `$p.WaitForExit(20000)) { throw 'Fixture did not close.' }; exit 0") | Out-Null
    $script:AppPid = 0
}
try {
    Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Start-PreviewVolumeFixture.ps1'),'FluentDL/Start-PreviewVolumeFixture.ps1') | Out-Null
    Launch
    Test 'Large additions show progress, keep the add button disabled and save metadata before all covers finish' {
        Ui @('invoke','QueueLoadPrepare') | Out-Null
        Wait { param($s) $s -like 'phase=prepared*' -and (Number $s 'active') -eq 0 } 'the prepared search results' | Out-Null
        Wait-SearchIdle
        Ui @('invoke','QueueLoadSlow') | Out-Null
        Ui @('invoke','QueueLoadArm') | Out-Null
        Ui @('invoke','AddToQueueButton') | Out-Null
        Wait { param($s) $s -like '*bulk=True*' } 'bulk progress' | Out-Null
        Ui @('wait-for','AddToQueueButton','-p','IsEnabled','--value','False','-t','3000') | Out-Null
        Wait { param($s) (Number $s 'count') -eq 1200 -and (Number $s 'saved') -eq 1200 -and (Number $s 'covers') -lt 1140 -and $s -like '*bulk=True*' } 'metadata-first persistence' -Storage | Out-Null
        Capture '01-search-artwork-progress.png'
    }
    Test 'Queue navigation remains available during artwork caching and displays the same progress' {
        Ui @('invoke','QueueLoadNavigate') | Out-Null
        Wait { param($s) $s -like 'phase=queue*' -and $s -like '*bulk=True*' -and (Number $s 'navMs') -ge 0 } 'Queue navigation during caching' | Out-Null
        Ui @('wait-for','ProgressText','--value','Caching artwork','--contains','-t','5000') | Out-Null
        Capture '02-queue-artwork-progress.png'
    }
    Test 'Shared covers use four workers, persist all metadata and report a failed cover without retry loops' {
        $state = Wait { param($s) $s -like '*bulk=False*' -and (Number $s 'saved') -eq 1200 -and (Number $s 'covers') -eq 1140 } 'completed artwork and the failed-cover notice' -Storage
        if ((Number $state 'peak') -gt 4 -or (Number $state 'requests') -ne 20) { throw "Unbounded or repeated artwork requests. $state" }
        if ($state -notlike '*shared=True*' -or $state -notlike '*ordered=True*' -or $state -notlike '*isrc=True*' -or (Number $state 'failed') -ne 1) { throw "Sharing, order or metadata assertion failed. $state" }
        if ((Number $state 'maxGap') -gt 1000) { throw "The UI heartbeat paused for over a second. $state" }
        Capture '03-queue-completed-with-cover-warning.png'
    }
    Test 'Restart reuses cached covers, restores every track and retries only the missing cover after the window opens' {
        Ui @('invoke','QueueLoadAllowCover') | Out-Null
        Close
        Launch
        Ui @('invoke','QueueLoadNavigate') | Out-Null
        $state = Wait { param($s) (Number $s 'count') -eq 1200 -and (Number $s 'saved') -eq 1200 -and (Number $s 'covers') -eq 1200 -and $s -like '*shared=True*' } 'the restored covers and successful retry' -Storage
        if ((Number $state 'requests') -ne 1 -or $state -notlike '*ordered=True*' -or $state -notlike '*isrc=True*') { throw "Restoration refetched cached covers or lost metadata. $state" }
        Capture '04-restored-queue.png'
    }
    Test 'Cached album expansion keeps its artwork without starting work inside the exclusive queue edit' {
        $before = Number (State) 'requests'
        Ui @('invoke','QueueLoadExpand') | Out-Null
        $state = Wait { param($s) $s -like 'phase=expanded*' -and (Number $s 'count') -eq 1202 -and (Number $s 'saved') -eq 1202 -and (Number $s 'covers') -eq 1202 } 'the expanded cached album' -Storage
        if ((Number $state 'requests') -ne $before) { throw "Album expansion refetched artwork. $state" }
        Capture '05-cached-album-expanded.png'
    }
    Test 'Clearing the queue releases artwork sharing and small additions do not show bulk progress' {
        Ui @('invoke','QueueLoadClear') | Out-Null
        Wait { param($s) (Number $s 'count') -eq 0 -and (Number $s 'entries') -eq 0 -and (Number $s 'saved') -eq 0 } 'released queue artwork' -Storage | Out-Null
        Ui @('invoke','QueueLoadSmall') | Out-Null
        Wait { param($s) $s -like 'phase=prepared*' -and (Number $s 'active') -eq 0 } 'small search results' | Out-Null
        Ui @('invoke','QueueLoadArm') | Out-Null
        Ui @('invoke','AddToQueueButton') | Out-Null
        Wait { param($s) (Number $s 'count') -eq 5 -and $s -like '*bulk=False*' } 'the small queue addition' | Out-Null
        Ui @('wait-for','AddToQueueButton','-p','IsEnabled','--value','True','-t','3000') | Out-Null
        Ui @('wait-for','InfobarProgress','--gone','-t','3000') | Out-Null
        Capture '05-small-addition.png'
    }
    Test 'Removing a queue during pending artwork does not resurrect saved rows' {
        Ui @('invoke','QueueLoadPrepare') | Out-Null
        Wait { param($s) $s -like 'phase=prepared*' -and (Number $s 'active') -eq 0 } 'the pending-artwork scenario' | Out-Null
        Ui @('invoke','QueueLoadSlow') | Out-Null
        Ui @('invoke','AddToQueueButton') | Out-Null
        Wait { param($s) $s -like '*bulk=True*' -and (Number $s 'count') -eq 1200 } 'the active addition' | Out-Null
        Ui @('invoke','QueueLoadClear') | Out-Null
        Wait { param($s) $s -like '*bulk=False*' -and (Number $s 'count') -eq 0 -and (Number $s 'active') -eq 0 -and (Number $s 'entries') -eq 0 -and (Number $s 'saved') -eq 0 } 'cancelled artwork and an empty database' -Storage | Out-Null
        Ui @('invoke','QueueLoadNavigate') | Out-Null
        Capture '06-cleared-queue.png'
    }
    Test 'Artwork progress remains determinate and does not move backwards within its phase' {
        Ui @('invoke','QueueLoadPrepare') | Out-Null
        Wait-SearchIdle
        Ui @('invoke','QueueLoadSlow') | Out-Null
        Ui @('invoke','QueueLoadArm') | Out-Null
        Ui @('invoke','AddToQueueButton') | Out-Null
        $state = Wait { param($s) (Number $s 'saved') -eq 1200 -and $s -like '*bulk=True*' -and (Number $s 'barValue') -gt 0 -and $s -like '*progress=Caching artwork*' } 'a visible percentage during caching' -Storage
        if ($state -notlike '*barIndeterminate=False*' -or (Number $state 'backwards') -ne 0) { throw "The artwork progress bar reset. $state" }
        Capture '07-determinate-artwork-progress.png'
    }
    Test 'Closing during artwork caching stops UI callbacks and flushes metadata without corrupting SQLite' {
        Ui @('invoke','QueueLoadAllowCover') | Out-Null
        Close
        $errors = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',
            "`$p=Get-AppxPackage -Name 'DerekYang2.FluentDL.Dev'; `$f=Join-Path `$env:LOCALAPPDATA ('Packages\'+`$p.PackageFamilyName+'\LocalState\queue-load-close-exceptions.log'); if (Test-Path -LiteralPath `$f) { Get-Content -LiteralPath `$f }; exit 0")
        if ($errors.Count -gt 0) { throw "Queue artwork touched WinUI during shutdown. $($errors -join ' ')" }
        Launch
        Ui @('invoke','QueueLoadNavigate') | Out-Null
        $state = Wait { param($s) (Number $s 'count') -eq 1200 -and (Number $s 'saved') -eq 1200 -and (Number $s 'covers') -eq 1200 -and $s -like '*integrity=ok*' } 'all metadata and a healthy queue after an interrupted artwork load' -Storage
        if ($state -notlike '*ordered=True*' -or $state -notlike '*isrc=True*') { throw "Metadata changed during shutdown. $state" }
        Capture '08-restored-after-interrupted-caching.png'
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    if ($script:hwnd) { try { Capture 'failure.png' } catch { } }
}
finally {
    if ($script:AppPid -gt 0) { try { Close } catch { } }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } catch { $results.Add(@{name='Release workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = @{target='sandbox';passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray());observations=@($observations.ToArray())}
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'test-results.json') -Encoding utf8
Write-Host "Passed $($report.passed), failed $failed. Results: $ArtifactDirectory"
if ($failed) { exit 1 }
