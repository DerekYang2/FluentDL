using System.Net;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Models;
using Newtonsoft.Json.Linq;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("PlaylistWrite")]
[DoNotParallelize]
public class DeezerQobuzPlaylistLiveTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeezerProductionWriterCreatesPrivateTracks(bool small)
    {
        RequireWrites();
        await LiveSources.EnsureDeezerAsync();
        var catalog = new List<SongSearchObject>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string[] ids;
        if (small) ids = [Fixtures.Tracks[0].DeezerId!, Fixtures.Tracks[1].DeezerId!];
        else
        {
            await TestSources.Deezer.SearchAsync(catalog, "bach", new(0, 60), false, timeout.Token);
            ids = catalog.Select(t => t.Id).Distinct().Take(51).ToArray();
            Assert.AreEqual(51, ids.Length);
        }
        string? createdId = null;
        var client = new DeezNET.DeezerClient();
        await client.SetARL(Secrets.Require("deezer_arl"));
        try
        {
            Assert.IsTrue(await TestSources.Deezer.CanCreatePlaylistAsync(timeout.Token));
            var name = "FluentDL test " + Guid.NewGuid().ToString("N");
            Console.WriteLine("Disposable recovery name: " + name);
            var result = await TestSources.Deezer.CreatePlaylistAsync(new(name, "Disposable 51-track test", PlaylistVisibility.Private, ids), null, timeout.Token);
            createdId = result.Url?.Segments.Last();
            Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
            Assert.AreEqual(ids.Length, result.Added);
            Assert.IsNotNull(createdId);
            var tracks = await client.GWApi.GetPlaylistTracks(long.Parse(createdId), timeout.Token);
            CollectionAssert.AreEqual(ids, tracks["data"]!.Select(t => t["SNG_ID"]!.ToString()).ToArray());
            var page = await client.GWApi.GetPlaylistPage(long.Parse(createdId), timeout.Token);
            Assert.AreEqual("1", page["DATA"]?["STATUS"]?.ToString());
            Console.WriteLine($"Deezer: {ids.Length} tracks, private visibility and order verified.");
            var listed = await TestSources.Deezer.GetOwnedPlaylistsAsync(timeout.Token);
            var target = listed.Single(p => p.Id == createdId);
            CollectionAssert.AreEqual(ids, (await TestSources.Deezer.GetPlaylistTrackIdsAsync(target.Id, timeout.Token)).ToArray());
            var extra = Fixtures.Tracks.Select(t => t.DeezerId).First(id => id != null && !ids.Contains(id))!;
            var appended = await TestSources.Deezer.CreatePlaylistAsync(new("", "", PlaylistVisibility.Private, [ids[0], extra, extra], target), null, timeout.Token);
            Assert.AreEqual(PlaylistCreationStatus.Completed, appended.Status, appended.Message);
            Assert.AreEqual(1, appended.Added);
            Assert.AreEqual(2, appended.Skipped);
            var after = await client.GWApi.GetPlaylistTracks(long.Parse(createdId), timeout.Token);
            CollectionAssert.AreEqual(ids.Append(extra).ToArray(), after["data"]!.Select(t => t["SNG_ID"]!.ToString()).ToArray());
            var unchanged = await client.GWApi.GetPlaylistPage(long.Parse(createdId), timeout.Token);
            Assert.AreEqual(name, unchanged["DATA"]?["TITLE"]?.ToString());
            Assert.AreEqual("1", unchanged["DATA"]?["STATUS"]?.ToString());
            var repeated = await TestSources.Deezer.CreatePlaylistAsync(new("", "", PlaylistVisibility.Private, [extra, ids[0]], target), null, timeout.Token);
            Assert.AreEqual(0, repeated.Added);
            Assert.AreEqual(2, repeated.Skipped);
            Assert.AreEqual(PlaylistCreationStatus.Completed, repeated.Status);
            Console.WriteLine("Deezer owned listing, existing append, duplicate skipping and unchanged metadata verified.");
        }
        finally
        {
            if (createdId != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var id = long.Parse(createdId);
                var ack = await client.GWApi.DeletePlaylist(id, cleanup.Token);
                Assert.AreEqual("True", ack.ToString());
                try
                {
                    var page = await client.GWApi.GetPlaylistPage(id, cleanup.Token);
                    Assert.IsTrue(page["DATA"]?["PLAYLIST_ID"]?.ToString() != createdId, "Remove disposable Deezer playlist " + createdId);
                }
                catch (DeezNET.Exceptions.APIException error) { Assert.IsNotNull(JObject.Parse(error.Message)["PERMISSION_ERROR"]); }
                Console.WriteLine("Deezer test playlist deleted and account read denied as expected.");
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QobuzProductionWriterSkipsDuplicatesAndAppendsToExisting(bool duplicates)
    {
        RequireWrites();
        await LiveSources.EnsureQobuzAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string[] ids;
        if (duplicates) ids = [Fixtures.Tracks[0].QobuzId!, Fixtures.Tracks[1].QobuzId!, Fixtures.Tracks[0].QobuzId!];
        else
        {
            var catalog = new List<SongSearchObject>();
            await TestSources.Qobuz.SearchAsync(catalog, "bach", new(0, 60), false, timeout.Token);
            ids = catalog.Select(t => t.Id).Distinct().Take(51).ToArray();
            Assert.AreEqual(51, ids.Length);
        }
        string? createdId = null;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-App-Id", Secrets.Require("qobuz_app_id"));
        http.DefaultRequestHeaders.Add("X-User-Auth-Token", Secrets.Require("qobuz_token"));
        try
        {
            var name = "FluentDL playlist writer test " + Guid.NewGuid().ToString("N");
            Console.WriteLine("Disposable recovery name: " + name);
            var result = await TestSources.Qobuz.CreatePlaylistAsync(new(name, "Disposable batch/duplicate test", PlaylistVisibility.Private, ids), null, timeout.Token);
            createdId = result.Url?.Segments.Last();
            Assert.AreEqual(PlaylistCreationStatus.Completed, result.Status, result.Message);
            Assert.AreEqual(ids.Distinct().Count(), result.Added);
            Assert.AreEqual(ids.Length - ids.Distinct().Count(), result.Skipped);
            var playlist = await TestSources.Qobuz.GetPlaylistAsync(createdId!, timeout.Token);
            Assert.IsNotNull(playlist);
            Assert.AreEqual(false, playlist.IsPublic);
            CollectionAssert.AreEqual(ids.Distinct().ToArray(), playlist.TrackIds.Select(id => id.ToString()).ToArray());
            Console.WriteLine($"Qobuz: {ids.Length} tracks, private visibility and complete order verified.");
            var listed = await TestSources.Qobuz.GetOwnedPlaylistsAsync(timeout.Token);
            var target = listed.Single(p => p.Id == createdId);
            CollectionAssert.AreEqual(ids.Distinct().ToArray(), (await TestSources.Qobuz.GetPlaylistTrackIdsAsync(target.Id, timeout.Token)).ToArray());
            var extra = Fixtures.Tracks.Select(t => t.QobuzId).First(id => id != null && !ids.Contains(id))!;
            var appended = await TestSources.Qobuz.CreatePlaylistAsync(new("", "", PlaylistVisibility.Private, [ids[0], extra, extra], target), null, timeout.Token);
            Assert.AreEqual(PlaylistCreationStatus.Completed, appended.Status, appended.Message);
            Assert.AreEqual(1, appended.Added);
            Assert.AreEqual(2, appended.Skipped);
            var after = await TestSources.Qobuz.GetPlaylistAsync(createdId!, timeout.Token);
            Assert.IsNotNull(after);
            CollectionAssert.AreEqual(ids.Distinct().Append(extra).ToArray(), after.TrackIds.Select(id => id.ToString()).ToArray());
            Assert.AreEqual(name, after.Name);
            Assert.AreEqual(false, after.IsPublic);
            Console.WriteLine("Qobuz owned listing, existing append, duplicate skipping and unchanged metadata verified.");
        }
        finally
        {
            if (createdId != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var deleted = await http.PostAsync("https://www.qobuz.com/api.json/0.2/playlist/delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["playlist_id"] = createdId }), cleanup.Token);
                Assert.IsTrue(deleted.IsSuccessStatusCode, "Remove disposable Qobuz playlist " + createdId);
                using var verify = await http.GetAsync("https://www.qobuz.com/api.json/0.2/playlist/get?playlist_id=" + createdId, cleanup.Token);
                Assert.AreEqual(HttpStatusCode.NotFound, verify.StatusCode);
                Console.WriteLine("Qobuz test playlist deleted and HTTP 404 verified.");
            }
        }
    }

    private static void RequireWrites()
    {
        if (Environment.GetEnvironmentVariable("FLUENTDL_PLAYLIST_WRITE_TESTS") != "1") Assert.Inconclusive("Set FLUENTDL_PLAYLIST_WRITE_TESTS=1 for disposable playlist writes.");
    }
}
