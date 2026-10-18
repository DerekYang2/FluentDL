using System.Net;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using SpotifyAPI.Web;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class SpotifyPlaylistTests
{
    private static string Id(int value) => value.ToString("D22");
    private static OwnedPlaylist Target => new(Id(9000), "Existing", new("https://open.spotify.com/playlist/" + Id(9000)));
    private static PlaylistCreationRequest Request(int count) => new(" Playlist ", " Description ", PlaylistVisibility.Private,
        Enumerable.Range(1, count).Select(Id).ToArray());

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(100, 1)]
    [DataRow(101, 2)]
    [DataRow(812, 9)]
    public async Task CreateUsesOfficialItemsEndpointsAndOrderedHundredTrackBatches(int count, int batches)
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.CreateAsync(Request(count), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(count, result.Added);
        Assert.AreEqual(1, fixture.Http.Creates);
        Assert.AreEqual(batches, fixture.Http.Appends);
        Assert.AreEqual(false, fixture.Http.Public);
        CollectionAssert.AreEqual(Request(count).TrackIds.ToArray(), fixture.Http.Tracks.Select(item => item?.GetProperty("id").GetString()).ToArray());
    }

    [TestMethod]
    public async Task ExistingAppendSkipsDuplicatesAndNeverChangesMetadata()
    {
        using var fixture = new Fixture();
        fixture.Http.Tracks.Add(Track(Id(1)));
        var request = Request(4) with { TrackIds = [Id(1), Id(2), Id(2), Id(3)], Target = Target };
        var result = await fixture.Client.CreateAsync(request, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(2, result.Added);
        Assert.AreEqual(2, result.Skipped);
        Assert.AreEqual(0, fixture.Http.Creates);
        Assert.AreEqual(1, fixture.Http.Appends);
        var repeated = await fixture.Client.CreateAsync(request, null, CancellationToken.None);
        Assert.AreEqual(0, repeated.Added);
        Assert.AreEqual(4, repeated.Skipped);
        Assert.AreEqual(1, fixture.Http.Appends);
    }

    [TestMethod]
    public async Task PublicCreationIsExplicitAndAnUnconfirmedVisibilityNeverSendsTracks()
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.CreateAsync(Request(1) with { Visibility = PlaylistVisibility.Public }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.IsTrue(fixture.Http.Public);
        using var mismatch = new Fixture();
        mismatch.Http.WrongVisibility = true;
        var rejected = await mismatch.Client.CreateAsync(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, rejected.Status);
        Assert.AreEqual(0, mismatch.Http.Appends);
        Assert.IsNotNull(rejected.Url);
    }

    [TestMethod]
    public async Task AReadFailureAfterAppendDoesNotClaimCompletionOrResendTheBatch()
    {
        using var fixture = new Fixture();
        fixture.Http.FailPostWriteRead = true;
        var result = await fixture.Client.CreateAsync(Request(101), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.AreEqual(1, fixture.Http.Appends);
        Assert.AreEqual(0, result.Added);
    }

    [TestMethod]
    public async Task ExternalEditsBetweenPreparationAndAppendPreventTheWrite()
    {
        using var fixture = new Fixture();
        fixture.Http.ChangeBeforeAppend = true;
        var result = await fixture.Client.CreateAsync(Request(1) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(0, fixture.Http.Appends);
    }

    [TestMethod]
    public async Task Base62IdsPreserveLeadingZerosAndCase()
    {
        using var fixture = new Fixture();
        var ids = new[] { "0000000000000000000001", "0000000000000000000010", "a000000000000000000001", "A000000000000000000001" };
        var result = await fixture.Client.CreateAsync(Request(4) with { TrackIds = ids }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(4, result.Added);
        CollectionAssert.AreEqual(ids, fixture.Http.Tracks.Select(item => item!.Value.GetProperty("id").GetString()).ToArray());
    }

    [TestMethod]
    public async Task OwnedListingPaginatesAndExcludesFollowedAndCollaborativeNonOwnedPlaylists()
    {
        using var fixture = new Fixture();
        fixture.Http.ListPageSize = 1;
        var owned = await fixture.Client.GetOwnedAsync(CancellationToken.None);
        Assert.AreEqual(2, owned.Count);
        Assert.AreEqual(3, fixture.Http.ListCalls);
        Assert.IsFalse(owned.Any(p => p.Id == Id(9001)));
    }

    [TestMethod]
    [DataRow(403, PlaylistCreationStatus.Partial)]
    [DataRow(429, PlaylistCreationStatus.Partial)]
    [DataRow(503, PlaylistCreationStatus.Uncertain)]
    public async Task AppendFailuresAreNeverReplayed(int status, PlaylistCreationStatus expected)
    {
        using var fixture = new Fixture();
        fixture.Http.AppendFailure = (HttpStatusCode)status;
        var result = await fixture.Client.CreateAsync(Request(101), null, CancellationToken.None);
        Assert.AreEqual(expected, result.Status);
        Assert.AreEqual(1, fixture.Http.Appends);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LostOrCancelledAcknowledgementRemainsUncertainWithoutReplay(bool cancel)
    {
        using var fixture = new Fixture();
        fixture.Http.LoseAppend = true;
        fixture.Http.CancelAppend = cancel;
        var result = await fixture.Client.CreateAsync(Request(101), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(1, fixture.Http.Appends);
        Assert.AreEqual(100, fixture.Http.Tracks.Count);
    }

    [TestMethod]
    [DataRow(403, PlaylistCreationStatus.Failed)]
    [DataRow(503, PlaylistCreationStatus.Uncertain)]
    public async Task CreateFailuresAreNeverReplayed(int status, PlaylistCreationStatus expected)
    {
        using var fixture = new Fixture();
        fixture.Http.CreateFailure = (HttpStatusCode)status;
        var result = await fixture.Client.CreateAsync(Request(1), null, CancellationToken.None);
        Assert.AreEqual(expected, result.Status);
        Assert.AreEqual(1, fixture.Http.Creates);
        Assert.AreEqual(0, fixture.Http.Appends);
        Assert.IsNull(result.Url);
    }

    [TestMethod]
    public async Task ForeignOwnerNeverReceivesAnAppend()
    {
        using var fixture = new Fixture();
        fixture.Http.Owner = "other-user";
        var result = await fixture.Client.CreateAsync(Request(1) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, fixture.Http.Appends);
    }

    [TestMethod]
    public async Task ChangedSnapshotsWithIdenticalContentsDoNotStopAnUpload()
    {
        using var fixture = new Fixture();
        fixture.Http.ChangeSnapshotOnPage = true;
        var result = await fixture.Client.CreateAsync(Request(1) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, fixture.Http.Appends);
    }

    [TestMethod]
    public async Task SnapshotChangesAfterSixHundredTracksDoNotTruncateTheRemainingBatches()
    {
        using var fixture = new Fixture();
        fixture.Http.DriftAfterAppend = 6;
        var request = Request(901);
        var result = await fixture.Client.CreateAsync(request, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(901, result.Added);
        Assert.AreEqual(10, fixture.Http.Appends, "Only the original append batches may be sent.");
        CollectionAssert.AreEqual(request.TrackIds.ToArray(), fixture.Http.Tracks.Select(item => item!.Value.GetProperty("id").GetString()).ToArray());
    }

    [TestMethod]
    public async Task DelayedReadbackRetriesOnlyReadsAndNeverAppendsTheSameBatchAgain()
    {
        using var fixture = new Fixture();
        fixture.Http.DelayedReadback = true;
        var result = await fixture.Client.CreateAsync(Request(101), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(101, result.Added);
        Assert.AreEqual(2, fixture.Http.Appends);
        Assert.AreEqual(101, fixture.Http.Tracks.Count);
    }

    [TestMethod]
    public async Task ContinuallyChangingContentsHaveABoundedReadBudgetAndStopBeforeWriting()
    {
        using var fixture = new Fixture();
        fixture.Http.ChangeContentsOnPage = true;
        var result = await fixture.Client.CreateAsync(Request(1) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.AreEqual(0, fixture.Http.Appends);
        Assert.AreEqual(3, fixture.Http.ReadStarts);
    }

    [TestMethod]
    public async Task ConcurrentItemEditsAfterAnAppendStillStopWithoutReplayingTheWrite()
    {
        using var fixture = new Fixture();
        fixture.Http.ChangeContentsOnPage = true;
        fixture.Http.ChangeContentsAfterAppend = 1;
        var result = await fixture.Client.CreateAsync(Request(101), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.AreEqual(1, fixture.Http.Appends);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    public async Task RetryingAnExistingSixHundredTrackPlaylistSkipsConfirmedDuplicates()
    {
        using var fixture = new Fixture();
        fixture.Http.Tracks.AddRange(Request(600).TrackIds.Select(id => (JsonElement?)Track(id)));
        fixture.Http.ChangeSnapshotOnPage = true;
        var result = await fixture.Client.CreateAsync(Request(901) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(301, result.Added);
        Assert.AreEqual(600, result.Skipped);
        Assert.AreEqual(4, fixture.Http.Appends);
        Assert.AreEqual(901, fixture.Http.Tracks.Count);
        Assert.AreEqual(0, fixture.Http.Creates);
    }

    [TestMethod]
    public async Task ChangedAccountBetweenBatchesStopsFurtherWrites()
    {
        using var fixture = new Fixture();
        var progress = new ImmediateProgress(_ => fixture.Session = null);
        var result = await fixture.Client.CreateAsync(Request(101), progress, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(100, result.Added);
        Assert.AreEqual(1, fixture.Http.Appends);
    }

    [TestMethod]
    public async Task CancellationBetweenBatchesKeepsConfirmedCount()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.Client.CreateAsync(Request(101), new ImmediateProgress(_ => cancellation.Cancel()), cancellation.Token);
        Assert.AreEqual(PlaylistCreationStatus.Cancelled, result.Status);
        Assert.AreEqual(100, result.Added);
        Assert.AreEqual(1, fixture.Http.Appends);
    }

    [TestMethod]
    [DataRow("item")]
    [DataRow("track")]
    public async Task ExistingLocalNullEpisodeAndRelinkedEntriesStayInPlace(string itemKey)
    {
        using var fixture = new Fixture();
        fixture.Http.ItemKey = itemKey;
        fixture.Http.Tracks.AddRange([null, Track(Id(50), "episode"),
            JsonSerializer.SerializeToElement(new { type = "track", uri = "spotify:local:artist:album:song:100", local = true }),
            JsonSerializer.SerializeToElement(new { id = Id(60), type = "track", linked_from = new { id = Id(1) } })]);
        var result = await fixture.Client.CreateAsync(Request(2) with { Target = Target }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, result.Skipped);
        Assert.AreEqual(5, fixture.Http.Tracks.Count);
    }

    [TestMethod]
    [DataRow("123")]
    [DataRow("spotify:track:1234567890123456789012")]
    [DataRow("../../bad-path")]
    [DataRow("123456789012345678901_")]
    public async Task InvalidIdsNeverReachSpotify(string id)
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.CreateAsync(Request(1) with { TrackIds = [id] }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, fixture.Http.Calls);
    }

    [TestMethod]
    public async Task AnonymousSessionsCannotListOrWritePlaylists()
    {
        using var fixture = new Fixture();
        fixture.Session = null;
        Assert.IsFalse(await fixture.Client.CanCreateAsync(CancellationToken.None));
        var result = await fixture.Client.CreateAsync(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, fixture.Http.Calls);
    }

    [TestMethod]
    public void SpotifyDialogKeepsBase62IdsAndDoesNotClaimAccessPrivacy()
    {
        var source = new SpotifySource(new TidalParsingTests.DictionarySettings([]), null!, null!);
        Assert.IsInstanceOfType<IPlaylistWriter>(source);
        Assert.AreEqual(Id(1), source.NormalizePlaylistTrackId(Id(1)));
        var model = new PlaylistDialogViewModel([source], [new QueueObject(new SongSearchObject { Id = Id(1), Source = SourceIds.Spotify })]);
        StringAssert.Contains(model.VisibilityOptions[0].DisplayName, "Not shown on profile");
        StringAssert.Contains(model.Message, "FluentDL only controls whether this playlist appears on your profile");
    }

    [TestMethod]
    public async Task SharedUserAuthorizationRefreshesOnceAndRetainsScopesAndRotatedRefreshToken()
    {
        int refreshes = 0;
        var response = new AuthorizationCodeTokenResponse
        {
            AccessToken = "expired", RefreshToken = "old-refresh", CreatedAt = DateTime.UtcNow.AddHours(-2), ExpiresIn = 1,
            Scope = string.Join(" ", SpotifyUserAuthorization.PlaylistScopes),
        };
        var auth = new SpotifyUserAuthorization(response, async _ =>
        {
            Interlocked.Increment(ref refreshes);
            await Task.Yield();
            return new() { AccessToken = "renewed", RefreshToken = "new-refresh", CreatedAt = DateTime.UtcNow, ExpiresIn = 3600, Scope = null! };
        });
        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => auth.GetAccessTokenAsync(CancellationToken.None)));
        Assert.AreEqual(1, refreshes);
        Assert.IsTrue(tokens.All(token => token == "renewed"));
        Assert.IsTrue(auth.CanWritePlaylists);
        Assert.AreEqual("new-refresh", response.RefreshToken);
    }

    [TestMethod]
    [DataRow("playlist-read-private")]
    [DataRow("playlist-modify-private playlist-modify-public")]
    [DataRow("playlist-read-private playlist-modify-private")]
    public void ReadOnlyOrIncompletePermissionsNeverEnablePlaylistWrites(string scope)
    {
        var auth = new SpotifyUserAuthorization(new() { Scope = scope }, _ => throw new AssertFailedException("No refresh should run."));
        Assert.IsFalse(auth.CanWritePlaylists);
    }

    private static JsonElement Track(string id, string type = "track") => JsonSerializer.SerializeToElement(new { id, type });
    private sealed class ImmediateProgress(Action<int> callback) : IProgress<int> { public void Report(int value) => callback(value); }

    private sealed class Fixture : IDisposable
    {
        internal readonly FakeHttp Http = new();
        private readonly HttpClient transport;
        internal SpotifyPlaylistSession? Session = new("user", new object(), _ => Task.FromResult("test-user-token"));
        internal SpotifyPlaylistClient Client { get; }
        internal Fixture()
        {
            transport = new(Http);
            Client = new(() => Session, transport, new("Spotify test", interval: TimeSpan.Zero, baseDelay: TimeSpan.Zero, transientRetries: 0, rateRetries: 0));
        }
        public void Dispose() => transport.Dispose();
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        internal List<JsonElement?> Tracks = [];
        internal int Creates, Appends, Calls, ListCalls, ListPageSize = 50;
        internal bool Public, LoseAppend, CancelAppend, ChangeSnapshotOnPage, WrongVisibility, FailPostWriteRead, ChangeBeforeAppend;
        internal bool DelayedReadback, ChangeContentsOnPage;
        internal int DriftAfterAppend, ChangeContentsAfterAppend, ReadStarts;
        internal HttpStatusCode? AppendFailure, CreateFailure;
        internal string Owner = "user", ItemKey = "item";
        private int snapshot;
        private int metadataReads;
        private JsonElement?[] staleTracks = [];
        private JsonElement?[]? pageView;
        private int staleReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.AreEqual("api.spotify.com", request.RequestUri!.Host);
            Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
            Assert.AreEqual("test-user-token", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.IsNull(query["access_token"]);
            if (FailPostWriteRead && Appends > 0 && request.Method == HttpMethod.Get) return Reply(new { }, HttpStatusCode.Forbidden);
            if (path == "/v1/me") return Reply(new { id = "user" });
            if (path == "/v1/me/playlists" && request.Method == HttpMethod.Get)
            {
                ListCalls++;
                var offset = int.Parse(query["offset"]!);
                var items = Enumerable.Range(offset, Math.Min(ListPageSize, 3 - offset)).Select(i => new
                {
                    id = Id(9000 + i), name = "Playlist " + i, owner = new { id = i == 1 ? "other-user" : "user" },
                });
                return Reply(new { total = 3, offset, items });
            }
            if (path == "/v1/me/playlists")
            {
                Creates++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Public = body.RootElement.GetProperty("public").GetBoolean();
                Assert.IsFalse(body.RootElement.GetProperty("collaborative").GetBoolean());
                Assert.AreEqual("Playlist", body.RootElement.GetProperty("name").GetString());
                return CreateFailure is { } failure ? Reply(new { }, failure)
                    : Reply(new { id = Target.Id, owner = new { id = Owner }, @public = WrongVisibility || Public, collaborative = false });
            }
            Assert.IsTrue(path.StartsWith("/v1/playlists/" + Target.Id));
            if (path.EndsWith("/items") && request.Method == HttpMethod.Post)
            {
                Appends++;
                if (AppendFailure is { } failure) return Reply(new { }, failure);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.IsFalse(body.RootElement.TryGetProperty("position", out _));
                var ids = body.RootElement.GetProperty("uris").EnumerateArray().Select(uri => uri.GetString()!).ToArray();
                Assert.IsTrue(ids.Length <= 100);
                Assert.IsTrue(ids.All(id => id.StartsWith("spotify:track:")));
                staleTracks = Tracks.ToArray();
                Tracks.AddRange(ids.Select(uri => (JsonElement?)Track(uri[14..])));
                staleReads = DelayedReadback ? 2 : 0;
                snapshot++;
                if (LoseAppend)
                {
                    if (CancelAppend) throw new OperationCanceledException();
                    throw new HttpRequestException("Lost acknowledgement");
                }
                return Reply(new { snapshot_id = snapshot.ToString() });
            }
            if (path.EndsWith("/items"))
            {
                Assert.AreEqual("50", query["limit"]);
                var offset = int.Parse(query["offset"]!);
                if (offset == 0)
                {
                    ReadStarts++;
                    pageView = staleReads > 0 ? staleTracks : null;
                    if (staleReads > 0) staleReads--;
                }
                if (ChangeSnapshotOnPage || (DriftAfterAppend > 0 && Appends >= DriftAfterAppend)) snapshot++;
                if (ChangeContentsOnPage && Appends >= ChangeContentsAfterAppend)
                {
                    Tracks.Add(Track(Id(10000 + Calls)));
                    snapshot++;
                }
                var visible = pageView ?? Tracks.ToArray();
                var items = visible.Skip(offset).Take(50).Select(item => new Dictionary<string, object?>
                {
                    [ItemKey] = item, ["is_local"] = item?.TryGetProperty("local", out _) == true,
                }).ToArray();
                return Reply(new { total = visible.Length, offset, items });
            }
            if (++metadataReads == 3 && ChangeBeforeAppend)
            {
                Tracks.Add(Track(Id(8000)));
                snapshot++;
            }
            return Reply(new { id = Target.Id, owner = new { id = Owner }, snapshot_id = snapshot.ToString() });
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body)),
        };
    }
}
