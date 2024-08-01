using System.Collections.ObjectModel;
using System.Diagnostics;
using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Service;

namespace FluentDL.Services;

internal class QobuzApi
{
    private static QobuzApiService apiService = new QobuzApiService();

    public static void Initialize(string? userId, string? AuthToken)
    {
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(AuthToken))
        {
            apiService.LoginWithToken(userId, AuthToken);
        }
    }

    public static void AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token)
    {
        if (!url.Contains("/album/")) return;
        // Get string after the last slash
        var albumId = url.Split('/').Last();
        Debug.WriteLine("Album ID: " + albumId);
        var album = apiService.GetAlbum(albumId);
        if (album.Tracks != null)
        {
            foreach (var track in album.Tracks.Items)
            {
                if (token.IsCancellationRequested) return;
                itemSource.Add(CreateSongSearchObject(track, album));
            }
        }
    }

    public static SongSearchObject ConvertSongSearchObject(Track track)
    {
        return new SongSearchObject()
        {
            AlbumName = track.Album.Title,
            Artists = track.Performer.Name,
            Duration = track.Duration.ToString(),
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.ToString(),
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
            ImageLocation = track.Album.Image.Small,
            LocalBitmapImage = null,
            Rank = "0",
            ReleaseDate = track.ReleaseDateOriginal.ToString(),
            Title = track.Title,
        };
    }

    public static SongSearchObject CreateSongSearchObject(Track track, Album album)
    {
        return new SongSearchObject()
        {
            AlbumName = album.Title,
            Artists = track.Performer.Name,
            Duration = track.Duration.ToString(),
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.ToString(),
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
            ImageLocation = album.Image.Small,
            LocalBitmapImage = null,
            Rank = "0",
            ReleaseDate = track.ReleaseDateOriginal.ToString(),
            Title = track.Title,
        };
    }

    public static Track GetQobuzTrack(string id)
    {
        return apiService.GetTrack(id);
    }

    public static SongSearchObject GetTrack(string id)
    {
        return ConvertSongSearchObject(apiService.GetTrack(id));
    }

    public static void TestSearch(string query)
    {
        var results = apiService.SearchTracks(query, 5);
        if (results.Tracks == null)
        {
            Debug.WriteLine("No results found");
        }
        else
        {
            foreach (var track in results.Tracks.Items)
            {
                Debug.WriteLine(track.Title + " " + track.ReleaseDateOriginal.ToString() + " " + track.Album.Image.Small);
            }
        }
    }

    public static async Task GetTrackTest(string id)
    {
        var fileUrl = apiService.GetTrackFileUrl(id, "27");
        Debug.WriteLine("fileURL: " + fileUrl.Url);
        await DownloadFileAsync(fileUrl.Url, "E:\\Other Downloads\\test\\test.flac");
        //var trackBytes = await new HttpClient().GetByteArrayAsync(fileUrl.Url);
        //await File.WriteAllBytesAsync("E:\\Other Downloads\\test\\test.flac", trackBytes);
    }

    // For any file downloading with progress
    public static async Task DownloadFileAsync(string downloadUrl, string filePath)
    {
        var httpClient = new HttpClient();
        using (Stream streamToReadFrom = await httpClient.GetStreamAsync(downloadUrl))
        {
            using (FileStream streamToWriteTo = System.IO.File.Create(filePath))
            {
                long totalBytesRead = 0;
                Stopwatch stopwatch = Stopwatch.StartNew();
                byte[] buffer = new byte[32768]; // 32KB buffer size
                bool firstBufferRead = false;

                int bytesRead;
                while ((bytesRead = await streamToReadFrom.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    // Write only the minimum of buffer.Length and bytesRead bytes to the file
                    await streamToWriteTo.WriteAsync(buffer, 0, Math.Min(buffer.Length, bytesRead));

                    // Calculate download speed
                    totalBytesRead += bytesRead;
                    double speed = totalBytesRead / 1024d / 1024d / stopwatch.Elapsed.TotalSeconds;

                    // Update with the current speed at download start and then max. every 500 ms
                    if (!firstBufferRead || stopwatch.ElapsedMilliseconds >= 500)
                    {
                        // TODO: place this on some UI: ($"Downloading... {speed:F3} MB/s");
                        Debug.WriteLine($"Downloading... {speed:F3} MB/s");
                    }

                    firstBufferRead = true;
                }
            }
        }
    }
}