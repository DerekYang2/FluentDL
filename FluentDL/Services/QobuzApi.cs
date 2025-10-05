using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Service;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace FluentDL.Services;

internal partial class QobuzApi
{
    private static QobuzApiService apiService = new QobuzApiService();
    private static HttpClient QobuzHttpClient = new HttpClient();

    private static string GetFullTitle(Track? track)
    {
        if (track == null) 
            return "";

        if (string.IsNullOrWhiteSpace(track.Version))
            return track.Title;

        return $"{track.Title} ({track.Version})";
    }


    public static bool IsInitialized = false;
    public static string oldI = "VuCHDsuyiFjcl994xa1eyg==";
    public static string oldS = "5mLYFjeXUrtSoZvPIYn7ymMz6QQY65+XBg2OBH9cxLJlT9hMiDIrRB8Yj4OfOikn";

    public static void Initialize(string? email, string? password, string? userId, string? AuthToken, AuthenticationCallback? authCallback = null)
    {
        IsInitialized = false;
        bool oldInitialization = false;

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(AuthToken))  // Token route
        {
            try
            {
                apiService = new QobuzApiService();
                apiService.LoginWithToken(userId, AuthToken);
                IsInitialized = true;
                Debug.WriteLine("Qobuz initialized");
                authCallback?.Invoke(IsInitialized);

            }
            catch (Exception e)
            {
                Debug.WriteLine("Qobuz initialization failed: " + e.Message);

                // Try intialize using old app credentials
                try
                {
                    apiService = new QobuzApiService(AesHelper.Decrypt(oldI), AesHelper.Decrypt(oldS));
                    apiService.LoginWithToken(userId, AuthToken);
                    IsInitialized = true;
                    oldInitialization = true;
                    Debug.WriteLine("Qobuz initialized (old)");
                    authCallback?.Invoke(IsInitialized);
                }
                catch (Exception e2)
                {
                    Debug.WriteLine("Qobuz (old) initialization failed: " + e2.Message);
                    authCallback?.Invoke(false);
                }
            }
        }

