using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;
using Newtonsoft.Json.Linq;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class ProgressivePlaylistImportTests
{
    [TestMethod]
    [DataRow("spotify")]
    [DataRow("tidal")]
    [DataRow("qobuz")]
    public async Task CompleteFirstPageIsPublishedBeforeTheNextPageReturns(string provider)
    {
        using var fixture = new Fixture(provider);
        var items = new List<SongSearchObject>();
        var load = fixture.Load(items, CancellationToken.None);
        await fixture.Http.NextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { "1", "1" }, items.Select(item => item.Id).ToArray());
        Assert.IsFalse(load.IsCompleted);
        Assert.IsTrue(items.All(item => item.Isrc == "USABC2400001" && item.AlbumName == "Album" && item.Artists == "Artist"));
        fixture.Http.Release.TrySetResult();
        await load;
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, items.Select(item => item.Id).ToArray());
        Assert.AreNotSame(items[0], items[1]);
        Assert.AreEqual(StatusLevel.Success, fixture.Status.Last());
        Assert.AreEqual(0, fixture.Http.DetailRequests);
    }

    [TestMethod]
    [DataRow("spotify")]
    [DataRow("tidal")]
    [DataRow("qobuz")]
    public async Task CancellationKeepsTheCompletedPageAndStopsThePendingRead(string provider)
    {
        using var fixture = new Fixture(provider);
        using var cancellation = new CancellationTokenSource();
        var items = new List<SongSearchObject>();
        var load = fixture.Load(items, cancellation.Token);
        await fixture.Http.NextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await load; Assert.Fail("Cancellation should propagate."); }
        catch (OperationCanceledException) { }
        CollectionAssert.AreEqual(new[] { "1", "1" }, items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    [DataRow("spotify")]
    [DataRow("tidal")]
    [DataRow("qobuz")]
    public async Task LaterPageFailureKeepsThePrefixWithoutRestartingAndDuplicatingIt(string provider)
    {
        using var fixture = new Fixture(provider);
        fixture.Http.FailNext = true;
        var items = new List<SongSearchObject>();
        var load = fixture.Load(items, CancellationToken.None);
        await fixture.Http.NextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Http.Release.TrySetResult();
        await load;
        CollectionAssert.AreEqual(new[] { "1", "1" }, items.Select(item => item.Id).ToArray());
        Assert.AreEqual(StatusLevel.Warning, fixture.Status.Last());
        Assert.AreEqual(2, fixture.Http.Pages);
    }

    [TestMethod]
    public async Task QobuzFetchesOnlyIncompleteRecordsAndReusesDetailsForDuplicateEntries()
    {
        using var fixture = new Fixture("qobuz");
        fixture.Http.IncompleteTrack = true;
        fixture.Http.Release.TrySetResult();
        var items = new List<SongSearchObject>();
        await fixture.Load(items, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, items.Select(item => item.Id).ToArray());
        Assert.AreEqual(1, fixture.Http.DetailRequests);
        Assert.IsTrue(items.All(item => item.Isrc == "USABC2400001" && item.AlbumName == "Album"));
    }

    [TestMethod]
    [DataRow("album")]
    [DataRow("isrc")]
    [DataRow("release_date_original")]
    [DataRow("performers")]
    [DataRow("duration")]
    public void QobuzDoesNotTreatIncompletePlaylistMetadataAsAFullTrack(string field)
    {
        var track = JObject.FromObject(QobuzTrack(1));
        Assert.IsTrue(QobuzSource.CompletePlaylistTrack(track));
        track.Remove(field);
        Assert.IsFalse(QobuzSource.CompletePlaylistTrack(track));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task QobuzLegacyIdsPreserveOrderAndDuplicatesWithOneDetailReadPerId(bool map, bool rejectPages)
    {
        using var fixture = new Fixture("qobuz");
        fixture.Http.LegacyIds = true;
        fixture.Http.LegacyMap = map;
        fixture.Http.RejectTrackPages = rejectPages;
        var items = new List<SongSearchObject>();
        await fixture.Load(items, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, items.Select(item => item.Id).ToArray());
        Assert.AreNotSame(items[0], items[1]);
        Assert.AreEqual(2, fixture.Http.DetailRequests);
        Assert.AreEqual(1, fixture.Http.LegacyRequests);
        Assert.AreEqual(StatusLevel.Success, fixture.Status.Last());
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("missing")]
    [DataRow("repeated")]
    public async Task QobuzIncompleteLaterPagesKeepThePrefixWithoutUsingLegacyFallback(string shape)
    {
        using var fixture = new Fixture("qobuz");
        fixture.Http.LaterPageShape = shape;
        fixture.Http.Release.TrySetResult();
        var items = new List<SongSearchObject>();
        await fixture.Load(items, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1" }, items.Select(item => item.Id).ToArray());
        Assert.AreEqual(StatusLevel.Warning, fixture.Status.Last());
        Assert.AreEqual(2, fixture.Http.Pages);
        Assert.AreEqual(0, fixture.Http.LegacyRequests);
    }

    [TestMethod]
    public async Task QobuzSessionChangeStopsBeforePublishingThePendingPage()
    {
        using var fixture = new Fixture("qobuz");
        var items = new List<SongSearchObject>();
        var load = fixture.Load(items, CancellationToken.None);
        await fixture.Http.NextEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.InvalidateQobuzSession();
        fixture.Http.Release.TrySetResult();
        await load;
        CollectionAssert.AreEqual(new[] { "1", "1" }, items.Select(item => item.Id).ToArray());
        Assert.AreEqual(StatusLevel.Warning, fixture.Status.Last());
    }

    [TestMethod]
    public async Task QobuzDoesNotPublishIncompleteDetailFallbacks()
    {
        using var fixture = new Fixture("qobuz");
        fixture.Http.IncompleteTrack = fixture.Http.IncompleteDetail = true;
        fixture.Http.Release.TrySetResult();
        var items = new List<SongSearchObject>();
        await fixture.Load(items, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "2" }, items.Select(item => item.Id).ToArray());
        Assert.AreEqual(1, fixture.Http.DetailRequests);
        Assert.AreEqual(StatusLevel.Warning, fixture.Status.Last());
    }

    [TestMethod]
    public async Task QobuzPlaylistPublishesSpacedArtistCreditsWithoutExtraDetailRequests()
    {
        using var fixture = new Fixture("qobuz");
        fixture.Http.SpacedArtistCredits = true;
        fixture.Http.Release.TrySetResult();
        var items = new List<SongSearchObject>();
        await fixture.Load(items, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, items.Select(item => item.Id).ToArray());
        Assert.IsTrue(items.All(item => item.Artists == "Calvin Harris, Disciples"));
        Assert.AreEqual(0, fixture.Http.DetailRequests);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Handler Http;
        internal readonly List<StatusLevel> Status = [];
        private readonly ILinkSource source;
        private readonly string url;
        private readonly HttpClient http;

        internal Fixture(string provider)
        {
            Http = new(provider);
            http = new(Http);
            var settings = new TidalParsingTests.DictionarySettings(new()
            {
                [SettingKeys.TidalClientId] = "test-client", [SettingKeys.TidalClientSecret] = "test-secret",
            });
            if (provider == "spotify")
            {
                var auth = new SpotifyUserAuthorization(new AuthorizationCodeTokenResponse
                {
                    AccessToken = "synthetic", CreatedAt = DateTime.UtcNow, ExpiresIn = 3600,
                    Scope = string.Join(" ", SpotifyUserAuthorization.PlaylistScopes),
                }, _ => throw new AssertFailedException("No token refresh is expected."));
                var spotify = new SpotifySource(settings, null!, null!) { IsInitialized = true };
                Set(spotify, "spotifyUser", new SpotifyClient(SpotifyClientConfig.CreateDefault()
                    .WithHTTPClient(new NetHttpClient(http)).WithAuthenticator(auth)));
                Set(spotify, "userAuthorization", auth);
                source = spotify;
                url = "https://open.spotify.com/playlist/0000000000000000000999";
            }
            else if (provider == "tidal")
            {
                source = new TidalSource(new TidalApi(settings, http));
                url = "https://tidal.com/playlist/00000000-0000-0000-0000-000000000999";
            }
            else
            {
                var qobuz = new QobuzSource(settings) { CatalogRequests = new ProviderRequestPolicy("Qobuz test", interval: TimeSpan.Zero, transientRetries: 0) };
                Set(qobuz, "QobuzHttpClient", http);
                Set(qobuz, "HttpClientInitialized", true);
                source = qobuz;
                url = "https://play.qobuz.com/playlist/999";
            }
        }
        internal Task Load(List<SongSearchObject> items, CancellationToken token) => source.AddFromLinkAsync(items, url,
            new(true, new(0, 0), false), (level, _, _) => Status.Add(level), token);
        internal void InvalidateQobuzSession() => Set(source, "HttpClientInitialized", false);
        public void Dispose() { Http.Release.TrySetResult(); http.Dispose(); }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private sealed class Handler(string provider) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource NextEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailNext, IncompleteTrack, IncompleteDetail, LegacyIds, LegacyMap, RejectTrackPages;
        internal bool SpacedArtistCredits;
        internal string? LaterPageShape;
        internal int Pages, DetailRequests, LegacyRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "auth.tidal.com") return Reply(new { access_token = "synthetic", expires_in = 3600, token_type = "Bearer" });
            Assert.AreEqual(HttpMethod.Get, request.Method, "Playlist imports must not send mutations.");
            if (provider == "spotify" && !path.EndsWith("tracks") && !path.EndsWith("items")) return Reply(new { name = "Playlist" });
            if (path.EndsWith("track/get"))
            {
                DetailRequests++;
                var detail = JObject.FromObject(QobuzTrack(int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["track_id"]!)));
                if (IncompleteDetail) detail.Remove("isrc");
                return Reply(detail);
            }
            if (provider == "qobuz" && LegacyIds)
            {
                var extra = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["extra"];
                if (extra == "track_ids") LegacyRequests++;
                else
                {
                    Pages++;
                    return RejectTrackPages ? Reply(new { error = "Unsupported extra" }, HttpStatusCode.BadRequest) : Reply(new { name = "Playlist" });
                }
                return LegacyMap
                    ? Reply(new { name = "Legacy playlist", track_ids = new JObject { ["2"] = 2, ["0"] = 1, ["1"] = 1 } })
                    : Reply(new { name = "Legacy playlist", track_ids = new[] { 1, 1, 2 } });
            }
            var next = ++Pages > 1;
            if (next)
            {
                NextEntered.TrySetResult();
                await Release.Task.WaitAsync(token);
                if (FailNext) return Reply(new { error = new { status = 400, message = "Synthetic page failure" } }, HttpStatusCode.BadRequest);
            }
            if (provider == "spotify")
            {
                var track = SpotifyTrack(next ? "2" : "1");
                var entry = new { track, item = track };
                return Reply(new { items = next ? new[] { entry } : new[] { entry, entry }, offset = next ? 2 : 0, limit = 2, total = 3,
                    next = next ? null : "https://api.spotify.com/v1/playlists/0000000000000000000999/tracks?offset=2" });
            }
            if (provider == "qobuz")
            {
                if (next && LaterPageShape == "missing") return Reply(new { name = "Playlist", track_ids = new[] { 1, 1, 2 } });
                if (next && LaterPageShape == "empty") return Reply(new { name = "Playlist", tracks = new { items = Array.Empty<object>(), offset = 2, total = 3 } });
                if (next && LaterPageShape == "repeated") return Reply(new { name = "Playlist", tracks = new { items = new[] { QobuzTrack(1) }, offset = 0, total = 3 } });
                var track = JObject.FromObject(QobuzTrack(next ? 2 : 1));
                if (SpacedArtistCredits)
                {
                    track["performers"] = QobuzArtistCreditTests.HowDeepCredits;
                    track["performer"] = new JObject { ["name"] = "Calvin Harris" };
                }
                if (!next && IncompleteTrack) track.Remove("isrc");
                return Reply(new { name = "Playlist", tracks_count = 3, tracks = new { items = next ? new[] { track } : new[] { track, track }, offset = next ? 2 : 0, total = 3 } });
            }
            var id = next ? "2" : "1";
            var reference = new { id, type = "tracks" };
            var included = TidalIncluded(id);
            return next
                ? Reply(new { data = new[] { reference }, included })
                : Reply(new
                {
                    data = new { id = "playlist", type = "playlists", attributes = new { name = "Playlist" }, relationships = new
                    { items = new { data = new[] { reference, reference }, links = new { next = "https://openapi.tidal.com/v2/playlist-next?cursor=2" } } } },
                    included,
                });
        }
    }

    private static object QobuzTrack(int id) => new
    {
        id, title = "Song", isrc = "USABC2400001", duration = 239, track_number = 1, parental_warning = false,
        release_date_original = "2024-01-02", performer = new { name = "Artist" }, performers = "Artist, MainArtist",
        album = new { title = "Album", popularity = 10, image = new { small = "https://covers.example.test/a.jpg" } },
    };
    private static object SpotifyTrack(string id) => new
    {
        id, type = "track", name = "Song", duration_ms = 239000, track_number = 1, @explicit = false,
        artists = new[] { new { name = "Artist" } }, external_ids = new { isrc = "USABC2400001" },
        album = new { id = "album", name = "Album", release_date = "2024-01-02", images = new[]
        { new { url = "https://covers.example.test/large.jpg" }, new { url = "https://covers.example.test/a.jpg" } } },
    };
    private static object[] TidalIncluded(string id) =>
    [
        new { id, type = "tracks", attributes = new { title = "Song", isrc = "USABC2400001", duration = "PT3M59S" }, relationships = new
        { artists = new { data = new[] { new { type = "artists", id = "artist" } } }, albums = new { data = new[] { new { type = "albums", id = "album" } } } } },
        new { id = "artist", type = "artists", attributes = new { name = "Artist" } },
        new { id = "album", type = "albums", attributes = new { title = "Album", releaseDate = "2024-01-02" } },
    ];
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };
}
