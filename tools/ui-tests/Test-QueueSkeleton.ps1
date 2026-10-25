<# Queue artwork skeleton states, lifecycle, themes and reduced motion. Windows Sandbox only. #>
param([string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\queue-skeleton-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'queue-skeleton-' + [guid]::NewGuid().ToString('N')
$owner = $env:WINAPP_UI_WORKFLOW_ID
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$observations = [Collections.Generic.List[string]]::new()
$contrast = $false
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $global:LASTEXITCODE = $null; $output = @(& winapp @Arguments); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($null -eq $code -or $code -ne 0) { throw "winapp $($Arguments -join ' ') failed, exit $code. $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-w',"$script:hwnd")) }
function State { (Ui @('get-value','QueueLoadStatus','--json') | ConvertFrom-Json).text }
function Number([string]$State, [string]$Name) {
    if ($State -notmatch "\b$Name=(-?[0-9]+)") { throw "No $Name in $State" }
    [long]$Matches[1]
}
function Wait([scriptblock]$Predicate, [string]$Description) {
    $deadline = (Get-Date).AddSeconds(15)
    do {
        Ui @('invoke','QueueSkeletonInspect') | Out-Null
        $state = State
        if (& $Predicate $state) { $observations.Add($state); return $state }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Description. $state"
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); throw }
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a fresh screenshot directory.' }
    Ui @('screenshot','--capture-screen','--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Motion([switch]$Enable, [switch]$Restore) {
    $args = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Set-QueueSkeletonMotion.ps1','-Owner',$owner)
    if ($Enable) { $args += '-Enable' }
    if ($Restore) { $args += '-Restore' }
    Checked $args | Out-Null
}
function Contrast([switch]$Restore) {
    $args = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Set-CompactContrast.ps1')
    if ($Restore) { $args += '-Restore' }
    Checked $args | Out-Null
}
function Close {
    Ui @('invoke','QueueLoadClose') | Out-Null
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',"`$p=Get-Process -Id $script:AppPid -ErrorAction SilentlyContinue; if (`$p -and -not `$p.WaitForExit(20000)) { throw 'Fixture did not close.' }; exit 0") | Out-Null
    $script:AppPid = 0
}
try {
    foreach ($file in @('Start-PreviewVolumeFixture.ps1','Set-QueueSkeletonMotion.ps1','Set-CompactContrast.ps1')) {
        Checked @('target','push','sandbox',(Join-Path $PSScriptRoot "sandbox\$file"),"FluentDL/$file") | Out-Null
    }
    Motion -Enable
    $launch = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1','-Seed') | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Checked @('ui','wait-for','QueueSkeletonPreview','--on','sandbox','-a',"$script:AppPid",'-t','60000') | Out-Null
    $windows = Checked @('ui','list-windows','--on','sandbox','-a',"$script:AppPid",'--json') | ConvertFrom-Json
    $script:hwnd = ($windows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.title -eq 'FluentDL' } | Select-Object -First 1).hwnd
    if (-not $script:hwnd) { throw 'No fixture window.' }
    Test 'Compact rows distinguish pending, existing and unavailable artwork without network fallback' {
        Ui @('invoke','QueueSkeletonPreview') | Out-Null
        Ui @('invoke','QueueSkeletonLight') | Out-Null
        Wait { param($s) (Number $s 'tiles') -eq 5 -and (Number $s 'skeletons') -eq 3 -and (Number $s 'pulsing') -eq 3 -and (Number $s 'tileWidth') -eq 44 } 'three pulsing compact artwork slots' | Out-Null
        Ui @('hover','QueueLoadStatus') | Out-Null
        Capture '01-compact-skeleton-pulse-a.png'
        Start-Sleep -Milliseconds 450
        Capture '01-compact-skeleton-pulse-b.png'
    }
    Test 'Card rows reuse the same rounded artwork skeleton at the larger size' {
        Ui @('invoke','CompactQueueButton') | Out-Null
        Wait { param($s) (Number $s 'tileWidth') -eq 76 -and (Number $s 'skeletons') -eq 3 -and (Number $s 'pulsing') -gt 0 } 'the card artwork slots' | Out-Null
        Capture '02-card-skeletons.png'
    }
    Test 'Dark and Contrast themes keep loading, ready and unavailable states readable' {
        Ui @('invoke','QueueSkeletonDark') | Out-Null
        Capture '03-dark-skeletons.png'
        $script:contrast = $true
        Contrast
        Wait { param($s) (Number $s 'pulsing') -eq 0 -and (Number $s 'skeletons') -eq 3 } 'static Contrast placeholders' | Out-Null
        Capture '04-contrast-skeletons.png'
        Contrast -Restore
        $script:contrast = $false
        Ui @('invoke','QueueSkeletonLight') | Out-Null
        Ui @('invoke','CompactQueueButton') | Out-Null
    }
    Test 'Windows reduced motion keeps the skeleton shape but stops the pulse' {
        Motion
        Wait { param($s) (Number $s 'pulsing') -eq 0 -and (Number $s 'skeletons') -eq 3 -and (Number $s 'tileWidth') -eq 44 } 'reduced-motion placeholders' | Out-Null
        Capture '05-reduced-motion-skeletons.png'
        Motion -Enable
        Wait { param($s) (Number $s 'pulsing') -eq 3 } 'resumed pulse animations' | Out-Null
    }
    Test 'URL-only search artwork shows a skeleton without requesting the cached image URL' {
        Ui @('invoke','QueueUrlPreview') | Out-Null
        Wait { param($s) $s -like 'phase=url-preview*' -and (Number $s 'skeletons') -eq 2 -and (Number $s 'forbidden') -eq 0 } 'two pending URL images and an immediate cached image' | Out-Null
        Capture '06-search-url-loading.png'
        $state = Wait { param($s) $s -like 'phase=url-preview*' -and (Number $s 'skeletons') -eq 0 -and (Number $s 'urlReady') -eq 1 -and (Number $s 'urlFailed') -eq 1 } 'distinct URL success and failure'
        if ((Number $state 'urlRequests') -ne 2 -or (Number $state 'forbidden') -ne 0 -or (Number $state 'requests') -ne 0) { throw "A skeleton started an extra image request. $state" }
        Capture '06-search-url-loaded-and-failed.png'
    }
    Test 'Conversion results reuse the actual queued bitmap and never start a second URL image load' {
        Ui @('invoke','QueueResultPreview') | Out-Null
        Ui @('wait-for','ConversionListView','-t','5000') | Out-Null
        Wait { param($s) $s -like 'phase=result-preview*' -and (Number $s 'skeletons') -gt 0 } 'the queued conversion cover to load' | Out-Null
        Capture '07-conversion-result-loading.png'
        $state = Wait { param($s) $s -like '*resultShared=True*' } 'a single shared bitmap in the queue and results dialog'
        if ((Number $state 'requests') -ne 1 -or (Number $state 'urlRequests') -ne 0) { throw "The conversion result fetched artwork again. $state" }
        Capture '07-conversion-result-cached.png'
        Ui @('invoke','CloseButton') | Out-Null
    }
    Test 'Converging provider conversions keep one saved target at the earliest position in either completion order' {
        Ui @('invoke','QueueDuplicatePreview') | Out-Null
        $state = Wait { param($s) $s -like 'phase=duplicates-pass*' -and (Number $s 'count') -eq 2 } 'duplicate conversion, restoration and removal checks'
        if ($state -notlike '*ordered=True*') { throw "Merged queue order changed. $state" }
        Capture '08-conversion-duplicates.png'
    }
    Test 'Stop on a 5000-row conversion cancels only active workers and leaves cancelled rows unmarked' {
        Ui @('invoke','QueueStopPreview') | Out-Null
        Wait { param($s) $s -like 'phase=stop-running*' -and (Number $s 'stopCalls') -eq 7 -and (Number $s 'stopSuccess') -eq 1 -and (Number $s 'stopErrors') -eq 2 -and (Number $s 'stopRunning') -eq 4 } 'four active requests after one success and two genuine failures' | Out-Null
        Capture '09-huge-conversion-running.png'
        Ui @('invoke','ConvertStopButton') | Out-Null
        Ui @('wait-for','ConversionListView','-t','5000') | Out-Null
        $state = Wait { param($s) (Number $s 'stopRunning') -eq 0 -and (Number $s 'stopCancelled') -eq 4 } 'cancelled workers and the partial result dialog'
        if ((Number $state 'stopCalls') -ne 7 -or (Number $state 'stopMarkedCancelled') -ne 0 -or (Number $state 'stopErrors') -ne 2 -or (Number $state 'stopSuccess') -ne 1 -or (Number $state 'stopDarkErrors') -ne 1 -or (Number $state 'stopPinkErrors') -ne 1) { throw "Cancellation changed badges or results. $state" }
        if ((Number $state 'maxGap') -gt 1000) { throw "Conversion Stop blocked the UI heartbeat for over a second. $state" }
        Capture '09-huge-conversion-stopped.png'
        Ui @('invoke','CloseButton') | Out-Null
        Wait { param($s) $s -like 'phase=stop-pass*' } 'saved partial results without a late cancelled replacement' | Out-Null
    }
    Test 'Only visible realized rows animate in a virtualized 1200-item queue and recycled rows update' {
        Ui @('invoke','QueueSkeletonMany') | Out-Null
        Wait { param($s) (Number $s 'count') -eq 1200 -and (Number $s 'tiles') -lt 150 -and (Number $s 'pulsing') -gt 0 -and (Number $s 'pulsing') -eq (Number $s 'visibleSkeletons') } 'visible-only skeleton animations' | Out-Null
        Ui @('invoke','QueueSkeletonScroll') | Out-Null
        Ui @('wait-for','Skeleton track 1199','-t','5000') | Out-Null
        Wait { param($s) (Number $s 'tiles') -lt 150 -and (Number $s 'pulsing') -gt 0 -and (Number $s 'pulsing') -eq (Number $s 'visibleSkeletons') } 'recycled visible artwork states' | Out-Null
        Capture '06-recycled-skeletons.png'
    }
    Test 'Closing while skeletons animate does not access disposed composition objects' {
        Close
        $errors = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-Command',
            "`$p=Get-AppxPackage -Name 'DerekYang2.FluentDL.Dev'; `$f=Join-Path `$env:LOCALAPPDATA ('Packages\'+`$p.PackageFamilyName+'\LocalState\queue-load-close-exceptions.log'); if (Test-Path -LiteralPath `$f) { Get-Content -LiteralPath `$f }; exit 0")
        if ($errors.Count -gt 0) { throw ($errors -join ' ') }
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    if ($script:hwnd -and $script:AppPid -gt 0) { try { Capture 'failure.png' } catch { } }
}
finally {
    if ($script:contrast) { try { Contrast -Restore } catch { $results.Add(@{name='Restore Contrast';status='FAIL';detail="$_"}) } }
    try { Motion -Restore } catch { $results.Add(@{name='Restore motion';status='FAIL';detail="$_"}) }
    if ($script:AppPid -gt 0) { try { Close } catch { } }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } catch { $results.Add(@{name='Release workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
$report = @{target='sandbox';passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray());observations=@($observations.ToArray())}
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'test-results.json') -Encoding utf8
Write-Host "Passed $($report.passed), failed $failed. Results: $ArtifactDirectory"
if ($failed) { exit 1 }
