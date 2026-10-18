param([Parameter(Mandatory)][int]$AppPid, [int]$Width = 800, [int]$Height = 740,
    [ValidateSet('None','On','Restore')][string]$Contrast = 'None')
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This helper is for Windows Sandbox only.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PlaylistTestWindow {
    [StructLayout(LayoutKind.Sequential)]
    public struct HighContrast { public uint Size; public uint Flags; public IntPtr Scheme; }
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Parameters(uint action, uint size, ref HighContrast value, uint flags);
}
'@
$window = (Get-Process -Id $AppPid).MainWindowHandle
if ($window -eq [IntPtr]::Zero -or -not [PlaylistTestWindow]::MoveWindow($window, 100, 100, $Width, $Height, $true)) { throw 'Cannot resize the test window.' }
if ($Contrast -ne 'None') {
    $statePath = Join-Path $PSScriptRoot 'contrast-original.json'
    $hc = New-Object PlaylistTestWindow+HighContrast
    $hc.Size = [Runtime.InteropServices.Marshal]::SizeOf($hc)
    if (-not [PlaylistTestWindow]::Parameters(0x42, $hc.Size, [ref]$hc, 0)) { throw 'Cannot read contrast settings.' }
    if ($Contrast -eq 'On') {
        if (Test-Path $statePath) { throw 'Restore the previous contrast test first.' }
        @{Flags=$hc.Flags;Scheme=[Runtime.InteropServices.Marshal]::PtrToStringUni($hc.Scheme)} | ConvertTo-Json | Set-Content $statePath
        $flags = $hc.Flags -bor 1
        $scheme = 'High Contrast Black'
    } else {
        $saved = Get-Content $statePath -Raw | ConvertFrom-Json
        $flags = [uint32]$saved.Flags
        $scheme = [string]$saved.Scheme
    }
    $buffer = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($scheme)
    try {
        $hc.Flags = $flags
        $hc.Scheme = $buffer
        if (-not [PlaylistTestWindow]::Parameters(0x43, $hc.Size, [ref]$hc, 2)) { throw 'Cannot change guest contrast settings.' }
    } finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer) }
    if ($Contrast -eq 'Restore') { Remove-Item $statePath }
}
"Guest test window resized to ${Width}x${Height}. Contrast: $Contrast."
