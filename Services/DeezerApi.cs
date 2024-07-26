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
using ABI.Windows.Data.Json;
using CommunityToolkit.WinUI.UI.Controls.TextToolbarSymbols;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using RestSharp;
using YoutubeExplode.Search;

namespace FluentDL.Services;

public class SongSearchObject
{
    public string Title
    {
        get;
        set;
    }

    public string ImageLocation
    {
        get;
        set;
    }

    public string Id
    {
        get;
        set;
    }

    public string ReleaseDate
    {
        get;
        set;
    }

    public string Artists
    {
        get;
        set;
    }

    public string Duration
    {
        get;
        set;
    }

    public string Rank
    {
        get;
        set;
    }

    public string AlbumName
    {
        get;
        set;
    }

    public string Source
    {
        get;
        set;
    }

    public bool Explicit
    {
        get;
        set;
    }

    public string TrackPosition
    {
        get;
        set;
    }

    public SongSearchObject()
    {
    }

    public override string ToString()
    {
        return Source + " | Title: " + Title + ", Artists: " + Artists + ", Duration: " + Duration + ", Rank: " + Rank + ", Release Date: " + ReleaseDate + ", Image Location: " + ImageLocation + ", Id: " + Id + ", Album Name: " + AlbumName;
    }
}

// TODO: Handle null warnings and catch network errors

internal class DeezerApi
{
    public static readonly string baseURL = "https://api.deezer.com";
    private static readonly RestClient client = new RestClient(new RestClientOptions(baseURL) { Timeout = new TimeSpan(0, 0, 5) });

    public static async Task<JsonElement> FetchJsonElement(string req)
    {
        try
        {
            var request = new RestRequest(req);
            var response = await client.GetAsync(request);
            return JsonDocument.Parse(response.Content).RootElement;
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed: " + req);
            req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
            var request = new RestRequest(req);
            var response = await client.GetAsync(request);
            return JsonDocument.Parse(response.Content).RootElement;
        }
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    private static string EnforceAscii(string text)
    {
        var result = RemoveDiacritics(text);
        result = Regex.Replace(result, @"[^\u0000-\u007F]+", string.Empty);
        return result;
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

        // Remove punctuation that may cause inconsistency
        titlePruned = titlePruned.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "").Replace("—", "").Replace("'", "").Replace("\"", "");

        // Remove non ascii and replaced accented with normal
        titlePruned = EnforceAscii(titlePruned);
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
        return EnforceAscii(titlePruned).Trim();
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, CancellationToken token)
    {
        itemSource.Clear();
        query = query.Trim(); // Trim the query
        if (query.Length == 0)
        {
            return;
        }

        var req = "search?q=" + WebUtility.UrlEncode(query);
        req = req.Replace("%28", "").Replace("%29", ""); // Remove brackets, causes issues occasionally for some reason
        var jsonObject = await FetchJsonElement(req);

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            if (token.IsCancellationRequested) // Stop the search
            {
                return;
            }

            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);
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
                var editDist = CalcLevenshteinDistance(PruneTitle(song.AlbumName), PruneTitle(songObj.AlbumName)); // Calculate and update min edit dist
                if (editDist < minDist)
                {
                    minDist = editDist;
                    closest = songObj;
                }
            }
        }

        return closest;
    }

    public static async Task<SongSearchObject?> AdvancedSearch(SongSearchObject song)
    {
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
            var titlePruned = PruneTitle(trackName);
            var songObjTitlePruned = PruneTitle(songObj.Title);
            if (titlePruned.Equals(songObjTitlePruned) || titlePruned.Replace("radioedit", "").Equals(songObjTitlePruned.Replace("radioedit", ""))) // If the title matches without punctuation
            {
                if (albumName.ToLower().Replace(" ", "").Equals(songObj.AlbumName.ToLower().Replace(" ", ""))) // If the album name is exact match
                {
                    return await GetTrack(songObj.Id);
                }

                var dist = CalcLevenshteinDistance(PruneTitle(albumName), PruneTitle(songObj.AlbumName));
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
            if (PruneTitle(albumName).Equals(PruneTitle(songObj.AlbumName))) // If the album name is exact match
            {
                string pruneTargetName = PruneTitle(trackName), pruneName = PruneTitle(songObj.Title);
                if (pruneName.Contains(pruneTargetName) || pruneTargetName.Contains(pruneName)) // Should at least be substrings
                {
                    var dist = CalcLevenshteinDistance(pruneTargetName, pruneName); // Calculate and update min edit dist
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

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token)
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

    public static async Task<SongSearchObject> GetTrack(string trackId)
    {
        var jsonObject = await FetchJsonElement("track/" + trackId);

        // Get the contributors of the track
        HashSet<string> contributors = new HashSet<string>();
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
            TrackPosition = jsonObject.GetProperty("track_position").ToString()
        };
    }

    public static int CalcLevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(a))
        {
            return b.Length;
        }

        if (string.IsNullOrEmpty(b))
        {
            return a.Length;
        }

        int lengthA = a.Length;
        int lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (int i = 0; i <= lengthA; distances[i, 0] = i++) ;
        for (int j = 0; j <= lengthB; distances[0, j] = j++) ;

        for (int i = 1; i <= lengthA; i++)
        {
            for (int j = 1; j <= lengthB; j++)
            {
                int cost = b[j - 1] == a[i - 1] ? 0 : 1;

                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost
                );
            }
        }

        return distances[lengthA, lengthB];
    }
}