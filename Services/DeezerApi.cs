using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ABI.Windows.Data.Json;
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

    public SongSearchObject()
    {
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

    public static async Task<List<SongSearchObject>> SearchTrack(string artistName, string trackName)
    {
        var req = "search?q=artist:%22" + artistName + "%22%20track:%22" + trackName + "%22";
        // Create json object from the response
        // Use System.Text.Json to parse the json object
        var jsonObject = await FetchJsonElement(req);

        var objects = new List<SongSearchObject>(); // Create a list of CustomDataObjects

        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var trackId = track.GetProperty("id").ToString();
            var albumIdStr = track.GetProperty("album").GetProperty("id").ToString();

            /*
            var contributors = await Contributors(albumIdStr); // Get the contributors of the album

            if (contributors.Count == 0) // If no certainty on contributor, use track artist
            {
                contributors.Add(track.GetProperty("artist").GetProperty("name").GetString());
            }
            */
            objects.Add(await GetTrack(trackId));
        }

        return objects;
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
            Title = jsonObject.GetProperty("title").GetString(),
            ImageLocation = jsonObject.GetProperty("album").GetProperty("cover").GetString(),
            Id = jsonObject.GetProperty("id").ToString(),
            ReleaseDate = jsonObject.GetProperty("release_date").ToString(),
            Artists = contribCsv,
            Duration = FormatTime(jsonObject.GetProperty("duration").GetInt32()),
            Rank = jsonObject.GetProperty("rank").ToString()
        };
    }

    // https://api.deezer.com/album/{id}
    public static async Task<List<string>> Contributors(string albumId)
    {
        var jsonObject = await FetchJsonElement("album/" + albumId);

        if (jsonObject.GetProperty("nb_tracks").GetInt32() == 1) // Safe to assume contributors is same as track artists
        {
            var contributors = new List<string>();

            foreach (var contributor in jsonObject.GetProperty("contributors").EnumerateArray())
            {
                contributors.Add(contributor.GetProperty("name").GetString());
            }

            return contributors;
        }
        else
        {
            return new List<string>(); // Do not assume anything
        }
    }

    /**
     * Helper method to format the time in seconds to a string
     * Format as H hr, M min, S sec
     */
    private static string FormatTime(int seconds)
    {
        int sec = seconds % 60;
        seconds /= 60;
        int min = seconds % 60;
        seconds /= 60;
        int hr = seconds;
        return (hr > 0 ? hr + " hr, " : "") + (min > 0 ? min + " min, " : "") + sec + " sec";
    }
}