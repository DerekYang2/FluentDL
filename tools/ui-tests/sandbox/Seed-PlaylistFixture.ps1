param(
    [ValidateSet('Ready', 'AlbumsOnly', 'SignedOut', 'MultiSource')][string]$Scenario = 'Ready',
    [ValidateSet('Light', 'Dark')][string]$Theme = 'Dark'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Synthetic input only, in the disposable guest. Never point this fixture at the user's installed app.
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This fixture is for Windows Sandbox only.' }
$data = [Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily('DerekYang2.FluentDL_qy10v4cp46cw8')
$session = @{
    ClientId = 'ui-test-client'; AccessToken = 'ui-test-token'; RefreshToken = 'ui-test-refresh'
    Expires = [DateTimeOffset]::UtcNow.AddHours(1).ToString('O'); Country = 'US'; AccountName = 'UI test account'; UserId = '7'
} | ConvertTo-Json -Compress
Add-Type -AssemblyName System.Security
$encrypted = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($session), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
$values = $data.LocalSettings.Values
# PowerShell's WinRT projection exposes the dictionary interface but not its indexer directly.
$item = [System.Collections.Generic.IDictionary[string,object]].GetProperty('Item')
$item.SetValue($values, [object](ConvertTo-Json -InputObject $(if ($Scenario -eq 'SignedOut') { '' } else { $encrypted }) -Compress), [object[]]@('tidal_download_session'))
$item.SetValue($values, [object](ConvertTo-Json -InputObject $Theme -Compress), [object[]]@('AppBackgroundRequestedTheme'))
$item.SetValue($values, [object]$(if ($Scenario -eq 'SignedOut') { 'false' } else { 'true' }), [object[]]@('PlaylistUiPreviewSignedIn'))

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FixtureSqlite {
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)]
    public static extern int sqlite3_close(IntPtr db);
}
'@
$db = [IntPtr]::Zero
if ([FixtureSqlite]::sqlite3_open((Join-Path $data.LocalFolder.Path 'fluentdl.db'), [ref]$db) -ne 0) { throw 'Cannot open guest fixture database.' }
try {
    $rows = @(@{ Source='tidal'; Id='293301129'; Title='Example TIDAL album'; Artists='Example artist'; AlbumTracksCount=22; QueueCounter=2 })
    if ($Scenario -ne 'AlbumsOnly') {
        $rows += @{ Source='tidal'; Id='20115564'; Title='First TIDAL track with a deliberately long title for ellipsis and hover testing'; Artists='Example artist'; QueueCounter=0 }
        $rows += @{ Source='tidal'; Id='293301151'; Title='Second TIDAL track'; Artists='Example artist'; QueueCounter=1 }
        $rows += @{ Source='deezer'; Id='3135556'; Title='Other source, excluded'; Artists='Example artist'; QueueCounter=3 }
    }
    if ($Scenario -eq 'MultiSource') {
        $rows += @{ Source='deezer'; Id='67238735'; Title='Second Deezer track'; Artists='Example artist'; QueueCounter=4 }
        $rows += @{ Source='deezer'; Id='302127'; Title='Example Deezer album'; Artists='Example artist'; AlbumTracksCount=12; QueueCounter=5 }
        $rows += @{ Source='qobuz'; Id='9140031'; Title='First Qobuz track'; Artists='Example artist'; QueueCounter=6 }
        $rows += @{ Source='qobuz'; Id='8767428'; Title='Second Qobuz track'; Artists='Example artist'; QueueCounter=7 }
        $rows += @{ Source='qobuz'; Id='0002894790375'; Title='Example Qobuz album'; Artists='Example artist'; AlbumTracksCount=10; QueueCounter=8 }
    }
    $sql = 'CREATE TABLE IF NOT EXISTS Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL); DELETE FROM Items;'
    foreach ($row in $rows) {
        $json = ($row | ConvertTo-Json -Compress).Replace("'", "''")
        $hash = "$($row.Source)$($row.Id)"
        if ($row.ContainsKey('AlbumTracksCount')) { $hash = 'album:' + $hash }
        $sql += "INSERT INTO Items VALUES ('$hash','$json',NULL,'2026-10-17T00:00:00Z');"
    }
    $errorPointer = [IntPtr]::Zero
    if ([FixtureSqlite]::sqlite3_exec($db, $sql, [IntPtr]::Zero, [IntPtr]::Zero, [ref]$errorPointer) -ne 0) { throw 'Could not write synthetic queue rows.' }
}
finally { [void][FixtureSqlite]::sqlite3_close($db) }
"Synthetic playlist fixture ready: $Scenario, $Theme. No real credentials supplied."