        if (!IsInitialized && !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))  // Email route
        {
            try
            {
                apiService = new QobuzApiService();
                apiService.LoginWithEmail(email, password);
                IsInitialized = true;
                Debug.WriteLine("Qobuz initialized email");
                authCallback?.Invoke(IsInitialized);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Qobuz initialization failed (email): " + e.Message);
                authCallback?.Invoke(false);
            }
        }

        // If still not initialized
        if (!IsInitialized) {
            authCallback?.Invoke(false);
        }

        if (oldInitialization)
        {
           var defaultService = new QobuzApiService();
            InitializeQobuzHttpClient(defaultService.AppId, defaultService.UserAuthToken);
        }
        else
        {
            InitializeQobuzHttpClient(apiService.AppId, apiService.UserAuthToken);
        }
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25, bool albumMode = false)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        itemSource.Clear(); // Clear the item source

        try
        {
            if (albumMode)
            {   
                await foreach (var album in ApiSearch<Album>(query, limit))
                {
                    if (token.IsCancellationRequested) return;
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                }
            }
            else
            {
                await foreach (var track in ApiSearch<Track>(query, limit))
                {
                    if (token.IsCancellationRequested) return;
                    var song = ConvertSongSearchObject(track);
                    if (song != null)
                    {
                        itemSource.Add(song);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("General search failed: " + e.Message);
        }
    }

    private static bool CloseMatch(string str1, string str2)
    {
        return ApiHelper.IsSubstring(str1.ToLower(), str2.ToLower());
    }

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int limit = 25)
    {
        // Qobuz doesn't have an advanced search, must be done manually
        artistName = artistName.Trim();
        trackName = trackName.Trim();
        albumName = albumName.Trim();

        if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(albumName))
        {
            return;
        }

        itemSource.Clear(); // Clear the item source

        bool isArtistSpecified = !string.IsNullOrWhiteSpace(artistName);
        bool isTrackSpecified = !string.IsNullOrWhiteSpace(trackName);

        var trackIdList = new HashSet<int>();

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            var albumResults = await Task.Run(() => apiService.SearchAlbums(albumName, 5, withAuth: true), token);

            if (token.IsCancellationRequested) return; // Check if task is cancelled

            // Add if album name match
            foreach (var album in albumResults.Albums.Items)
            {
                if (token.IsCancellationRequested) return; // Check if task is cancelled

                // Ensure artist matches to quickly filter out irrelevant albums
                var oneArtistMatch = false;
                if (isArtistSpecified)
                {
                    foreach (var artist in album.Artists) // For every artist in album
                    {
                        if (CloseMatch(artistName, artist.Name))
                        {
                            oneArtistMatch = true;
                            break;
                        }
                    }
                }
                else
                {
                    oneArtistMatch = true; // Assume true if artist not specified
                }

                if (oneArtistMatch && CloseMatch(albumName, album.Title))
                {
                    var fullAlbumObj = await Task.Run(() => apiService.GetAlbum(album.Id, withAuth: true), token);

                    if (fullAlbumObj == null || fullAlbumObj.Tracks == null || fullAlbumObj.Tracks.Items.Count == 0) continue;

                    foreach (var track in fullAlbumObj.Tracks.Items)
                    {
                        if (track.Id == null) continue;

                        bool valid = true;
                        if (isArtistSpecified) // Album and artist specified
                        {
                            var trackArtists = GetAllContributorsList(track.Performers);
                            oneArtistMatch = trackArtists.Any(x => CloseMatch(artistName, x)); // AT least one artist match

                            if (isTrackSpecified) // Album, artist, and track specified
                            {
                                valid = oneArtistMatch && CloseMatch(trackName, GetFullTitle(track));
                            }
                            else // Album and artist specified
                            {
                                valid = oneArtistMatch; // Different case for validity
                            }
                        }
                        else if (isTrackSpecified) // Track name and artist specified
                        {
                            valid = CloseMatch(trackName, GetFullTitle(track));
                        }

                        var id = track.Id.GetValueOrDefault();

                        if (valid && !trackIdList.Contains(id)) // Check if track is already added
                        {
                            var trackObj = await GetTrackAsync(id);
                            if (trackObj == null) 
                                continue; 
                            itemSource.Add(trackObj);
                            trackIdList.Add(id);
                            if (trackIdList.Count >= limit) 
                                break;
                        }
                    }
                }

                if (trackIdList.Count >= limit) break;
            }
        }
        else
        {
            if (isTrackSpecified && isArtistSpecified) // If artist and track are specified
            {
                await foreach (var track in ApiSearch<Track>(artistName + " " + trackName, limit))
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled
                    if (track.Id == null) continue;

                    var trackArtists = GetAllContributorsList(track.Performers);

                    // At least one artist close match
                    if (trackArtists.Any(x => CloseMatch(artistName, x)))
                    {
                        var id = track.Id.GetValueOrDefault();
                        if (!trackIdList.Contains(id)) // Add this track to the item source
                        {
                            itemSource.Add(ConvertSongSearchObject(track));
                            trackIdList.Add(id);
                        }
                    }
                }
            }
            else
            {
                var query = isTrackSpecified ? trackName : artistName; 
                var searchType = isTrackSpecified ? EnumQobuzSearchType.ByReleaseName : EnumQobuzSearchType.ByMainArtist;
                await foreach (var track in ApiSearch<Track>(query, limit, searchType))
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled
                    if (track.Id == null) continue;

                    var id = track.Id.GetValueOrDefault();
                    if (!trackIdList.Contains(id)) // Add this track to the item source
                    {
                        itemSource.Add(ConvertSongSearchObject(track));
                        trackIdList.Add(id);
                    }
                }
            }
        }
    }

    public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> itemSource, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
    {
        var isTrack = url.StartsWith("https://play.qobuz.com/track/") || url.StartsWith("https://open.qobuz.com/track/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/track/.*");
        var isAlbum = url.StartsWith("https://play.qobuz.com/album/") || url.StartsWith("https://open.qobuz.com/album/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/album/.*");
        var isPlaylist = url.StartsWith("https://play.qobuz.com/playlist/") || url.StartsWith("https://open.qobuz.com/playlist/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/playlist/.*"); // Remove any query parameters

        url = url.Split('?')[0]; // Remove any query parameters
        var id = url.Split('/').Last(); // Get string after the last slash

        if (isTrack)
        {
            var track = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetTrack(id, withAuth: true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz track: " + e.Message);
                    return null;
                }
            }, token);

            if (track != null) {
                itemSource.Add(await Task.Run(() => ConvertSongSearchObject(track)));
                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded track <a href='{url}'>{GetFullTitle(track)}</a>"); 
            }
            else
            {
                statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>Qobuz</b>   Track may not exist or is private.");
            }
        }
        else if (isAlbum)
        {
            var album = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetAlbum(id, withAuth: true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz album: " + e.Message);
                    return null;
                }
            }, token);

            if (album != null && album.Tracks != null && album.Tracks.Items.Count > 0)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Qobuz</b>   Loading album <a href='{url}'>{album.Title}</a>", -1); // Show an informational message

                if (albumMode)
                {
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                }
                else
                {
                    itemSource.Clear(); // Clear the item source
                    foreach (var track in album.Tracks.Items)
                    {
                        if (token.IsCancellationRequested)
                        {
                            statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Qobuz</b>   Cancelled loading album <a href='{url}'>{album.Title}</a>"); // Show a warning message
                            return;
                        }

                        itemSource.Add(CreateSongSearchObject(track, album));
                    }
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded album <a href='{url}'>{album.Title}</a>"); // Show a success message
            }
            else
            {
                statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>Qobuz</b>   Album may not exist or is private.");
            }
        }
        else if (isPlaylist)
        {
            Playlist? playlist = null;
            try
            {
                playlist = await GetPlaylistAsync(id, token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to get qobuz playlist: " + e.Message);
            }

            if (playlist?.TrackIds != null && (playlist.TrackIds?.Count ?? 0) > 0)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Qobuz</b>   Loading playlist <a href='{url}'>{playlist.Name}</a>", -1); // Show an informational message
                itemSource.Clear(); // Clear the item source

                int failedCount = 0; // Count of failed tracks
                foreach (var trackId in playlist.TrackIds) // Need to recreate the tracks so they have album objects
                {
                    if (token.IsCancellationRequested)
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Qobuz</b>   Cancelled loading playlist <a href='{url}'>{playlist.Name}</a>"); // Show a warning message
                        return;
                    }

                    var track = await Task.Run(() =>
                    {
                        try
                        {
                            return apiService.GetTrack(trackId.ToString(), withAuth: true);
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine($"Failed to get qobuz track: {trackId}");
                            failedCount++;
                            return null;
                        }
                    });

                    if (track != null)
                    {
                        itemSource.Add(ConvertSongSearchObject(track));
                    }
                }

                if (failedCount == 0)
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded playlist <a href='{url}'>{playlist.Name}</a>"); // Show a success message
                }
                else
                {
                    statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Qobuz</b>   Loaded playlist <a href='{url}'>{playlist.Name}</a>, with {failedCount} track{(failedCount > 1 ? "s" : "")} failing to load."); // Show a warning message
                }
            }
            else
            {
                statusUpdate?.Invoke(InfoBarSeverity.Error, $"<b>Qobuz</b>   Playlist may not exist or is private.");
            }
        }
    }

    public static SongSearchObject ConvertSongSearchObject(Track track, string? mainArtist = null)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrWhiteSpace(mainArtist)) // If no contributors, fallback
        {
            contribList = [mainArtist];
        }   
        contribList ??= [];

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = track.Album?.Title ?? "",
            Artists = string.Join(", ", contribList),
            Duration = track.Duration?.ToString() ?? "0",
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.ToString(),
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
            ImageLocation = track.Album?.Image?.Small ?? "",
            LocalBitmapImage = null,
            Rank = (track.Album?.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = GetFullTitle(track),
            Isrc = track.Isrc
        };
    }

    public static SongSearchObject CreateSongSearchObject(Track track, Album album)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrEmpty(album.Artist?.Name))
        {
            contribList = [album.Artist.Name];
        }
        contribList ??= [];
        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = album.Title,
            Artists = string.Join(", ", contribList),
            Duration = track.Duration?.ToString() ?? "0",
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.GetValueOrDefault().ToString(),
            TrackPosition = track.TrackNumber.GetValueOrDefault().ToString(),
            ImageLocation = album.Image.Small,
            LocalBitmapImage = null,
            Rank = (album.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = GetFullTitle(track),
            Isrc = track.Isrc
        };
    }

    public static AlbumSearchObject ConvertAlbumSearchObject(Album album)
    {
        var mainArtist = album.Artist.Name;
        var artists = album.Artists.Select(a => a.Name).ToList();

        if (artists.Count == 0)
        {
            artists.Add("unlisted");
        }

        // Ensure main artist appears first
        artists.Remove(mainArtist);
        artists.Insert(0, mainArtist);

        return new AlbumSearchObject()
        {
            AlbumName = album.Title,
            Artists = string.Join(", ", artists),
            Duration = album.Duration?.ToString() ?? "0",
            Explicit = album.ParentalWarning ?? false,
            Source = "qobuz",
            Id = album.Id.ToString(),
            TrackPosition = "1", // Not applicable for albums
            ImageLocation = album.Image?.Small ?? "",
            LocalBitmapImage = null,
            Rank = (album.Popularity ?? 0).ToString(),
            ReleaseDate = album.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = album.Title,
            Isrc = album.Upc,
            TracksCount = album.TracksCount ?? 0,
        };
    }

    public static async Task<Album> GetInternalAlbum(string id)
    {
        return await Task.Run(() => apiService.GetAlbum(id, withAuth: true));
    }

    public static async Task<Track> GetInternalTrack(string id)
    {
        return await Task.Run(() => apiService.GetTrack(id, withAuth: true));
    }

    public static async Task<SongSearchObject?> GetTrackAsync(int? id, CancellationToken token = default)
    {
        if (id == null) return await Task.FromResult<SongSearchObject?>(null);
        return await GetTrackAsync(id.ToString(), token);
    }

    public static async Task<SongSearchObject?> GetTrackAsync(string? id, CancellationToken token = default)
    {
        if (id == null) return null;
        var track = await Task.Run(() => apiService.GetTrack(id, withAuth: true), token);
        if (track == null)
        {
            return null;
        }

        return ConvertSongSearchObject(track);
    }

    public static async Task<SongSearchObject?> GetQobuzTrack(SongSearchObject songObj, CancellationToken token = default, ConversionUpdateCallback? callback = null, bool onlyISRC = false)
    {
        // No built-in method for this, so we have to get all tracks and search for the ISRC
        string? isrc = songObj.Isrc;
        string query = songObj.Artists.Split(", ")[0] + " " + songObj.Title;

        if (isrc != null)
        {
            await foreach (var track in ApiSearch<Track>(query, 50))
            {
                if (token.IsCancellationRequested) return null;

                if (track.Isrc == isrc)
                {
                    var retObj = ConvertSongSearchObject(track);
                    callback?.Invoke(InfoBarSeverity.Success, retObj); // Show a success message
                    return retObj;
                }
            }
        }

        if (onlyISRC) // If only ISRC is allowed, return null
        {
            callback?.Invoke(InfoBarSeverity.Error, songObj); // Show an error message with original object
            return null;
        }

        // BELOW: try matching by metadata
        // Convert artists csv to array
        var artists = songObj.Artists.Split(", ");
        for (int i = 0; i < artists.Length; i++)
        {
            artists[i] = artists[i].ToLower();
        }


        var albumName = songObj.AlbumName;
        var trackName = songObj.Title;

        // Search by album
        var albumResults = await Task.Run(() => apiService.SearchAlbums(albumName, 5, withAuth: true), token);

        if (token.IsCancellationRequested) return null; // Check if task is cancelled

        foreach (var album in albumResults.Albums.Items)
        {
            if (token.IsCancellationRequested) return null; // Check if task is cancelled

            // Ensure artist matches before checking tracks
            var oneArtistMatch = false;
            foreach (var artist in artists) // For every artist in SongObject
            {
                foreach (var albumArtist in album.Artists) // For every artist in album
                {
                    if (CloseMatch(artist, albumArtist.Name))
                    {
                        oneArtistMatch = true;
                        break;
                    }
                }
            }

            if (oneArtistMatch && CloseMatch(albumName, album.Title)) // Album close match
            {
                var fullAlbumObj = await Task.Run(() => apiService.GetAlbum(album.Id, withAuth: true), token);

                if (fullAlbumObj == null || fullAlbumObj.Tracks == null || fullAlbumObj.Tracks.Items.Count == 0) continue;

                foreach (var track in fullAlbumObj.Tracks.Items)
                {
                    if (track.Id == null) continue;

                    oneArtistMatch = false;
                    foreach (var artist in artists) // For every artist in SongObject
                    {
                        var a1 = ApiHelper.PrunePunctuation(artist.ToLower());
                        var performerPrune = ApiHelper.PrunePunctuation(track.Performers?.ToLower() ?? "");
                        if (performerPrune.Contains(a1)) // Check if artist is in performers
                        {
                            oneArtistMatch = true;
                            break;
                        }
                    }

                    if (oneArtistMatch && CloseMatch(trackName, GetFullTitle(track))) // Album close match, artist match, title close match
                    {
                        var retObj = await GetTrackAsync(track.Id);
                        if (retObj != null)
                        {
                            callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                            return retObj;
                        }
                    }
                }
            }
        }

        // Try searching without album, same as above
        if (token.IsCancellationRequested) return null; // Check if task is cancelled

        await foreach (var result in ApiSearch<Track>(query, 28))
        {
            if (token.IsCancellationRequested) return null;

            // Check if at least one pair of artists match
            foreach (var artist in artists)
            {
                var a1 = ApiHelper.PrunePunctuation(artist.ToLower());
                var performerPrune = ApiHelper.PrunePunctuation(result.Performers.ToLower());
                if (performerPrune.Contains(a1) && ApiHelper.PrunePunctuation(trackName.ToLower()).Equals(ApiHelper.PrunePunctuation(GetFullTitle(result).ToLower())))
                {
                    var retObj = ConvertSongSearchObject(result);
                    callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                    return retObj;
                }
            }
        }

        callback?.Invoke(InfoBarSeverity.Error, songObj); // Show an error message with original object
        return null;
    }

    public static string? GetPreviewUri(string trackId)
    {
        return apiService.GetTrackFileUrl(trackId, "5").Url;
    }

    public static List<string> GetAllContributorsList(string performerStr)
    {
        if (string.IsNullOrWhiteSpace(performerStr))  // Issue with QobuzAPI, should be nullable but is not
        {
            return new List<string>();
        }

        var performers = performerStr
            .Split(new string[] { " - " }, StringSplitOptions.None)
            .Select(performer => performer.Split(',')) // Split name & roles in best effort by ',', first part is name, next parts roles
            .GroupBy(parts => parts[0].Trim()) // Group performers by name since they can occur multiple times
            .ToDictionary(group => group.Key, group => group.SelectMany(parts => parts.Skip(1).Select(role => role.Trim())).Distinct().ToList()); // Flatten roles by performer and remove duplicates
        var artistRoles = new HashSet<string>
        {
            "MainArtist",
            "main-artist",
            "Performer",
            "FeaturedArtist",
            "Featuring",
            "featured-artist"
        };

        var artistList = new SortedSet<string>();

        foreach (var performer in performers) // For every performer
        {
            var name = performer.Key;
            var roles = performer.Value; // List of the roles for this person

            if (roles.Any(role => artistRoles.Contains(role))) // If at least one role is an artist role
            {
                artistList.Add(name);
            }
        }

        return artistList.ToList();
    }

    public static async Task<string> DownloadTrack(string filePath, SongSearchObject song, string? format = null)
    {
        if (!IsInitialized)
        {
            throw new Exception("Not logged in");
        }

        // Remove extension if it exists
        filePath = ApiHelper.RemoveExtension(filePath);

        if (format == null) // If format is not specified, get it from the settings
        {
            // Get the format from settings
            var selectedIndex = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.QobuzQuality) ?? 0;

            format = selectedIndex switch
            {
                0 => "5",
                1 => "6",
                2 => "7",
                3 => "27",
                _ => "6" // Flac, 16/44.1 as default
            };
        }

        // Add the extension
        filePath += "." + (format == "5" ? "mp3" : "flac"); // Only one format is mp3

        if (System.IO.File.Exists(filePath) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false)
        {
            throw new Exception("File already exists");
        }

        var fileUrl = apiService.GetTrackFileUrl(song.Id, format);
        await ApiHelper.DownloadFileAsync(filePath, fileUrl.Url);
        return filePath;
        //var trackBytes = await new HttpClient().GetByteArrayAsync(fileUrl.Url);
        //await File.WriteAllBytesAsync(filePath, trackBytes);
    }

    /**
     * Qobuz genre list sometimes includes sub-genres separately, such as
     * Musiques du monde | Musiques du monde→Europe | Musiques du monde→Europe→Chanson française
     */
    public static List<string> PruneGenreList(List<string> rawGenreList)
    {
        var genreList = new List<string>();
        foreach (var genre in rawGenreList)
        {
            var genreArray = genre.Split("\u2192");
            genreList.Add(genreArray.Last().Trim());
        }

        return genreList.ToList();
    }

    public static async Task UpdateMetadata(string filePath, string trackId)
    {
        var track = await GetInternalTrack(trackId);

        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);
        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrWhiteSpace(track.Album?.Artist?.Name))
        {
            contribList = [track.Album.Artist.Name];
        }

        contribList ??= [];

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        var metadata = new MetadataObject(filePath)
        {
            Title = GetFullTitle(track),
            Artists = contribList.ToArray(),
            Genres = PruneGenreList(track?.Album?.GenresList ?? []).ToArray(),
            AlbumName = track.Album?.Title ?? "",
            AlbumArtists = track.Album?.Artists?.Select(x => x.Name)?.ToArray() ?? [],
            Isrc = track.Isrc,
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().Date,
            TrackNumber = track.TrackNumber.GetValueOrDefault(),
            TrackTotal = track.Album?.TracksCount ?? 0,
            Upc = track.Album?.Upc ?? "",
            AlbumArtPath = track.Album?.Image.Large ?? "",
        };
        await metadata.SaveAsync();

        /*
        var tfile = TagLib.File.Create(filePath);
        tfile.Mode = File.AccessMode.Write;

        TagLib.Ogg.XiphComment custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

        tfile.Tag.Title = track.Title;
        tfile.Tag.Album = track.Album.Title;
        //atlTrack.AlbumArtist = albumArtistStr;
        tfile.Tag.Performers = contribList.ToArray();

        // Release Year tag (The "tfile.Tag.Year" field actually writes to the DATE tag, so use custom tag)
        custom.SetField("YEAR", releaseDateString.Substring(0, 4));

        // Release Date tag
        custom.SetField("DATE", releaseDateString);


        tfile.Tag.Track = Convert.ToUInt32(track.TrackNumber);
        // Override TRACKNUMBER tag again to prevent using "two-digit zero-filled value"
        // See https://github.com/mono/taglib-sharp/pull/240 where this change was introduced in taglib-sharp v2.3
        custom.SetField("TRACKNUMBER", Convert.ToUInt32(track.TrackNumber));

        tfile.Tag.TrackCount = Convert.ToUInt32(track.Album.TracksCount);
        //atlTrack.Genre = genreStr;
        tfile.Tag.ISRC = track.Isrc;

        custom.SetField("UPC", track.Album.Upc);

        // Get image bytes for cover art
        var imageBytes = await new HttpClient().GetByteArrayAsync(track.Album.Image.Large);

        // Define cover art to use for FLAC file(s)
        TagLib.Id3v2.AttachmentFrame pic = new TagLib.Id3v2.AttachmentFrame { TextEncoding = TagLib.StringType.Latin1, MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg, Type = TagLib.PictureType.FrontCover, Data = new ByteVector(imageBytes) };

        // Save cover art to FLAC file.
        tfile.Tag.Pictures = new TagLib.IPicture[1] { pic };


        tfile.Save();
        */
    }
}