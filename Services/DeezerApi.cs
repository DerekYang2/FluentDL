using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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

    public string Link
    {
        get;
        set;
    }

    public string Rank
    {
        get;
        set;
    }

    public string Artists
    {
        get;
        set;
    }

    public string AlbumId
    {
        get;
        set;
    }

    public string AlbumName
    {
        get;
        set;
    }

    public SongSearchObject()
    {
    }
}

internal class DeezerApi
{
    public static readonly string baseURL = "https://api.deezer.com";
    private static readonly RestClient client = new RestClient(baseURL);

    public static async Task<List<SongSearchObject>> SearchTrack(string artistName, string trackName)
    {
        var req = "search?q=artist:%22" + artistName + "%22%20track:%22" + trackName + "%22";

        var request = new RestRequest(req);
        // Output the response to the console
        var response = await client.GetAsync(request);

        // Create json object from the response
        // Use System.Text.Json to parse the json object
        var jsonObject = JsonDocument.Parse(response.Content).RootElement;

        var objects = new List<SongSearchObject>(); // Create a list of CustomDataObjects


        foreach (var track in jsonObject.GetProperty("data").EnumerateArray())
        {
            var albumIdStr = track.GetProperty("album").GetProperty("id").ToString();

            /*
            var contributors = await Contributors(albumIdStr); // Get the contributors of the album

            if (contributors.Count == 0) // If no certainty on contributor, use track artist
            {
                contributors.Add(track.GetProperty("artist").GetProperty("name").GetString());
            }
            */

            objects.Add(new SongSearchObject()
            {
                Title = track.GetProperty("title").GetString(),
                ImageLocation =
                    track.GetProperty("album").GetProperty("cover")
                        .GetString(), // "cover_small, cover_medium, cover_big, cover_xl" are available
                Link = track.GetProperty("link").GetString(), // "link" is the link to the track on Deezer
                Rank = track.GetProperty("rank").ToString(),
                Artists = track.GetProperty("artist").GetProperty("name").GetString(),
                AlbumId = albumIdStr,
                AlbumName = track.GetProperty("album").GetProperty("title").GetString(),
            });
        }

        return objects;
    }

    // https://api.deezer.com/album/{id}
    public static async Task<List<string>> Contributors(string albumId)
    {
        var req = "album/" + albumId;
        // Send a GET request to the Deezer API
        var request = new RestRequest(req);
        // Output the response to the console
        var response = await client.GetAsync(request);

        // Create json object from the response
        // Use System.Text.Json to parse the json object
        var jsonObject = JsonDocument.Parse(response.Content).RootElement;

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
}