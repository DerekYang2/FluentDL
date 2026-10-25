using EmbedIO.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SQLitePCL;
using Serilog;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FluentDL.Services
{
    internal class DatabaseService
    {
        private sealed record AlbumReplacement(string Hash, IReadOnlyList<(string Hash, string Json)> Tracks);
        private enum WriteKind { Full, Metadata, Artwork, Remove, Clear, ReplaceTrack }
        private sealed record QueueWrite(string Hash, string Json, byte[]? Bytes, IReadOnlyList<(string Hash, string Json)>? Snapshot = null,
            AlbumReplacement? Replacement = null, WriteKind Kind = WriteKind.Full, string? CoverUrl = null, string? RemovedHash = null);
        private static FluentDL.Core.Updates.AsyncPersistenceQueue<QueueWrite>? _writes;
        private static SqliteConnection? _consumerConnection;
        private static SqliteConnection Conn => _consumerConnection
            ?? throw new InvalidOperationException("Database not initialized. Call InitDatabase first.");
        public const string BackupFilePrefix = "backup_";
        private static string _applicationDataFolder = ApplicationData.Current.LocalFolder.Path;
        private static string _databaseFile = "fluentdl.db";
        private static string _dbPath = Path.Combine(_applicationDataFolder, _databaseFile);

        private static SqliteConnection CreateConnection()
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            var conn = new SqliteConnection(cs);
            return conn;
        }

        public static void StartConsumerTask()
        {
            _writes = new(async item =>
            {
                if (item.Replacement != null) await QueueAlbumPersistence.ReplaceAsync(Conn, item.Replacement.Hash, item.Replacement.Tracks);
                else if (item.Snapshot != null) await SaveSnapshotAsync(item.Snapshot);
                else if (item.Kind == WriteKind.Metadata) await QueueItemPersistence.SaveMetadataAsync(Conn, item.Hash, item.Json);
                else if (item.Kind == WriteKind.Artwork) await QueueItemPersistence.SetArtworkAsync(Conn, item.Hash, item.CoverUrl!, item.Bytes);
                else if (item.Kind == WriteKind.ReplaceTrack) await QueueItemPersistence.ReplaceAsync(Conn, item.RemovedHash, item.Hash, item.Json);
                else if (item.Kind is WriteKind.Remove or WriteKind.Clear)
                {
                    using var command = Conn.CreateCommand();
                    command.CommandText = item.Kind == WriteKind.Clear ? "DELETE FROM Items" : "DELETE FROM Items WHERE Hash=@h";
                    if (item.Kind == WriteKind.Remove) command.Parameters.AddWithValue("@h", item.Hash);
                    await command.ExecuteNonQueryAsync();
                }
                else await SaveAsync(item.Hash, item.Json, item.Bytes);
            },
                error => Log.Error(error, "Queue persistence failed"));
        } 

        public static async Task<string?> InitDatabase()
        {
            if (_consumerConnection != null) return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

                var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
                _consumerConnection = new SqliteConnection(cs);
                await _consumerConnection.OpenAsync();

                using var cmd = _consumerConnection.CreateCommand();
                cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                CREATE TABLE IF NOT EXISTS Items (
                    Hash TEXT PRIMARY KEY,
                    JsonText TEXT NOT NULL,
                    Image BLOB,
                    CreatedUtc TEXT NOT NULL
                );";
                await cmd.ExecuteNonQueryAsync();

                StartConsumerTask();
                Log.Information("Queue database initialized");
                return null;
            } catch (Exception ex)
            {
                Log.Error(ex, "Queue database initialization failed");
                return $"Failed to initialize queue database: {ex.Message}";
            }
        }

        // Read all bytes from an IRandomAccessStream
        //private static async Task<byte[]> ConvertStream(IRandomAccessStream s)
        //{
        //    var dr = new DataReader(s.GetInputStreamAt(0));
        //    var bytes = new byte[s.Size];
        //    await dr.LoadAsync((uint)s.Size);
        //    dr.ReadBytes(bytes);
        //    return bytes;
        //}
        private static async Task<byte[]?> ConvertStream(IRandomAccessStream? s)
        {
            if (s == null) return null;
            var originalPosition = s.Position;
            try
            {
                s.Seek(0);
                var bytes = new byte[s.Size];
                await s.ReadAsync(bytes.AsBuffer(), (uint)s.Size, InputStreamOptions.None);
                return bytes;
            }
            finally
            {
                s.Seek(originalPosition);
            }
        }

        public static async Task QueueSave(string hash, string json, IRandomAccessStream? stream)
        {
            try
            {
                var queue = _writes ?? throw new InvalidOperationException("Queue persistence is not initialized.");
                await queue.EnqueueAsync(async () => new QueueWrite(hash, json, await ConvertStream(stream)));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Queue item could not be scheduled for persistence");
            }
        }

        internal static async Task SaveForUpdateAsync(IReadOnlyList<(string Hash, string Json)> snapshot, CancellationToken token)
        {
            var queue = _writes ?? throw new InvalidOperationException("Queue persistence is not initialized.");
            await queue.FlushAsync(token);
            await queue.EnqueueAsync(() => Task.FromResult(new QueueWrite("", "", null, snapshot))).WaitAsync(token);
            await queue.FlushAsync(token);
        }

        internal static Task ReplaceAlbumAsync(string hash, IReadOnlyList<(string Hash, string Json)> tracks)
        {
            var queue = _writes ?? throw new InvalidOperationException("Queue persistence is not initialized.");
            return queue.EnqueueAsync(() => Task.FromResult(new QueueWrite("", "", null, Replacement: new(hash, tracks))));
        }

        internal static Task SaveMetadataAsync(string hash, string json) =>
            (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized."))
                .EnqueueAsync(() => Task.FromResult(new QueueWrite(hash, json, null, Kind: WriteKind.Metadata)));

        internal static Task SaveArtworkAsync(string hash, string url, byte[]? bytes) =>
            (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized."))
                .EnqueueAsync(() => Task.FromResult(new QueueWrite(hash, "", bytes, Kind: WriteKind.Artwork, CoverUrl: url)));

        internal static Task ReplaceTrackAsync(string? removedHash, string hash, string json) =>
            (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized."))
                .EnqueueAsync(() => Task.FromResult(new QueueWrite(hash, json, null, Kind: WriteKind.ReplaceTrack, RemovedHash: removedHash)));

        internal static Task FlushAsync() => (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized.")).FlushAsync();
        internal static Task FlushOnCloseAsync() => _writes?.FlushAsync() ?? Task.CompletedTask;

        internal static async Task<byte[]?> LoadArtworkByUrlAsync(string url)
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Image FROM Items WHERE Image IS NOT NULL AND ({QueueItemPersistence.CoverExpression})=@u LIMIT 1";
            command.Parameters.AddWithValue("@u", url);
            return await command.ExecuteScalarAsync() as byte[];
        }

        private static async Task SaveSnapshotAsync(IReadOnlyList<(string Hash, string Json)> snapshot)
        {
            using var transaction = Conn.BeginTransaction();
            var hashes = snapshot.Select(item => item.Hash).ToHashSet(StringComparer.Ordinal);
            var stale = new List<string>();
            using (var select = Conn.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT Hash FROM Items";
                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync()) if (!hashes.Contains(reader.GetString(0))) stale.Add(reader.GetString(0));
            }
            foreach (var hash in stale)
            {
                using var remove = Conn.CreateCommand();
                remove.Transaction = transaction;
                remove.CommandText = "DELETE FROM Items WHERE Hash = @h";
                remove.Parameters.AddWithValue("@h", hash);
                await remove.ExecuteNonQueryAsync();
            }
            foreach (var item in snapshot)
            {
                using var save = Conn.CreateCommand();
                save.Transaction = transaction;
                save.CommandText = "INSERT INTO Items (Hash, JsonText, Image, CreatedUtc) VALUES (@h, @j, NULL, @t) "
                    + "ON CONFLICT(Hash) DO UPDATE SET JsonText = excluded.JsonText";
                save.Parameters.AddWithValue("@h", item.Hash);
                save.Parameters.AddWithValue("@j", item.Json);
                save.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                await save.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }

        // Save image bytes (from IRandomAccessStream) plus metadata into DB
        private static async Task SaveAsync(string hash, string jsonText, byte[]? bytes)
        {
            if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("hash required", nameof(hash));
            ArgumentNullException.ThrowIfNull(jsonText);

            await using var cmd = Conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Items (Hash, JsonText, Image, CreatedUtc)
                VALUES (@h, @j, @b, @t);";

            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@j", jsonText);
            var blobParam = cmd.CreateParameter();
            blobParam.ParameterName = "@b";
            blobParam.Value = bytes ?? (object)DBNull.Value;
            cmd.Parameters.Add(blobParam);
            cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync(); 
            Log.Debug("Queue item persisted");
        }

        public static async Task<Dictionary<string, string>> LoadQueueJSON()
        {
            var results = new Dictionary<string, string>();

            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Hash, JsonText FROM Items";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // reader.GetString(0) is Hash, reader.GetString(1) is JsonText
                results.Add(reader.GetString(0), reader.GetString(1));
            }

            return results;
        }

        public static async Task<InMemoryRandomAccessStream?> LoadImageStreamAsync(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return null;

            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Image FROM Items WHERE Hash = @h LIMIT 1;";
                cmd.Parameters.AddWithValue("@h", hash);

                // Using ExecuteScalar or a Reader is fine for small/medium blobs
                var data = await cmd.ExecuteScalarAsync();
                if (data is byte[] bytes)
                {
                    var ms = new InMemoryRandomAccessStream();
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    return ms;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Cached queue image could not be read");
            }
            return null;
        }

        public static async Task<BitmapImage?> GetBitmapAsync(string hash, DispatcherQueue dispatcher, int decodeHeight = 76)
        {
            try
            {
                var stream = await LoadImageStreamAsync(hash);
                if (stream == null) return null;

                TaskCompletionSource<BitmapImage?> tcs = new();

                bool success = dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        if (decodeHeight > 0) bitmap.DecodePixelHeight = decodeHeight;

                        await bitmap.SetSourceAsync(stream);
                        tcs.SetResult(bitmap);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Cached queue image could not be decoded");
                        tcs.SetResult(null);
                    }
                    finally
                    {
                        // Ensure the stream is cleaned up after the bitmap is done with it
                        stream.Dispose();
                    }
                });

                if (!success) return null; // Dispatcher is shutting down

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Cached queue image could not be loaded");
                return null;
            }
        }

        public static async Task Clear()
        {
            try
            {
                await (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized."))
                    .EnqueueAsync(() => Task.FromResult(new QueueWrite("", "", null, Kind: WriteKind.Clear)));
                Log.Information("Persisted queue cleared");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Persisted queue could not be cleared");
            }
        }

        public static async Task Remove(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return;

            try
            {
                await (_writes ?? throw new InvalidOperationException("Queue persistence is not initialized."))
                    .EnqueueAsync(() => Task.FromResult(new QueueWrite(hash, "", null, Kind: WriteKind.Remove)));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Queue item could not be removed from storage");
            }
        }

        public static List<FileInfo> GetBackups()
        {
            var backupsDir = Path.Combine(_applicationDataFolder, "Backups");
            if (!Directory.Exists(backupsDir)) return [];
            return new DirectoryInfo(backupsDir)
                    .GetFiles($"{BackupFilePrefix}*.zip")
                    .OrderByDescending(f => f.Name)
                    .ToList();
        }

        public static async Task CreateBackup(int keep = 5)
        {
            var backupsDir = Path.Combine(_applicationDataFolder, "Backups");
            Directory.CreateDirectory(backupsDir);
            var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var raw = Path.Combine(backupsDir, $"{BackupFilePrefix}{ts}.db");
            var zip = Path.Combine(backupsDir, $"{BackupFilePrefix}{ts}.zip");

            // Create safe snapshot
            using (var src = CreateConnection())
            using (var dst = new SqliteConnection($"Data Source={raw}"))
            {
                await src.OpenAsync();
                using (var cmd = src.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    var result = await cmd.ExecuteScalarAsync();
                }

                await dst.OpenAsync();
                await Task.Run(()=>src.BackupDatabase(dst));
                await dst.CloseAsync();
                await src.CloseAsync();
            }

            SqliteConnection.ClearAllPools();

            // Compress (Zip)
            using (var zipFs = new FileStream(zip, FileMode.CreateNew))
            using (var archive = new ZipArchive(zipFs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(Path.GetFileName(raw), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(raw);
                await fileStream.CopyToAsync(entryStream);
            }

            // Delete raw snapshot
            File.Delete(raw);

            // Rotate keep latest `keep` zip files
            var files = GetBackups();
            foreach (var f in files.Skip(keep)) f.Delete();
        }

        public static async Task CreateBackupAtPath(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("zipPath required", nameof(zipPath));

            var directory = Path.GetDirectoryName(zipPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("zipPath must include a directory", nameof(zipPath));

            Directory.CreateDirectory(directory);
            var raw = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(zipPath)}.db");

            // Create safe snapshot
            using (var src = CreateConnection())
            using (var dst = new SqliteConnection($"Data Source={raw}"))
            {
                await src.OpenAsync();
                using (var cmd = src.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    var result = await cmd.ExecuteScalarAsync();
                }

                await dst.OpenAsync();
                await Task.Run(() => src.BackupDatabase(dst));
                await dst.CloseAsync();
                await src.CloseAsync();
            }

            SqliteConnection.ClearAllPools();

            // Compress (Zip)
            using (var zipFs = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipFs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(Path.GetFileName(raw), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(raw);
                await fileStream.CopyToAsync(entryStream);
            }

            File.Delete(raw);
        }

        public static async Task RestoreFromZip(string zipPath, string targetDbPath)
        {
            var temp = Path.GetTempFileName();
            try
            {
                // 1. Extract to temp file
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault();
                    if (entry == null) throw new InvalidOperationException("Zip contains no entries");

                    using (var entryStream = entry.Open())
                    using (var outFs = File.Open(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await entryStream.CopyToAsync(outFs);
                    }
                }

                // 2. Verify integrity
                using (var check = new SqliteConnection($"Data Source={temp}"))
                {
                    await check.OpenAsync();
                    using var cmd = check.CreateCommand();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var result = await cmd.ExecuteScalarAsync();
                    if (result?.ToString() != "ok") throw new InvalidOperationException("Backup integrity check failed: " + result);

                    await check.CloseAsync();
                }

                // Release any pooled connections to the temp file before replacing
                SqliteConnection.ClearAllPools();

                // 3. Atomically replace target DB
                var backupOld = targetDbPath + ".bak";
                File.Replace(temp, targetDbPath, backupOld, ignoreMetadataErrors: true);
                if (File.Exists(backupOld)) File.Delete(backupOld);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                throw;
            }
        }

        public static async Task RestoreZip(string zipPath)
        {
            // Ensure all pooled connections are released
            SqliteConnection.ClearAllPools();

            try
            {
                // Wait for tasks to finish
                if (_writes != null)
                {
                    await _writes.CompleteAsync();
                }

                // Close global consumer connection
                if (_consumerConnection != null)
                {
                    await _consumerConnection.CloseAsync();
                    await _consumerConnection.DisposeAsync();
                    _consumerConnection = null;
                }

                await RestoreFromZip(zipPath, _dbPath);

                // Remove wal and shm files if they exist, since the restored DB won't have them
                var wal = _dbPath + "-wal";
                var shm = _dbPath + "-shm";
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
            } catch
            {
                throw;
            }
            finally
            {
                // Reopen global consumer connection
                await InitDatabase();
            }
        }
    }
}
