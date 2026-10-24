<# Runs only against the explicit PickerCancellationPreview.targets package in Windows Sandbox. #>
param(
    [int]$AppPid = 0,
    [string]$ArtifactDirectory = (Join-Path $env:LOCALAPPDATA ('Temp\opencode\picker-cancel-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = 'picker-cancel-' + [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$dialogOpen = $false
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
function Cancel-Picker([string]$Button) {
    Ui @('invoke',$Button) | Out-Null
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $windows = Checked @('ui','list-windows','--on','sandbox','--json') | ConvertFrom-Json
        $pickers = @($windows | Where-Object { $_.processName -eq 'PickerHost' -and $_.ownerHwnd -eq $script:mainHwnd -and $_.className -eq '#32770' })
        if ($pickers.Count -gt 1) { throw 'More than one picker belongs to the test app.' }
        if ($pickers.Count -eq 1) {
            Checked @('ui','invoke','Cancel','--on','sandbox','-w',"$($pickers[0].hwnd)") | Out-Null
            return
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "The native picker for $Button did not appear."
}
function Assert-Notice([string]$Message) { Ui @('wait-for','InfoBarTextBlock','--value',$Message,'-t','4000') | Out-Null }
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw 'Choose a new screenshot path.' }
    Checked @('ui','screenshot','--on','sandbox','-w',"$script:mainHwnd",'--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot not delivered.' }
    $screenshots.Add($path)
}

try {
    if ($AppPid -eq 0) {
        Checked @('target','push','sandbox',(Join-Path $PSScriptRoot 'sandbox\Start-PreviewVolumeFixture.ps1'),'FluentDL/Start-PreviewVolumeFixture.ps1') | Out-Null
        $launch = Checked @('target','exec','sandbox','--','powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File','C:\WinApp\work\FluentDL\Start-PreviewVolumeFixture.ps1','-Seed') | ConvertFrom-Json
        if ($launch.ProcessScope -ne 'sandbox' -or $launch.ProcessId -le 0) { throw 'No scoped Sandbox PID.' }
        $script:AppPid = [int]$launch.ProcessId
    }
    Ui @('wait-for','PreparePickerFixture','-t','60000') | Out-Null
    $windows = Ui @('list-windows','--json') | ConvertFrom-Json
    $main = $windows | Where-Object { $_.hwnd -and $_.title -eq 'FluentDL' -and $_.className -eq 'WinUIDesktopWin32WindowClass' } | Select-Object -First 1
    if (-not $main) { throw 'No fixture window.' }
    $script:mainHwnd = $main.hwnd
    Ui @('invoke','PreparePickerFixture') | Out-Null
    Ui @('wait-for','PickerFixtureStatus','--value','ready','-t','5000') | Out-Null
    Test 'Canceling Add folder is informational and leaves the file list unchanged' {
        Cancel-Picker 'UploadButton'
        Assert-Notice 'Folder selection cancelled.'
        Ui @('wait-for','ResultsText','--value','No results','-t','3000') | Out-Null
    }
    Test 'Canceling Add files does not start empty-file processing or show an error' {
        Cancel-Picker 'UploadFileButton'
        Assert-Notice 'File selection cancelled.'
        Capture '01-files-cancelled.png'
        Ui @('wait-for','ResultsText','--value','No results','-t','3000') | Out-Null
    }
    Test 'Canceling the conversion output picker preserves the previous directory' {
        Ui @('invoke','ConvertDialogOpenButton') | Out-Null
        $script:dialogOpen = $true
        Ui @('wait-for','OutputTextBox','-t','5000') | Out-Null
        $before = (Ui @('get-value','OutputTextBox','--json') | ConvertFrom-Json).text
        if (-not $before) { throw 'The original output folder is missing.' }
        Cancel-Picker 'SelectOutputButton'
        Ui @('wait-for','OutputTextBox','--value',$before,'-t','3000') | Out-Null
        Capture '02-output-folder-preserved.png'
        Ui @('invoke','CloseButton') | Out-Null
        $script:dialogOpen = $false
    }
    Test 'A worker can show a timed notice without accessing the timer from its thread' {
        Ui @('invoke','WorkerNoticeFixture') | Out-Null
        Ui @('wait-for','PickerFixtureStatus','--value','worker OK','-t','4000') | Out-Null
        Assert-Notice 'Background notice delivered.'
    }
    Test 'The original empty-file worker path safely reports No tracks found' {
        Ui @('invoke','EmptyFilesFixture') | Out-Null
        Ui @('wait-for','PickerFixtureStatus','--value','empty OK','-t','4000') | Out-Null
        Assert-Notice 'No tracks found'
        Capture '03-empty-worker-notice.png'
    }
    Test 'An older timed fade cannot hide a new permanent progress notice' {
        Ui @('invoke','NoticeFadeFixture') | Out-Null
        Ui @('wait-for','PickerFixtureStatus','--value','replacement OK','-t','6000') | Out-Null
        Assert-Notice 'New permanent progress notice.'
        Capture '04-permanent-notice.png'
    }
    Test 'Canceling settings import is informational, not Import Failed' {
        Ui @('invoke','PickerSettingsFixture') | Out-Null
        Ui @('wait-for','UploadImportFile','-t','10000') | Out-Null
        Cancel-Picker 'UploadImportFile'
        Assert-Notice 'Settings import cancelled.'
        Capture '05-settings-import-cancelled.png'
    }
    Test 'Canceling settings export is informational, not Export Failed' {
        Cancel-Picker 'UploadExportFile'
        Assert-Notice 'Settings export cancelled.'
    }
}
catch {
    if ($results.Count -eq 0) { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
    if ($script:mainHwnd) { try { Capture 'failure.png' } catch { $results.Add(@{name='Failure capture';status='FAIL';detail="$_"}) } }
}
finally {
    if ($AppPid -gt 0) {
        if ($script:mainHwnd) {
            try {
                $windows = Checked @('ui','list-windows','--on','sandbox','--json') | ConvertFrom-Json
                $pickers = $windows | Where-Object { $_.processName -eq 'PickerHost' -and $_.ownerHwnd -eq $script:mainHwnd }
                foreach ($picker in $pickers) { Checked @('ui','invoke','Cancel','--on','sandbox','-w',"$($picker.hwnd)") | Out-Null }
            } catch { }
        }
        if ($script:dialogOpen) { try { Ui @('invoke','CloseButton') | Out-Null } catch { } }
        try { Ui @('invoke','ClosePickerFixture') | Out-Null } catch { }
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
