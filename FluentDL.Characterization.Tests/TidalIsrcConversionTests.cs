using System.Net;
using System.Text;
using System.Web;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Converting a track to TIDAL by ISRC. TIDAL's ISRC filter lists the same recording on every release that has it,
/// 20 a page and compilations first, so the artist's own release can be hundreds of results in. These run against a
/// fake catalog, so they don't change when TIDAL's does.
/// </summary>
[TestClass]
public class TidalIsrcConversionTests
{
    private const string Isrc = "USQX91300108";
    private static readonly Release Album = new("20115564", "20115556", "Random Access Memories", "Daft Punk");
    private static readonly Release Single = new("single-track", "single", "Get Lucky", "Daft Punk");
    private static readonly Release Disco = new("disco-track", "disco", "Disco Hits 2013", "Various Artists");
    private static readonly Release Summer = new("summer-track", "summer", "Summer Party", "Various Artists");

    private static SongSearchObject Song(string albumName = "Random Access Memories") => new()
    {
        Source = SourceIds.Spotify, Id = "spotify", Title = "Get Lucky (feat. Pharrell Williams & Nile Rodgers)",
        Artists = "Daft Punk, Pharrell Williams, Nile Rodgers", AlbumName = albumName, Isrc = Isrc,
    };

    [TestMethod]
    public async Task TheSearchHitOnTheNamedAlbumBeatsEarlierCompilations()
    {
        var catalog = new FakeCatalog { TrackSearch = [Disco, Summer, Single, Album] };

        var match = await Convert(catalog, Song());

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(Album.TrackId, match.Track?.Id);
        Assert.AreEqual(Album.Name, match.Track?.AlbumName);
        Assert.AreEqual(1, catalog.Requests.Count(r => r.Contains("searchResults")), "The named album was in the track search.");
        Assert.IsFalse(catalog.Requests.Any(r => r.Contains("filter[isrc]")));
    }

    [TestMethod]
    public async Task TheNamedAlbumIsLookedUpWhenTheSearchMissesIt()
    {
        var catalog = new FakeCatalog { TrackSearch = [Disco, Single], AlbumSearch = [Album], AlbumTracks = { [Album.AlbumId] = [Album] } };

        var match = await Convert(catalog, Song());

        Assert.AreEqual(Album.TrackId, match.Track?.Id);
        Assert.AreEqual(Album.Name, match.Track?.AlbumName);
        Assert.IsFalse(catalog.Requests.Any(r => r.Contains("filter[isrc]")));
    }

    // The album TIDAL has may list the song under another ISRC; then the artist's own single is the same recording.
    [TestMethod]
    public async Task TheArtistsOwnReleaseIsUsedWhenTheAlbumLacksTheRecording()
    {
        var catalog = new FakeCatalog
        {
            TrackSearch = [Disco, Single], AlbumSearch = [Album], AlbumTracks = { [Album.AlbumId] = [Album with { Isrc = "USQX91300999" }] },
        };

        var match = await Convert(catalog, Song());

        Assert.AreEqual(Single.TrackId, match.Track?.Id);
        Assert.IsFalse(catalog.Requests.Any(r => r.Contains("filter[isrc]")));
    }

    [TestMethod]
    public async Task TheIsrcFilterSkipsVariousArtistsCompilations()
    {
        var catalog = new FakeCatalog { TrackSearch = [Disco], IsrcFilter = [Summer, Disco, Single] };

        var match = await Convert(catalog, Song("A name TIDAL doesn't have"));

        Assert.AreEqual(Single.TrackId, match.Track?.Id);
        Assert.IsTrue(catalog.Requests.Any(r => r.Contains("filter[isrc]")));
    }

    // Ranking by edit distance between album names would pick "Disco Hits 2013" here.
    [TestMethod]
    public async Task ASimilarCompilationNameDoesNotBeatTheArtistsOwnRelease()
    {
        var match = await Convert(new FakeCatalog { IsrcFilter = [Disco, Single] }, Song("Disco Party Hits"));

        Assert.AreEqual(Single.TrackId, match.Track?.Id);
    }

    [TestMethod]
    public async Task ACompilationIsUsedOnlyWhenNothingElseHasTheRecording()
    {
        Assert.AreEqual(Disco.TrackId, (await Convert(new FakeCatalog { TrackSearch = [Disco] }, Song())).Track?.Id);
        Assert.AreEqual(MatchKind.NotFound, (await Convert(new FakeCatalog(), Song())).Kind);
    }

