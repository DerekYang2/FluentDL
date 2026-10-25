param([string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\help-navigation-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'help-navigation-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
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
function State { (Ui @('get-value','HelpFixtureStatus','--json') | ConvertFrom-Json).text }
function Wait([string]$Pattern) {
    $deadline = (Get-Date).AddSeconds(10)
    do { $state = State; if ($state -like $Pattern) { return $state }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Pattern. $state"
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); throw }
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a fresh evidence path.' }
    Ui @('screenshot','--capture-screen','--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Contrast([switch]$Restore) {
    $args = @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Set-CompactContrast.ps1')
    if ($Restore) { $args += '-Restore' }
    Checked $args | Out-Null
}
try {
    foreach ($file in @('Start-PreviewVolumeFixture.ps1','Set-CompactContrast.ps1')) {
        Checked @('target','push','sandbox',(Join-Path $PSScriptRoot "sandbox\$file"),"FluentDL/$file") | Out-Null
    }
    $launch = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1','-Seed') | ConvertFrom-Json
    if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped development app PID.' }
    $script:AppPid = [int]$launch.ProcessId
    Checked @('ui','wait-for','HelpFixtureArm','--on','sandbox','-a',"$script:AppPid",'-t','60000') | Out-Null
    $windows = Checked @('ui','list-windows','--on','sandbox','-a',"$script:AppPid",'--json') | ConvertFrom-Json
    $script:hwnd = ($windows | Where-Object { $_.className -eq 'WinUIDesktopWin32WindowClass' -and $_.title -eq 'FluentDL' } | Select-Object -First 1).hwnd
    if (-not $script:hwnd) { throw 'No fixture window.' }
    Ui @('wait-for','SearchBox','-t','5000') | Out-Null
    Test 'Help opens the project wiki without changing the selected app page' {
        Ui @('invoke','HelpFixtureArm') | Out-Null
        Ui @('invoke','HelpFixtureLight') | Out-Null
        Ui @('wait-for','HelpNavigationItem','-p','Name','--value','Help','-t','3000') | Out-Null
        Ui @('wait-for','HelpNavigationItem','-p','HelpText','--value','Open the FluentDL wiki in your browser','-t','3000') | Out-Null
        Ui @('hover','HelpNavigationItem') | Out-Null
        Capture '01-help-light.png'
        # This command item has no InvokePattern. The driver's selection fallback invokes it twice.
        Ui @('click','HelpNavigationItem') | Out-Null
        Wait '*calls=1; launched=True; samePage=True; sameSelection=True; uri=https://github.com/DerekYang2/FluentDL/wiki' | Out-Null
    }
    Test 'The collapsed-pane Help action supports keyboard Enter' {
        Ui @('invoke','HelpFixtureCompact') | Out-Null
        Ui @('focus','HelpNavigationItem') | Out-Null
        Ui @('send-keys','enter','--via','send-input') | Out-Null
        Wait '*calls=2; launched=True; samePage=True; sameSelection=True; uri=https://github.com/DerekYang2/FluentDL/wiki' | Out-Null
        Capture '02-help-collapsed.png'
        Ui @('invoke','HelpFixtureCompact') | Out-Null
    }
    Test 'Help remains readable in Dark and Contrast themes' {
        Ui @('invoke','HelpFixtureDark') | Out-Null
        Capture '03-help-dark.png'
        $script:contrast = $true
        Contrast
        Capture '04-help-contrast.png'
        Contrast -Restore
        $script:contrast = $false
        Ui @('invoke','HelpFixtureLight') | Out-Null
    }
    Test 'Rejected and throwing browser launches show a dismissible localized error' {
        foreach ($mode in @('HelpFixtureFail','HelpFixtureThrow')) {
            Ui @('invoke',$mode) | Out-Null
            Ui @('click','HelpNavigationItem') | Out-Null
            Ui @('wait-for','HelpLaunchError','-t','3000') | Out-Null
            if ((State) -notlike '*samePage=True; sameSelection=True;*') { throw 'Failed Help navigation changed the current app page.' }
            Capture "$mode-error.png"
            Ui @('invoke','CloseButton') | Out-Null
            Ui @('wait-for','HelpLaunchError','--gone','-t','3000') | Out-Null
        }
    }
    Test 'Windows accepts the wiki URL through the actual default-browser launcher' {
        Ui @('invoke','HelpFixtureReal') | Out-Null
        Ui @('click','HelpNavigationItem') | Out-Null
        Wait 'mode=real; calls=5; launched=True; samePage=True; sameSelection=True; uri=https://github.com/DerekYang2/FluentDL/wiki' | Out-Null
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    if ($script:hwnd) { try { Capture 'failure.png' } catch { } }
}
finally {
    if ($script:contrast) { try { Contrast -Restore } catch { $results.Add(@{name='Restore Contrast';status='FAIL';detail="$_"}) } }
    if ($script:hwnd) { try { Ui @('invoke','HelpFixtureClose') | Out-Null } catch { } }
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } catch { $results.Add(@{name='Release workflow';status='FAIL';detail="$_"}) }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$failed = @($results | Where-Object { $_.status -eq 'FAIL' }).Count
@{target='sandbox';passed=$results.Count-$failed;failed=$failed;results=@($results.ToArray());screenshots=@($screenshots.ToArray())} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'test-results.json') -Encoding utf8
Write-Host "Passed $($results.Count-$failed), failed $failed. Results: $ArtifactDirectory"
if ($failed) { exit 1 }
