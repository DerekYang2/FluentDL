using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Helpers;

namespace FluentDL.Services.Playlists;

internal sealed record SpotifyPlaylistSession(string UserId, object Identity, Func<CancellationToken, Task<string>> AccessToken);

internal sealed class SpotifyPlaylistClient(Func<SpotifyPlaylistSession?> session, HttpClient? transport = null, ProviderRequestPolicy? requests = null)
{
    private const string BaseUrl = "https://api.spotify.com/v1/";
    private readonly HttpClient http = transport ?? new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    private readonly ProviderRequestPolicy policy = requests ?? ProviderRequests.SpotifyPlaylists;
    internal static IReadOnlyList<PlaylistVisibility> Visibilities { get; } = [PlaylistVisibility.Private, PlaylistVisibility.Public];
    internal static bool ValidId(string? id) => id is { Length: 22 } && id.All(char.IsAsciiLetterOrDigit);

    internal async Task<bool> CanCreateAsync(CancellationToken token)
    {
        var account = session();
        if (account == null) return false;
        using var me = await Send("me", null, account, token);
        return me.RootElement.GetProperty("id").GetString() == account.UserId;
    }

    private SpotifyPlaylistSession Current(SpotifyPlaylistSession? expected = null)
    {
        var active = session();
        if (active == null || string.IsNullOrWhiteSpace(active.UserId) || (expected != null
            && (!ReferenceEquals(expected.Identity, active.Identity) || expected.UserId != active.UserId)))
            throw new PlaylistWriteRejected("The Spotify user sign-in changed or expired. Sign in again at Settings > Authentication > Spotify.");
        return active;
    }

