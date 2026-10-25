param([switch]$Restore, [switch]$Enable, [Parameter(Mandatory)][string]$Owner)
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'Animation changes are restricted to Windows Sandbox.' }
$stateFile = 'C:\WinApp\work\FluentDL\queue-skeleton-motion-state.json'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class QueueSkeletonMotion {
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Parameters(uint action, uint size, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool Set(uint action, uint size, IntPtr value, uint flags);
}
'@
$value = 0
if (Test-Path -LiteralPath $stateFile) {
    $saved = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    if ($saved.owner -ne $Owner) { throw 'Another workflow owns the saved animation setting.' }
} elseif ($Restore) { exit 0 }
else {
    if (-not [QueueSkeletonMotion]::Parameters(0x1042, 0, [ref]$value, 0)) { throw 'Cannot read the guest animation setting.' }
    @{owner=$Owner;enabled=$value} | ConvertTo-Json | Set-Content -LiteralPath $stateFile
    $saved = @{enabled=$value}
}
$value = if ($Restore) { [int]$saved.enabled } elseif ($Enable) { 1 } else { 0 }
if (-not [QueueSkeletonMotion]::Set(0x1043, 0, [IntPtr]$value, 3)) { throw 'Cannot change the guest animation setting.' }
if ($Restore) { Remove-Item -LiteralPath $stateFile }
