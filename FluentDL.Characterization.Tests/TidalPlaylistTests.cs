using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class TidalPlaylistTests
{
    [TestMethod]
    public async Task CreationBatchesTracksInOrderAndSkipsDuplicates()
    {
        var http = new FakeTidal();
        var client = await Client(http);
        var ids = Enumerable.Range(1, 100).Select(i => i.ToString()).Append("1").ToArray();
        var progress = new RecordedProgress();

        var result = await client.CreateAsync(new(" Test ", " Description ", PlaylistVisibility.Unlisted, ids), progress, CancellationToken.None);

        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(100, result.Added);
        Assert.AreEqual(1, result.Skipped);
        CollectionAssert.AreEqual(new[] { 50, 50 }, http.Batches.Select(b => b.Length).ToArray());
        CollectionAssert.AreEqual(ids.Distinct().ToArray(), http.Batches.SelectMany(b => b).ToArray());
        CollectionAssert.AreEqual(new[] { 50, 100 }, progress.Values.ToArray());
        Assert.AreEqual("Test", http.Name);
        Assert.AreEqual("Description", http.Description);
        Assert.AreEqual("UNLISTED", http.Visibility);
        Assert.AreEqual("https://tidal.com/browse/playlist/" + FakeTidal.PlaylistId, result.Url!.AbsoluteUri);
        Assert.AreEqual(3, http.Keys.Distinct().Count());
        Assert.IsTrue(http.Methods.All(m => m == "POST"));
    }

    [TestMethod]
    public async Task PublicVisibilityIsExplicit()
    {
        var http = new FakeTidal();
        var result = await (await Client(http)).CreateAsync(new("Name", "", PlaylistVisibility.Public, ["1"]), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual("PUBLIC", http.Visibility);
    }

    [TestMethod]
    [DataRow(403)]
    [DataRow(429)]
    public async Task FailedAppendKeepsThePlaylistAndDoesNotRetry(int status)
    {
        var http = new FakeTidal { FailBatch = 2, Failure = (HttpStatusCode)status };
        var result = await (await Client(http)).CreateAsync(Request(51), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(50, result.Added);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(2, http.Batches.Count);
        Assert.IsFalse(http.Methods.Contains("DELETE"));
    }

    [TestMethod]
    public async Task TransportFailureIsUncertainAndNeverRetried()
    {
        var http = new FakeTidal { ThrowOnAppend = true };
        var result = await (await Client(http)).CreateAsync(Request(3), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(1, http.Batches.Count);
    }

    [TestMethod]
    public async Task ServerErrorDuringCreationMayAlreadyHaveCreatedAPlaylist()
    {
        var http = new FakeTidal { CreateStatus = HttpStatusCode.InternalServerError };
        var result = await (await Client(http)).CreateAsync(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.IsNull(result.Url);
        Assert.AreEqual(1, http.Methods.Count);
    }

    [TestMethod]
    public async Task CancellationBetweenBatchesKeepsConfirmedProgressAndLink()
    {
        var http = new FakeTidal();
        using var cancellation = new CancellationTokenSource();
        var result = await (await Client(http)).CreateAsync(Request(51), new RecordedProgress(_ => cancellation.Cancel()), cancellation.Token);
        Assert.AreEqual(PlaylistCreationStatus.Cancelled, result.Status);
        Assert.AreEqual(50, result.Added);
        Assert.IsNotNull(result.Url);
        Assert.AreEqual(1, http.Batches.Count);
    }

    [TestMethod]
    public async Task SignedOutDoesNotUseAppCredentialsOrCreateAnything()
    {
        var http = new FakeTidal();
        var settings = new TidalParsingTests.DictionarySettings([]);
        var client = new TidalPlaylistClient(new TidalDownloadClient(settings, new HttpClient(http)), new HttpClient(http));
        Assert.IsFalse(await client.CanCreateAsync(CancellationToken.None));
        var result = await client.CreateAsync(Request(1), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, http.Methods.Count);
    }

    [TestMethod]
    public async Task InvalidInputNeverSendsARequest()
    {
        var http = new FakeTidal();
        var client = await Client(http);
        foreach (var request in new[] { Request(0), Request(1) with { Name = " " }, Request(1) with { TrackIds = ["bad/id"] } })
            Assert.AreEqual(PlaylistCreationStatus.Failed, (await client.CreateAsync(request, null, CancellationToken.None)).Status);
        Assert.AreEqual(0, http.Methods.Count);
    }

    [TestMethod]
    public async Task FewerAcknowledgedItemsStopsTheExportAsPartial()
    {
        var http = new FakeTidal { ShortResponse = true };
        var result = await (await Client(http)).CreateAsync(Request(51), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Partial, result.Status);
        Assert.AreEqual(49, result.Added);
        Assert.AreEqual(1, http.Batches.Count);
    }

    private static PlaylistCreationRequest Request(int count) => new("Name", "", PlaylistVisibility.Unlisted,
        Enumerable.Range(1, count).Select(i => i.ToString()).ToArray());

    [TestMethod]
    public async Task ExistingPlaylistSkipsExistingAndQueuedDuplicatesWithoutCreating()
    {
        var http = new FakeTidal();
        http.Tracks.AddRange(["9", "9", "1"]);
        var client = await Client(http);
        var owned = await client.GetOwnedAsync(CancellationToken.None);
        Assert.AreEqual(1, owned.Count);
        var result = await client.CreateAsync(Request(5) with { Name = "", TrackIds = ["1", "2", "2", "3", "9"], Target = owned[0] }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(2, result.Added);
        Assert.AreEqual(3, result.Skipped);
        Assert.IsNull(http.Name, "There must not be a create or rename.");
        CollectionAssert.AreEqual(new[] { "9", "9", "1", "2", "3" }, http.Tracks.ToArray());
    }

    [TestMethod]
    public async Task ExistingPlaylistSkipsEverythingWithoutPosting()
    {
        var http = new FakeTidal();
        http.Tracks.Add("1");
        var result = await (await Client(http)).CreateAsync(Request(1) with { Target = new(FakeTidal.PlaylistId, "Existing", new Uri("https://tidal.com/browse/playlist/" + FakeTidal.PlaylistId)) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status);
        Assert.AreEqual(1, result.Skipped);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(0, http.Methods.Count);
    }

    [TestMethod]
    public async Task TidalOwnershipIsCheckedAgainBeforeAppending()
    {
        var http = new FakeTidal { OwnerId = "8" };
        var client = await Client(http);
        Assert.AreEqual(0, (await client.GetOwnedAsync(CancellationToken.None)).Count);
        var result = await client.CreateAsync(Request(1) with { Target = new(FakeTidal.PlaylistId, "Not owned", new Uri("https://tidal.com/browse/playlist/" + FakeTidal.PlaylistId)) }, null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Failed, result.Status);
        Assert.AreEqual(0, http.Methods.Count);
    }

    [TestMethod]
    public async Task ReadBackFollowsApiRelativeCursorLinks()
    {
        var http = new FakeTidal { PageSize = 1 };
        var result = await (await Client(http)).CreateAsync(Request(3), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(3, result.Added);
        Assert.AreEqual(3, http.ReadCalls);
    }

    [TestMethod]
    public async Task UntrustedPaginationLinkNeverReceivesTheAccountToken()
    {
        var http = new FakeTidal { PageSize = 1, ExternalNextLink = true };
        var result = await (await Client(http)).CreateAsync(Request(3), null, CancellationToken.None);
        Assert.AreEqual(PlaylistCreationStatus.Uncertain, result.Status);
        Assert.AreEqual(1, http.ReadCalls);
        Assert.IsNotNull(result.Url);
    }

    [TestMethod]
    public async Task DuplicatePreviewReadsOnlyOwnedContentsAndSendsNoMutation()
    {
        var http = new FakeTidal();
        http.Tracks.AddRange(["1", "1", "2"]);
        var client = await Client(http);
        var ids = await client.GetOwnedTrackIdsAsync(FakeTidal.PlaylistId, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "1", "1", "2" }, ids.ToArray());
        Assert.AreEqual(0, http.Methods.Count);
    }

    private static async Task<TidalPlaylistClient> Client(FakeTidal http)
    {
        var session = new TidalDownloadClient.Session("client", "test-token", "refresh", DateTimeOffset.UtcNow.AddHours(1), "CA", "Test", "7");
        var settings = new TidalParsingTests.DictionarySettings([]);
        await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, Convert.ToBase64String(ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(session), null, DataProtectionScope.CurrentUser)));
        return new(new TidalDownloadClient(settings, new HttpClient(http)), new HttpClient(http));
    }

    private sealed class RecordedProgress(Action<int>? report = null) : IProgress<int>
    {
        public List<int> Values { get; } = [];
        public void Report(int value) { Values.Add(value); report?.Invoke(value); }
    }

    private sealed class FakeTidal : HttpMessageHandler
    {
        public const string PlaylistId = "11111111-2222-3333-4444-555555555555";
        public List<string[]> Batches { get; } = [];
        public List<string> Keys { get; } = [];
        public List<string> Methods { get; } = [];
        public string? Name, Description, Visibility;
        public int FailBatch { get; init; }
        public HttpStatusCode Failure { get; init; } = HttpStatusCode.Forbidden;
        public HttpStatusCode CreateStatus { get; init; } = HttpStatusCode.Created;
        public bool ThrowOnAppend { get; init; }
        public bool ShortResponse { get; init; }
        public List<string> Tracks { get; } = [];
        public string OwnerId { get; init; } = "7";
        public int PageSize { get; init; } = 1000;
        public bool ExternalNextLink { get; init; }
        public int ReadCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("openapi.tidal.com", request.RequestUri!.Host);
            Assert.AreEqual("test-token", request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/items"))
                {
                    ReadCalls++;
                    var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                    var offset = int.TryParse(query["page[cursor]"], out var number) ? number : 0;
                    var page = Tracks.Skip(offset).Take(PageSize).ToArray();
                    var next = offset + page.Length < Tracks.Count ? ExternalNextLink ? "https://untrusted.invalid/v2/playlists"
                        : $"/playlists/{PlaylistId}/relationships/items?page%5Bcursor%5D={offset + page.Length}" : null;
                    return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = page.Select(id => new { id, type = "tracks" }), links = new { next } }));
                }
                object metadata = new { id = PlaylistId, attributes = new { name = "Existing" }, relationships = new { owners = new { data = new[] { new { id = OwnerId, type = "users" } } } } };
                object responseData = request.RequestUri.AbsolutePath.EndsWith("/items") ? Tracks.Select(id => new { id, type = "tracks" }).ToArray()
                    : request.RequestUri.AbsolutePath.EndsWith("/playlists") ? new[] { metadata } : metadata;
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = responseData }));
            }
            Assert.AreEqual("application/vnd.api+json", request.Content!.Headers.ContentType!.MediaType);
            Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            Methods.Add(request.Method.Method);
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var data = json.RootElement.GetProperty("data");
            if (request.RequestUri.AbsolutePath.EndsWith("/playlists"))
            {
                var attributes = data.GetProperty("attributes");
                Name = attributes.GetProperty("name").GetString();
                Description = attributes.GetProperty("description").GetString();
                Visibility = attributes.GetProperty("accessType").GetString();
                return Json(CreateStatus, JsonSerializer.Serialize(new { data = new { id = PlaylistId, type = "playlists" } }));
            }
            Assert.AreEqual("SKIP", json.RootElement.GetProperty("meta").GetProperty("onDuplicates").GetString());
            var ids = data.EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
            Batches.Add(ids);
            if (ThrowOnAppend) throw new HttpRequestException("Simulated connection loss");
            if (Batches.Count == FailBatch) return Json(Failure, "{}");
            Tracks.AddRange(ids.Take(ShortResponse ? ids.Length - 1 : ids.Length));
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = ids.Take(ShortResponse ? ids.Length - 1 : ids.Length).Select(id => new { id, type = "tracks" }) }));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    }
}