    internal Task<PlaylistCreationResult> CreateAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token)
    {
        var account = session();
        string? id = request.Target?.Id;
        if (id != null && !ValidId(id))
            return Task.FromResult(new PlaylistCreationResult(null, 0, request.TrackIds.Count, PlaylistCreationStatus.Failed, "Choose a valid Spotify playlist."));
        return PlaylistUpload.RunAsync("Spotify", request, Visibilities,
            async (created, ct) =>
            {
                var active = Current(account);
                using var response = await Send("me/playlists", new
                {
                    name = request.Name.Trim(), description = request.Description.Trim(),
                    @public = request.Visibility == PlaylistVisibility.Public, collaborative = false,
                }, active, ct);
                id = response.RootElement.GetProperty("id").GetString();
                if (id == null || !ValidId(id)) throw new JsonException("Spotify did not return a playlist ID.");
                created(Url(id));
                EnsureOwner(response.RootElement, active);
                if (response.RootElement.GetProperty("public").GetBoolean() != (request.Visibility == PlaylistVisibility.Public)
                    || response.RootElement.GetProperty("collaborative").GetBoolean())
                    throw new PlaylistWriteRejected("Spotify did not confirm the requested profile visibility. No tracks were added.");
            },
            async (batch, expected, ct) =>
            {
                var active = Current(account);
                PlaylistUpload.EnsureUnchanged(await ReadTracks(id!, active, ct), expected.Take(expected.Count - batch.Count).ToArray());
                using var response = await Send($"playlists/{id}/items", new { uris = batch.Select(track => "spotify:track:" + track).ToArray() }, active, ct);
                if (string.IsNullOrWhiteSpace(response.RootElement.GetProperty("snapshot_id").GetString()))
                    throw new JsonException("Spotify did not acknowledge the playlist update.");
                return PlaylistUpload.Confirm(await ReadTracks(id!, active, ct, expected), expected, batch.Count);
            }, progress, token,
            async ct => new(Url(id!), await ReadTracks(id!, Current(account), ct)),
            nameLimit: 100, validTrackId: ValidId, normalizeTrackId: id => id, batchSize: 100);
    }

    internal async Task<IReadOnlyList<OwnedPlaylist>> GetOwnedAsync(CancellationToken token)
    {
        var active = Current();
        var playlists = new List<OwnedPlaylist>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int offset = 0;
        int? total = null;
        while (true)
        {
            using var page = await Send($"me/playlists?limit=50&offset={offset}", null, active, token);
            var root = page.RootElement;
            var count = root.GetProperty("total").GetInt32();
            total ??= count;
            if (count != total || root.GetProperty("offset").GetInt32() != offset) throw new JsonException("Spotify changed the playlist listing during pagination.");
            var items = root.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                if (!ValidId(id) || !seen.Add(id)) throw new JsonException("Spotify returned an invalid or repeated playlist.");
                if (item.GetProperty("owner").GetProperty("id").GetString() == active.UserId)
                    playlists.Add(new(id, item.GetProperty("name").GetString() ?? "Untitled playlist", Url(id)));
            }
            offset += items.GetArrayLength();
            if (offset == total) break;
            if (offset > total || items.GetArrayLength() == 0) throw new JsonException("Incomplete Spotify playlist listing.");
        }
        Current(active);
        return playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal Task<IReadOnlyList<string>> GetOwnedTrackIdsAsync(string id, CancellationToken token)
    {
        if (!ValidId(id)) throw new PlaylistWriteRejected("Choose a valid Spotify playlist.");
        return ReadTracks(id, Current(), token);
    }

    private static void EnsureOwner(JsonElement playlist, SpotifyPlaylistSession account)
    {
        if (playlist.GetProperty("owner").GetProperty("id").GetString() != account.UserId)
            throw new PlaylistWriteRejected("Only playlists owned by the current Spotify user can receive tracks.");
    }

    private async Task<IReadOnlyList<string>> ReadTracks(string id, SpotifyPlaylistSession account, CancellationToken token,
        IReadOnlyList<string>? expected = null)
    {
        async Task<string> Snapshot()
        {
            using var metadata = await Send($"playlists/{id}?fields=id,owner(id),snapshot_id", null, account, token);
            var root = metadata.RootElement;
            if (root.GetProperty("id").GetString() != id) throw new JsonException("Spotify returned a different playlist.");
            EnsureOwner(root, account);
            var snapshot = root.GetProperty("snapshot_id").GetString();
            if (string.IsNullOrEmpty(snapshot)) throw new JsonException("Missing Spotify playlist version.");
            return snapshot;
        }
        IReadOnlyList<string>? previous = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var before = await Snapshot();
            var contents = new List<string>();
            int? total = null;
            var complete = true;
            while (true)
            {
                var offset = contents.Count;
                using var page = await Send($"playlists/{id}/items?limit=50&offset={offset}&additional_types=track,episode", null, account, token);
                var root = page.RootElement;
                var count = root.GetProperty("total").GetInt32();
                total ??= count;
                if (root.GetProperty("offset").GetInt32() != offset) throw new JsonException("Spotify returned a different playlist page.");
                if (count != total) { complete = false; break; }
                var items = root.GetProperty("items");
                foreach (var entry in items.EnumerateArray())
                {
                    // Keep non-track entries in their positions so an append never removes or ignores existing content.
                    if (!entry.TryGetProperty("item", out var item)) item = entry.GetProperty("track");
                    if (item.ValueKind == JsonValueKind.Null) contents.Add("@unavailable:" + contents.Count);
                    else if (entry.TryGetProperty("is_local", out var local) && local.GetBoolean())
                        contents.Add("@local:" + contents.Count + ":" + item.GetProperty("uri").GetString());
                    else
                    {
                        var type = item.GetProperty("type").GetString();
                        var trackId = item.TryGetProperty("linked_from", out var linked) && linked.ValueKind == JsonValueKind.Object
                            ? linked.GetProperty("id").GetString() : item.GetProperty("id").GetString();
                        if (trackId == null || !ValidId(trackId) || type is not ("track" or "episode"))
                            throw new JsonException("Spotify returned an unsupported playlist item.");
                        contents.Add(type == "track" ? trackId : "spotify:episode:" + trackId);
                    }
                }
                if (contents.Count == total) break;
                if (contents.Count > total || items.GetArrayLength() == 0) { complete = false; break; }
            }
            var after = await Snapshot();
            if (complete)
            {
                // A snapshot change alone does not prove the items changed. Require a stable snapshot or two identical full reads.
                var stable = after == before || (previous != null && previous.SequenceEqual(contents));
                if (stable && (expected == null || contents.SequenceEqual(expected))) return contents;
                if (stable && attempt == 2) return contents; // The caller reports a verified item mismatch without sending another batch.
                previous = contents;
            }
            else previous = null;
            if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(150), token);
        }
        throw new PlaylistWriteRejected("Spotify did not provide stable playlist contents after three read checks. No further tracks were sent. Check the playlist before trying again.", uncertain: true);
    }

    private async Task<JsonDocument> Send(string endpoint, object? body, SpotifyPlaylistSession account, CancellationToken token)
    {
        Current(account);
        var accessToken = await account.AccessToken(token);
        Current(account);
        using var response = await policy.SendAsync(http, () =>
        {
            Current(account);
            var message = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, BaseUrl + endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (body != null) message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return message;
        }, token, allowRetries: body == null);
        if (!response.IsSuccessStatusCode)
        {
            var hint = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Spotify user authorization expired. Sign in again at Settings > Authentication > Spotify.",
                HttpStatusCode.Forbidden => "Spotify denied access. Check playlist permissions. For Development Mode, add this Spotify account in your developer app's Settings > Users Management.",
                HttpStatusCode.TooManyRequests => "Spotify is rate limiting requests. Wait before trying again.",
                _ => $"Spotify refused the playlist request, HTTP {(int)response.StatusCode}.",
            };
            if (body == null) throw new HttpRequestException(hint, null, response.StatusCode);
            throw new PlaylistWriteRejected(hint, uncertain: (int)response.StatusCode >= 500);
        }
        if (!ReferenceEquals(session()?.Identity, account.Identity))
            throw new PlaylistWriteRejected("The Spotify account changed during the request. Check the playlist before trying again.", uncertain: body != null);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private static Uri Url(string id) => new("https://open.spotify.com/playlist/" + id);
}
