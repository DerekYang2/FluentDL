using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class DeezerPlaylistImportTests
{
    private static DeezerPlaylistSession Session() => new("test-arl", "test-sid", "test-form", new object(), "7");

    [TestMethod]
    public async Task ReadOnlyImportPaginatesAndKeepsOrderAndDuplicatesWithoutRequiringOwnership()
    {
        var gateway = new Gateway { TrackIds = ["4", "4", "2", "8", "1"] };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        var contents = await client.ReadPlaylistAsync("123", CancellationToken.None);
        Assert.AreEqual("Private fixture", contents.Name);
        CollectionAssert.AreEqual(gateway.TrackIds, contents.TrackIds.ToArray());
        CollectionAssert.AreEqual(new[] { "deezer.pagePlaylist", "playlist.getSongs", "playlist.getSongs", "playlist.getSongs" }, gateway.Methods.ToArray());
        Assert.IsNull(client.ReadAccess, "Importing a readable playlist does not replace the authenticated account check.");
    }

    [TestMethod]
    public async Task AccessCheckUsesTheAuthenticatedAccountAndPlaylistReadPath()
    {
        var gateway = new Gateway();
        DeezerPlaylistSession? active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        Assert.IsNull(client.ReadAccess);
        await client.CheckReadAccessAsync(CancellationToken.None);
        Assert.AreEqual(true, client.ReadAccess);
        CollectionAssert.AreEqual(new[] { "deezer.getUserData", "deezer.pageProfile" }, gateway.Methods.ToArray());
        active = Session();
        Assert.IsNull(client.ReadAccess, "A previous account's check must not apply to a new session.");
        active = null;
        Assert.IsFalse(client.CanRead);
        await client.CheckReadAccessAsync(CancellationToken.None);
        Assert.IsNull(client.ReadAccess);
        Assert.AreEqual(2, gateway.Methods.Count);
    }

    [TestMethod]
    [DataRow("deezer.getUserData")]
    [DataRow("deezer.pageProfile")]
    public async Task FailedAccessChecksDoNotAdvertisePrivateReads(string denied)
    {
        var gateway = new Gateway { DeniedMethod = denied };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await client.CheckReadAccessAsync(CancellationToken.None);
        Assert.AreEqual(false, client.ReadAccess);
    }

    [TestMethod]
    public async Task AnonymousOrWrongAccountResponsesCannotPassTheAccessCheck()
    {
        var gateway = new Gateway { UserId = "0" };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await client.CheckReadAccessAsync(CancellationToken.None);
        Assert.AreEqual(false, client.ReadAccess);
        Assert.AreEqual(1, gateway.Methods.Count);
    }

    [TestMethod]
    public async Task SignOutDuringPaginationStopsBeforeAnotherAuthenticatedRequest()
    {
        DeezerPlaylistSession? active = Session();
        var gateway = new Gateway { TrackIds = ["1", "2", "3"], AfterRequest = method => { if (method == "playlist.getSongs") active = null; } };
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await Assert.ThrowsExceptionAsync<PlaylistWriteRejected>(() => client.ReadPlaylistAsync("123", CancellationToken.None));
        Assert.AreEqual(2, gateway.Methods.Count);
        Assert.IsNull(client.ReadAccess);
    }

    [TestMethod]
    public async Task CancelledOrSignedOutImportsSendNothing()
    {
        var gateway = new Gateway();
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.ReadPlaylistAsync("123", cancellation.Token));
        var signedOut = new DeezerPlaylistClient(() => null, new HttpClient(gateway));
        await Assert.ThrowsExceptionAsync<PlaylistWriteRejected>(() => signedOut.ReadPlaylistAsync("123", CancellationToken.None));
        Assert.AreEqual(0, gateway.Methods.Count);
    }

    [TestMethod]
    public async Task CancellationBetweenPagesDoesNotReturnAPartialPlaylist()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new Gateway { TrackIds = ["1", "2", "3"], AfterRequest = method => { if (method == "playlist.getSongs") cancellation.Cancel(); } };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => client.ReadPlaylistAsync("123", cancellation.Token));
        Assert.AreEqual(2, gateway.Methods.Count);
        Assert.IsNull(client.ReadAccess);
    }

    [TestMethod]
    public async Task AccountChangeDuringTheAccessCheckDoesNotReadAnotherProfileOrKeepTheOldResult()
    {
        var active = Session();
        var gateway = new Gateway { AfterRequest = _ => active = Session() };
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await client.CheckReadAccessAsync(CancellationToken.None);
        Assert.IsNull(client.ReadAccess);
        Assert.AreEqual(1, gateway.Methods.Count);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("123/path")]
    [DataRow("999999999999999999999999999")]
    public async Task InvalidPlaylistIdsNeverReachTheGateway(string id)
    {
        var gateway = new Gateway();
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await Assert.ThrowsExceptionAsync<PlaylistWriteRejected>(() => client.ReadPlaylistAsync(id, CancellationToken.None));
        Assert.AreEqual(0, gateway.Methods.Count);
    }

    [TestMethod]
    public async Task EmptyPageBeforeTotalFailsRatherThanReturningAPartialPlaylist()
    {
        var gateway = new Gateway { TrackIds = ["1"], Incomplete = true };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await Assert.ThrowsExceptionAsync<JsonException>(() => client.ReadPlaylistAsync("123", CancellationToken.None));
        Assert.IsNull(client.ReadAccess);
    }

    [TestMethod]
    [DataRow("https://www.deezer.com/playlist/123", "123")]
    [DataRow("https://www.deezer.com/en/playlist/123/?utm_source=test", "123")]
    [DataRow("https://www.deezer.com/en-US/playlist/123#tracks", "123")]
    [DataRow("https://www.deezer.com/album/123", "")]
    [DataRow("https://www.deezer.com.evil.test/playlist/123", "")]
    [DataRow("https://www.deezer.com/playlist/0", "")]
    [DataRow("https://www.deezer.com/playlist/abc", "")]
    public void PlaylistIdParsingIgnoresSharingParametersAndRejectsOtherLinks(string url, string expected)
    {
        Assert.AreEqual(expected.Length > 0, DeezerSource.TryGetPlaylistId(url, out var id));
        Assert.AreEqual(expected, id);
    }

    [TestMethod]
    public async Task PlaylistLinkImportUsesTheGatewayThenLoadsCatalogMetadataInPlaylistOrder()
    {
        await using var catalog = new Catalog();
        var gateway = new Gateway { TrackIds = ["4", "2", "4"] };
        var active = Session();
        var client = new DeezerPlaylistClient(() => active, new HttpClient(gateway));
        await client.CheckReadAccessAsync(CancellationToken.None);
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!, client)
        {
            IsInitialized = true, restClient = new RestHelper(catalog.Address),
        };
        var tracks = new List<SongSearchObject>();
        var statuses = new List<StatusLevel>();
        await source.AddFromLinkAsync(tracks, "https://www.deezer.com/en/playlist/123?utm_source=test",
            new(true, new(0, 10), false), (level, _, _) => statuses.Add(level), CancellationToken.None);
        CollectionAssert.AreEqual(gateway.TrackIds, tracks.Select(track => track.Id).ToArray());
        Assert.AreEqual(StatusLevel.Success, statuses.Last());
        Assert.AreEqual(true, source.PlaylistReadAccess);
        Assert.IsTrue(catalog.Paths.All(path => path.StartsWith("/track/")), "Playlist reads must not use the public catalog API.");
        Assert.AreEqual(2, catalog.Paths.Count, "Duplicate playlist entries should reuse fetched metadata.");
    }

    [TestMethod]
    public async Task CompleteBatchMetadataNeedsNoIndividualRequestsAndKeepsSeparateDuplicateModels()
    {
        await using var catalog = new Catalog();
        var gateway = new Gateway { TrackIds = ["4", "2", "4"], BatchRecord = BatchTrack };
        var tracks = await Import(gateway, catalog);
        CollectionAssert.AreEqual(gateway.TrackIds, tracks.Select(track => track.Id).ToArray());
        Assert.AreEqual(0, catalog.Paths.Count);
        Assert.AreEqual(1, gateway.Batches.Count);
        CollectionAssert.AreEqual(new[] { "4", "2" }, gateway.Batches.Single());
        Assert.AreNotSame(tracks[0], tracks[2]);
        Assert.IsTrue(tracks.All(track => track.Isrc == "USABC2400001" && track.ReleaseDate == "2024-01-02"));
        Assert.IsTrue(tracks.All(track => track.Artists == "Artist A, Artist B"));
    }

    [TestMethod]
    public async Task IncompleteBatchRecordFallsBackBeforeAddingOnlyThatTrack()
    {
        await using var catalog = new Catalog();
        var gateway = new Gateway { TrackIds = ["4", "2", "4"], BatchRecord = id =>
        {
            var record = BatchTrack(id);
            if (id == "2") record.Remove("ISRC");
            return record;
        } };
        var tracks = await Import(gateway, catalog);
        CollectionAssert.AreEqual(gateway.TrackIds, tracks.Select(track => track.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "/track/2" }, catalog.Paths.ToArray());
        Assert.IsTrue(tracks.All(track => !string.IsNullOrEmpty(track.Isrc)));
        Assert.IsTrue(tracks.All(track => track.Artists == "Artist A, Artist B"), "Individual fallback must apply the same artist deduplication.");
    }

    [TestMethod]
    public async Task MetadataBatchesAreBoundedAndDuplicatesAcrossBatchesDoNotRefetch()
    {
        await using var catalog = new Catalog();
        var gateway = new Gateway { TrackIds = Enumerable.Range(1, 51).Select(id => id.ToString()).Append("1").ToArray(), BatchRecord = BatchTrack };
        var tracks = await Import(gateway, catalog);
        CollectionAssert.AreEqual(gateway.TrackIds, tracks.Select(track => track.Id).ToArray());
        Assert.AreEqual(0, catalog.Paths.Count);
        CollectionAssert.AreEqual(new[] { 50, 1 }, gateway.Batches.Select(batch => batch.Length).ToArray());
    }

    [TestMethod]
    public async Task FailedBatchFallsBackWithoutRetryingTheUnsupportedRouteForEachPage()
    {
        await using var catalog = new Catalog();
        var gateway = new Gateway { TrackIds = Enumerable.Range(1, 51).Select(id => id.ToString()).Append("1").ToArray(), DeniedMethod = "song.getListData" };
        var tracks = await Import(gateway, catalog);
        CollectionAssert.AreEqual(gateway.TrackIds, tracks.Select(track => track.Id).ToArray());
        Assert.AreEqual(1, gateway.Methods.Count(method => method == "song.getListData"));
        Assert.AreEqual(51, catalog.Paths.Count);
    }

    [TestMethod]
    public async Task AccountChangeDuringMetadataBatchDoesNotEnqueueOrFallBack()
    {
        await using var catalog = new Catalog();
        var active = Session();
        var gateway = new Gateway { TrackIds = ["1"], BatchRecord = BatchTrack,
            AfterRequest = method => { if (method == "song.getListData") active = Session(); } };
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!,
            new DeezerPlaylistClient(() => active, new HttpClient(gateway)))
            { IsInitialized = true, restClient = new RestHelper(catalog.Address) };
        var tracks = new List<SongSearchObject>();
        var statuses = new List<StatusLevel>();
        await source.AddFromLinkAsync(tracks, "https://www.deezer.com/playlist/123", new(true, new(0, 10), false),
            (level, _, _) => statuses.Add(level), CancellationToken.None);
        Assert.AreEqual(0, tracks.Count);
        Assert.AreEqual(0, catalog.Paths.Count);
        Assert.AreEqual(StatusLevel.Error, statuses.Last());
    }

    [TestMethod]
    public async Task CancelledMetadataBatchDoesNotFallBackOrEnqueueIncompleteTracks()
    {
        await using var catalog = new Catalog();
        using var cancellation = new CancellationTokenSource();
        var active = Session();
        var gateway = new Gateway { TrackIds = ["1"], BatchRecord = BatchTrack,
            AfterRequest = method => { if (method == "song.getListData") cancellation.Cancel(); } };
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!,
            new DeezerPlaylistClient(() => active, new HttpClient(gateway)))
            { IsInitialized = true, restClient = new RestHelper(catalog.Address) };
        var tracks = new List<SongSearchObject>();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => source.AddFromLinkAsync(tracks,
            "https://www.deezer.com/playlist/123", new(true, new(0, 10), false), null, cancellation.Token));
        Assert.AreEqual(0, tracks.Count);
        Assert.AreEqual(0, catalog.Paths.Count);
    }

    private static async Task<List<SongSearchObject>> Import(Gateway gateway, Catalog catalog)
    {
        var active = Session();
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!,
            new DeezerPlaylistClient(() => active, new HttpClient(gateway)))
            { IsInitialized = true, restClient = new RestHelper(catalog.Address) };
        var tracks = new List<SongSearchObject>();
        await source.AddFromLinkAsync(tracks, "https://www.deezer.com/playlist/123", new(true, new(0, 10), false), null, CancellationToken.None);
        return tracks;
    }

    internal static Dictionary<string, object> BatchTrack(string id) => new()
    {
        ["SNG_ID"] = id, ["ALB_ID"] = "123", ["SNG_TITLE"] = "Fixture track", ["VERSION"] = "(Remix)",
        ["ALB_TITLE"] = "Fixture album", ["ART_NAME"] = "Artist A", ["ISRC"] = "USABC2400001",
        ["DURATION"] = "239", ["TRACK_NUMBER"] = "2", ["RANK_SNG"] = "100",
        ["DIGITAL_RELEASE_DATE"] = "2024-01-02", ["PHYSICAL_RELEASE_DATE"] = "2024-02-03", ["EXPLICIT_LYRICS"] = "1",
        ["ARTISTS"] = new[] { new { ART_NAME = "Artist A" }, new { ART_NAME = "Artist B" }, new { ART_NAME = "Artist B" } },
        ["MEDIA"] = new[] { new { HREF = "https://cdnt-preview.dzcdn.net/preview.mp3?test=1" } },
    };

    [TestMethod]
    public async Task DeniedPrivatePlaylistGivesAnActionableErrorAndAddsNothing()
    {
        var gateway = new Gateway { DeniedMethod = "deezer.pagePlaylist" };
        var active = Session();
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!,
            new DeezerPlaylistClient(() => active, new HttpClient(gateway))) { IsInitialized = true };
        var tracks = new List<SongSearchObject>();
        var statuses = new List<(StatusLevel Level, string Text)>();
        await source.AddFromLinkAsync(tracks, "https://www.deezer.com/playlist/123", new(true, new(0, 10), false),
            (level, text, _) => statuses.Add((level, text)), CancellationToken.None);
        Assert.AreEqual(0, tracks.Count);
        Assert.AreEqual(StatusLevel.Error, statuses.Single().Level);
        StringAssert.Contains(statuses.Single().Text, "signed-in account");
        Assert.AreEqual(1, gateway.Methods.Count);
    }

    [TestMethod]
    public async Task NormalTrackSearchUsesOneBatchAndPreservesSearchOrderAndPaging()
    {
        await using var catalog = new Catalog { SearchIds = ["4", "2", "4"] };
        var gateway = new Gateway { BatchRecord = BatchTrack };
        var source = SearchSource(catalog, gateway, Session());
        var tracks = new List<SongSearchObject>();
        await source.GeneralSearch(tracks, "fixture query", CancellationToken.None, offset: 25, limit: 25);
        CollectionAssert.AreEqual(catalog.SearchIds, tracks.Select(track => track.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "/search" }, catalog.Paths.ToArray());
        StringAssert.Contains(catalog.Queries.Single(), "index=25");
        StringAssert.Contains(catalog.Queries.Single(), "limit=25");
        CollectionAssert.AreEqual(new[] { "4", "2" }, gateway.Batches.Single());
        Assert.AreNotSame(tracks[0], tracks[2]);
        Assert.IsTrue(tracks.All(track => track.Isrc == "USABC2400001" && track.ReleaseDate == "2024-01-02" && track.Artists == "Artist A, Artist B"));
    }

    [TestMethod]
    public async Task SearchBatchFallsBackOnlyForTheIncompleteResult()
    {
        await using var catalog = new Catalog { SearchIds = ["4", "2", "4"] };
        var gateway = new Gateway { BatchRecord = id =>
        {
            var record = BatchTrack(id);
            if (id == "2") record.Remove("ISRC");
            return record;
        } };
        var tracks = new List<SongSearchObject>();
        await SearchSource(catalog, gateway, Session()).GeneralSearch(tracks, "query", CancellationToken.None);
        CollectionAssert.AreEqual(catalog.SearchIds, tracks.Select(track => track.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "/search", "/track/2" }, catalog.Paths.ToArray());
        Assert.IsTrue(tracks.All(track => track.Isrc == "USABC2400001"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SignedOutOrUnsupportedSearchBatchesRetainIndividualTrackReads(bool signedIn)
    {
        await using var catalog = new Catalog { SearchIds = ["4", "2", "4"] };
        var gateway = new Gateway { DeniedMethod = "song.getListData" };
        var tracks = new List<SongSearchObject>();
        await SearchSource(catalog, gateway, signedIn ? Session() : null).GeneralSearch(tracks, "query", CancellationToken.None);
        CollectionAssert.AreEqual(catalog.SearchIds, tracks.Select(track => track.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "/search", "/track/4", "/track/2" }, catalog.Paths.ToArray());
        Assert.AreEqual(signedIn ? 1 : 0, gateway.Methods.Count);
    }

    [TestMethod]
    public async Task LargeSearchPagesUseSequentialBatchesOfAtMostFiftyUniqueIds()
    {
        await using var catalog = new Catalog { SearchIds = Enumerable.Range(1, 51).Select(id => id.ToString()).Append("1").ToArray() };
        var gateway = new Gateway { BatchRecord = BatchTrack };
        var tracks = new List<SongSearchObject>();
        await SearchSource(catalog, gateway, Session()).GeneralSearch(tracks, "query", CancellationToken.None, limit: 100);
        CollectionAssert.AreEqual(catalog.SearchIds, tracks.Select(track => track.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 50, 1 }, gateway.Batches.Select(batch => batch.Length).ToArray());
        Assert.AreEqual(1, catalog.Paths.Count);
    }

    [TestMethod]
    public async Task SearchSessionChangeDoesNotPublishOrFallBackToAnotherAccount()
    {
        await using var catalog = new Catalog { SearchIds = ["4"] };
        DeezerPlaylistSession? active = Session();
        var gateway = new Gateway { BatchRecord = BatchTrack, AfterRequest = _ => active = null };
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!,
            new DeezerPlaylistClient(() => active, new HttpClient(gateway)))
            { IsInitialized = true, restClient = new RestHelper(catalog.Address) };
        var tracks = new List<SongSearchObject>();
        await Assert.ThrowsExceptionAsync<PlaylistWriteRejected>(() => source.GeneralSearch(tracks, "query", CancellationToken.None));
        Assert.AreEqual(0, tracks.Count);
        CollectionAssert.AreEqual(new[] { "/search" }, catalog.Paths.ToArray());
    }

    [TestMethod]
    public async Task CancelledSearchBatchDoesNotFetchIndividualMetadataOrPublishTracks()
    {
        await using var catalog = new Catalog { SearchIds = ["4"] };
        using var cancellation = new CancellationTokenSource();
        var gateway = new Gateway { BatchRecord = BatchTrack, AfterRequest = _ => cancellation.Cancel() };
        var tracks = new List<SongSearchObject>();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => SearchSource(catalog, gateway, Session())
            .GeneralSearch(tracks, "query", cancellation.Token));
        Assert.AreEqual(0, tracks.Count);
        CollectionAssert.AreEqual(new[] { "/search" }, catalog.Paths.ToArray());
    }

    private static DeezerSource SearchSource(Catalog catalog, Gateway gateway, DeezerPlaylistSession? active) =>
        new(new TidalParsingTests.DictionarySettings([]), null!, new DeezerPlaylistClient(() => active, new HttpClient(gateway)))
        { IsInitialized = true, restClient = new RestHelper(catalog.Address) };

    private sealed class Gateway : HttpMessageHandler
    {
        internal string[] TrackIds = [];
        internal string UserId = "7";
        internal string? DeniedMethod;
        internal bool Incomplete;
        internal Action<string>? AfterRequest;
        internal List<string> Methods { get; } = [];
        internal Func<string, object?>? BatchRecord;
        internal List<string[]> Batches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.AreEqual("www.deezer.com", request.RequestUri!.Host);
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("arl=test-arl; sid=test-sid", request.Headers.GetValues("Cookie").Single());
            StringAssert.Contains(request.RequestUri.Query, "api_token=test-form");
            var method = Uri.UnescapeDataString(request.RequestUri.Query.Split("method=")[1].Split('&')[0]);
            Methods.Add(method);
            if (method == DeniedMethod) return new(HttpStatusCode.OK) { Content = new StringContent("{\"error\":{\"PERMISSION_ERROR\":\"Denied\"},\"results\":{}}") };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (method == "song.getListData") Batches.Add(body.RootElement.GetProperty("SNG_IDS").EnumerateArray().Select(id => id.ToString()).ToArray());
            object result = method switch
            {
                "deezer.getUserData" => new { USER = new { USER_ID = UserId } },
                "deezer.pageProfile" => new { TAB = new { playlists = new { data = Array.Empty<object>(), total = 0 } } },
                "deezer.pagePlaylist" => new { DATA = new { TITLE = "Private fixture", PARENT_USER_ID = "8", STATUS = 1 } },
                "playlist.getSongs" => new
                {
                    data = TrackIds.Skip(body.RootElement.GetProperty("start").GetInt32()).Take(2).Select(id => new { SNG_ID = id }).ToArray(),
                    total = TrackIds.Length + (Incomplete ? 1 : 0),
                },
                "song.getListData" => new { data = Batches.Last().Reverse().Select(id => BatchRecord?.Invoke(id)).Where(record => record != null).ToArray() },
                _ => throw new AssertFailedException("Unexpected request, imports must not write: " + method),
            };
            AfterRequest?.Invoke(method);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { }, results = result })) };
        }
    }

    private sealed class Catalog : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task worker;
        internal List<string> Paths { get; } = [];
        internal List<string> Queries { get; } = [];
        internal string[] SearchIds = [];
        internal string Address { get; }

        internal Catalog()
        {
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            Address = $"http://localhost:{((IPEndPoint)port.LocalEndpoint).Port}/";
            port.Stop();
            listener.Prefixes.Add(Address);
            listener.Start();
            worker = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); }
                    catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { break; }
                    var path = context.Request.Url!.AbsolutePath;
                    Paths.Add(path);
                    Queries.Add(context.Request.Url.Query);
                    object response = path == "/search" ? new { data = SearchIds.Select(id => new { id }).ToArray() }
                        : new { id = path.Split('/').Last(), title = "Fixture track", isrc = "USABC2400001", artist = new { name = "Artist A" },
                            contributors = new[] { new { name = "Artist A" }, new { name = "Artist B" }, new { name = "Artist B" } } };
                    var json = JsonSerializer.Serialize(response);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            listener.Close();
            await worker;
        }
    }
}
