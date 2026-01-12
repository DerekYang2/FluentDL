using CommunityToolkit.WinUI.UI.Animations;
using DeezNET;
using DeezNET.Data;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Devices.PointOfService;
using Windows.Media.Streaming.Adaptive;

namespace FluentDL.Services;

// TODO: Handle null warnings and catch network errors

internal class DeezerApi
{
    public static readonly string baseURL = "https://api.deezer.com";
    private static DeezerClient deezerClient = new DeezerClient();
    public static RestHelper restClient = new RestHelper(baseURL, 5); // 5 seconds timeout
    public static bool IsInitialized = false;
    private static string? loginString = null;

    public static async Task InitDeezerClient(string? ARL, AuthenticationCallback? authCallback = null)
    {
        IsInitialized = false;
        loginString = null;

        if (!string.IsNullOrWhiteSpace(ARL))
        {
            try
            {
                deezerClient = new DeezerClient();
                await deezerClient.SetARL(ARL);
                var data = await deezerClient.GWApi.GetUserData();
    
                var userId = data?["USER"]?["USER_ID"]?.ToString() ?? "0";

                if (userId != "0")
                {
                    IsInitialized = true;

                    loginString = data?["OFFER_NAME"]?.ToString() ?? "Unknown Offer";

                    // Sound quality (is lossless/320kbps available)
                    var soundQualityOptions = data?["USER"]?["OPTIONS"]?["web_sound_quality"];

                    if (soundQualityOptions?.Value<bool>("lossless") ?? false)
                    {
                        authCallback?.Invoke(InfoBarSeverity.Success, "Log in success: premium account");
                        loginString += "\nLossless supported";
                    }
                    else
                    {
                        authCallback?.Invoke(InfoBarSeverity.Success, "Log in success: free account");
                        loginString += "\nOnly 128 kbps MP3 supported";
                    }
                    loginString += "\nCOUNTRY: " + data?["COUNTRY"]?.ToString() ?? "Unknown";
                }
                else
                {
                    authCallback?.Invoke(InfoBarSeverity.Error, "Log in failure: ARL invalid");
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                authCallback?.Invoke(InfoBarSeverity.Error, "Log in failure: " +  e.Message);
            }
        }
    }

    public static string? LoginString()
    {
        return loginString;
    }

    public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> list, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
    {
        if (url.StartsWith("https://deezer.page.link/") || url.StartsWith("https://dzr.page.link/") || url.StartsWith("https://link.deezer.com/"))
        {
            url = (await ApiHelper.GetRedirectedUrlAsync(new Uri(url)))?.AbsoluteUri ?? "";
            if (string.IsNullOrEmpty(url))
            {
                statusUpdate?.Invoke(InfoBarSeverity.Error, "Invalid Deezer URL");
                return;
            }
        }

        if (Regex.IsMatch(url, @"https://www\.deezer\.com(/[^/]+)?/track/.*"))
        {
            var firstQuestion = url.IndexOf('?');
            if (firstQuestion != -1)
            {
                url = url.Substring(0, firstQuestion);
            }

            if (url.Last() == '/') // Remove any trailing slash
            {
                url = url.Remove(url.Length - 1);
            }

            var trackId = url.Split('/').Last(); // Get the last part of the url
            var songObj = await GetTrack(trackId);
            if (songObj != null)
            {
                list.Add(songObj);
                // Send message to infobar
                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Deezer</b>   Loaded track <a href='{url}'>{songObj.Title}</a>");
            }
        }

        if (Regex.IsMatch(url, @"https://www\.deezer\.com(/[^/]+)?/(album|playlist)/.*"))
        {
            if (albumMode && url.Contains("/album/"))
            {
                var firstQuestion = url.IndexOf('?');
                if (firstQuestion != -1)
                {
                    url = url.Substring(0, firstQuestion);
                }

                if (url.Last() == '/') // Remove any trailing slash
                {
                    url = url.Remove(url.Length - 1);
                }

                var albumId = url.Split('/').Last(); // Get the last part of the url
                var albumObj = await GetAlbum(albumId);
                if (albumObj != null)
                {
                    list.Add(albumObj);
                    statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Deezer</b>   Loaded album <a href='{url}'>{albumObj.Title}</a>");
                }
            } else if (DeezerURL.TryParse(url, out var urlData))
            {
                var tracksInAlbum = await urlData.GetAssociatedTracks(deezerClient, 1000, token);

                // Send message to infobar
                var listName = await urlData.GetTitle(deezerClient);
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"<b>Deezer</b>   Loading {(url.Contains("/album/") ? "album" : "playlist")} <a href='{url}'>{listName}</a>", -1);

                list.Clear(); // Clear the item source for lists like playlist/albums

                foreach (var trackId in tracksInAlbum)
                {
                    if (token.IsCancellationRequested) // Stop the search
                    {
                        statusUpdate?.Invoke(InfoBarSeverity.Warning, $"<b>Deezer</b>   Cancelled loading {(url.Contains("/album/") ? "album" : "playlist")} <a href='{url}'>{listName}</a>");
                        return;
                    }

                    var songObj = await GetTrack(trackId.ToString());
                    if (songObj != null)
                    {
                        list.Add(songObj);
                    }
                }

                statusUpdate?.Invoke(InfoBarSeverity.Success, $"<b>Deezer</b>   Loaded {(url.Contains("/album/") ? "album" : "playlist")} <a href='{url}'>{listName}</a>");
            }
        }
    }


