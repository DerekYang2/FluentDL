using System.Net;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class DeezerQobuzPlaylistTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task QobuzOpenPlaylistLinksAlwaysUseTheWebPlayer(bool existing, bool failAppend)
    {
        var http = new FakeHttp("qobuz") { FailAppend = failAppend ? 1 : 0 };
        var client = Client("qobuz", http);
        var listed = await client.List(CancellationToken.None);
        foreach (var playlist in listed)
            Assert.AreEqual("https://play.qobuz.com/playlist/" + playlist.Id, playlist.Url.AbsoluteUri);
        var result = await client.Create(Request(1) with { Target = existing ? Target("qobuz") : null }, null, CancellationToken.None);
        Assert.AreEqual("https://play.qobuz.com/playlist/123456789", result.Url?.AbsoluteUri);
        Assert.AreEqual(failAppend ? PlaylistCreationStatus.Partial : PlaylistCreationStatus.Completed, result.Status);
    }

    [TestMethod]
    public async Task SourceAdaptersRequireCurrentLoginStateAndAdvertiseOnlySupportedDestinations()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        // The download converter is not used by playlist creation.
        var deezer = new DeezerSource(settings, null!);
        var qobuz = new QobuzSource(settings);
        Assert.IsFalse(await deezer.CanCreatePlaylistAsync(CancellationToken.None));
        Assert.IsFalse(await qobuz.CanCreatePlaylistAsync(CancellationToken.None));
        deezer.IsInitialized = true;
        qobuz.IsInitialized = true;
        Assert.IsFalse(await deezer.CanCreatePlaylistAsync(CancellationToken.None), "An initialization flag without an authenticated session is insufficient.");
        Assert.IsFalse(await qobuz.CanCreatePlaylistAsync(CancellationToken.None), "A stale API client without a current login must not enable writes.");
        Assert.AreEqual(PlaylistCreationStatus.Failed, (await qobuz.CreatePlaylistAsync(Request(1), null, CancellationToken.None)).Status);
        Assert.AreEqual(PlaylistCreationStatus.Failed, (await deezer.CreatePlaylistAsync(Request(1), null, CancellationToken.None)).Status);
        var registry = new SourceRegistry([deezer, qobuz]);
        Assert.AreEqual(2, registry.All<IPlaylistWriter>().Count());
        var spotify = new SpotifySource(settings, null!, null!) { IsInitialized = true };
        Assert.IsTrue(typeof(IPlaylistWriter).IsAssignableFrom(typeof(SpotifySource)));
        Assert.IsFalse(await spotify.CanCreatePlaylistAsync(CancellationToken.None), "Spotify catalog or anonymous sign-in does not grant user playlist access.");
        Assert.IsFalse(typeof(IPlaylistWriter).IsAssignableFrom(typeof(YoutubeSource)));
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task CreatesPrivateAndAppends51TracksInOrder(string source)
    {
        var http = new FakeHttp(source) { PageSize = 20 };
        var client = Client(source, http);
        var progress = new ProgressLog();
        var result = await client.Create(Request(51), progress, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(51, result.Added);
        Assert.AreEqual("0", http.PublicFlag);
        Assert.AreEqual(2, http.AppendCalls);
        Assert.AreEqual(1, http.CreateCalls);
        CollectionAssert.AreEqual(Request(51).TrackIds.ToArray(), http.Tracks.ToArray());
        CollectionAssert.AreEqual(new[] { 50, 51 }, progress.Values.ToArray());
        Assert.IsNotNull(result.Url);
        Assert.IsTrue(result.Url.Host.EndsWith(source == "deezer" ? "deezer.com" : "qobuz.com"));
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task PublicIsExplicit(string source)
    {
        var http = new FakeHttp(source);
        var result = await Client(source, http).Create(Request(1) with { Visibility = PlaylistVisibility.Public }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual("1", http.PublicFlag);
    }

    [TestMethod]
    [DataRow("deezer", 403)]
    [DataRow("deezer", 429)]
    [DataRow("qobuz", 403)]
    [DataRow("qobuz", 429)]
    public async Task FailedAppendKeepsTheLinkAndNeverRetries(string source, int code)
    {
        var http = new FakeHttp(source) { FailAppend = 2, Failure = (HttpStatusCode)code };
        var result = await Client(source, http).Create(Request(51), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(50, result.Added);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(2, http.AppendCalls);
        Assert.AreEqual(1, http.CreateCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ConnectionLossIsUncertainAndNeverReplayed(string source)
    {
        var http = new FakeHttp(source) { LoseConnection = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(1, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task VerificationFailureKeepsTheKnownPlaylistLink(string source)
    {
        var http = new FakeHttp(source) { FailReadback = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(1, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task UnconfirmedVisibilityStopsBeforeUploadingTracks(string source)
    {
        var http = new FakeHttp(source) { WrongVisibility = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(0, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task SignedOutNeverSendsARequest(string source)
    {
        var http = new FakeHttp(source);
        var result = await Client(source, http, signedIn: false).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, http.CreateCalls);
    }

    [TestMethod]
    public async Task DeezerRejectsNamesThatItsGatewayWouldSilentlyTruncate()
    {
        var http = new FakeHttp("deezer");
        var result = await Client("deezer", http).Create(Request(1) with { Name = new string('x', 51) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, http.CreateCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task SwitchingAccountsStopsWithoutAppendingToAnotherAccount(string source)
    {
        var http = new FakeHttp(source);
        var client = Client(source, http);
        var result = await client.Create(Request(51), new ProgressLog(_ => client.SignOut()), CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(50, result.Added);
        Assert.AreEqual(1, http.AppendCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    public async Task DeezerSkipsDuplicatesBeforeCreating()
    {
        var http = new FakeHttp("deezer");
        var result = await Client("deezer", http).Create(Request(3) with { TrackIds = ["1", "2", "1"] }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(1, http.CreateCalls);
        Assert.AreEqual(2, result.Added);
        Assert.AreEqual(1, result.Skipped);
    }

    [TestMethod]
    public async Task QobuzSkipsDuplicatesWithoutChangingFirstOccurrenceOrder()
    {
        var http = new FakeHttp("qobuz");
        var result = await Client("qobuz", http).Create(Request(3) with { TrackIds = ["1", "2", "1"] }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(1, http.AppendCalls);
        Assert.AreEqual(1, result.Skipped);
        CollectionAssert.AreEqual(new[] { "1", "2" }, http.Tracks.ToArray());
    }

    [TestMethod]
    public async Task DeezerTokenErrorsDoNotTriggerAutomaticGatewayRetries()
    {
        var http = new FakeHttp("deezer") { TokenError = true };
        var result = await Client("deezer", http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(1, http.AppendCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task CancellationBetweenBatchesKeepsConfirmedProgress(string source)
    {
        var http = new FakeHttp(source);
        using var cancellation = new CancellationTokenSource();
        var result = await Client(source, http).Create(Request(51), new ProgressLog(_ => cancellation.Cancel()), cancellation.Token);
        Assert.AreEqual(PlaylistCreationStatus.Cancelled, result.Status);
        Assert.AreEqual(50, result.Added);
        Assert.AreEqual(1, http.AppendCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task CancellationDuringAppendIsUncertain(string source)
    {
        var http = new FakeHttp(source) { CancelAppend = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(1, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task CancellationBeforeCreationSendsNothing(string source)
    {
        var http = new FakeHttp(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await Client(source, http).Create(Request(1), null, cancellation.Token);
        Assert.AreEqual(PlaylistCreationStatus.Cancelled, result.Status);
        Assert.IsNull(result.Url);
        Assert.AreEqual(0, http.CreateCalls);
    }

    [TestMethod]
    [DataRow("deezer", 403, PlaylistCreationStatus.Failed)]
    [DataRow("qobuz", 403, PlaylistCreationStatus.Failed)]
    [DataRow("deezer", 503, PlaylistCreationStatus.Uncertain)]
    [DataRow("qobuz", 503, PlaylistCreationStatus.Uncertain)]
    public async Task CreationErrorsNeverInventALinkOrRetry(string source, int code, PlaylistCreationStatus status)
    {
        var http = new FakeHttp(source) { FailCreate = (HttpStatusCode)code };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(status, result.Status);
        Assert.IsNull(result.Url);
        Assert.AreEqual(1, http.CreateCalls);
        Assert.AreEqual(0, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task MissingCreationIdIsUncertain(string source)
    {
        var http = new FakeHttp(source) { MissingCreateId = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNull(result.Url);
        Assert.AreEqual(1, http.CreateCalls);
        Assert.AreEqual(0, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task MissingVisibilityKeepsTheCreationLinkButSendsNoTracks(string source)
    {
        var http = new FakeHttp(source) { MissingVisibility = true };
        var result = await Client(source, http).Create(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(0, http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    public async Task SkippedTracksStopAtTheFirstPartialBatch(string source)
    {
        var http = new FakeHttp(source) { SkipLast = true };
        var result = await Client(source, http).Create(Request(51), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(49, result.Added);
        Assert.AreEqual(1, http.AppendCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QobuzCountsTracksAfterAnOmissionAndProcessesAllBatches(bool existing)
    {
        var http = new FakeHttp("qobuz") { OmitIds = ["6", "77", "812"] };
        if (existing) http.Tracks.AddRange(["900", "901"]);
        var progress = new ProgressLog();
        var request = Request(812) with { Target = existing ? new("123456789", "Existing", new("https://play.qobuz.com/playlist/123456789")) : null };
        var client = Client("qobuz", http);
        var result = await client.Create(request, progress, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(809, result.Added);
        Assert.AreEqual(0, result.Skipped);
        Assert.AreEqual(17, http.AppendCalls);
        Assert.AreEqual(809, progress.Values.Last());
        StringAssert.Contains(result.Message!, "did not add 3 requested tracks");
        CollectionAssert.AreEqual((existing ? new[] { "900", "901" } : []).Concat(request.TrackIds.Where(id => !http.OmitIds.Contains(id))).ToArray(), http.Tracks.ToArray());

        var retry = await client.Create(request with { Target = new("123456789", "Existing", result.Url!) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, retry.Status);
        Assert.AreEqual(0, retry.Added);
        Assert.AreEqual(809, retry.Skipped);
        Assert.AreEqual(18, http.AppendCalls, "Omitted tracks must not trigger mutation retries.");
    }

    [TestMethod]
    public async Task QobuzContinuesAfterAnEntireBatchIsOmitted()
    {
        var http = new FakeHttp("qobuz") { OmitIds = Enumerable.Range(1, 50).Select(i => i.ToString()).ToHashSet() };
        var result = await Client("qobuz", http).Create(Request(51), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(2, http.AppendCalls);
        CollectionAssert.AreEqual(new[] { "51" }, http.Tracks.ToArray());
    }

    [TestMethod]
    [DataRow("1,2,4", 1, true)]
    [DataRow("1,2,3,4", 2, true)]
    [DataRow("1,2", 0, true)]
    [DataRow("2,1,3,4", 0, false)]
    [DataRow("1,2,4,3", 2, false)]
    [DataRow("1,2,3,3", 1, false)]
    [DataRow("1,2,3,999", 1, false)]
    [DataRow("1", 0, false)]
    public void QobuzOnlyContinuesForOrderedOmissionsWithAnUnchangedPrefix(string contents, int added, bool matches)
    {
        var actual = contents.Split(',');
        var confirmation = FluentDL.Services.Playlists.PlaylistUpload.ConfirmAllowingOmissions(actual, ["1", "2", "3", "4"], 2);
        Assert.AreEqual(added, confirmation.Added);
        Assert.AreEqual(matches, confirmation.Matches);
        if (matches) CollectionAssert.AreEqual(actual, confirmation.ConfirmedTrackIds!.ToArray());
        else Assert.IsNull(confirmation.ConfirmedTrackIds);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ChangedOrderCannotBeReportedAsCompleted(string source)
    {
        var http = new FakeHttp(source) { ReverseTracks = true };
        var result = await Client(source, http).Create(Request(2), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(source == "qobuz" ? 2 : 0, result.Added, "Qobuz counts acknowledged additions even when an order change stops the upload.");
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    public async Task QobuzAcceptsTheDocumentedObjectTrackIdsShapeInNumericOrder()
    {
        var http = new FakeHttp("qobuz") { ObjectTrackIds = true };
        var result = await Client("qobuz", http).Create(Request(12), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(12, result.Added);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task InvalidRequestsNeverReachTheService(string source)
    {
        var http = new FakeHttp(source);
        var client = Client(source, http);
        foreach (var request in new[] { Request(1) with { Name = " " }, Request(1) with { Name = new string('x', 251) },
            Request(1) with { Description = new string('x', 501) }, Request(0), Request(1) with { TrackIds = ["0"] },
            Request(1) with { TrackIds = ["12/path"] }, Request(1) with { TrackIds = ["99999999999999999999999999"] },
            Request(1) with { Visibility = PlaylistVisibility.Unlisted } })
        {
            var result = await client.Create(request, null, CancellationToken.None);
            Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
            Assert.IsNull(result.Url);
        }
        Assert.AreEqual(0, http.CreateCalls);
    }

    private static PlaylistCreationRequest Request(int count) => new(" Test ", " Description ", PlaylistVisibility.Private,
        Enumerable.Range(1, count).Select(n => n.ToString()).ToArray());

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ExistingPlaylistAppendsOnlyNewIdsAndPreservesExistingDuplicates(string source)
    {
        var http = new FakeHttp(source);
        http.Tracks.AddRange(["9", "9", "1"]);
        var request = Request(5) with { Name = "", Description = "", TrackIds = ["1", "2", "2", "3", "9"], Target = Target(source) };
        var result = await Client(source, http).Create(request, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(2, result.Added);
        Assert.AreEqual(3, result.Skipped);
        Assert.AreEqual(5, result.Total);
        Assert.AreEqual(0, http.CreateCalls);
        Assert.AreEqual(1, http.AppendCalls);
        CollectionAssert.AreEqual(new[] { "9", "9", "1", "2", "3" }, http.Tracks.ToArray());
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ExistingPlaylistWithAllTracksPresentSendsNoMutations(string source)
    {
        var http = new FakeHttp(source);
        http.Tracks.AddRange(["1", "2"]);
        var result = await Client(source, http).Create(Request(3) with { TrackIds = ["1", "1", "2"], Target = Target(source) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(3, result.Skipped);
        Assert.AreEqual(0, http.CreateCalls + http.AppendCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ReadOnlyOrNoLongerOwnedPlaylistCannotReceiveWrites(string source)
    {
        var http = new FakeHttp(source) { OwnerId = "8" };
        var result = await Client(source, http).Create(Request(1) with { Target = Target(source) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, http.CreateCalls + http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task ConcurrentPlaylistChangesStopBeforeAppending(string source)
    {
        var http = new FakeHttp(source) { ChangeBeforeAppend = true };
        var result = await Client(source, http).Create(Request(1) with { Target = Target(source) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(0, http.AppendCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task OwnedPlaylistListingPaginatesAndExcludesFollowedPlaylists(string source)
    {
        var http = new FakeHttp(source);
        var playlists = await Client(source, http).List(CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "123456789", "123456791" }, playlists.Select(p => p.Id).ToArray());
        Assert.AreEqual(3, http.ListCalls);
        Assert.AreEqual(0, http.CreateCalls + http.AppendCalls);
    }

    private static OwnedPlaylist Target(string source) => new("123456789", "Existing", new Uri(source == "deezer" ? "https://www.deezer.com/playlist/123456789" : "https://play.qobuz.com/playlist/123456789"));
    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task DuplicatePreviewReadsOwnedContentsWithoutMutatingThePlaylist(string source)
    {
        var http = new FakeHttp(source);
        http.Tracks.AddRange(["1", "1", "2"]);
        var ids = await Client(source, http).ReadIds("123456789", CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, ids.ToArray());
        Assert.AreEqual(0, http.CreateCalls + http.AppendCalls);
    }

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task DuplicatePreviewRejectsAPlaylistNotOwnedByTheCurrentAccount(string source)
    {
        var http = new FakeHttp(source) { OwnerId = "8" };
        await Assert.ThrowsExceptionAsync<PlaylistWriteRejected>(() => Client(source, http).ReadIds("123456789", CancellationToken.None));
        Assert.AreEqual(0, http.CreateCalls + http.AppendCalls);
    }

    private sealed record TestClient(Func<PlaylistCreationRequest, IProgress<int>?, CancellationToken, Task<PlaylistCreationResult>> Create, Action SignOut,
        Func<CancellationToken, Task<IReadOnlyList<OwnedPlaylist>>> List, Func<string, CancellationToken, Task<IReadOnlyList<string>>> ReadIds);
    private static TestClient Client(string source, FakeHttp http, bool signedIn = true)
    {
        var identity = new object();
        if (source == "deezer")
        {
            DeezerPlaylistSession? session = signedIn ? new("test-arl", "test-sid", "test-form", identity, "7") : null;
            var client = new DeezerPlaylistClient(() => session, new HttpClient(http));
            return new(client.CreateAsync, () => session = null, client.GetOwnedAsync, client.GetOwnedTrackIdsAsync);
        }
        else
        {
            QobuzPlaylistSession? session = signedIn ? new("test-app", "test-user", identity, "7") : null;
            var client = new QobuzPlaylistClient(() => session, new HttpClient(http));
            return new(client.CreateAsync, () => session = null, client.GetOwnedAsync, client.GetOwnedTrackIdsAsync);
        }
    }

    private sealed class ProgressLog(Action<int>? report = null) : IProgress<int>
    {
        public List<int> Values { get; } = [];
        public void Report(int value) { Values.Add(value); report?.Invoke(value); }
    }

    private sealed class FakeHttp(string source) : HttpMessageHandler
    {
        public List<string> Tracks { get; } = [];
        public int CreateCalls, AppendCalls;
        public string PublicFlag = "";
        public int FailAppend { get; init; }
        public HttpStatusCode Failure { get; init; } = HttpStatusCode.Forbidden;
        public bool LoseConnection { get; init; }
        public bool FailReadback { get; init; }
        public bool WrongVisibility { get; init; }
        public bool TokenError { get; init; }
        public bool CancelAppend { get; init; }
        public HttpStatusCode? FailCreate { get; init; }
        public bool MissingCreateId { get; init; }
        public bool MissingVisibility { get; init; }
        public bool SkipLast { get; init; }
        public HashSet<string> OmitIds { get; init; } = [];
        public bool ReverseTracks { get; init; }
        public bool ObjectTrackIds { get; init; }
        public string OwnerId { get; init; } = "7";
        public bool ChangeBeforeAppend { get; init; }
        public int ListCalls;
        private int trackReads;
        public int PageSize { get; init; } = 1000;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string method;
            JsonElement payload = default;
            System.Collections.Specialized.NameValueCollection? form = null;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            if (source == "deezer")
            {
                Assert.AreEqual("www.deezer.com", request.RequestUri.Host);
                Assert.AreEqual("test-form", query["api_token"]);
                StringAssert.Contains(request.Headers.GetValues("Cookie").Single(), "arl=test-arl; sid=test-sid");
                method = query["method"]!;
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                payload = document.RootElement.Clone();
            }
            else
            {
                Assert.AreEqual("test-app", request.Headers.GetValues("X-App-Id").Single());
                Assert.AreEqual("test-user", request.Headers.GetValues("X-User-Auth-Token").Single());
                Assert.IsNull(query["user_auth_token"], "Tokens belong in headers, not URLs.");
                method = request.RequestUri.AbsolutePath.Split('/').Last();
                if (request.Content != null) form = System.Web.HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync(token));
            }
            if (method is "deezer.pageProfile" or "getUserPlaylists")
            {
                ListCalls++;
                var offset = source == "deezer" ? payload.GetProperty("start").GetInt32() : int.Parse(query["offset"]!);
                object item = source == "deezer" ? new { PLAYLIST_ID = 123456789 + offset, TITLE = "Playlist " + offset, PARENT_USER_ID = offset == 1 ? "8" : "7" }
                    : new { id = 123456789 + offset, name = "Playlist " + offset, owner = new { id = offset == 1 ? "8" : "7" } };
                return Reply(source == "deezer" ? new { TAB = new { playlists = new { data = new[] { item }, total = 3 } } }
                    : new { playlists = new { items = new[] { item }, total = 3 } });
            }
            if (method is "playlist.create" or "create")
            {
                CreateCalls++;
                if (FailCreate is { } failed) return new(failed) { Content = new StringContent("{}") };
                if (MissingCreateId) return Reply(new { });
                PublicFlag = source == "deezer" ? payload.GetProperty("status").GetInt32() == 0 ? "1" : "0" : form!["is_public"]!;
                if (source == "qobuz" && MissingVisibility) return Reply(new { id = 123456789 });
                return Reply(source == "deezer" ? 123456789L : new { id = 123456789, is_public = WrongVisibility || PublicFlag == "1", tracks_count = 0 });
            }
            if (method == "deezer.pagePlaylist") return Reply(MissingVisibility ? new { DATA = new { } } : new { DATA = new { STATUS = WrongVisibility || PublicFlag == "1" ? 0 : 1, PARENT_USER_ID = OwnerId } });
            if (method is "playlist.addSongs" or "addTracks")
            {
                AppendCalls++;
                if (CancelAppend) throw new OperationCanceledException();
                if (LoseConnection) throw new HttpRequestException("Simulated loss");
                if (AppendCalls == FailAppend) return new(Failure) { Content = new StringContent("{}") };
                if (TokenError) return new(HttpStatusCode.OK) { Content = new StringContent("""{"error":{"VALID_TOKEN_REQUIRED":"redacted"}}""") };
                if (source == "deezer")
                {
                    Assert.AreEqual(-1, payload.GetProperty("offset").GetInt32());
                    Tracks.AddRange(payload.GetProperty("songs").EnumerateArray().Select(pair => pair[0].GetString()!));
                }
                else
                {
                    Assert.AreEqual("1", form!["no_duplicate"]);
                    Tracks.AddRange(form["track_ids"]!.Split(',').Distinct().Where(id => !OmitIds.Contains(id)));
                }
                if (SkipLast) Tracks.RemoveAt(Tracks.Count - 1);
                if (ReverseTracks) Tracks.Reverse();
                return Reply(source == "deezer" ? true : new { id = 123456789, tracks_count = Tracks.Count });
            }
            if (FailReadback) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
            if (++trackReads == 2 && ChangeBeforeAppend) Tracks.Add("999");
            if (source == "deezer")
            {
                var offset = payload.GetProperty("start").GetInt32();
                return Reply(new { data = Tracks.Skip(offset).Take(PageSize).Select(id => new { SNG_ID = id }).ToArray(), total = Tracks.Count });
            }
            return Reply(new { track_ids = ObjectTrackIds ? (object)Tracks.Select((id, index) => (id, index)).Reverse().ToDictionary(p => p.index.ToString(), p => long.Parse(p.id))
                : Tracks.Select(long.Parse).ToArray(), tracks_count = Tracks.Count, owner = new { id = OwnerId } });
        }

        private HttpResponseMessage Reply(object payload) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(source == "deezer" ? new { error = new { }, results = payload } : payload)),
        };
    }
}
