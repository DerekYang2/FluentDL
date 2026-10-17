#Requires -Version 7
# Tests a running dev app with local generated audio. FixturePath and ExportPath belong to the selected target.
# No source-service credentials are needed. Start the dev app in Sandbox before passing its scoped PID here.
param(
    [Parameter(Mandatory)] [ValidateSet('sandbox', 'local')] [string] $Target,
    [Parameter(Mandatory)] [int] $AppPid,
    [Parameter(Mandatory)] [string] $FixturePath,
    [string] $ExportPath = 'C:/WinApp/work/SpekFixtures/export.png',
    [Parameter(Mandatory)] [string] $ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$scope = if ($Target -eq 'sandbox') { @('--on', 'sandbox') } else { @() }
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = "fluentdl-spek-$([guid]::NewGuid().ToString('N'))"
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$mainWindow = $null

function Checked([string[]] $Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $($output -join ' ')" }
    $output
}
function Ui([string[]] $Arguments, [string] $Window) {
    $selector = if ($Window) { @('-w', $Window) } elseif ($script:mainWindow) { @('-w', "$script:mainWindow") } else { @('-a', "$AppPid") }
    Checked (@('ui') + $Arguments + $scope + $selector)
}
function Nodes($items) { foreach ($item in $items) { if ($item) { $item; Nodes $item.children } } }
function Element([string] $Id) {
    $tree = Ui @('inspect', '--json', '--depth', '10') | ConvertFrom-Json
    $match = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.automationId -eq $Id -and -not $_.isOffscreen } | Sort-Object width -Descending | Select-Object -First 1
    if (-not $match) { throw "No visible $Id element." }
    $match
}
function Test-Ui([string] $Name, [scriptblock] $Test) {
    try { & $Test | Out-Null; $results.Add(@{ name = $Name; status = 'PASS' }) }
    catch { $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" }) }
}
function Screenshot([string] $Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}
function Picker([string] $Action, [string] $Path) {
    $windows = Checked (@('ui', 'list-windows', '--json') + $scope) | ConvertFrom-Json
    $picker = @($windows | Where-Object { "$($_.ownerHwnd)" -eq "$script:mainWindow" -and $_.className -eq '#32770' })
    if (-not $picker.Count) {
        Ui @('invoke', $Action) | Out-Null
        Start-Sleep -Milliseconds 700
        $windows = Checked (@('ui', 'list-windows', '--json') + $scope) | ConvertFrom-Json
        $picker = @($windows | Where-Object { "$($_.ownerHwnd)" -eq "$script:mainWindow" -and $_.className -eq '#32770' })
    }
    if ($picker.Count -ne 1) { throw 'Expected one native file picker owned by the app.' }
    $hwnd = "$($picker[0].hwnd)"
    $tree = Ui @('inspect', '--interactive', '--json') -Window $hwnd | ConvertFrom-Json
    $edit = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.type -eq 'Edit' -and $_.name -eq 'File name:' } | Select-Object -First 1
    if (-not $edit) { throw 'No filename edit in the file picker.' }
    # ValuePattern changes the displayed text but Save can still use the original suggested name.
    # Type a native Windows path so the picker commits the edit to its selected file.
    $nativePath = $Path.Replace('/', '\')
    Ui @('send-keys', 'ctrl+a', '--target', $edit.selector, '--via', 'send-input') -Window $hwnd | Out-Null
    Ui @('send-keys', $nativePath, '--verbatim', '--via', 'send-input') -Window $hwnd | Out-Null
    $value = Ui @('get-value', $edit.selector, '--json') -Window $hwnd | ConvertFrom-Json
    if ($value.text -ne $nativePath) { throw 'The filename field did not accept the requested path.' }
    Ui @('send-keys', 'tab', '--via', 'send-input') -Window $hwnd | Out-Null
    Ui @('invoke', '1') -Window $hwnd | Out-Null
}
function Ready {
    Ui @('wait-for', 'SpectrogramStatus', '--value', 'Ready', '-t', '15000') | Out-Null
    Ui @('wait-for', 'SpectrogramSave', '-p', 'IsEnabled', '--value', 'True', '-t', '3000') | Out-Null
}
function Menu([string] $Button, [string] $Choice) {
    Ui @('invoke', $Button) | Out-Null
    Ui @('wait-for', $Choice, '-t', '3000') | Out-Null
    $tree = Ui @('inspect', '--interactive', '--json') | ConvertFrom-Json
    $item = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.type -eq 'MenuItem' -and $_.name -eq $Choice } | Select-Object -First 1
    if (-not $item) { throw "No menu item $Choice." }
    Ui @('invoke', $item.selector) | Out-Null
}