    private static bool CloseMatch(string str1, string str2)
    {
        return ApiHelper.IsSubstring(str1.ToLower(), str2.ToLower());
    }

    public static string PruneTitle(string title)
    {
        var titlePruned = title.ToLower().Trim();

        // Remove (feat. X) from the title
        var index = titlePruned.IndexOf("(feat.");
        if (index != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index);
            titlePruned = titlePruned.Remove(index, closingIndex - index + 1);
        }

        // Remove (ft. X) from the title
        var index2 = titlePruned.IndexOf("(ft.");
        if (index2 != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index2);
            titlePruned = titlePruned.Remove(index2, closingIndex - index2 + 1);
        }

        // Remove (with X) from the title
        var index3 = titlePruned.IndexOf("(with");
        if (index3 != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index3);
            titlePruned = titlePruned.Remove(index3, closingIndex - index3 + 1);
        }

        // Remove punctuation that may cause inconsistency
        titlePruned = titlePruned.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "").Replace("—", "").Replace("'", "").Replace("\"", "");

        // Remove non ascii and replaced accented with normal
        titlePruned = ApiHelper.EnforceAscii(titlePruned);
        return titlePruned.Trim();
    }

    public static string PruneTitleSearch(string title)
    {
        var titlePruned = title.ToLower().Trim();
        titlePruned = titlePruned.Replace("-", " ").Replace("—", "").Replace("[", "").Replace("]", ""); // Replace strange chars

        // Remove (feat. X) from the title
        var index = titlePruned.IndexOf("(feat.");
        if (index != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index);
            titlePruned = titlePruned.Remove(index, closingIndex - index + 1);
        }

        // Remove (ft. X) from the title
        var index2 = titlePruned.IndexOf("(ft.");
        if (index2 != -1)
        {
            var closingIndex = titlePruned.IndexOf(")", index2);
            titlePruned = titlePruned.Remove(index2, closingIndex - index2 + 1);
        }

        // Remove (radio edit) and radio edit from the title
        titlePruned = titlePruned.Replace("(radio edit)", "").Replace("radio edit", "");
        // Remove duplicate spaces
        titlePruned = Regex.Replace(titlePruned, @"\s+", " ");
        return ApiHelper.EnforceAscii(titlePruned).Trim();
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25, bool albumMode = false)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        itemSource.Clear();

        var req = (albumMode ? "search/album?q=" : "search?q=") + WebUtility.UrlEncode(query);
        req += $"%20&limit={limit}"; // Added limit to the query 
        req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason

        var jsonObject = await restClient.FetchJsonElement(req);

        foreach (var trackJson in jsonObject.GetProperty("data").EnumerateArray())
        {
            if (token.IsCancellationRequested) // Stop the search
            {
                return;
            }

            var trackId = trackJson.GetProperty("id").ToString();
            var songObj = albumMode ? await GetAlbum(trackId) : await GetTrack(trackId);
            if (songObj != null)
            {
                itemSource.Add(songObj);
            }
        }
    }

    public static async Task<SongSearchObject?> GeneralSearch(SongSearchObject song)
    {
        var title = PruneTitleSearch(song.Title);
        var artists = song.Artists.Split(", ").ToList();
        var req = "search?q=" + WebUtility.UrlEncode(artists[0] + " " + title); // Search for the first artist and title

        var jsonObject = await restClient.FetchJsonElement(req);

        // Create list of SongSearchObject results
        var searchResults = new List<SongSearchObject>();

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray()) // Check if at least one artist matches
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId); // Return the first result

            if (songObj == null)
                continue;

            List<string> songObjArtists = songObj.Artists.Split(", ").ToList();

            // Check if at least one artist matches to add to list
            foreach (var artist in artists)
            {
                foreach (var songObjArtist in songObjArtists)
                {
                    if (songObjArtist.ToLower().Contains(artist.ToLower()) || artist.ToLower().Contains(songObjArtist.ToLower())) // If artist names contain each other (sometimes inconsistency in artist name, The Black Eyed Peas vs Black Eyed Peas)
                    {
                        searchResults.Add(songObj);
                    }
                }
            }
        }

        // Loop through exact matches and get one with minimum album edit distance
        SongSearchObject? closest = null;
        int minDist = int.MaxValue;
        foreach (var songObj in searchResults)
        {
            // Due to inconsistency of brackets and hyphens, delete them, all spaces, and compare
            var titlePruned = PruneTitle(title);
            var songObjTitlePruned = PruneTitle(songObj.Title); // Remove periods due to inconsistency with Jr. and Jr

            if (titlePruned.Equals(songObjTitlePruned)) // If pruned titles are equal
            {
                var editDist = ApiHelper.CalcLevenshteinDistance(PruneTitle(song.AlbumName), PruneTitle(songObj.AlbumName)); // Calculate and update min edit dist
                if (editDist < minDist)
                {
                    minDist = editDist;
                    closest = songObj;
                }
            }
        }

        return closest;
    }

    public static async Task<SongSearchObject?> GetDeezerTrack(SongSearchObject song, CancellationToken token = default, ConversionUpdateCallback? callback = null, bool onlyISRC = false)
    {
        // Try to find by ISRC first
        if (song.Isrc != null)
        {
            var songObj = await GetTrackFromISRC(song.Isrc);

            if (songObj != null)
            {
                callback?.Invoke(InfoBarSeverity.Success, songObj);
                return songObj;
            }
        }

        if (onlyISRC) // If only ISRC search
        {
            callback?.Invoke(InfoBarSeverity.Error, song);
            return null;
        }

        var artists = song.Artists.Split(", ").ToList();
        var trackName = PruneTitleSearch(song.Title);
        var albumName = song.AlbumName;

        if (artists.Count == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            callback?.Invoke(InfoBarSeverity.Error, song);
            return null;
        }

        var songObjList = new List<SongSearchObject>(); // List of SongSearchObject results
        HashSet<string> idSet = new HashSet<string>(); // Set of track ids

        // With album
        var req = "search?q=" + WebUtility.UrlEncode(("artist:%22" + song.Artists + "%22 ") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : ""));
        var jsonObject = await restClient.FetchJsonElement(req); // Create json object from the response

        if (jsonObject.TryGetProperty("data", out var dataElement))
        {
            foreach (var track in dataElement.EnumerateArray())
            {
                if (token.IsCancellationRequested) // Cancel requested, terminate this method
                {
                    return null;
                }

                var trackId = track.GetProperty("id").ToString();
                if (!idSet.Contains(trackId)) // If the track id is not already in the set
                {
                    idSet.Add(trackId);
                    //var songObj = await GetTrack(trackId);
                    var songObj = GetTrackPreview(track);

                    // Check if close artist match to add to list
                    var queryArtists = song.Artists.Split(", ").ToList();
                    var oneArtistMatch = queryArtists.Any(queryArtist => artists.Any(artist => CloseMatch(queryArtist, artist)));

                    if (oneArtistMatch)
                    {
                        songObjList.Add(songObj);
                    }
                }
            }
        }

        // Without album
        req = "search?q=" + WebUtility.UrlEncode(("artist:%22" + song.Artists + "%22 ") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "")) + "?strict=on"; // Strict search
        jsonObject = await restClient.FetchJsonElement(req); // Create json object from the response

        if (jsonObject.TryGetProperty("data", out var dataElement2))
        {
            foreach (var track in dataElement2.EnumerateArray())
            {
                if (token.IsCancellationRequested)
                {
                    return null;
                }

                var trackId = track.GetProperty("id").ToString();
                if (!idSet.Contains(trackId)) // If the track id is not already in the set
                {
                    idSet.Add(trackId);
                    // var songObj = await GetTrack(trackId);
                    var songObj = GetTrackPreview(track);

                    // Check if close artist match to add to list
                    var queryArtists = song.Artists.Split(", ").ToList();
                    var oneArtistMatch = queryArtists.Any(queryArtist => artists.Any(artist => CloseMatch(queryArtist, artist)));

                    if (oneArtistMatch)
                    {
                        songObjList.Add(songObj);
                    }
                }
            }
        }

        // Pass 1: exact title match, find least edit distance album name
        SongSearchObject? closeMatchObj = null;
        int minEditDistance = int.MaxValue;
        foreach (var songObj in songObjList)
        {
            if (token.IsCancellationRequested) // Cancel requested, terminate this method
            {
                return null;
            }

            var titlePruned = PruneTitle(trackName);
            var songObjTitlePruned = PruneTitle(songObj.Title);
            if (titlePruned.Equals(songObjTitlePruned) || titlePruned.Replace("radioedit", "").Equals(songObjTitlePruned.Replace("radioedit", ""))) // If the title matches without punctuation
            {
                if (albumName.ToLower().Replace(" ", "").Equals(songObj.AlbumName.ToLower().Replace(" ", ""))) // If the album name is exact match
                {
                    var fullTrack = await GetTrack(songObj.Id);
                    if (fullTrack != null)
                    {
                        callback?.Invoke(InfoBarSeverity.Warning, fullTrack);
                        return fullTrack;
                    }
                }

                var dist = ApiHelper.CalcLevenshteinDistance(PruneTitle(albumName), PruneTitle(songObj.AlbumName));
                if (dist < minEditDistance)
                {
                    minEditDistance = dist;
                    closeMatchObj = songObj;
                }
            }
        }

        if (closeMatchObj != null)
        {
            var fullTrack = await GetTrack(closeMatchObj.Id);
            if (fullTrack != null) // If the track was found
            {
                callback?.Invoke(InfoBarSeverity.Warning, fullTrack);
                return fullTrack;
            }
        }

        // pass 2: if exact album match, find least edit distance title name
        minEditDistance = int.MaxValue; // Reset min edit distance
        foreach (var songObj in songObjList)
        {
            if (token.IsCancellationRequested) // Cancel requested, terminate this method
            {
                return null;
            }

            if (PruneTitle(albumName).Equals(PruneTitle(songObj.AlbumName))) // If the album name is exact match
            {
                string pruneTargetName = PruneTitle(trackName), pruneName = PruneTitle(songObj.Title);
                if (pruneName.Contains(pruneTargetName) || pruneTargetName.Contains(pruneName)) // Should at least be substrings
                {
                    var dist = ApiHelper.CalcLevenshteinDistance(pruneTargetName, pruneName); // Calculate and update min edit dist
                    if (dist < minEditDistance)
                    {
                        minEditDistance = dist;
                        closeMatchObj = songObj;
                    }
                }
            }
        }

        if (closeMatchObj != null)
        {
            var fullTrack = await GetTrack(closeMatchObj.Id);
            if (fullTrack != null) // If the track was found
            {
                callback?.Invoke(InfoBarSeverity.Warning, fullTrack);
                return fullTrack;
            }
        }

        callback?.Invoke(InfoBarSeverity.Error, song); // No match found
        return null;
    }

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int limit = 25)
    {
        itemSource.Clear();

        // Trim
        artistName = artistName.Trim();
        trackName = trackName.Trim();
        albumName = albumName.Trim();

        if (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            return;
        }

        var req = "search?q=" + WebUtility.UrlEncode((artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : "")) + "?strict=on"; // Strict search
        req += $"%20&limit={limit}"; // Add limit to the query
        var jsonObject = await restClient.FetchJsonElement(req); // Create json object from the response

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            if (token.IsCancellationRequested) // Cancel requested, terminate this method
            {
                return;
            }

            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            if (songObj != null)
            {
                itemSource.Add(songObj);
            }
        }
    }

    private static SongSearchObject GetTrackPreview(JsonElement jsonObj)
    {
        return new SongSearchObject()
        {
            AlbumName = jsonObj.SafeGetString("album", "title") ?? "Unknown",
            Artists = jsonObj.SafeGetString("artist", "name") ?? "Unknown",
            Duration = jsonObj.SafeGetString("duration") ?? "0",
            Explicit = jsonObj.SafeGetBool("explicit_lyrics"),
            Id = jsonObj.SafeGetString("id") ?? "",
            ImageLocation = jsonObj.SafeGetString("album", "cover"),
            Rank = jsonObj.SafeGetString("rank") ?? "0",
            Source = "deezer",
            Title = jsonObj.SafeGetString("title") ?? "Unknown",
        };
    }

    // Problem: Deezer search album objects don't have duration
    private static AlbumSearchObject GetAlbumPreview(JsonElement jsonObj)
    {
        
        return new AlbumSearchObject()
        {
            AlbumName = jsonObj.SafeGetString("title") ?? "Unknown",
            Artists = jsonObj.SafeGetString("artist", "name") ?? "Unknown",
            Duration = "0",
            Explicit = jsonObj.SafeGetBool("explicit_lyrics"),
            Id = jsonObj.SafeGetString("id") ?? "",
            ImageLocation = jsonObj.SafeGetString("cover"),
            Rank = jsonObj.SafeGetString("rank") ?? "0",
            Source = "deezer",
            Title = jsonObj.SafeGetString("title") ?? "Unknown",
            Isrc = jsonObj.SafeGetString("upc"),
            TracksCount = jsonObj.SafeGetInt32("nb_tracks") ?? 0,
        };
    }

    private static SongSearchObject? GetTrackFromJsonElement(JsonElement jsonObject)
    {
        if (string.IsNullOrWhiteSpace(jsonObject.ToString()))
        {
            return null;
        }

        // Get the contributors of the track
        var contributors = new HashSet<string>();
        if (jsonObject.TryGetProperty("contributors", out var contribElement))
        {
            foreach (var contribObject in contribElement.EnumerateArray())
            {
                var name = contribObject.SafeGetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (name.Contains(','))
                    {
                        foreach (var n in name.Split(", "))
                        {
                            if (!contributors.Contains(n))
                                contributors.Add(n);
                        }
                    }
                    else
                    {
                        contributors.Add(name);
                    }
                }
            }
        }

        if (contributors.Count == 0)
        {
            contributors.Add(jsonObject.SafeGetString("artist", "name") ?? "");
        }

        Dictionary<string, object> additionalFields = [];
        additionalFields["preview"] = jsonObject.SafeGetString("preview") ?? "";

        return new SongSearchObject
        {
            Source = "deezer",
            Title = jsonObject.SafeGetString("title") ?? "Unknown",
            ImageLocation = jsonObject.SafeGetString("album", "cover"),
            Id = jsonObject.SafeGetString("id") ?? "",
            ReleaseDate = jsonObject.SafeGetString("release_date") ?? "",
            Artists = string.Join(", ", contributors),
            Duration = jsonObject.SafeGetString("duration") ?? "0",
            Rank = jsonObject.SafeGetString("rank") ?? "0",
            AlbumName = jsonObject.SafeGetString("album", "title") ?? "Unknown",
            Explicit = jsonObject.SafeGetBool("explicit_lyrics"),
            TrackPosition = jsonObject.SafeGetString("track_position") ?? "",
            Isrc = jsonObject.SafeGetString("isrc"),
            AdditionalFields = additionalFields
        };
    }

    public static AlbumSearchObject? GetAlbumFromJsonElement(JsonElement jsonObject)
    {
        if (string.IsNullOrWhiteSpace(jsonObject.ToString()))
        {
            return null;
        }
        // Get the contributors of the album
        var contributors = new HashSet<string>();
        if (jsonObject.TryGetProperty("contributors", out var contribElement))
        {
            foreach (var contribObject in contribElement.EnumerateArray())
            {
                var name = contribObject.SafeGetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (name.Contains(','))
                    {
                        foreach (var n in name.Split(", "))
                        {
                            if (!contributors.Contains(n))
                                contributors.Add(n);
                        }
                    }
                    else
                    {
                        contributors.Add(name);
                    }
                }
            }
        }

        List<SongSearchObject> trackList = [];
        if (jsonObject.TryGetProperty("tracks", out var tracksElement) && tracksElement.TryGetProperty("data", out var dataElement))
        {
            foreach (JsonElement jsonTrack in dataElement.EnumerateArray())
            {
                var trackObj = GetTrackFromJsonElement(jsonTrack);
                if (trackObj != null)
                {
                    trackList.Add(trackObj);
                }
            }
        }

        Dictionary<string, object> additionalFields = [];
        additionalFields["preview"] = jsonObject.SafeGetString("preview") ?? "";

        return new AlbumSearchObject
        {
            Source = "deezer",
            Title = jsonObject.SafeGetString("title") ?? "Unknown",
            ImageLocation = jsonObject.SafeGetString("cover"),
            Id = jsonObject.SafeGetString("id") ?? "",
            ReleaseDate = jsonObject.SafeGetString("release_date") ?? "",
            Artists = string.Join(", ", contributors),
            Duration = jsonObject.SafeGetString("duration") ?? "0",
            Rank = jsonObject.SafeGetString("fans") ?? "0",
            AlbumName = jsonObject.SafeGetString("title") ?? "Unknown",
            Explicit = jsonObject.SafeGetBool("explicit_lyrics"),
            TrackPosition = "1",
            Isrc = jsonObject.SafeGetString("upc"),
            TrackList = trackList,
            TracksCount = jsonObject.SafeGetInt32("nb_tracks") ?? 0,
            AdditionalFields = additionalFields
        };
    }

    public static async Task<SongSearchObject?> GetTrack(string trackId)
    {
        var jsonObject = await restClient.FetchJsonElement("track/" + trackId);
        return GetTrackFromJsonElement(jsonObject);
    }

    public static async Task<AlbumSearchObject?> GetAlbum(string albumId)
    {
        var jsonObject = await restClient.FetchJsonElement("album/" + albumId);
        return GetAlbumFromJsonElement(jsonObject);
    }

    public static async Task<SongSearchObject?> GetTrackFromISRC(string ISRC)
    {
        var jsonObject = await restClient.FetchJsonElement("track/isrc:" + ISRC);
        if (jsonObject.TryGetProperty("error", out var errorObj)) // Has an error field
        {
            Debug.WriteLine($"Error getting track {ISRC}: {errorObj}");
            return null;
        }

        return GetTrackFromJsonElement(jsonObject);
    }

    public static async Task<AlbumSearchObject?> GetAlbumFromUPC(string UPC)
    {
        var jsonObject = await restClient.FetchJsonElement("album/upc:" + UPC);
        if (jsonObject.TryGetProperty("error", out var errorObj)) // Has an error field
        {
            Debug.WriteLine($"Error getting album {UPC}: {errorObj}");
            return null;
        }
        return GetAlbumFromJsonElement(jsonObject);
    }

    public static async Task<string> DownloadTrack(string filePath, SongSearchObject? song, DeezNET.Data.Bitrate? bitrateEnum = null, bool use128Fallback = false)
    {
        // Remove extension if it exists
        filePath = ApiHelper.RemoveExtension(filePath);

        if (song == null || song.Source != "deezer" || !Path.IsPathRooted(filePath))
        {
            throw new Exception("Invalid song");
        }


        var id = long.Parse(song.Id);

        if (bitrateEnum == null)
        {
            // Get quality based on setting
            var settingIdx = await SettingsViewModel.GetSetting<int?>(SettingsViewModel.DeezerQuality) ?? 0;
            bitrateEnum = settingIdx switch
            {
                0 => DeezNET.Data.Bitrate.MP3_128,
                1 => DeezNET.Data.Bitrate.MP3_320,
                _ => DeezNET.Data.Bitrate.FLAC // 2 or anything else
            };
        }

        // Add the extension according to bitrateEnum
        filePath += bitrateEnum switch
        {
            DeezNET.Data.Bitrate.MP3_128 => ".mp3",
            DeezNET.Data.Bitrate.MP3_320 => ".mp3",
            DeezNET.Data.Bitrate.FLAC => ".flac",
            _ => throw new ArgumentOutOfRangeException("Invalid bitrate enum")
        };

        if (File.Exists(filePath) && await SettingsViewModel.GetSetting<bool>(SettingsViewModel.Overwrite) == false)
        {
            throw new Exception("File already exists");
        }
        byte[]? trackBytes = null;
        try
        {
            trackBytes = await deezerClient.Downloader.GetRawTrackBytes(id, (DeezNET.Data.Bitrate)bitrateEnum);

            if (trackBytes == null || trackBytes.Length == 0)
            {
                throw new Exception("Failed to download track");
            }
        } catch (Exception e)
        {
            if (!use128Fallback || (!FFmpegRunner.IsInitialized && bitrateEnum == DeezNET.Data.Bitrate.FLAC))  // Rethrow if not using fallback
            {
                throw;
            }
            Debug.WriteLine($"Error downloading track {id}: {e.Message}");
            // Fallback to 128 kbps
            filePath = ApiHelper.RemoveExtension(filePath) + ".mp3";
            trackBytes = await deezerClient.Downloader.GetRawTrackBytes(id, DeezNET.Data.Bitrate.MP3_128);
            if (trackBytes == null || trackBytes.Length == 0)
            {
                throw new Exception("Failed to download track even with fallback");
            }
        }

        //trackBytes = await deezerClient.Downloader.ApplyMetadataToTrackBytes(id, trackBytes);
        await File.WriteAllBytesAsync(filePath, trackBytes);

        // Convert to flac if needed
        if (bitrateEnum == DeezNET.Data.Bitrate.FLAC && filePath.EndsWith(".mp3"))
        {
            try
            {
                await FFmpegRunner.ConvertToFlacAsync(filePath, 44100);
            } catch (Exception e)
            {
                throw new Exception("Failed to convert to FLAC: " + e.Message);
            }
            filePath = ApiHelper.RemoveExtension(filePath) + ".flac";
        }
        return filePath;
    }

    public static async Task<string> GetGenreStr(int albumId)
    {
        var albumJson = await restClient.FetchJsonElement("album/" + albumId);
        return GetGenreStr(albumJson);
    }

    public static string GetGenreStr(JsonElement jsonElement)
    {
        // Get Genres
        var genreList = new List<string>();
        if (jsonElement.TryGetProperty("genres", out var genresProperty) && genresProperty.TryGetProperty("data", out var dataProperty))
        {
            foreach (var genreData in dataProperty.EnumerateArray())
            {
                genreList.Add(genreData.SafeGetString("name") ?? "Unknown Genre");
            }
        }
        return string.Join(", ", genreList);
    }

    public static async Task UpdateMetadata(string filePath, string trackId)
    {
        var jsonObject = await restClient.FetchJsonElement("track/" + trackId);
        var albumJson = await restClient.FetchJsonElement("album/" + jsonObject.SafeGetString("album", "id"));
        if (string.IsNullOrWhiteSpace(jsonObject.ToString()))
        {
            return;
        }

        // Get the contributors of the track
        var contributors = new HashSet<string>();
        if (jsonObject.TryGetProperty("contributors", out var contribElement))
        {
            foreach (var contribObject in contribElement.EnumerateArray())
            {
                var name = contribObject.SafeGetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                    contributors.Add(name);
            }
        }

        var albumContribs = new HashSet<string>();
        if (albumJson.TryGetProperty("contributors", out var albumContribElement))
        {
            foreach (var contribObject in albumContribElement.EnumerateArray())
            {
                var name = contribObject.SafeGetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                    albumContribs.Add(name);
            }
        }

        // Get Genres
        var genreList = new List<string>();
        if (albumJson.TryGetProperty("genres", out var genresElement) &&
            genresElement.TryGetProperty("data", out var dataElement))
        {
            foreach (var genreData in dataElement.EnumerateArray())
            {
                genreList.Add(genreData.SafeGetString("name") ?? "Unknown Genre");
            }
        }

        var metadata = new MetadataObject(filePath)
        {
            Title = jsonObject.SafeGetString("title"),
            Artists = contributors.ToArray(),
            AlbumName = albumJson.SafeGetString("title"),
            AlbumArtists = albumContribs.ToArray(),
            Isrc = jsonObject.SafeGetString("isrc"),
            ReleaseDate = DateTime.TryParseExact(jsonObject.SafeGetString("release_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null,
            TrackNumber = jsonObject.SafeGetInt32("track_position"),
            AlbumArtPath = albumJson.SafeGetString("cover_big"),
            Genres = genreList.ToArray(),
            TrackTotal = albumJson.SafeGetInt32("nb_tracks"),
            Upc = albumJson.SafeGetString("upc"),
            Url = jsonObject.SafeGetString("link"),
        };

        await metadata.SaveAsync();
    }
}