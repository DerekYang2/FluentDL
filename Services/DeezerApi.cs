using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using ABI.Windows.Data.Json;
using ATL;
using CommunityToolkit.WinUI.UI.Controls.TextToolbarSymbols;
using DeezNET;
using DeezNET.Data;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using YoutubeExplode.Search;

namespace FluentDL.Services;

// TODO: Handle null warnings and catch network errors

internal class DeezerApi
{
    public static readonly string baseURL = "https://api.deezer.com";
    private static readonly RestClient client = new RestClient(new RestClientOptions(baseURL) { Timeout = new TimeSpan(0, 0, 5) });
    private static DeezerClient deezerClient = new DeezerClient();

    public static async Task InitDeezerClient(string? ARL)
    {
        if (!string.IsNullOrWhiteSpace(ARL))
        {
            await deezerClient.SetARL(ARL);
        }
    }

    public static async Task AddTracksFromLink(ObservableCollection<SongSearchObject> list, string url, CancellationToken token, Search.UrlStatusUpdateCallback? statusUpdate)
    {
        if (url.StartsWith("https://deezer.page.link/"))
        {
            url = (await ApiHelper.GetRedirectedUrlAsync(new Uri(url))).AbsoluteUri;
        }

        Debug.WriteLine(url);
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
                statusUpdate?.Invoke(InfoBarSeverity.Success, $"Loaded track \"{songObj.Title}\"");
            }
        }

        if (Regex.IsMatch(url, @"https://www\.deezer\.com(/[^/]+)?/(album|playlist)/.*"))
        {
            if (DeezerURL.TryParse(url, out var urlData))
            {
                var tracksInAlbum = await urlData.GetAssociatedTracks(deezerClient, 1000, token);

                // Send message to infobar
                var listName = await urlData.GetTitle(deezerClient);
                statusUpdate?.Invoke(InfoBarSeverity.Informational, $"Loading {(url.Contains("/album/") ? "album" : "playlist")} \"{listName}\" ...");

                list.Clear(); // Clear the item source for lists like playlist/albums

                foreach (var trackId in tracksInAlbum)
                {
                    if (token.IsCancellationRequested) // Stop the search
                    {
                        return;
                    }

                    var songObj = await GetTrack(trackId.ToString());
                    if (songObj != null)
                    {
                        list.Add(songObj);
                    }
                }
            }
        }
    }

    public static async Task<JsonElement> FetchJsonElement(string req, CancellationToken token = default)
    {
        try
        {
            var request = new RestRequest(req);
            var response = await client.GetAsync(request, token);
            return JsonDocument.Parse(response.Content).RootElement;
        }
        catch (Exception e)
        {
            try
            {
                Debug.WriteLine("Failed: " + req);
                req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
                var request = new RestRequest(req);
                var response = await client.GetAsync(request, token);
                return JsonDocument.Parse(response.Content).RootElement;
            }
            catch (Exception e2)
            {
                Debug.WriteLine("Failed again: " + req);
                Debug.WriteLine(e2);
                return new JsonElement();
            }
        }
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

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token, int limit = 25)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        itemSource.Clear();

        var req = "search?q=" + WebUtility.UrlEncode(query);
        req += $"%20&limit={limit}"; // Added limit to the query 
        req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
        var jsonObject = await FetchJsonElement(req);

        foreach (var trackJson in jsonObject.GetProperty("data").EnumerateArray())
        {
            if (token.IsCancellationRequested) // Stop the search
            {
                return;
            }

            var trackId = trackJson.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            if (songObj != null)
            {
                itemSource.Add(songObj);
            }
        }
    }

    public static async Task<SongSearchObject> GeneralSearch(SongSearchObject song)
    {
        var title = PruneTitleSearch(song.Title);
        var artists = song.Artists.Split(", ").ToList();
        var req = "search?q=" + WebUtility.UrlEncode(artists[0] + " " + title); // Search for the first artist and title

        var jsonObject = await FetchJsonElement(req);

        // Create list of SongSearchObject results
        var searchResults = new List<SongSearchObject>();

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray()) // Check if at least one artist matches
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId); // Return the first result

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

    public static async Task<SongSearchObject?> GetDeezerTrack(SongSearchObject song, CancellationToken token = default)
    {
        // Try to find by ISRC first
        if (song.Isrc != null)
        {
            var songObj = await GetTrackFromISRC(song.Isrc);
            if (songObj != null)
            {
                return songObj;
            }
        }

        var artists = song.Artists.Split(", ").ToList();
        var trackName = PruneTitleSearch(song.Title);
        var albumName = song.AlbumName;

        if (artists.Count == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            return null;
        }

        var songObjList = new List<SongSearchObject>(); // List of SongSearchObject results
        HashSet<string> idSet = new HashSet<string>(); // HashSet to prevent duplicate tracks

        foreach (var artistName in artists) // Try searching for each artist
        {
            // With album
            var req = "search?q=" + WebUtility.UrlEncode((artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : ""));
            var jsonObject = await FetchJsonElement(req); // Create json object from the response

            foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
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
                    var songObj = GetTrackQuick(track);
                    songObjList.Add(songObj);
                }
            }

            // Without album
            req = "search?q=" + WebUtility.UrlEncode((artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "")) + "?strict=on"; // Strict search
            jsonObject = await FetchJsonElement(req); // Create json object from the response

            foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
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
                    var songObj = GetTrackQuick(track);
                    songObjList.Add(songObj);
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
                    return await GetTrack(songObj.Id);
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
            return await GetTrack(closeMatchObj.Id); // Get the full track object
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
            return await GetTrack(closeMatchObj.Id); // Get the full track object
        }

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
        var jsonObject = await FetchJsonElement(req); // Create json object from the response

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            if (token.IsCancellationRequested) // Cancel requested, terminate this method
            {
                return;
            }

            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);
        }
    }

    private static SongSearchObject GetTrackQuick(JsonElement jsonObj)
    {
        return new SongSearchObject()
        {
            AlbumName = jsonObj.GetProperty("album").GetProperty("title").GetString(),
            Artists = jsonObj.GetProperty("artist").GetProperty("name").GetString(),
            Duration = jsonObj.GetProperty("duration").ToString(),
            Explicit = jsonObj.GetProperty("explicit_lyrics").GetBoolean(),
            Id = jsonObj.GetProperty("id").ToString(),
            ImageLocation = jsonObj.GetProperty("album").GetProperty("cover").GetString(),
            Rank = jsonObj.GetProperty("rank").ToString(),
            Source = "deezer",
            Title = jsonObj.GetProperty("title").GetString(),
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
        var contribCsv = "";

        foreach (var contribObject in jsonObject.GetProperty("contributors").EnumerateArray())
        {
            var name = contribObject.GetProperty("name").GetString();
            if (name.Contains(','))
            {
                // Split
                var names = name.Split(", ");
                foreach (var n in names)
                {
                    if (!contributors.Contains(n))
                    {
                        contribCsv += n + ", ";
                        contributors.Add(n);
                    }
                }
            }
            else if (!contributors.Contains(name)) // If the name is not already in the list and does not contain a comma
            {
                contribCsv += name + ", ";
                contributors.Add(name);
            }
        }

        contribCsv = contribCsv.Remove(contribCsv.Length - 2); // Remove the last comma and space

        return new SongSearchObject()
        {
            Source = "deezer",
            Title = jsonObject.GetProperty("title").GetString(),
            ImageLocation = jsonObject.GetProperty("album").GetProperty("cover").GetString(),
            Id = jsonObject.GetProperty("id").ToString(),
            ReleaseDate = jsonObject.GetProperty("release_date").ToString(),
            Artists = contribCsv,
            Duration = jsonObject.GetProperty("duration").ToString(),
            Rank = jsonObject.GetProperty("rank").ToString(),
            AlbumName = jsonObject.GetProperty("album").GetProperty("title").GetString(),
            Explicit = jsonObject.GetProperty("explicit_lyrics").GetBoolean(),
            TrackPosition = jsonObject.GetProperty("track_position").ToString(),
            Isrc = jsonObject.GetProperty("isrc").GetString(),
        };
    }

    public static async Task<SongSearchObject?> GetTrack(string trackId)
    {
        var jsonObject = await FetchJsonElement("track/" + trackId);
        return GetTrackFromJsonElement(jsonObject);
    }

    public static async Task<SongSearchObject?> GetTrackFromISRC(string ISRC)
    {
        var jsonObject = await FetchJsonElement("track/isrc:" + ISRC);
        if (jsonObject.TryGetProperty("error", out var errorObj)) // Has an error field
        {
            Debug.WriteLine($"Error getting track {ISRC}: {errorObj}");
            return null;
        }

        return GetTrackFromJsonElement(jsonObject);
    }


    public static async Task DownloadTrack(SongSearchObject? song, string filePath)
    {
        if (song == null || song.Source != "deezer" || !Path.IsPathRooted(filePath))
        {
            return;
        }

        var id = long.Parse(song.Id);
        var trackBytes = await deezerClient.Downloader.GetRawTrackBytes(id, Bitrate.FLAC);
        //trackBytes = await deezerClient.Downloader.ApplyMetadataToTrackBytes(id, trackBytes);
        await File.WriteAllBytesAsync(filePath, trackBytes);
        Debug.WriteLine("COMPLETE: " + filePath);
        await UpdateMetadata(filePath, song.Id);
        Debug.WriteLine("COMPLETE Metadata: " + filePath);
    }

    public static async Task UpdateMetadata(string filePath, string trackId)
    {
        var jsonObject = await FetchJsonElement("track/" + trackId);
        var albumJson = await FetchJsonElement("album/" + jsonObject.GetProperty("album").GetProperty("id"));
        if (string.IsNullOrWhiteSpace(jsonObject.ToString()))
        {
            return;
        }

        // Get the contributors of the track
        HashSet<string> contributors = new HashSet<string>();
        var contribStr = "";

        foreach (var contribObject in jsonObject.GetProperty("contributors").EnumerateArray())
        {
            var name = contribObject.GetProperty("name").GetString();
            if (name.Contains(','))
            {
                // Split
                var names = name.Split(", ");
                foreach (var n in names)
                {
                    if (!contributors.Contains(n))
                    {
                        contribStr += n + ";";
                        contributors.Add(n);
                    }
                }
            }
            else if (!contributors.Contains(name)) // If the name is not already in the list and does not contain a comma
            {
                contribStr += name + ";";
                contributors.Add(name);
            }
        }

        contribStr = contribStr.Remove(contribStr.Length - 1); // Remove the last semicolon
        contribStr = contribStr.Replace(";", ", ");
        // Get Genres
        var genreStr = "";
        foreach (var genreData in albumJson.GetProperty("genres").GetProperty("data").EnumerateArray())
        {
            genreStr += genreData.GetProperty("name").GetString() + ";";
        }

        genreStr = genreStr.Remove(genreStr.Length - 1); // Remove the last semicolon

        // Get image bytes for cover art
        var imageBytes = await new HttpClient().GetByteArrayAsync(albumJson.GetProperty("cover_big").GetString());
        PictureInfo newPicture = PictureInfo.fromBinaryData(imageBytes, PictureInfo.PIC_TYPE.Front);

        var track = new Track(filePath);
        track.Title = jsonObject.GetProperty("title").GetString();
        track.Album = albumJson.GetProperty("title").GetString();
        track.AlbumArtist = albumJson.GetProperty("artist").GetProperty("name").GetString();
        track.Artist = contribStr;
        track.AudioSourceUrl = jsonObject.GetProperty("link").GetString();
        track.BPM = jsonObject.GetProperty("bpm").TryGetInt32(out var bpm
        )
            ? bpm
            : 0;
        track.AdditionalFields["YEAR"] = jsonObject.GetProperty("release_date").GetString().Substring(0, 4);
        track.Date = DateTime.Parse(jsonObject.GetProperty("release_date").GetString());
        track.TrackNumber = jsonObject.GetProperty("track_position").GetInt32();
        track.TrackTotal = albumJson.GetProperty("nb_tracks").GetInt32();
        track.Genre = genreStr;
        track.ISRC = jsonObject.GetProperty("isrc").GetString();
        track.Popularity = jsonObject.GetProperty("rank").GetInt32();
        // Append to front if pictures already exist
        if (track.EmbeddedPictures.Count > 0)
        {
            track.EmbeddedPictures.Insert(0, newPicture);
        }
        else
        {
            track.EmbeddedPictures.Add(newPicture);
        }

        await track.SaveAsync();
    }
}