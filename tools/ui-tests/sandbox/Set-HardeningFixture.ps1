param([ValidateSet('Seed','Fail','Read')][string]$Mode = 'Seed')
$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This fixture is only for Windows Sandbox.' }
$package = Get-AppxPackage -Name 'DerekYang2.FluentDL.V4HardeningTest'
if (-not $package -or -not $package.IsDevelopmentMode) { throw 'The isolated hardening test package is missing.' }
$data = [Windows.Management.Core.ApplicationDataManager,Windows.Management,ContentType=WindowsRuntime]::CreateForPackageFamily($package.PackageFamilyName)
$folder = $data.LocalFolder.Path
$marker = Join-Path $folder 'hardening-fixture.marker'
if ($Mode -eq 'Read') {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'The fixture ownership marker is missing.' }
    $state = Join-Path $folder 'hardening-state.json'
    $startup = Join-Path $folder 'hardening-startup.json'
    @{ State = $(if (Test-Path $state) { Get-Content $state -Raw | ConvertFrom-Json }); Startup = $(if (Test-Path $startup) { Get-Content $startup -Raw | ConvertFrom-Json }) } | ConvertTo-Json -Depth 20
    exit 0
}
$running = Get-Process -Name FluentDL -ErrorAction SilentlyContinue | Where-Object Path -eq (Join-Path $package.InstallLocation 'FluentDL.exe')
if ($running) { throw 'Close the isolated app normally before changing its fixture.' }
if ($Mode -eq 'Fail') {
    if (-not (Test-Path -LiteralPath $marker)) { throw 'The fixture ownership marker is missing.' }
    'fail' | Set-Content -LiteralPath $marker
    exit 0
}
if (Test-Path -LiteralPath $marker) { throw 'Refusing to overwrite an existing hardening fixture.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HardeningSqlite {
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr error);
 [DllImport("winsqlite3.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int sqlite3_close(IntPtr db);
}
'@
$dbPath = Join-Path $folder 'fluentdl.db'
if (Test-Path -LiteralPath $dbPath) { throw 'Refusing to overwrite an existing queue database.' }
$db = [IntPtr]::Zero
if ([HardeningSqlite]::sqlite3_open_v2($dbPath,[ref]$db,6,[IntPtr]::Zero) -ne 0) { throw 'Cannot create the fixture database.' }
try {
    $rows = @(0..2 | ForEach-Object {
        $id = @('first','second','third')[$_]
        @{ Source='local'; Id="Z:\fixture-missing\$id.flac"; Title="Missing-drive $id"; Artists='Saved fixture metadata'; AlbumName='Saved album'; Duration='1'; QueueCounter=$_ }
    })
    $rows += @{ Source='deezer'; Id='fixture-album'; Title='Mixed-outcome album'; Artists='Fixture'; Duration='3'; AlbumTracksCount=3; QueueCounter=3 }
    $cover = [IO.File]::ReadAllBytes((Join-Path $package.InstallLocation 'Assets\Unloaded.jpg'))
    $hex = [BitConverter]::ToString($cover).Replace('-','')
    $sql = 'CREATE TABLE Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL); BEGIN;'
    foreach ($row in $rows) {
        $hash = $(if ($row.AlbumTracksCount) { 'album:' } else { '' }) + $row.Source + $row.Id
        $json = $row | ConvertTo-Json -Depth 20 -Compress
        $image = if ($row.Title -eq 'Missing-drive second') { "X'$hex'" } else { 'NULL' }
        $sql += "INSERT INTO Items VALUES ('" + $hash.Replace("'","''") + "','" + $json.Replace("'","''") + "',$image,'2026-10-27T00:00:00Z');"
    }
    $sql += "INSERT INTO Items VALUES ('corrupt-fixture','{unreadable',NULL,'2026-10-27T00:00:00Z'); COMMIT;"
    $errorPointer = [IntPtr]::Zero
    if ([HardeningSqlite]::sqlite3_exec($db,$sql,[IntPtr]::Zero,[IntPtr]::Zero,[ref]$errorPointer) -ne 0) { throw 'Cannot seed the fixture database.' }
} finally { [void][HardeningSqlite]::sqlite3_close($db) }
'stall' | Set-Content -LiteralPath $marker
$indexer = [Collections.Generic.IDictionary[string,object]].GetProperty('Item')
$settings = @{
    auto_play='false'; notify_update='false'; queue_compact_layout='false'; AppBackgroundRequestedTheme='"Dark"';
    download_directory=((Join-Path $folder 'downloads') | ConvertTo-Json -Compress); ask_before_download='false';
    file_naming_wildcard='"{title}"'; album_subfolders='false'; subfolders='false'; download_album_cover_art='false';
    download_lyrics='false'; replaygain_auto_apply='false'; replaygain_album_downloads='false'; conversion_threads='2';
    use_ytdlp='false'; auto_update_ytdlp='false';
}
foreach ($pair in $settings.GetEnumerator()) { $indexer.SetValue($data.LocalSettings.Values,[object]$pair.Value,[object[]]@($pair.Key)) }
$sha = [Security.Cryptography.SHA256]::Create()
try { $artworkHash = [BitConverter]::ToString($sha.ComputeHash($cover)).Replace('-','') } finally { $sha.Dispose() }
@{ Family=$package.PackageFamilyName; Data=$folder; Rows=$rows.Count; Saved=5; ArtworkLength=$cover.Length; ArtworkHash=$artworkHash } | ConvertTo-Json
