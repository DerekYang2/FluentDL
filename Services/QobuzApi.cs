using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ABI.Windows.Media.Core;
using FluentDL.Models;
using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Service;

namespace FluentDL.Services;

internal class QobuzApi
{
    private static QobuzApiService apiService = new QobuzApiService();
    private static bool loggedIn = false;

    public static void Initialize(string? userId, string? AuthToken)
    {
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(AuthToken))
        {
            apiService.LoginWithToken(userId, AuthToken);
            loggedIn = true;
        }
    }

    public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token)
    {
        var isTrack = url.StartsWith("https://play.qobuz.com/track/") || url.StartsWith("https://open.qobuz.com/track/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/track/.*");
        var isAlbum = url.StartsWith("https://play.qobuz.com/album/") || url.StartsWith("https://open.qobuz.com/album/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/album/.*");
        var isPlaylist = url.StartsWith("https://play.qobuz.com/playlist/") || url.StartsWith("https://open.qobuz.com/playlist/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/playlist/.*"); // Remove any query parameters

        url = url.Split('?')[0]; // Remove any query parameters
        var id = url.Split('/').Last(); // Get string after the last slash

        if (isTrack)
        {
            var track = apiService.GetTrack(id);
            itemSource.Add(ConvertSongSearchObject(track));
        }

        if (isAlbum)
        {
            var album = await Task.Run(() => apiService.GetAlbum(id), token);

            if (album.Tracks != null)
            {
                itemSource.Clear(); // Clear the item source
                foreach (var track in album.Tracks.Items)
                {
                    if (token.IsCancellationRequested) return;
                    itemSource.Add(CreateSongSearchObject(track, album));
                }
            }
        }

        if (isPlaylist)
        {
            var playlist = await Task.Run(() => apiService.GetPlaylist(id, withAuth: loggedIn), token);

            if (playlist.Tracks != null)
            {
                itemSource.Clear(); // Clear the item source
                foreach (var track in playlist.Tracks.Items) // Need to recreate the tracks so they have album objects
                {
                    if (token.IsCancellationRequested) return;
                    Debug.WriteLine(track.Id);
                    itemSource.Add(ConvertSongSearchObject(apiService.GetTrack(track.Id.ToString())));
                }
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
            Isrc = track.Isrc
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
            Isrc = track.Isrc
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

    public static Uri GetPreviewUri(string trackId)
    {
        return new Uri(apiService.GetTrackFileUrl(trackId, "5").Url);
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