param([switch]$Restore)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Contrast fixture changes are restricted to Windows Sandbox.' }
$stateFile = 'C:\WinApp\work\FluentDL\compact-contrast-state.json'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CompactContrast {
    [StructLayout(LayoutKind.Sequential)] public struct State { public uint Size; public uint Flags; public IntPtr Scheme; }
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Parameters(uint action, uint size, ref State state, uint flags);
}
'@
$state = New-Object CompactContrast+State
$state.Size = [Runtime.InteropServices.Marshal]::SizeOf($state)
if ($Restore) {
    if (-not (Test-Path -LiteralPath $stateFile)) { exit 0 }
    $saved = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    $flags = [uint32]$saved.flags
    $scheme = [string]$saved.scheme
} else {
    if (Test-Path -LiteralPath $stateFile) { throw 'Restore the previous owned Contrast state first.' }
    if (-not [CompactContrast]::Parameters(0x42,$state.Size,[ref]$state,0)) { throw 'Cannot read guest Contrast settings.' }
    $scheme = [Runtime.InteropServices.Marshal]::PtrToStringUni($state.Scheme)
    @{flags=$state.Flags;scheme=$scheme} | ConvertTo-Json | Set-Content -LiteralPath $stateFile
    $flags = $state.Flags -bor 1
    $scheme = 'High Contrast Black'
}
$buffer = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($scheme)
try {
    $state.Flags = $flags
    $state.Scheme = $buffer
    if (-not [CompactContrast]::Parameters(0x43,$state.Size,[ref]$state,2)) { throw 'Cannot set guest Contrast settings.' }
} finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer) }
if ($Restore) { Remove-Item -LiteralPath $stateFile }
