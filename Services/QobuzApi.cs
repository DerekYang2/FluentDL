using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.Windows.Media.Core;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
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

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var results = await Task.Run(() => apiService.SearchTracks(query, limit), token);

        if (results == null || results.Tracks == null)
        {
            return;
        }

        itemSource.Clear(); // Clear the item source
        foreach (var track in results.Tracks.Items)
        {
            if (token.IsCancellationRequested) return;
            itemSource.Add(await Task.Run(() => GetTrack(track.Id.ToString()), token));
        }
    }

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int limit = 25)
    {
        // Qobuz doesn't have an advanced search, must be done manually
        artistName = artistName.Trim();
        trackName = trackName.Trim();
        albumName = albumName.Trim();
        bool isArtistSpecified = !string.IsNullOrWhiteSpace(artistName);
        bool isTrackSpecified = !string.IsNullOrWhiteSpace(trackName);

        var trackIdList = new HashSet<long>();

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            var albumResults = await Task.Run(() => apiService.SearchAlbums(albumName, 5), token);

            // Add if album name match
            foreach (var album in albumResults.Albums.Items)
            {
                if (ApiHelper.IsSubstring(albumName.ToLower(), album.Title.ToLower()))
                {
                    var fullAlbumObj = apiService.GetAlbum(album.Id);

                    foreach (var track in fullAlbumObj.Tracks.Items)
                    {
                        bool valid = true;
                        if (isArtistSpecified) // Album and artist specified
                        {
                            if (isTrackSpecified) // Album, artist, and track specified
                            {
                                valid = ApiHelper.IsSubstring(artistName.ToLower(), track.Performer.Name.ToLower()) && ApiHelper.IsSubstring(trackName.ToLower(), track.Title.ToLower());
                            }
                            else // Album and artist specified
                            {
                                valid = ApiHelper.IsSubstring(artistName.ToLower(), track.Performer.Name.ToLower()); // Different case for validity
                            }
                        }
                        else if (isTrackSpecified) // Track name and artist specified
                        {
                            valid = ApiHelper.IsSubstring(trackName.ToLower(), track.Title.ToLower());
                        }

                        if (valid)
                        {
                            trackIdList.Add((long)track.Id);
                            if (trackIdList.Count >= limit) break;
                        }
                    }
                }

                if (trackIdList.Count >= limit) break;
            }
        }
        else
        {
            if (isTrackSpecified) // If artist and track are specified
            {
                var offset = 0;

                do // Iterate through all tracks of the artist
                {
                    var result = await Task.Run(() => apiService.SearchTracks(artistName + " " + trackName, 10, offset), token);

                    if (result.Tracks != null)
                    {
                        foreach (var track in result.Tracks.Items)
                        {
                            offset++;
                            // Check if artist name and track somewhat match
                            if (ApiHelper.IsSubstring(artistName.ToLower(), track.Performer.Name.ToLower()) && ApiHelper.IsSubstring(trackName.ToLower(), track.Title.ToLower()))
                            {
                                trackIdList.Add(track.Id.GetValueOrDefault());
                                if (trackIdList.Count >= limit) break;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (trackIdList.Count < limit && offset <= Math.Max((int)(1.5 * limit), 50));
            }
            else // Only artist specified, do a general search
            {
                var offset = 0;

                do
                {
                    var result = await Task.Run(() => apiService.SearchTracks(artistName, 10, offset), token);
                    if (result.Tracks != null)
                    {
                        foreach (var track in result.Tracks.Items)
                        {
                            offset++;
                            // Check if artist name match
                            if (ApiHelper.IsSubstring(artistName.ToLower(), track.Performer.Name.ToLower()))
                            {
                                trackIdList.Add(track.Id.GetValueOrDefault());
                                if (trackIdList.Count >= limit) break;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (trackIdList.Count < limit);
            }
        }

        /*
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            var artistResults = await Task.Run(() => apiService.SearchArtists(artistName, 5), token);

            if (artistResults.Artists != null)
            {
                foreach (var artist in artistResults.Artists.Items) // Iterate through the top 5 artist results
                {
                    Debug.WriteLine("ARTIST: " + artist.Name);
                    if (ApiHelper.PrunePunctuation(artistName.ToLower()) == ApiHelper.PrunePunctuation(artist.Name.ToLower())) // Check if artist name match (TODO: would substr be better?)
                    {
                        // If album is specified, check if artist has this album
                        var artistFullObject = await Task.Run(() => apiService.GetArtist(artist.Id.ToString()), token);
                        var albumList = artistFullObject.Albums.Items;
                        if (!string.IsNullOrWhiteSpace(albumName))
                        {
                            foreach (var album in albumList)
                            {
                                if (ApiHelper.IsSubstring(albumName.ToLower(), album.Title.ToLower())) // Check if album name match or close match
                                {
                                    foreach (var track in album.Tracks.Items) // Add all tracks from the album
                                    {
                                        // Check if track is specified
                                        if (isTrackSpecified)
                                        {
                                            if (ApiHelper.IsSubstring(trackName.ToLower(), track.Title.ToLower())) // Check if track name match or close match
                                            {
                                                trackIdList.Add(track.Id.GetValueOrDefault());
                                            }
                                        }
                                        else // Nothing specified
                                        {
                                            trackIdList.Add(track.Id.GetValueOrDefault());
                                        }
                                    }
                                }
                            }
                        }
                        else // No specified album
                        {
                            if (isTrackSpecified) // If artist and track are specified
                            {
                                var offset = 0;

                                do // Iterate through all tracks of the artist
                                {
                                    var result = await Task.Run(() => apiService.SearchTracks(artistName + " " + trackName, 10, offset), token);

                                    if (result.Tracks != null)
                                    {
                                        foreach (var track in result.Tracks.Items)
                                        {
                                            offset++;
                                            // Check if artist name and track somewhat match
                                            if (ApiHelper.IsSubstring(artist.Name.ToLower(), track.Performer.Name.ToLower()) && ApiHelper.IsSubstring(trackName.ToLower(), track.Title.ToLower()))
                                            {
                                                trackIdList.Add(track.Id.GetValueOrDefault());
                                                if (trackIdList.Count >= limit) break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                } while (trackIdList.Count < limit);
                            }
                            else // Only artist specified, do a general search
                            {
                                var offset = 0;

                                do
                                {
                                    var result = await Task.Run(() => apiService.SearchTracks(artistName, 10, offset), token);
                                    if (result.Tracks != null)
                                    {
                                        foreach (var track in result.Tracks.Items)
                                        {
                                            offset++;
                                            // Check if artist name match
                                            if (ApiHelper.IsSubstring(artist.Name.ToLower(), track.Performer.Name.ToLower()))
                                            {
                                                trackIdList.Add(track.Id.GetValueOrDefault());
                                                if (trackIdList.Count >= limit) break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                } while (trackIdList.Count < limit);
                            }
                        }
                    }
                }
            }
        }
        else // No artist specified
        {
        }
        */

        itemSource.Clear(); // Clear the item source
        foreach (var trackId in trackIdList)
        {
            if (token.IsCancellationRequested) return;
            itemSource.Add(await Task.Run(() => GetTrack(trackId.ToString()), token));
        }
    }

    public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate)
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
            statusUpdate?.Invoke(InfoBarSeverity.Success, $"Loaded track \"{track.Title}\""); // Show a success message
        }

        if (isAlbum)
        {
            var album = await Task.Run(() => apiService.GetAlbum(id), token);

            if (album.Tracks != null)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"Loading album \"{album.Title}\" ..."); // Show an informational message
                itemSource.Clear(); // Clear the item source
                foreach (var track in album.Tracks.Items)
                {
                    if (token.IsCancellationRequested) return;
                    itemSource.Add(await Task.Run(() => CreateSongSearchObject(track, album), token));
                }
            }
        }

        if (isPlaylist)
        {
            var playlist = await Task.Run(() => apiService.GetPlaylist(id, withAuth: loggedIn), token);

            if (playlist.Tracks != null)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"Loading playlist \"{playlist.Name}\" ..."); // Show an informational message
                itemSource.Clear(); // Clear the item source
                foreach (var track in playlist.Tracks.Items) // Need to recreate the tracks so they have album objects
                {
                    if (token.IsCancellationRequested) return;
                    itemSource.Add(await Task.Run(() => ConvertSongSearchObject(apiService.GetTrack(track.Id.ToString())), token));
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
            Id = track.Id.GetValueOrDefault().ToString(),
            TrackPosition = track.TrackNumber.GetValueOrDefault().ToString(),
            ImageLocation = album.Image.Small,
            LocalBitmapImage = null,
            Rank = "0",
            ReleaseDate = FormatDateTimeOffset(track.ReleaseDateStream),
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

    public static SongSearchObject? GetQobuzTrack(SongSearchObject songObj)
    {
        // No built-in method for this, so we have to get all tracks and search for the ISRC
        string? isrc = songObj.Isrc;

        string query = songObj.Artists.Split(", ")[0] + " " + songObj.Title;
        var result = apiService.SearchTracks(query);
        if (result.Tracks == null)
        {
            return null;
        }

        if (isrc != null)
        {
            foreach (var track in result.Tracks.Items)
            {
                if (track.Isrc == isrc)
                {
                    return ConvertSongSearchObject(track);
                }
            }
        }

        var searchResults = new List<SongSearchObject>();

        // TODO: a match similar to deezer general search
        return null;
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

    public static string FormatDateTimeOffset(DateTimeOffset? dateTimeOffset)
    {
        if (dateTimeOffset != null)
        {
            return dateTimeOffset.GetValueOrDefault().ToString("yyyy-MM-dd");
        }
        else
        {
            return "";
        }
    }
}