    private static async Task<MatchResult> Convert(FakeCatalog catalog, SongSearchObject song)
    {
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [SettingKeys.TidalClientId] = "test-client", [SettingKeys.TidalClientSecret] = "test-secret", [SettingKeys.TidalCountry] = "US",
        });
        var source = new TidalSource(new TidalApi(settings, new HttpClient(catalog)));
        return await source.FindEquivalentAsync(song, song.Isrc, CancellationToken.None, isrcOnly: true);
    }

    private sealed record Release(string TrackId, string AlbumId, string Name, string AlbumArtist, string Isrc = Isrc);

    private sealed class FakeCatalog : HttpMessageHandler
    {
        public List<Release> TrackSearch { get; init; } = [];
        public List<Release> AlbumSearch { get; init; } = [];
        public Dictionary<string, List<Release>> AlbumTracks { get; } = [];
        public List<Release> IsrcFilter { get; init; } = [];
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "auth.tidal.com") return Reply(new { access_token = "token", expires_in = 3600, token_type = "Bearer" });
            Requests.Add(Uri.UnescapeDataString(uri.PathAndQuery));
            var query = HttpUtility.ParseQueryString(uri.Query);
            var path = uri.AbsolutePath;
            if (path.EndsWith("/searchResults"))
            {
                var albums = query["include"]!.StartsWith("albums");
                var hits = albums ? AlbumSearch : TrackSearch;
                return Reply(new
                {
                    data = new { id = query["filter[query]"], type = "searchResults", relationships = new Dictionary<string, object>
                    {
                        [albums ? "albums" : "tracks"] = new { data = hits.Select(r => Reference(albums ? "albums" : "tracks", albums ? r.AlbumId : r.TrackId)) },
                    } },
                    included = albums ? hits.SelectMany(AlbumWithArtist) : hits.SelectMany(TrackWithAlbum),
                });
            }
            if (path.EndsWith("/tracks") && query["filter[isrc]"] is { } isrc)
            {
                var tracks = IsrcFilter.Where(r => r.Isrc == isrc).ToList();
                return Reply(new { data = tracks.Select(Track), included = tracks.SelectMany(r => new[] { AlbumResource(r), Artist(r.AlbumArtist), Artist("Daft Punk") }) });
            }
            if (path.Contains("/albums/") && AlbumSearch.FirstOrDefault(r => path.EndsWith("/albums/" + r.AlbumId)) is { } album)
            {
                var tracks = AlbumTracks.GetValueOrDefault(album.AlbumId) ?? [];
                return Reply(new
                {
                    data = AlbumResource(album, tracks.Select((t, i) => (object)new { id = t.TrackId, type = "tracks", meta = new { trackNumber = i + 1 } })),
                    included = tracks.Select(Track).Append(Artist(album.AlbumArtist)).Append(Artist("Daft Punk")),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{ "errors": [] }""") });
        }

        private static object Reference(string type, string id) => new { id, type };

        private static object Artist(string name) => new { id = ArtistId(name), type = "artists", attributes = new { name } };

        private static string ArtistId(string name) => name.Replace(" ", "-").ToLowerInvariant();

        private static object Track(Release r) => new
        {
            id = r.TrackId, type = "tracks",
            attributes = new { title = "Get Lucky", version = "feat. Pharrell Williams and Nile Rodgers", isrc = r.Isrc, duration = "PT6M9S" },
            relationships = new
            {
                artists = new { data = new[] { Reference("artists", ArtistId("Daft Punk")) } },
                albums = new { data = new[] { Reference("albums", r.AlbumId) } },
            },
        };

        private static object AlbumResource(Release r, IEnumerable<object>? items = null) => new
        {
            id = r.AlbumId, type = "albums",
            attributes = new { title = r.Name, releaseDate = "2013-05-17", numberOfItems = 13 },
            relationships = new Dictionary<string, object>
            {
                ["artists"] = new { data = new[] { Reference("artists", ArtistId(r.AlbumArtist)) } },
                ["items"] = new { data = items ?? [] },
            },
        };

        private static IEnumerable<object> AlbumWithArtist(Release r) => [AlbumResource(r), Artist(r.AlbumArtist)];

        private static IEnumerable<object> TrackWithAlbum(Release r) => [Track(r), AlbumResource(r), Artist(r.AlbumArtist), Artist("Daft Punk")];

        private static Task<HttpResponseMessage> Reply(object body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/vnd.api+json"),
        });
    }
}
