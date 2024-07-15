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

    private static string PruneTitle(string title)
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
        titlePruned = titlePruned.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "");
        return titlePruned;
    }

    private static string PruneTitleSearch(string title)
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


        foreach (var track in jsonObject.GetProperty("data").EnumerateArray()) // Check if at least one artist matches
        {
            var trackId = track.GetProperty("id").ToString();
            var songObj = await GetTrack(trackId); // Return the first result


            // Due to inconsistency of brackets and hyphens, delete them, all spaces, and compare
            var titlePruned = PruneTitle(title);
            var songObjTitlePruned = PruneTitle(songObj.Title); // Remove periods due to inconsistency with Jr. and Jr

            bool exactTitleMatch = titlePruned.Equals(songObjTitlePruned);
            bool substringMatch = titlePruned.Contains(songObjTitlePruned) || songObjTitlePruned.Contains(titlePruned);


            if (exactTitleMatch || substringMatch) // If the title matches
            {
                List<string> songObjArtists = songObj.Artists.Split(", ").ToList();
                bool allArtistsMatch = true, oneArtistMatch = false;

                artists.Sort();
                songObjArtists.Sort();

                // Check if all artists match
                if (artists.Count != songObjArtists.Count)
                {
                    allArtistsMatch = false;
                }
                else
                {
                    for (int i = 0; i < artists.Count; i++)
                    {
                        if (!songObjArtists[i].ToLower().Contains(artists[i].ToLower()) && !artists[i].ToLower().Contains(songObjArtists[i].ToLower())) // If neither works
                        {
                            allArtistsMatch = false;
                            break;
                        }
                    }
                }

                // Check if at least one artist matches
                foreach (var artist in artists)
                {
                    foreach (var songObjArtist in songObjArtists)
                    {
                        if (songObjArtist.ToLower().Contains(artist.ToLower()) || artist.ToLower().Contains(songObjArtist.ToLower())) // If artist names contain each other (sometimes inconsistency in artist name, The Black Eyed Peas vs Black Eyed Peas)
                        {
                            oneArtistMatch = true; // If at least one artist matches
                        }
                    }
                }

                if (allArtistsMatch && exactTitleMatch)
                {
                    return songObj;
                }

                if (oneArtistMatch && exactTitleMatch) // If the title matches
                {
                    secondPriority = songObj; // Save for now, in case we find an exact match later
                }

                if (allArtistsMatch && substringMatch) // If the title is a substring match
                {
                    thirdPriority = songObj; // Save for now, in case we find an exact match later
                }
            }
        }

        return secondPriority ?? thirdPriority;
    }

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

        SongSearchObject closeMatchObj = null;


        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();

            var songObj = await GetTrack(trackId); // Return the first result


            var titlePruned = PruneTitle(trackName);
            var songObjTitlePruned = PruneTitle(songObj.Title);

            if (titlePruned.Equals(songObjTitlePruned)) // If the title matches without punctuation
            {
                if (albumName.ToLower().Equals(songObj.AlbumName.ToLower())) // If the album name matches
                {
                    Debug.WriteLine(trackName);
                    return songObj;
                }
                else
                {
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
        Debug.WriteLine("New Track");

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
}