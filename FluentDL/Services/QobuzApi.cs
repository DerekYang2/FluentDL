using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ABI.Windows.Media.Core;
using AngleSharp.Text;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Service;
using TagLib;
using File = TagLib.File;
using Picture = TagLib.Flac.Picture;

namespace FluentDL.Services;

internal class QobuzApi
{
    private static QobuzApiService apiService = new QobuzApiService();
    public static bool IsInitialized = false;
    public static string oldI = "VuCHDsuyiFjcl994xa1eyg==";
    public static string oldS = "5mLYFjeXUrtSoZvPIYn7ymMz6QQY65+XBg2OBH9cxLJlT9hMiDIrRB8Yj4OfOikn";

    public static void Initialize(string? email, string? password, string? userId, string? AuthToken, AuthenticationCallback? authCallback = null)
    {
        IsInitialized = false;

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
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var results = await Task.Run(() => apiService.SearchTracks(query, limit, withAuth: true), token);

        if (results == null || results.Tracks == null)
        {
            return;
        }

        itemSource.Clear(); // Clear the item source
        foreach (var track in results.Tracks.Items)
        {
            if (token.IsCancellationRequested) return;
            itemSource.Add(await GetTrackAsync(track.Id, token));
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
                                valid = oneArtistMatch && CloseMatch(trackName, track.Title);
                            }
                            else // Album and artist specified
                            {
                                valid = oneArtistMatch; // Different case for validity
                            }
                        }
                        else if (isTrackSpecified) // Track name and artist specified
                        {
                            valid = CloseMatch(trackName, track.Title);
                        }

                        var id = track.Id.GetValueOrDefault();

