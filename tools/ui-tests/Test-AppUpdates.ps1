param([Parameter(Mandatory)][int]$AppPid,
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('opencode\updater-ui-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'fluentdl-updater-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
function Checked([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' '). $($output -join ' ')" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('--on','sandbox','-a',"$AppPid")) }
function Guest([string]$Code) {
    $bytes = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('$ErrorActionPreference="Stop"; $ProgressPreference="SilentlyContinue"; ' + $Code))
    Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-EncodedCommand',$bytes)
}
function Scenario([string]$Name) {
    if ($Name -notmatch '^[a-z-]+$') { throw 'Invalid synthetic scenario.' }
    Guest ('$data=[Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily("DerekYang2.FluentDL_qy10v4cp46cw8"); ' +
        '[Collections.Generic.IDictionary[string,object]].GetProperty("Item").SetValue($data.LocalSettings.Values,[object]"' + $Name + '",[object[]]@("UpdaterPreviewScenario"))') | Out-Null
}
function Test([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{ name=$Name; status='PASS' }) }
    catch { $results.Add(@{ name=$Name; status='FAIL'; detail="$_" }) }
}
function Shot([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot','--output',$path,'--json') | Out-Null
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Check([string]$Name, [string]$Text) {
    Scenario $Name
    Ui @('invoke','CheckAppUpdate') | Out-Null
    Ui @('wait-for','AppUpdateStatus','--value',$Text,'--contains','-t','10000') | Out-Null
}
function Download {
    Ui @('invoke','AppUpdateAction') | Out-Null
    Ui @('wait-for','AppUpdateAction','--value','Install update','-t','10000') | Out-Null
}
function Nodes($items) { foreach ($item in $items) { if ($item) { $item; Nodes $item.children } } }
function ScrollFooter {
    Guest ('Add-Type -AssemblyName UIAutomationClient; $app=Get-Process -Id ' + $AppPid + '; ' + @'
$root=[Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
$elements=$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
$found=$false
foreach ($element in $elements) {
 $pattern=$null
 if ($element.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$pattern) -and $pattern.Current.VerticallyScrollable -and $element.Current.BoundingRectangle.Width -gt ($root.Current.BoundingRectangle.Width/2)) {
  $pattern.SetScrollPercent(-1,100)
  $found=$true
  break
 }
}
if (-not $found) { throw 'No Settings scroll region found.' }
'@) | Out-Null
}
try {
    Ui @('wait-for','SettingsItem','-t','60000') | Out-Null
    Ui @('invoke','SettingsItem') | Out-Null
    ScrollFooter
    Ui @('wait-for','AboutExpander','-t','10000') | Out-Null
    $tree = Ui @('inspect','--depth','25','--json') | ConvertFrom-Json
    $about = Nodes @($tree.windows | ForEach-Object elements) | Where-Object automationId -eq 'AboutExpander' | Select-Object -First 1
    $expander = Nodes @($about.children) | Where-Object className -eq 'Microsoft.UI.Xaml.Controls.Expander' | Select-Object -First 1
    if (-not $expander) { throw 'The About card has no native expander peer.' }
    Ui @('invoke',$expander.selector,'--action','expand') | Out-Null
    Ui @('inspect','--depth','10','--json') | Out-Null
    ScrollFooter
    Ui @('focus','CheckAppUpdate') | Out-Null
    Ui @('wait-for','CheckAppUpdate','-p','IsOffscreen','--value','False','-t','5000') | Out-Null
    $synthetic = Ui @('search','Synthetic update source. No installer will run.','--json') | ConvertFrom-Json
    if (@($synthetic.matches).Count -eq 0) { throw 'Refusing to test install actions without the synthetic backend.' }
    Test 'Up-to-date check does not offer installation' {
        Check 'current' 'up to date'
        Ui @('wait-for','AppUpdateAction','-p','IsEnabled','--value','False')
        Shot '01-current.png'
    }
    Test 'Check errors leave retry available' {
        Check 'check-error' 'Synthetic update-check failure'
        Ui @('wait-for','CheckAppUpdate','-p','IsEnabled','--value','True')
        Shot '02-check-error.png'
    }
    Test 'Download cancellation does not enable installation' {
        Check 'slow' '9.0.0.0'
        Ui @('invoke','AppUpdateAction')
        Ui @('wait-for','CancelAppUpdate','-p','IsOffscreen','--value','False')
        Ui @('invoke','CancelAppUpdate')
        Ui @('wait-for','AppUpdateStatus','--value','canceled','--contains','-t','5000')
        Ui @('wait-for','AppUpdateAction','--value','Download update')
        Shot '03-canceled.png'
    }
    Test 'Failed downloads cannot offer an install action' {
        Check 'download-error' '9.0.0.0'
        Ui @('invoke','AppUpdateAction')
        Ui @('wait-for','AppUpdateStatus','--value','Synthetic download failure','--contains')
        Ui @('wait-for','AppUpdateAction','--value','Download update')
    }
    Test 'Active work blocks installation after confirmation' {
        Check 'busy' '9.0.0.0'
        Download
        Ui @('invoke','AppUpdateAction')
        Ui @('wait-for','PrimaryButton','-t','5000')
        Ui @('invoke','PrimaryButton')
        Ui @('wait-for','AppUpdateStatus','--value','Finish active','--contains','-t','10000')
        Shot '04-busy.png'
    }
    Test 'Explicit install saves a backup and calls only the synthetic backend' {
        Check 'available' '9.0.0.0'
        Download
        Shot '05-downloaded.png'
        Ui @('invoke','AppUpdateAction')
        Ui @('wait-for','PrimaryButton','-t','5000')
        Shot '06-confirmation.png'
        Ui @('invoke','PrimaryButton')
        Ui @('wait-for','AppUpdateStatus','--value','handed to Windows','--contains','-t','15000')
        $record = (Guest 'Get-Content (Join-Path $env:LOCALAPPDATA "Packages\DerekYang2.FluentDL_qy10v4cp46cw8\LocalState\updater-preview.json") -Raw') -join '' | ConvertFrom-Json
        $record | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'installer-record.json')
        if ($record.installs -ne 1 -or $record.nativeInstallerCalled -or -not $record.reserved -or $record.backups -lt 1) { throw 'Invalid synthetic install record.' }
        Shot '07-handoff.png'
    }
    Test 'Installer failure does not retry and re-enables the page' {
        Check 'install-error' '9.0.0.0'
        Download
        Ui @('invoke','AppUpdateAction')
        Ui @('wait-for','PrimaryButton','-t','5000')
        Ui @('invoke','PrimaryButton')
        Ui @('wait-for','AppUpdateStatus','--value','Synthetic installer failure','--contains','-t','15000')
        Ui @('wait-for','CheckAppUpdate','-p','IsEnabled','--value','True')
        Shot '08-install-error.png'
    }
} catch { $results.Add(@{ name='Target setup or test execution'; status='FAIL'; detail="$_" }) }
finally {
    try { Checked @('ui','yield','--on','sandbox') | Out-Null } finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}
$report = @{ target='sandbox'; appPid=$AppPid; results=@($results.ToArray()); screenshots=@($screenshots.ToArray()); nativeInstallerUsed=$false }
$report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'results.json') -Encoding utf8
$report | ConvertTo-Json -Depth 6
if (@($results | Where-Object status -eq 'FAIL').Count -gt 0) { exit 1 }
