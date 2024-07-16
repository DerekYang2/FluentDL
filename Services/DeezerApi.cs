using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ABI.Windows.Data.Json;
using CommunityToolkit.WinUI.UI.Controls.TextToolbarSymbols;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using RestSharp;

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
    private static readonly RestClient client = new RestClient(baseURL);

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
            req = req.Replace("(", "").Replace(")", ""); // Remove brackets, causes issues occasionally for some reason
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
        titlePruned = titlePruned.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "").Replace("—", "");

        // Remove non ascii and replaced accented with normal
        titlePruned = EnforceAscii(titlePruned);
        //Debug.WriteLine(title + "|" + titlePruned);
        return titlePruned;
    }

    public static string PruneTitleSearch(string title)
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

        return titlePruned;
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query)
    {
        itemSource.Clear();
        query = query.Trim(); // Trim the query
        if (query.Length == 0)
        {
            return;
        }

        var req = "search?q=" + query;

        var jsonObject = await FetchJsonElement(req);

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);
        }
    }

    public static async Task<SongSearchObject> GeneralSearch(SongSearchObject song)
    {
        var title = PruneTitleSearch(song.Title);
        var artists = song.Artists.Split(", ").ToList();
        var req = "search?q=" + artists[0] + " " + title; // Search for the first artist and title

        var jsonObject = await FetchJsonElement(req);

        SongSearchObject secondPriority = null, thirdPriority = null; // If no exact match, return the closest match

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

        // Pass 1: Loop through exact matches and get one with minimum album edit distance
        SongSearchObject closest = null;
        int minDist = int.MaxValue;
        foreach (var songObj in searchResults)
        {
            // Due to inconsistency of brackets and hyphens, delete them, all spaces, and compare
            var titlePruned = PruneTitle(title);
            var songObjTitlePruned = PruneTitle(songObj.Title); // Remove periods due to inconsistency with Jr. and Jr
            if (titlePruned.Equals(songObjTitlePruned))  // If pruned titles are equal
            {
                var editDist = CalcLevenshteinDistance(PruneTitle(song.AlbumName), PruneTitle(songObj.AlbumName));  // Calculate and update min edit dist
                if (editDist < minDist)
                {
                    minDist = editDist;
                    closest = songObj;
                }
            }
        }

        return closest;
    }

    // TODO: try all artists in req, hashset with deezer id to prevent dupes
    public static async Task<SongSearchObject> AdvancedSearch(string artistName, string trackName, string albumName)
    {
        // Trim
        artistName = artistName.Trim();
        trackName = PruneTitleSearch(trackName);
        albumName = albumName.Trim();

        if (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            return null;
        }

        var req = "search?q=" + (artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : "") + "?strict=on"; // Strict search
        req = req.Replace(" ", "%20"); // Replace spaces with %20
        req = req.Replace("&", "and");
        var jsonObject = await FetchJsonElement(req); // Create json object from the response

        Debug.WriteLine(req + " | " + jsonObject.GetProperty("data").EnumerateArray().Count());

        SongSearchObject closeMatchObj = null;
        int minEditDistance = int.MaxValue;
        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();

            var songObj = await GetTrack(trackId); // Return the first result


            var titlePruned = PruneTitle(trackName);
            var songObjTitlePruned = PruneTitle(songObj.Title);


            if (titlePruned.Equals(songObjTitlePruned)) // If the title matches without punctuation
            {
                if (albumName.ToLower().Replace(" ", "").Equals(songObj.AlbumName.ToLower().Replace(" ", ""))) // If the album name is exact match
                {
                    return songObj;
                }

                var dist = CalcLevenshteinDistance(PruneTitle(albumName), PruneTitle(songObj.AlbumName));
                if (dist < minEditDistance)
                {
                    minEditDistance = dist;
                    closeMatchObj = songObj;
                }
            }
        }

        return closeMatchObj; // If no results
    }

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName)
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

        var req = "search?q=" + (artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : "") + "?strict=on"; // Strict search
        req = req.Replace(" ", "%20"); // Replace spaces with %20
        req = req.Replace("(", "%20").Replace(")", "%20"); // Replace with spaces, brackets break advanced query for some reason
        req = req.Replace("&", "and");

        var jsonObject = await FetchJsonElement(req); // Create json object from the response

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);
        }
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
            if (!contributors.Contains(name) && !name.Contains(',')) // If the name is not already in the list and does not contain a comma
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
            AlbumName = jsonObject.GetProperty("album").GetProperty("title").GetString()
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