                        if (valid && !trackIdList.Contains(id)) // Check if track is already added
                        {
                            itemSource.Add(await GetTrackAsync(id));
                            trackIdList.Add(id);
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
                    var result = await Task.Run(() => apiService.SearchTracks(artistName + " " + trackName, 10, offset, withAuth: true), token);

                    if (token.IsCancellationRequested) return; // Check if task is cancelled

                    if (result.Tracks != null && result.Tracks.Items.Count > 0)
                    {
                        foreach (var track in result.Tracks.Items)
                        {
                            if (token.IsCancellationRequested) return; // Check if task is cancelled
                            if (track.Id == null) continue;
                            offset++;

                            var trackArtists = GetAllContributorsList(track.Performers);
                            var oneArtistMatch = trackArtists.Any(x => CloseMatch(artistName, x));

                            // Check if artist name and track somewhat match
                            if (oneArtistMatch && CloseMatch(trackName, track.Title))
                            {
                                var id = track.Id.GetValueOrDefault();
                                if (!trackIdList.Contains(id)) // Add this track to the item source
                                {
                                    itemSource.Add(await GetTrackAsync(id));
                                    trackIdList.Add(id);
                                    if (trackIdList.Count >= limit) break;
                                }
                            }
                        }
                    }
                    else // No more tracks
                    {
                        break;
                    }
                } while (trackIdList.Count < limit && offset < limit); // Limit the number of iterations
            }
            else // Only artist specified, do a general search
            {
                var offset = 0;
                // Give a bit more leeway for general searches
                var maxTracks = Math.Max(2 * limit, 30); // A minimum of 30 tracks or twice the limit

                do
                {
                    var result = await Task.Run(() => apiService.SearchTracks(artistName, 10, offset, withAuth: true), token);
                    if (token.IsCancellationRequested) return; // Check if task is cancelled

                    if (result.Tracks != null && result.Tracks.Items.Count > 0)
                    {
                        foreach (var track in result.Tracks.Items)
                        {
                            if (token.IsCancellationRequested) return; // Check if task is cancelled
                            if (track.Id == null) continue;
                            offset++;

                            var trackArtists = GetAllContributorsList(track.Performers); // Get all contributors
                            // Check if at least one artist name match
                            if (trackArtists.Any(x => CloseMatch(artistName, x)))
                            {
                                var id = track.Id.GetValueOrDefault();

                                if (!trackIdList.Contains(id)) // Add this track to the item source
                                {
                                    itemSource.Add(await GetTrackAsync(id));
                                    trackIdList.Add(id);
                                    if (trackIdList.Count >= limit) break;
                                }

                                trackIdList.Add(track.Id.GetValueOrDefault());
                            }
                        }
                    }
                    else // No more tracks
                    {
                        break;
                    }
                } while (trackIdList.Count < limit && offset < maxTracks);
            }
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
            var track = apiService.GetTrack(id, withAuth: true);
            itemSource.Add(ConvertSongSearchObject(track));

            statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded track <a href=\"{url}\">{track.Title}</a>"); // Show a success message
        }
        else if (isAlbum)
        {
            var album = await Task.Run(() => apiService.GetAlbum(id, withAuth: true), token);

            if (album.Tracks != null && album.Tracks.Items.Count > 0)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Qobuz</b>   Loading album <a href='{url}'>{album.Title}</a>", -1); // Show an informational message
                itemSource.Clear(); // Clear the item source
                foreach (var track in album.Tracks.Items)
                {
                    if (token.IsCancellationRequested)
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Qobuz</b>   Cancelled loading album <a href='{url}'>{album.Title}</a>"); // Show a warning message
                        return;
                    }

                    itemSource.Add(await Task.Run(() => CreateSongSearchObject(track, album), token));
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded album <a href='{url}'>{album.Title}</a>"); // Show a success message
            }
        }
        else if (isPlaylist)
        {
            var playlist = await Task.Run(() => apiService.GetPlaylist(id, withAuth: true), token);

            if (playlist.Tracks != null && playlist.Tracks.Items.Count > 0)
            {
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Qobuz</b>   Loading playlist <a href='{url}'>{playlist.Name}</a>", -1); // Show an informational message
                itemSource.Clear(); // Clear the item source
                foreach (var track in playlist.Tracks.Items) // Need to recreate the tracks so they have album objects
                {
                    if (token.IsCancellationRequested)
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Qobuz</b>   Cancelled loading playlist <a href='{url}'>{playlist.Name}</a>"); // Show a warning message
                        return;
                    }

                    itemSource.Add(await Task.Run(() => ConvertSongSearchObject(apiService.GetTrack(track.Id.ToString(), withAuth: true)), token));
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Qobuz</b>   Loaded playlist <a href='{url}'>{playlist.Name}</a>"); // Show a success message
            }
        }
    }

    public static SongSearchObject ConvertSongSearchObject(Track track)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = track.Album.Title,
            Artists = string.Join(", ", contribList),
            Duration = track.Duration.ToString(),
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.ToString(),
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
            ImageLocation = track.Album.Image.Small,
            LocalBitmapImage = null,
            Rank = (track.Album.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = track.Title,
            Isrc = track.Isrc
        };
    }

    public static SongSearchObject CreateSongSearchObject(Track track, Album album)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = album.Title,
            Artists = string.Join(", ", contribList),
            Duration = track.Duration.ToString(),
            Explicit = track.ParentalWarning ?? false,
            Source = "qobuz",
            Id = track.Id.GetValueOrDefault().ToString(),
            TrackPosition = track.TrackNumber.GetValueOrDefault().ToString(),
            ImageLocation = album.Image.Small,
            LocalBitmapImage = null,
            Rank = (album.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = track.Title,
            Isrc = track.Isrc
        };
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

    public static SongSearchObject? GetTrack(int? id)
    {
        if (id == null) return null;
        return GetTrack(id.ToString());
    }

    public static SongSearchObject? GetTrack(string id)
    {
        var track = apiService.GetTrack(id, withAuth: true);
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
            var offset = 0;

            do
            {
                var result = await Task.Run(() => apiService.SearchTracks(query, 5, offset, withAuth: true), token); // Search through chunks of 5 tracks
                if (token.IsCancellationRequested) return null;

                if (result.Tracks == null || result.Tracks.Items.Count == 0)
                {
                    break;
                }

                foreach (var track in result.Tracks.Items)
                {
                    if (track.Isrc == isrc)
                    {
                        var retObj = ConvertSongSearchObject(track);
                        callback?.Invoke(InfoBarSeverity.Success, retObj); // Show a success message
                        return retObj;
                    }

                    offset++;
                }
            } while (offset < 50); // Limit the number of iterations
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

                    if (oneArtistMatch && CloseMatch(trackName, track.Title)) // Album close match, artist match, title close match
                    {
                        var retObj = await GetTrackAsync(track.Id);
                        callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                        return retObj;
                    }
                }
            }
        }

        // Try searching without album, same as above
        var searchResult = await Task.Run(() => apiService.SearchTracks(query, 10, withAuth: true), token);

        if (token.IsCancellationRequested) return null; // Check if task is cancelled

        if (searchResult.Tracks != null && searchResult.Tracks.Items.Count > 0)
        {
            foreach (var result in searchResult.Tracks.Items)
            {
                if (token.IsCancellationRequested) return null;

                // Check if at least one pair of artists match
                foreach (var artist in artists)
                {
                    var a1 = ApiHelper.PrunePunctuation(artist.ToLower());
                    var performerPrune = ApiHelper.PrunePunctuation(result.Performers.ToLower());
                    if (performerPrune.Contains(a1) && ApiHelper.PrunePunctuation(trackName.ToLower()).Equals(ApiHelper.PrunePunctuation(result.Title.ToLower())))
                    {
                        var retObj = ConvertSongSearchObject(result);
                        callback?.Invoke(InfoBarSeverity.Warning, retObj); // Not found by ISRC
                        return retObj;
                    }
                }
            }
        }


        callback?.Invoke(InfoBarSeverity.Error, songObj); // Show an error message with original object
        return null;
    }

    public static Uri GetPreviewUri(string trackId)
    {
        return new Uri(apiService.GetTrackFileUrl(trackId, "5").Url);
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

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        var metadata = new MetadataObject(filePath)
        {
            Title = track.Title,
            Artists = contribList.ToArray(),
            Genres = PruneGenreList(track.Album.GenresList).ToArray(),
            AlbumName = track.Album.Title,
            AlbumArtists = track.Album.Artists.Select(x => x.Name).ToArray(),
            Isrc = track.Isrc,
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().Date,
            TrackNumber = track.TrackNumber.GetValueOrDefault(),
            TrackTotal = track.Album.TracksCount,
            Upc = track.Album.Upc,
            AlbumArtPath = track.Album.Image.Large,
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