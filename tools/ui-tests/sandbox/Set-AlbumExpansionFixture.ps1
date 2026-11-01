param([Parameter(Mandatory)][int]$AppPid, [switch]$ReadOnly)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This fixture is only for Windows Sandbox.' }
$package = Get-AppxPackage -Name 'DerekYang2.FluentDL.AlbumExpansionTest'
if (-not $package -or -not $package.IsDevelopmentMode) { throw 'The isolated album test package is missing.' }
$family = $package.PackageFamilyName
$data = [Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily($family)
$folder = $data.LocalFolder.Path
$marker = Join-Path $folder 'album-expansion-fixture.marker'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AlbumSqlite {
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int len, out IntPtr stmt, IntPtr tail);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr stmt);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr stmt);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
}
'@
function Read-Rows([IntPtr]$Database) {
    $statement = [IntPtr]::Zero
    if ([AlbumSqlite]::sqlite3_prepare_v2($Database,'SELECT JsonText FROM Items',-1,[ref]$statement,[IntPtr]::Zero) -ne 0) { throw 'Cannot read the fixture database.' }
    try { while ([AlbumSqlite]::sqlite3_step($statement) -eq 100) { [Runtime.InteropServices.Marshal]::PtrToStringAnsi([AlbumSqlite]::sqlite3_column_text($statement,0)) | ConvertFrom-Json } }
    finally { [void][AlbumSqlite]::sqlite3_finalize($statement) }
}
if ($ReadOnly) {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'The fixture ownership marker is missing.' }
    $db = [IntPtr]::Zero
    if ([AlbumSqlite]::sqlite3_open_v2((Join-Path $folder 'fluentdl.db'),[ref]$db,1,[IntPtr]::Zero) -ne 0) { throw 'Cannot open the fixture read-only.' }
    try { @(Read-Rows $db) | Sort-Object QueueCounter | Select-Object Id,Title,Source,QueueCounter,AlbumTracksCount | ConvertTo-Json -Depth 5 }
    finally { [void][AlbumSqlite]::sqlite3_close($db) }
    exit 0
}
$app = Get-Process -Id $AppPid
if ($app.Path -ne (Join-Path $package.InstallLocation 'FluentDL.exe')) { throw 'The process is not the isolated test app.' }
[void]$app.CloseMainWindow()
if (-not $app.WaitForExit(20000)) { throw 'The test app did not close normally.' }
$db = [IntPtr]::Zero
if ([AlbumSqlite]::sqlite3_open_v2((Join-Path $folder 'fluentdl.db'),[ref]$db,2,[IntPtr]::Zero) -ne 0) { throw 'Cannot open the fixture database.' }
try {
    if (@(Read-Rows $db).Count -ne 0) { throw 'Refusing to overwrite an existing queue.' }
    function Track([string]$Id,[string]$Title,[int]$Order) {
        @{ Source='youtube'; Id=$Id; Title=$Title; Artists='Expansion fixture'; AlbumName='Fixture album'; Duration='30'; ReleaseDate='2026'; TrackPosition="$Order"; ImageLocation='ms-appx:///Assets/Unloaded.jpg' }
    }
    function Album([string]$Id,[string]$Title,[int]$Counter,[object[]]$Tracks) {
        @{ Source='youtube'; Id=$Id; Title=$Title; Artists='Expansion fixture'; Duration='90'; ImageLocation='ms-appx:///Assets/Unloaded.jpg'; AlbumTracksCount=3; QueueCounter=$Counter; TrackList=@($Tracks) }
    }
    $regular = @( (Track 'regular-one' 'First regular track' 1), (Track 'existing' 'Already queued track' 2), (Track 'regular-three' 'Third regular track' 3) )
    $compact = @( (Track 'compact-one' 'First compact track' 1), (Track 'compact-two' 'Second compact track' 2), (Track 'compact-three' 'Third compact track' 3) )
    $incomplete = @( (Track 'incomplete-one' 'Unavailable album track one' 1), (Track 'incomplete-two' 'Unavailable album track two' 2) )
    $existing = Track 'existing' 'Already queued track' 2
    $existing.QueueCounter = 0
    $after = Track 'after' 'After albums' 1
    $after.QueueCounter = 4
    $rows = @($existing, (Album 'regular-album' 'Expand regular album' 1 $regular), (Album 'compact-album' 'Expand compact album' 2 $compact), (Album 'incomplete-album' 'Incomplete album' 3 $incomplete), $after)
    $sql = 'BEGIN;'
    foreach ($row in $rows) {
        $hash = $(if ($row.AlbumTracksCount) { 'album:' } else { '' }) + $row.Source + $row.Id
        $json = $row | ConvertTo-Json -Depth 20 -Compress
        $sql += "INSERT INTO Items (Hash,JsonText,Image,CreatedUtc) VALUES ('" + $hash.Replace("'","''") + "','" + $json.Replace("'","''") + "',NULL,'2026-10-19T00:00:00Z');"
    }
    $sql += 'COMMIT;'
    $errorPointer = [IntPtr]::Zero
    if ([AlbumSqlite]::sqlite3_exec($db,$sql,[IntPtr]::Zero,[IntPtr]::Zero,[ref]$errorPointer) -ne 0) { throw 'Cannot seed the synthetic queue.' }
} finally { [void][AlbumSqlite]::sqlite3_close($db) }
'owned-album-expansion-fixture' | Set-Content -LiteralPath $marker
$indexer = [Collections.Generic.IDictionary[string,object]].GetProperty('Item')
foreach ($pair in @{ auto_play='false'; notify_update='false'; queue_compact_layout='false'; AppBackgroundRequestedTheme='"Dark"' }.GetEnumerator()) {
    $indexer.SetValue($data.LocalSettings.Values,[object]$pair.Value,[object[]]@($pair.Key))
}
Start-Process ('shell:AppsFolder\' + $family + '!App')
$deadline = [DateTime]::UtcNow.AddSeconds(45)
do {
    $next = Get-Process -Name FluentDL -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $package.InstallLocation 'FluentDL.exe') | Select-Object -First 1
    if ($next) { break }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if (-not $next) { throw 'The seeded app did not start.' }
@{ProcessId=$next.Id; Family=$family; Data=$folder; Rows=$rows.Count} | ConvertTo-Json
