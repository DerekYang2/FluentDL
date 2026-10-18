using System.Net;
using System.Text.Json.Nodes;
using DeezNET;
using DeezNET.Data;
using FluentDL.Characterization.Tests.Infrastructure;
using Newtonsoft.Json.Linq;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("PlaylistWrite")]
[DoNotParallelize]
public class PlaylistApiProbeTests
{
    [TestMethod]
    public async Task DeezerPrivatePlaylistResponseContracts()
    {
        RequireWrites();
        var client = new DeezerClient();
        long? createdId = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await client.SetARL(Secrets.Require("deezer_arl")).WaitAsync(timeout.Token);
            Assert.AreNotEqual("0", client.GWApi.ActiveUserData?["USER"]?["USER_ID"]?.ToString());
            var name = "FluentDL playlist probe " + Guid.NewGuid().ToString("N");
            Console.WriteLine("Recovery name: " + name);
            var created = await client.GWApi.CreatePlaylist(name, "Disposable API response test", [], PlaylistStatus.PRIVATE, timeout.Token);
            Console.WriteLine("Deezer create: " + Shape(created));
            createdId = long.Parse(created.Type is JTokenType.Integer or JTokenType.String ? created.ToString() : created["PLAYLIST_ID"]!.ToString());
            var page = await client.GWApi.GetPlaylistPage(createdId.Value, timeout.Token);
            Console.WriteLine("Deezer metadata: " + Shape(page));
            Console.WriteLine("Deezer visibility: " + page["DATA"]?["STATUS"]);
            Assert.AreEqual("1", page["DATA"]?["STATUS"]?.ToString());
            var ids = new[] { long.Parse(Fixtures.Tracks[0].DeezerId!), long.Parse(Fixtures.Tracks[1].DeezerId!) };
            var added = await client.GWApi.AddSongsToPlaylist(createdId.Value, ids, token: timeout.Token);
            Console.WriteLine("Deezer append: " + Shape(added));
            var tracks = await client.GWApi.GetPlaylistTracks(createdId.Value, timeout.Token);
            Console.WriteLine("Deezer tracks: " + Shape(tracks));
            Console.WriteLine("Deezer read-back IDs: " + string.Join(",", tracks["data"]!.Select(t => t["SNG_ID"]!.ToString())));
            try
            {
                await client.GWApi.AddSongsToPlaylist(createdId.Value, [ids[0]], token: timeout.Token);
                var duplicate = await client.GWApi.GetPlaylistTracks(createdId.Value, timeout.Token);
                Console.WriteLine("Deezer separate duplicate read-back IDs: " + string.Join(",", duplicate["data"]!.Select(t => t["SNG_ID"]!.ToString())));
            }
            catch (DeezNET.Exceptions.APIException) { Console.WriteLine("Deezer rejected the separate duplicate append."); }
            var userId = long.Parse(client.GWApi.ActiveUserData!["USER"]!["USER_ID"]!.ToString());
            var profile = await client.GWApi.GetUserProfilePage(userId, "playlists", 1000, timeout.Token);
            Console.WriteLine("Deezer account playlist page includes new private ID: " + ((JContainer)profile).DescendantsAndSelf().OfType<JValue>().Any(v => v.ToString() == createdId.Value.ToString()));
        }
        catch (Exception error)
        {
            Console.WriteLine("Deezer probe exception type: " + error.GetType().Name);
            if (error is DeezNET.Exceptions.APIException)
            {
                try
                {
                    var parsed = JObject.Parse(error.Message);
                    Console.WriteLine("Deezer rejection keys: " + string.Join(",", parsed.Properties().Select(p => p.Name)));
                    Console.WriteLine("Known reason categories: " + string.Join(",", new[] { "duplicate", "offset", "parameter", "position", "exists", "not found", "permission", "limit", "rights" }.Where(word => error.Message.Contains(word, StringComparison.OrdinalIgnoreCase))));
                }
                catch (Newtonsoft.Json.JsonException) { }
            }
            Assert.Fail("Deezer probe stopped: " + error.GetType().Name + ". Response bodies and credentials omitted.");
        }
        finally
        {
            if (createdId is { } id)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    var ack = await client.GWApi.DeletePlaylist(id, cleanup.Token);
                    Assert.AreEqual("True", ack.ToString());
                    try
                    {
                        var deleted = await client.GWApi.GetPlaylistPage(id, cleanup.Token);
                        Assert.IsTrue(deleted["DATA"]?["PLAYLIST_ID"]?.ToString() != id.ToString(), "Deletion not verified for disposable playlist " + id);
                    }
                    catch (DeezNET.Exceptions.APIException error)
                    {
                        Assert.IsNotNull(JObject.Parse(error.Message)["PERMISSION_ERROR"], "Unexpected deletion verification response.");
                    }
                    Console.WriteLine("Deezer deletion acknowledged; created playlist is no longer readable.");
                }
                catch (DeezNET.Exceptions.InvalidIDException) { Console.WriteLine("Deezer disposable playlist deletion verified."); }
                catch (Exception error) { Assert.Fail("Check cleanup of Deezer test playlist " + id + ", " + error.GetType().Name); }
            }
        }
    }

    [TestMethod]
    public async Task QobuzPrivatePlaylistResponseContracts()
    {
        RequireWrites();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-App-Id", Secrets.Require("qobuz_app_id"));
        http.DefaultRequestHeaders.Add("X-User-Auth-Token", Secrets.Require("qobuz_token"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string? createdId = null;
        async Task<JsonNode> Post(string endpoint, Dictionary<string, string> fields, CancellationToken token)
        {
            using var response = await http.PostAsync("https://www.qobuz.com/api.json/0.2/" + endpoint, new FormUrlEncodedContent(fields), token);
            Assert.IsTrue(response.IsSuccessStatusCode, endpoint + ": HTTP " + (int)response.StatusCode);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!;
        }
        try
        {
            var name = "FluentDL playlist probe " + Guid.NewGuid().ToString("N");
            Console.WriteLine("Recovery name: " + name);
            var created = await Post("playlist/create", new() { ["name"] = name, ["description"] = "Disposable API response test", ["is_public"] = "0", ["is_collaborative"] = "0" }, timeout.Token);
            Console.WriteLine("Qobuz create fields: " + string.Join(",", created.AsObject().Select(p => p.Key)));
            createdId = created["id"]!.ToString();
            Assert.AreEqual("false", created["is_public"]?.ToJsonString());
            var ids = new[] { Fixtures.Tracks[0].QobuzId!, Fixtures.Tracks[1].QobuzId!, Fixtures.Tracks[0].QobuzId! };
            var appended = await Post("playlist/addTracks", new() { ["playlist_id"] = createdId, ["track_ids"] = string.Join(",", ids), ["no_duplicate"] = "0" }, timeout.Token);
            Console.WriteLine("Qobuz append fields: " + string.Join(",", appended.AsObject().Select(p => p.Key)));
            Console.WriteLine("Qobuz acknowledged tracks_count: " + appended["tracks_count"]);
            using var read = await http.GetAsync("https://www.qobuz.com/api.json/0.2/playlist/get?playlist_id=" + createdId + "&extra=track_ids", timeout.Token);
            Assert.IsTrue(read.IsSuccessStatusCode);
            var playlist = JsonNode.Parse(await read.Content.ReadAsStringAsync(timeout.Token))!;
            Console.WriteLine("Qobuz track_ids kind: " + playlist["track_ids"]!.GetType().Name);
            Console.WriteLine("Qobuz read-back IDs: " + playlist["track_ids"]!.ToJsonString());
            var duplicate = await Post("playlist/addTracks", new() { ["playlist_id"] = createdId, ["track_ids"] = ids[0], ["no_duplicate"] = "0" }, timeout.Token);
            Console.WriteLine("Qobuz separate duplicate tracks_count: " + duplicate["tracks_count"]);
        }
        catch (Exception error) { Assert.Fail("Qobuz probe stopped: " + error.GetType().Name + ". Response bodies and credentials omitted."); }
        finally
        {
            if (createdId != null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var deleted = await Post("playlist/delete", new() { ["playlist_id"] = createdId }, cleanup.Token);
                Assert.AreEqual("success", deleted["status"]?.ToString());
                using var verify = await http.GetAsync("https://www.qobuz.com/api.json/0.2/playlist/get?playlist_id=" + createdId, cleanup.Token);
                Assert.IsTrue(verify.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, "Deletion not verified for Qobuz test playlist " + createdId);
                Console.WriteLine("Qobuz disposable playlist deletion verified: HTTP " + (int)verify.StatusCode);
            }
        }
    }

    private static void RequireWrites()
    {
        if (Environment.GetEnvironmentVariable("FLUENTDL_PLAYLIST_WRITE_TESTS") != "1") Assert.Inconclusive("Opt in to disposable playlist writes with FLUENTDL_PLAYLIST_WRITE_TESTS=1.");
    }
    private static string Shape(JToken token) => token is JObject value ? "Object, fields=" + string.Join(",", value.Properties().Select(p => p.Name)) : token.Type.ToString();
}
