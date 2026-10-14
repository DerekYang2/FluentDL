using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Services.Tidal;
using FluentDL.Contracts.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
[DoNotParallelize]
public class TidalDownloadTests
{
    private static readonly string Dash = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011"><Period><AdaptationSet><Representation codecs="flac">
          <SegmentTemplate initialization="https://audio.example/init.mp4" media="https://audio.example/$Number$.mp4" startNumber="7">
            <SegmentTimeline><S d="4" r="1"/><S d="2"/></SegmentTimeline>
          </SegmentTemplate>
        </Representation></AdaptationSet></Period></MPD>
        """;

    private static string StreamResponse(string manifest, bool full = true, string mime = "application/dash+xml") => JsonSerializer.Serialize(new
    {
        assetPresentation = full ? "FULL" : "PREVIEW", audioQuality = "LOSSLESS", manifestMimeType = mime,
        manifest = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifest)),
    });

    [TestMethod]
    public void DashUsesInitializationStartNumberAndRepeatCounts()
    {
        using var json = JsonDocument.Parse(StreamResponse(Dash));
        var manifest = TidalDownloadManifest.Parse(json.RootElement);
        CollectionAssert.AreEqual(new[] { "/init.mp4", "/7.mp4", "/8.mp4", "/9.mp4" }, manifest.Parts.Select(u => u.AbsolutePath).ToArray());
        Assert.AreEqual(".flac", manifest.Extension);
    }

    [TestMethod]
    public void BtsUsesTheReturnedCodecInsteadOfTheRequestedQuality()
    {
        using var json = JsonDocument.Parse(StreamResponse("""{"encryptionType":"NONE","codecs":"mp4a.40.2","urls":["https://audio.example/track"]}""", mime: "application/vnd.tidal.bts"));
        Assert.AreEqual(".m4a", TidalDownloadManifest.Parse(json.RootElement).Extension);
    }

    [TestMethod]
    public void EncryptedDashIsRejected()
    {
        using var json = JsonDocument.Parse(StreamResponse(Dash.Replace("<Representation", "<ContentProtection/><Representation")));
        Assert.ThrowsException<NotSupportedException>(() => TidalDownloadManifest.Parse(json.RootElement));
    }

    [TestMethod]
    public async Task GistOverridesBundledClientAndCachedClientSurvivesAnOutage()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var handler = new FakeHttp();
        var client = Client(settings, handler);
        Assert.AreEqual("remote-client", (await client.BeginLoginAsync()).ClientId);
        handler.GistFails = true;
        Assert.AreEqual("remote-client", (await Client(settings, handler).BeginLoginAsync()).ClientId);
        Assert.AreEqual("bundled-client", (await Client(new TidalParsingTests.DictionarySettings([]), handler).BeginLoginAsync()).ClientId);
    }

    [TestMethod]
    public async Task ClientIdFromSettingsOverridesTheGist()
    {
        var settings = new TidalParsingTests.DictionarySettings(new() { [SettingKeys.TidalDownloadClientId] = " settings-client " });
        Assert.AreEqual("settings-client", (await Client(settings, new FakeHttp()).BeginLoginAsync()).ClientId);
        await settings.SaveSettingAsync(SettingKeys.TidalDownloadClientId, "not valid!");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Client(settings, new FakeHttp()).BeginLoginAsync());
    }

    // Makes only a GitHub request. Creating the PKCE address does not open a browser or contact TIDAL.
    [TestMethod]
    [TestCategory("Live")]
    public async Task RealGistSuppliesTheClientIdBeforeTheBundledFallback()
    {
        var url = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_GIST_TEST_URL");
        if (string.IsNullOrWhiteSpace(url)) Assert.Inconclusive("Set FLUENTDL_TIDAL_DOWNLOAD_GIST_TEST_URL for this manual Gist check.");
        var settings = new TidalParsingTests.DictionarySettings([]);
        var client = new TidalDownloadClient(settings, new HttpClient())
        {
            FallbackClientId = "intentionally-different-fallback",
        };
        Assert.AreEqual(url, client.ConfigUrl, "The build must include the configured Gist URL.");
        var login = await client.BeginLoginAsync();
        Assert.AreNotEqual(client.FallbackClientId, login.ClientId, "The remote fetch fell back to the bundled ID.");
        Assert.AreEqual(login.ClientId, await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadCachedClient));
        Assert.AreEqual(login.ClientId, System.Web.HttpUtility.ParseQueryString(login.Address.Query)["client_id"]);
        Console.WriteLine("Real Gist fetch, cache write and PKCE client selection passed without contacting TIDAL.");
    }

    [TestMethod]
    public async Task LoginUsesPkceAndRefreshKeepsTheOriginalClientAfterConfigChanges()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var handler = new FakeHttp { ExpiresIn = 0 };
        var client = Client(settings, handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        var exchange = handler.Forms.Single();
        Assert.AreEqual(TidalApi.CodeChallenge(exchange["code_verifier"]!), System.Web.HttpUtility.ParseQueryString(login.Address.Query)["code_challenge"]);
        Assert.IsNull(exchange["client_secret"]);
        Assert.IsNull(handler.TokenAuthorization);
        Assert.IsFalse((await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession))!.Contains("refresh-value"));
        await settings.SaveSettingAsync(SettingKeys.TidalDirectDownloads, true);

        handler.RemoteId = "replacement-client";
        handler.Preview = true;
        handler.ExpiresIn = 14400;
        client = Client(settings, handler);
        Assert.AreEqual("replacement-client", (await client.BeginLoginAsync()).ClientId);
        Assert.IsNull(await client.TryDownloadAsync("1", "unused", null));
        Assert.AreEqual("refresh_token", handler.Forms.Last()["grant_type"]);
        Assert.AreEqual("remote-client", handler.Forms.Last()["client_id"]);
        Assert.AreEqual("refresh-value", handler.Forms.Last()["refresh_token"]);
        Assert.AreEqual(0, handler.MediaRequests);
    }

    [TestMethod]
    public async Task WrongLoginStateNeverReachesTokenEndpoint()
    {
        var handler = new FakeHttp();
        var client = Client(new TidalParsingTests.DictionarySettings([]), handler);
        var login = await client.BeginLoginAsync();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.FinishLoginAsync(login, TidalDownloadClient.RedirectUri + "?code=code&state=wrong"));
        Assert.AreEqual(0, handler.Forms.Count);
    }

    [TestMethod]
    public async Task DownloadIdentityIsEncryptedRestoredAfterRestartAndClearedOnLogout()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var handler = new FakeHttp();
        var client = Client(settings, handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        Assert.AreEqual("downloads@example.test", await client.GetAccountNameAsync());
        var saved = await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession);
        Assert.IsFalse(saved!.Contains("downloads@example.test"));
        var restarted = Client(settings, handler);
        Assert.AreEqual("downloads@example.test", await restarted.GetAccountNameAsync());
        Assert.AreEqual(1, handler.Forms.Count, "A fresh session's identity needs no extra request.");
        await restarted.LogoutAsync();
        Assert.IsNull(await restarted.GetAccountNameAsync());
    }

    [TestMethod]
    public async Task OldSessionWithoutIdentityRecoversItOnRefresh()
    {
        var settings = new TidalParsingTests.DictionarySettings([]);
        var old = """{"ClientId":"old-client","AccessToken":"access","RefreshToken":"refresh","Expires":"2099-01-01T00:00:00Z","Country":"CA"}""";
        await settings.SaveSettingAsync(SettingKeys.TidalDownloadSession, Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(old), null, DataProtectionScope.CurrentUser)));
        var handler = new FakeHttp();
        var client = Client(settings, handler);
        Assert.AreEqual("downloads@example.test", await client.GetAccountNameAsync());
        Assert.AreEqual("refresh_token", handler.Forms.Single()["grant_type"]);
        Assert.AreEqual("old-client", handler.Forms.Single()["client_id"]);
        Assert.AreEqual("downloads@example.test", await client.GetAccountNameAsync());
        Assert.AreEqual(1, handler.Forms.Count);
    }

    [TestMethod]
    public void StatusSeparatelyLabelsCatalogAndDownloadAccounts()
    {
        var text = Views.SettingsPage.FormatTidalAccountInfo("catalog@example.test", "downloads@example.test", true, true, "Active");
        StringAssert.Contains(text, "API:\ncatalog@example.test\nOnly searching.");
        StringAssert.Contains(text, "Downloading:\ndownloads@example.test");
        StringAssert.Contains(text, "Subscription: Active");
        StringAssert.Contains(text, "Direct downloads: On");
    }

    [TestMethod]
    [DataRow(true, "Active")]
    [DataRow(false, "Not active")]
    public async Task SubscriptionStatusUsesPlaybackAccessNotTheActiveAccountFlag(bool premiumAccess, string expected)
    {
        var handler = new FakeHttp { PremiumAccess = premiumAccess };
        var client = Client(new TidalParsingTests.DictionarySettings([]), handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        Assert.AreEqual(expected, await client.GetSubscriptionStatusAsync());
        Assert.AreEqual(expected, await client.GetSubscriptionStatusAsync());
        Assert.AreEqual(1, handler.SubscriptionRequests);
        await client.LogoutAsync();
        Assert.AreEqual("Not signed in", await client.GetSubscriptionStatusAsync());
    }

    [TestMethod]
    public async Task DisabledDownloadsDoNotReadTheSessionOrCallTidal()
    {
        var handler = new FakeHttp();
        Assert.IsNull(await Client(new TidalParsingTests.DictionarySettings([]), handler).TryDownloadAsync("1", "unused", null));
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task SegmentFailurePreservesExistingFileAndRemovesTemporaryFiles()
    {
        var settings = new TidalParsingTests.DictionarySettings(new() { [SettingKeys.TidalDirectDownloads] = true, [SettingKeys.Overwrite] = true });
        var handler = new FakeHttp { FailSegment = true };
        var client = Client(settings, handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-download-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var originalSettings = TestSources.Settings;
        SourceServices.Configure(settings, new CopyConverter());
        try
        {
            var output = Path.Combine(folder, "track.flac");
            await File.WriteAllTextAsync(output, "existing");
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.TryDownloadAsync("1", Path.Combine(folder, "track"), null));
            Assert.AreEqual("existing", await File.ReadAllTextAsync(output));
            Assert.AreEqual(1, Directory.GetFiles(folder).Length);
            handler.FailSegment = false;
            var result = await client.TryDownloadAsync("1", Path.Combine(folder, "track"), null);
            Assert.AreEqual(output, result);
            Assert.AreEqual("/init.mp4/7.mp4/8.mp4/9.mp4", await File.ReadAllTextAsync(result!));
            Assert.AreEqual(1, Directory.GetFiles(folder).Length);
        }
        finally { SourceServices.Configure(originalSettings, TestSources.AudioConverter); Directory.Delete(folder, true); }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task TransferProgressCoversDashAndSingleFileStreams(bool singleFile, bool unknownLength)
    {
        var settings = new TidalParsingTests.DictionarySettings(new() { [SettingKeys.TidalDirectDownloads] = true });
        var handler = new FakeHttp { SingleFile = singleFile, ChunkedMedia = true, UnknownMediaLength = unknownLength };
        var client = Client(settings, handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-progress-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var originalSettings = TestSources.Settings;
        SourceServices.Configure(settings, new CopyConverter());
        var progress = new RecordingProgress();
        try
        {
            var path = await client.TryDownloadAsync("1", Path.Combine(folder, "track"), progress);
            var reports = progress.Reports.ToArray();
            Assert.IsTrue(reports.Length > (singleFile ? 1 : 4), "Report while reading a segment, not just after it finishes.");
            Assert.AreEqual(1, reports.Select(r => r.uniqueId).Distinct().Count());
            Assert.AreEqual(new FileInfo(path!).Length, reports.Last().currentBytes);
            Assert.AreEqual(1d, reports.Last().percent);
            for (var i = 0; i < reports.Length; i++)
            {
                Assert.IsTrue(reports[i].percent is >= 0 and <= 1);
                if (i > 0)
                {
                    Assert.IsTrue(reports[i].percent >= reports[i - 1].percent);
                    Assert.IsTrue(reports[i].currentBytes >= reports[i - 1].currentBytes);
                }
            }
            if (!unknownLength)
                Assert.IsTrue(reports.Any(r => r.percent is > 0 and < 0.25), "Known-length streams have progress within the first part.");
            if (!singleFile)
                Assert.IsTrue(reports.Any(r => r.percent == 0.5), "DASH counts all parts under the same download ID.");
        }
        finally { SourceServices.Configure(originalSettings, TestSources.AudioConverter); Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task ConcurrentTidalTracksKeepTheirProgressSeparateAndFailuresDoNotReportCompletion()
    {
        var settings = new TidalParsingTests.DictionarySettings(new() { [SettingKeys.TidalDirectDownloads] = true });
        var handler = new FakeHttp { ChunkedMedia = true };
        var client = Client(settings, handler);
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-album-progress-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var originalSettings = TestSources.Settings;
        SourceServices.Configure(settings, new CopyConverter());
        var progress = new RecordingProgress();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Task.Run(() => client.TryDownloadAsync(i.ToString(), Path.Combine(folder, $"track-{i}"), progress))));
            var tracks = progress.Reports.GroupBy(r => r.uniqueId).ToArray();
            Assert.AreEqual(3, tracks.Length);
            Assert.IsTrue(tracks.All(t => t.Last().percent == 1));
            handler.FailSegment = true;
            var failed = new RecordingProgress();
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.TryDownloadAsync("4", Path.Combine(folder, "failed"), failed));
            Assert.IsTrue(failed.Reports.All(r => r.percent < 1));
        }
        finally { SourceServices.Configure(originalSettings, TestSources.AudioConverter); Directory.Delete(folder, true); }
    }

    private sealed class RecordingProgress : IProgress<ProgressData>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<ProgressData> Reports = new();
        public void Report(ProgressData value) => Reports.Enqueue(value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlbumPipelineAggregatesConcurrentTracksWithoutFinishingOnSegmentFailure(bool failSegment)
    {
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [SettingKeys.TidalDirectDownloads] = true, ["conversion_threads"] = 3,
            ["file_naming_wildcard"] = "{title}", ["download_album_cover_art"] = false,
        });
        var client = Client(settings, new FakeHttp { ChunkedMedia = true, FailSegment = failSegment });
        var login = await client.BeginLoginAsync();
        await client.FinishLoginAsync(login, Callback(login));
        var registry = new SourceRegistry([new AlbumCatalog()]);
        var downloads = new DownloadService(registry, new EquivalentDownloader(registry), new NoLyrics(), client);
        var folder = Path.Combine(Path.GetTempPath(), "opencode", "tidal-album-pipeline-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var originalSettings = TestSources.Settings;
        SourceServices.Configure(settings, new CopyConverter());
        var helper = new DownloadProgressHelper("Album", 3);
        var progress = new RecordingProgress();
        try
        {
            var album = new AlbumSearchObject { Source = SourceIds.Tidal, Id = "album", Title = "Album", TracksCount = 3 };
            var paths = await downloads.DownloadAlbum(album, folder, progress);
            Assert.AreEqual(failSegment ? 0 : 3, paths.Count);
            Assert.AreEqual(3, progress.Reports.Select(r => r.uniqueId).Distinct().Count());
            foreach (var report in progress.Reports) helper.UpdateProgressLoop(report);
            // Failed tracks are processed too, so the album reaches 100% either way.
            Assert.AreEqual(1, helper.GetProgressUpdate()!.ProgressValue, 0.000001);
            Assert.IsFalse(Directory.GetFiles(folder).Any(p => p.EndsWith(".mp4")));
        }
        finally { SourceServices.Configure(originalSettings, TestSources.AudioConverter); Directory.Delete(folder, true); }
    }

    private sealed class AlbumCatalog : ICatalogSource
    {
        public SourceDescriptor Descriptor => SourceCatalog.Find(SourceIds.Tidal)!;
        public Task<SongSearchObject?> GetTrackAsync(string id) => Task.FromResult<SongSearchObject?>(Track(id));
        public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album) =>
            Task.FromResult(Enumerable.Range(0, 3).Select(i => Track(i.ToString())).ToList());
        public Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) => Task.FromResult<string?>(null);
        private static SongSearchObject Track(string id) => new() { Source = SourceIds.Tidal, Id = id, Title = $"Track {id}", Artists = "Artist" };
    }

    private sealed class NoLyrics : ILyricService
    {
        public Task<string?> GetLyricsAsync(SongSearchObject song) => Task.FromResult<string?>(null);
    }

    private static TidalDownloadClient Client(TidalParsingTests.DictionarySettings settings, FakeHttp http) => new(settings, new HttpClient(http))
    {
        FallbackClientId = "bundled-client", ConfigUrl = "https://gist.githubusercontent.com/test/raw/client.json",
    };
    private static string Callback(TidalDownloadClient.Login login) => TidalDownloadClient.RedirectUri + "?code=code&state=" + login.State;

    private sealed class FakeHttp : HttpMessageHandler
    {
        public bool GistFails, Preview, FailSegment;
        public bool SingleFile, ChunkedMedia, UnknownMediaLength;
        public bool PremiumAccess = true;
        public int SubscriptionRequests;
        public int ExpiresIn = 14400, MediaRequests, RequestCount;
        public string RemoteId = "remote-client";
        public string? TokenAuthorization;
        public List<System.Collections.Specialized.NameValueCollection> Forms = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            var uri = request.RequestUri!;
            if (uri.Host == "gist.githubusercontent.com") return Response(GistFails ? 503 : 200, "{\"client_id\":\"" + RemoteId + "\"}");
            if (uri.Host == "auth.tidal.com")
            {
                TokenAuthorization = request.Headers.Authorization?.ToString();
                var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(token));
                Forms.Add(form);
                var refresh = form["grant_type"] == "refresh_token" ? "" : ",\"refresh_token\":\"refresh-value\"";
                return Response(200, "{\"access_token\":\"access-value\",\"expires_in\":" + ExpiresIn + refresh + ",\"user_id\":7,\"user\":{\"countryCode\":\"CA\",\"email\":\"downloads@example.test\",\"username\":\"download-user\"}}");
            }
            if (uri.AbsolutePath == "/v1/users/7/subscription")
            {
                SubscriptionRequests++;
                return Response(200, "{\"status\":\"ACTIVE\",\"premiumAccess\":" + (PremiumAccess ? "true" : "false") + "}");
            }
            if (uri.Host == "api.tidal.com") return Response(200, SingleFile
                ? StreamResponse("""{"encryptionType":"NONE","codecs":"mp4a.40.2","urls":["https://audio.example/track"]}""", !Preview, "application/vnd.tidal.bts")
                : StreamResponse(Dash, !Preview));
            MediaRequests++;
            Assert.IsNull(request.Headers.Authorization, "Bearer tokens must not go to the media host.");
            if (ChunkedMedia && !(FailSegment && uri.AbsolutePath == "/8.mp4"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ChunkedStream(Encoding.UTF8.GetBytes(uri.AbsolutePath), UnknownMediaLength)),
                };
            return Response(FailSegment && uri.AbsolutePath == "/8.mp4" ? 503 : 200, uri.AbsolutePath);
        }
        private static HttpResponseMessage Response(int status, string content) => new((HttpStatusCode)status) { Content = new StringContent(content) };
    }

    private sealed class ChunkedStream(byte[] bytes, bool unknownLength) : MemoryStream(bytes)
    {
        public override bool CanSeek => !unknownLength;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private sealed class CopyConverter : IAudioConverter
    {
        public bool IsInitialized => true;
        public Task RemuxAudioAsync(string inputPath, string outputPath) { File.Copy(inputPath, outputPath); return Task.CompletedTask; }
        public Task ConvertToFlacAsync(string p, int samplingRate = 48000) => throw new NotSupportedException();
        public Task ConvertToFlacAsync(string i, string o) => throw new NotSupportedException();
        public Task ConvertMp4ToM4aAsync(string i, string o) => throw new NotSupportedException();
        public Task RemuxOpusAsync(string i, string o) => throw new NotSupportedException();
        public Task<string?> CreateMp3Async(string i, string? o = null) => throw new NotSupportedException();
    }
}
