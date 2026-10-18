using System.Net;
using System.Net.Http.Headers;
using FluentDL.Contracts.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("PlaylistWrite")]
[DoNotParallelize]
public class TidalPlaylistLiveTests
{
    [TestMethod]
    public async Task Create51TracksAndReadBackBothBatchesInOrder()
    {
        var path = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION");
        if (string.IsNullOrEmpty(path) || Environment.GetEnvironmentVariable("FLUENTDL_PLAYLIST_WRITE_TESTS") != "1")
            Assert.Inconclusive("Set the encrypted test session path and FLUENTDL_PLAYLIST_WRITE_TESTS=1 to create a disposable playlist.");
        var accounts = new TidalDownloadClient(new TidalCatalogLiveTests.SessionSettings(path!));
        var writer = new TidalPlaylistClient(accounts);
        var api = new TidalApi(new TidalCatalogLiveTests.SessionSettings(path!), new HttpClient(), downloadClient: accounts);
        var source = new FluentDL.Services.Sources.TidalSource(api);
        var catalog = new List<FluentDL.Models.SongSearchObject>();
        using var searchTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await source.SearchAsync(catalog, "bach", new(0, 60), false, searchTimeout.Token);
        var ids = catalog.Select(t => t.Id).Distinct().Take(51).ToArray();
        Assert.AreEqual(51, ids.Length);
        var name = "FluentDL playlist writer test " + Guid.NewGuid().ToString("N");
        Console.WriteLine("Disposable playlist name, for recovery if creation times out: " + name);
        Uri? url = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        try
        {
            var result = await writer.CreateAsync(new(name, "Disposable 51-track batch test", PlaylistVisibility.Unlisted, ids), null, timeout.Token);
            url = result.Url;
            Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
            Assert.AreEqual(51, result.Added);
            Assert.IsNotNull(url);
            var actual = new List<string>();
            string? next = $"playlists/{url.Segments.Last()}/relationships/items?sort=itemIndex";
            while (next != null)
            {
                var page = await api.GetAsync(next, timeout.Token);
                actual.AddRange(page.Data.Select(item => item.GetProperty("id").GetString()!));
                next = page.NextLink;
            }
            CollectionAssert.AreEqual(ids, actual.ToArray());
            Console.WriteLine("51 distinct tracks across two append requests verified in order.");
            var listed = await writer.GetOwnedAsync(timeout.Token);
            var target = listed.Single(p => p.Id == url.Segments.Last());
            CollectionAssert.AreEqual(ids, (await writer.GetOwnedTrackIdsAsync(target.Id, timeout.Token)).ToArray());
            var extra = new[] { "20115564", "19823990", "17771055" }.First(id => !ids.Contains(id));
            var appended = await writer.CreateAsync(new("", "", PlaylistVisibility.Unlisted, [ids[0], extra, extra], target), null, timeout.Token);
            Assert.AreEqual(PlaylistCreationStatus.Completed, appended.Status, appended.Message);
            Assert.AreEqual(1, appended.Added);
            Assert.AreEqual(2, appended.Skipped);
            var after = await api.GetAsync($"playlists/{target.Id}", timeout.Token);
            Assert.AreEqual(name, after.Data[0].GetProperty("attributes").GetProperty("name").GetString());
            Assert.AreEqual("UNLISTED", after.Data[0].GetProperty("attributes").GetProperty("accessType").GetString());
            Console.WriteLine("TIDAL owned listing, existing append, duplicate skipping and unchanged metadata verified.");
        }
        finally
        {
            if (url != null && Guid.TryParse(url.Segments.Last(), out var id))
            {
                // Cleanup is test-only. The production writer deliberately keeps partial playlists.
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var account = await accounts.GetApiSessionAsync(cleanupTimeout.Token);
                Assert.IsNotNull(account);
                using var delete = new HttpRequestMessage(HttpMethod.Delete, "https://openapi.tidal.com/v2/playlists/" + id);
                delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
                delete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                using var deleted = await http.SendAsync(delete, cleanupTimeout.Token);
                Assert.IsTrue(deleted.IsSuccessStatusCode, "Remove test playlist " + id);
                using var check = new HttpRequestMessage(HttpMethod.Get, delete.RequestUri);
                check.Headers.Authorization = delete.Headers.Authorization;
                check.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                using var missing = await http.SendAsync(check, cleanupTimeout.Token);
                Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode, "Test playlist deletion was not verified.");
                Console.WriteLine("Disposable playlist deleted and 404 verified.");
            }
        }
    }
}
