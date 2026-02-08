using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FluentDL.Services
{
    internal class DatabaseService
    {
        private static readonly Channel<(string Hash, string Json, byte[]? Bytes)> _saveQueue =
        Channel.CreateUnbounded<(string, string, byte[]?)>();
        private static SqliteConnection? _consumerConnection;
        private static SqliteConnection Conn => _consumerConnection
            ?? throw new InvalidOperationException("Database not initialized. Call InitDatabase first.");

        private static string _applicationDataFolder = ApplicationData.Current.LocalFolder.Path;
        private static string _databaseFile = "fluentdl.db";
        private static string _dbPath = Path.Combine(_applicationDataFolder, _databaseFile);

        private static SqliteConnection CreateConnection()
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            var conn = new SqliteConnection(cs);
            return conn;
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

                _ = Task.Run(ProcessQueueAsync);
                return null;
            } catch (Exception ex)
            {
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
                _saveQueue.Writer.TryWrite((hash, json, await ConvertStream(stream)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static async Task ProcessQueueAsync()
        {
            while (await _saveQueue.Reader.WaitToReadAsync())
            {
                while (_saveQueue.Reader.TryRead(out var item))
                {
                    try
                    {
                        await SaveAsync(item.Hash, item.Json, item.Bytes);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background save failed: {ex.Message}");
                    }
                }
            }
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
                Debug.WriteLine(ex.ToString());
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
                        Debug.WriteLine($"Bitmap processing failed for {hash}: {ex.Message}");
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
                Debug.WriteLine($"Failed to load image stream from DB: {ex.Message}");
                return null;
            }
        }

        public static async Task Clear()
        {
            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Items;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }

        public static async Task Remove(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return;

            try
            {
                await using var conn = CreateConnection();
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Items WHERE Hash = @h;";
                cmd.Parameters.AddWithValue("@h", hash);

                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }
}
