using System.Net;
using System.Reflection;
using System.Text;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FluentDL.Characterization.Tests;

/// <summary>The tracks of a Spotify album, as album downloads and expansion read them. No network.</summary>
[TestClass]
public class SpotifyAlbumTracksTests
{
    // A search result carries only Spotify's first page of album tracks. The rest load from the album, and a track
    // that can't be looked up keeps what the album listed, so it still gets a queue row.
    [TestMethod]
    public async Task EveryPageOfTheAlbumIsListedAndAFailedLookupKeepsTheListedTrack()
    {
        var http = new FakeSpotify();
        var source = new SpotifySource(new TidalParsingTests.DictionarySettings([]), null!, null!) { IsInitialized = true };
        typeof(SpotifySource).GetField("spotify", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source,
            new SpotifyClient(SpotifyClientConfig.CreateDefault("token").WithHTTPClient(new NetHttpClient(new HttpClient(http)))));
        var album = new AlbumSearchObject
        {
            Source = SourceIds.Spotify, Id = "ram", Title = "Random Access Memories", TracksCount = 3,
            TrackList = [Listed("t1", "Give Life Back to Music"), Listed("t2", "The Game of Love")],
        };

        var tracks = await source.GetAlbumTracksAsync(album);

        CollectionAssert.AreEqual(new[] { "t1", "t2", "t3" }, tracks.Select(t => t.Id).ToArray());
        Assert.AreEqual("USQX91300101", tracks[0].Isrc, "A track that loads comes back in full.");
        Assert.AreEqual("The Game of Love", tracks[1].Title, "A track that fails to load keeps its listed details.");
        Assert.AreEqual("Giorgio by Moroder", tracks[2].Title);
        Assert.AreEqual(1, http.AlbumPageRequests, "The second page of the album's tracks must be read.");
    }

    private static SongSearchObject Listed(string id, string title) => new() { Source = SourceIds.Spotify, Id = id, Title = title, Artists = "Daft Punk" };

    private sealed class FakeSpotify : HttpMessageHandler
    {
        private static readonly object[] Images = [new { url = "https://i.example.test/640" }, new { url = "https://i.example.test/300" }, new { url = "https://i.example.test/64" }];
        private static readonly object[] Artists = [new { id = "daftpunk", name = "Daft Punk" }];
        private static readonly (string Id, string Title, string Isrc)[] Album =
            [("t1", "Give Life Back to Music", "USQX91300101"), ("t2", "The Game of Love", "USQX91300102"), ("t3", "Giorgio by Moroder", "USQX91300103")];
        internal int AlbumPageRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/albums/ram") return Reply(new
            {
                id = "ram", name = "Random Access Memories", album_type = "album", artists = Artists, images = Images,
                release_date = "2013-05-17", total_tracks = 3, external_ids = new { upc = "886443927087" },
                tracks = Page(0, "https://api.spotify.com/v1/albums/ram/tracks?offset=2&limit=2"),
            });
            if (path == "/v1/albums/ram/tracks") { AlbumPageRequests++; return Reply(Page(2, null)); }
            if (path == "/v1/tracks/t2") return Reply(new { error = new { status = 404, message = "Non existing id" } }, HttpStatusCode.NotFound);
            if (path.StartsWith("/v1/tracks/") && Album.FirstOrDefault(t => path.EndsWith("/" + t.Id)) is { Id: not null } track) return Reply(new
            {
                id = track.Id, name = track.Title, artists = Artists, duration_ms = 300000, @explicit = false, track_number = 1, popularity = 50,
                external_ids = new { isrc = track.Isrc },
                album = new { id = "ram", name = "Random Access Memories", images = Images, release_date = "2013-05-17", total_tracks = 3, artists = Artists },
            });
            return Reply(new { error = new { status = 404, message = "Unexpected request " + path } }, HttpStatusCode.NotFound);
        }

        private static object Page(int offset, string? next) => new
        {
            href = $"https://api.spotify.com/v1/albums/ram/tracks?offset={offset}&limit=2", limit = 2, offset, total = 3, next,
            items = Album.Skip(offset).Take(2).Select((t, i) => new
            {
                id = t.Id, name = t.Title, artists = Artists, duration_ms = 300000, @explicit = false, track_number = offset + i + 1,
            }),
        };

        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });
    }
}