try {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    New-Item -ItemType Directory -Path $ArtifactDirectory -Force | Out-Null
    $windows = Ui @('list-windows', '--json') | ConvertFrom-Json
    $main = $windows | Where-Object className -eq 'WinUIDesktopWin32WindowClass' | Select-Object -First 1
    if (-not $main) { throw 'No app window in the selected target.' }
    $script:mainWindow = $main.hwnd
    Ui @('wait-for', 'Local Explorer', '-t', '60000') | Out-Null

    Test-Ui 'Local Explorer opens the incremental dialog' {
        Ui @('invoke', 'Local Explorer')
        Picker 'UploadFileButton' $FixturePath
        Ui @('wait-for', 'FileListView', '-t', '5000')
        Ui @('wait-for', 'Spectrogram sample', '-t', '5000')
        $list = Element 'FileListView'
        $row = Nodes @($list.children) | Where-Object type -eq 'ListItem' | Select-Object -First 1
        if (-not $row) { throw 'The imported file is missing.' }
        Ui @('invoke', $row.selector)
        Ui @('wait-for', 'LocalSpectrogram', '-t', '5000')
        Ui @('invoke', 'LocalSpectrogram')
        Ready
        Ui @('wait-for', 'SpectrogramPalette', '--value', 'SoX', '-t', '2000')
        Element 'Spectrogram' | Out-Null
        Screenshot '01-local.png'
    }
    if ($results[-1].status -ne 'PASS') { throw 'Import or opening failed; dependent dialog tests were not run.' }
    Test-Ui 'Palette, FFT size and channel controls work' {
        Menu 'SpectrogramPalette' 'Mono'
        Ui @('wait-for', 'SpectrogramPalette', '--value', 'Mono', '-t', '2000')
        Menu 'SpectrogramFft' '4096 samples'
        Ready
        Ui @('wait-for', 'SpectrogramFft', '--value', '4096 samples', '-t', '2000')
        Menu 'SpectrogramChannel' 'Channel 2'
        Ready
        Ui @('wait-for', 'SpectrogramChannel', '--value', 'Channel 2 / 2', '-t', '2000')
        Screenshot '02-options.png'
    }
    Test-Ui 'Two-axis zoom refines and resets without leaving the dialog' {
        Ui @('send-keys', 'vk=0xBB', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'SpectrogramInspectionStatus', '--value', 'Frequency view', '--contains', '-t', '2000')
        Ui @('wait-for', 'SpectrogramInspectionStatus', '--value', 'Zoom refined.', '--contains', '-t', '10000')
        Ui @('wait-for', 'SpectrogramInspectionStatus', '--value', 'Time view', '--contains', '-t', '2000')
        Screenshot '03-zoom.png'
        Ui @('invoke', 'SpectrogramResetZoom')
        Ready
        $text = Ui @('get-value', 'SpectrogramInspectionStatus', '--json') | ConvertFrom-Json
        if ($text.text -match 'Frequency view|Time view') { throw 'Reset zoom failed.' }
    }
    Test-Ui 'Selection clears with Escape before the dialog closes' {
        Ui @('send-keys', 'right shift+right', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'SpectrogramInspectionStatus', '--value', 'Passage', '--contains', '-t', '2000')
        Ui @('send-keys', 'esc', '--target', 'Spectrogram', '--via', 'send-input')
        Ready
    }
    Test-Ui 'Inspection tools stay optional' {
        Ui @('wait-for', 'SpectrogramReferences', '--value', 'None', '-t', '3000')
        Ui @('send-keys', 'home down enter', '--target', 'SpectrogramReferences', '--via', 'send-input')
        Ui @('send-keys', 'esc', '--via', 'send-input')
        Ui @('invoke', 'SpectrogramShowProfile')
        Ui @('invoke', 'SpectrogramShowCutoff')
        Ui @('wait-for', 'SpectrogramInspectionStatus', '--value', 'not prove', '--contains', '-t', '5000')
        Screenshot '04-inspection.png'
    }
    Test-Ui 'Save exports the current view' {
        Picker 'SpectrogramSave' $ExportPath
        Ui @('wait-for', "Saved $([IO.Path]::GetFileName($ExportPath))", '-t', '5000')
        if ($Target -eq 'sandbox') {
            $relative = $ExportPath.Replace('\', '/') -replace '^C:/WinApp/work/', ''
            if ($relative -eq $ExportPath.Replace('\', '/')) { throw 'Sandbox exports must be under C:\WinApp\work.' }
            Checked @('target', 'pull', 'sandbox', $relative, (Join-Path $ArtifactDirectory 'export.png')) | Out-Null
        }
        elseif (Test-Path $ExportPath) { Copy-Item $ExportPath (Join-Path $ArtifactDirectory 'export.png') }
        $png = Join-Path $ArtifactDirectory 'export.png'
        if (-not (Test-Path $png) -or (Get-Item $png).Length -lt 100) { throw 'No PNG was exported.' }
        $screenshots.Add($png)
    }
    Test-Ui 'Close and reopen does not retain a decoder' {
        Ui @('invoke', 'SpectrogramClose')
        Ui @('wait-for', 'SpectrogramStatus', '--gone', '-t', '3000')
        if ($Target -eq 'sandbox') {
            $code = '(Get-Process ffmpeg -ErrorAction SilentlyContinue | Measure-Object).Count'
            $count = Checked @('target', 'exec', 'sandbox', '--', 'powershell.exe', '-NoProfile', '-NonInteractive', '-Command', $code)
            if ([int](@($count)[-1]) -ne 0) { throw 'An ffmpeg process remains after closing the spectrogram.' }
        }
        Ui @('invoke', 'LocalSpectrogram')
        Ready
        Screenshot '05-reopened.png'
        Ui @('send-keys', 'esc', '--target', 'Spectrogram', '--via', 'send-input')
        Ui @('wait-for', 'SpectrogramStatus', '--gone', '-t', '3000')
    }
    Test-Ui 'Queue opens the same view' {
        Ui @('invoke', 'LocalAddToQueue')
        $tree = Ui @('inspect', '--json', '--depth', '8') | ConvertFrom-Json
        $navigation = Nodes @($tree.windows | ForEach-Object elements) | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq 'Queue' } | Select-Object -First 1
        if (-not $navigation) { throw 'No Queue navigation item.' }
        Ui @('invoke', $navigation.selector)
        if ((Ui @('get-value', 'CompactQueueButton', '--json') | ConvertFrom-Json).text -eq 'On') {
            Ui @('invoke', 'CompactQueueButton')
        }
        Ui @('wait-for', 'QueueSpectrogram', '-t', '5000')
        Ui @('invoke', 'QueueSpectrogram')
        Ready
        Screenshot '06-queue.png'
        Ui @('invoke', 'SpectrogramClose')
    }
    Test-Ui 'Compact queue opens the same view' {
        Ui @('invoke', 'CompactQueueButton')
        Ui @('wait-for', 'CompactQueueSpectrogram', '-t', '3000')
        Ui @('invoke', 'CompactQueueSpectrogram')
        Ready
        Screenshot '07-compact.png'
        Ui @('invoke', 'SpectrogramClose')
    }
} catch { $results.Add(@{ name = 'Test setup'; status = 'FAIL'; detail = "$_" }) }
finally {
    try { Checked (@('ui', 'yield') + $scope) | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ target = $Target; appPid = $AppPid; passed = $results.Count - $failed; failed = $failed; results = @($results.ToArray()); screenshots = @($screenshots.ToArray()) } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'test-results.json')
$results | ForEach-Object { "$($_.status) $($_.name) $($_.detail)" }
if ($failed) { exit 1 }
