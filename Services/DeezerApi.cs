using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ABI.Windows.Data.Json;
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
        var request = new RestRequest(req);
        var response = await client.GetAsync(request);
        return JsonDocument.Parse(response.Content).RootElement;
    }

    public static async Task GeneralSearch(ObservableCollection<SongSearchObject> itemSource, string query, TextBlock resultsText = null)
    {
        itemSource.Clear();
        query = query.Trim(); // Trim the query
        if (query.Length == 0)
        {
            return;
        }

        var req = "search?q=" + query;

        var jsonObject = await FetchJsonElement(req);
        var resultsCt = 0;

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);

            resultsCt++;
            if (resultsText != null)
            {
                resultsText.Text = resultsCt + (resultsCt == 1 ? " result" : " results");
            }
        }
    }

    public static async Task<SongSearchObject> GeneralSearch(SongSearchObject song)
    {
        var title = song.Title.ToLower();
        var artists = song.Artists.Split(", ").ToList();

        var req = "search?q=" + artists[0] + " " + title; // Search for the first artist and title

        var jsonObject = await FetchJsonElement(req);

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray()) // Check if at least one artist matches
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId); // Return the first result
            songObj.Title = songObj.Title.ToLower();

            bool exactTitleMatch = songObj.Title.Equals(title);
            bool substringMatch = songObj.Title.Contains(title) || title.Contains(songObj.Title);

            if (exactTitleMatch || substringMatch) // If the title matches
            {
                List<string> songObjArtists = songObj.Artists.Split(", ").ToList();

                foreach (var artist in artists)
                {
                    foreach (var songObjArtist in songObjArtists)
                    {
                        if (exactTitleMatch) // Be more lenient with artist name
                        {
                            if (songObjArtist.ToLower().Contains(artist.ToLower()) || artist.ToLower().Contains(songObjArtist.ToLower())) // If names contain each other
                            {
                                return songObj;
                            }
                        }
                        else // Be more strict with artist name
                        {
                            if (songObjArtist.ToLower().Equals(artist.ToLower())) // If names are equal
                            {
                                return songObj;
                            }
                        }
                    }
                }
            }
        }

        return null; // If no results
    }

    public static async Task<SongSearchObject> AdvancedSearch(string artistName, string trackName, string albumName)
    {
        // Trim
        artistName = artistName.Trim();
        trackName = trackName.Trim();
        albumName = albumName.Trim();

        if (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0) // If no search query
        {
            return null;
        }

        var req = "search?q=" + (artistName.Length > 0 ? "artist:%22" + artistName + "%22 " : "") + (trackName.Length > 0 ? "track:%22" + trackName + "%22 " : "") + (albumName.Length > 0 ? "album:%22" + albumName + "%22" : "") + "?strict=on"; // Strict search
        req = req.Replace(" ", "%20"); // Replace spaces with %20

        var jsonObject = await FetchJsonElement(req); // Create json object from the response

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            return await GetTrack(trackId); // Return the first result
        }

        return null; // If no results
    }

    public static async Task AdvancedSearch(ObservableCollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, TextBlock resultsText = null)
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

        var jsonObject = await FetchJsonElement(req); // Create json object from the response
        var resultsCt = 0;

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId);
            itemSource.Add(songObj);

            resultsCt++;
            if (resultsText != null)
            {
                resultsText.Text = resultsCt + (resultsCt == 1 ? " result" : " results");
            }
        }
    }

    public static async Task<SongSearchObject> GetTrack(string trackId)
    {
        var jsonObject = await FetchJsonElement("track/" + trackId);

        // Get the contributors of the track
        var contribCsv = "";
        foreach (var contribObject in jsonObject.GetProperty("contributors").EnumerateArray())
        {
            contribCsv += contribObject.GetProperty("name").GetString() + ", ";
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
}