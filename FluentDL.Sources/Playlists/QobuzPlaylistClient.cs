using System.Net;
using System.Text.Json;
using FluentDL.Contracts.Sources;

namespace FluentDL.Services.Playlists;

internal sealed record QobuzPlaylistSession(string AppId, string UserToken, object Identity, string UserId = "");

internal sealed class QobuzPlaylistClient(Func<QobuzPlaylistSession?> session, HttpClient? transport = null)
{
    private const string BaseUrl = "https://www.qobuz.com/api.json/0.2/";
    private readonly HttpClient http = transport ?? new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    internal static IReadOnlyList<PlaylistVisibility> Visibilities { get; } = [PlaylistVisibility.Private, PlaylistVisibility.Public];
    internal bool CanCreate => session() is { AppId.Length: > 0, UserToken.Length: > 0 };

    internal Task<PlaylistCreationResult> CreateAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token)
    {
        var account = session();
        string? playlistId = request.Target?.Id;
        if (playlistId != null && (!long.TryParse(playlistId, out var targetId) || targetId <= 0 || !playlistId.All(char.IsAsciiDigit)))
            return Task.FromResult(new PlaylistCreationResult(null, 0, request.TrackIds.Count, PlaylistCreationStatus.Failed, "Choose a valid Qobuz playlist."));
        QobuzPlaylistSession Current()
        {
            var current = session();
            if (account == null || current == null || !ReferenceEquals(account.Identity, current.Identity))
                throw new PlaylistWriteRejected("The Qobuz sign-in changed or expired. Check Settings before trying again.");
            return current;
        }
        return PlaylistUpload.RunAsync("Qobuz", request, Visibilities,
            async (created, ct) =>
            {
                var active = Current();
                if (!CanCreate) throw new PlaylistWriteRejected("Sign in to Qobuz with your ID and token in Settings first.");
                using var result = await Send("playlist/create", new() { ["name"] = request.Name.Trim(), ["description"] = request.Description.Trim(),
                    ["is_public"] = request.Visibility == PlaylistVisibility.Public ? "1" : "0", ["is_collaborative"] = "0" }, active, ct);
                var id = result.RootElement.GetProperty("id").ToString();
                if (!long.TryParse(id, out var number) || number <= 0) throw new JsonException("No playlist ID returned.");
                playlistId = id;
                created(new Uri("https://play.qobuz.com/playlist/" + id));
                if (result.RootElement.GetProperty("is_public").GetBoolean() != (request.Visibility == PlaylistVisibility.Public))
                    throw new PlaylistWriteRejected("Qobuz did not confirm the requested visibility. No tracks were added.");
            },
            async (batch, expected, ct) =>
            {
                var active = Current();
                if (request.Target != null)
                    PlaylistUpload.EnsureUnchanged(await ReadTracks(playlistId!, active, ct, requireOwner: true), expected.Take(expected.Count - batch.Count).ToArray());
                using var result = await Send("playlist/addTracks", new() { ["playlist_id"] = playlistId!, ["track_ids"] = string.Join(",", batch), ["no_duplicate"] = "1" }, active, ct);
                var actual = await ReadTracks(playlistId!, active, ct);
                return PlaylistUpload.ConfirmAllowingOmissions(actual, expected, batch.Count);
            }, progress, token, async ct =>
            {
                var active = Current();
                return new(new Uri("https://play.qobuz.com/playlist/" + playlistId), await ReadTracks(playlistId!, active, ct, requireOwner: true));
            });
    }

    internal async Task<IReadOnlyList<OwnedPlaylist>> GetOwnedAsync(CancellationToken token)
    {
        var active = session() ?? throw new PlaylistWriteRejected("Sign in to Qobuz in Settings first.");
        if (string.IsNullOrEmpty(active.UserId)) throw new PlaylistWriteRejected("The Qobuz account could not be identified. Sign in again.");
        var playlists = new List<OwnedPlaylist>();
        var offset = 0;
        var seen = new HashSet<string>();
        while (true)
        {
            using var document = await Send($"playlist/getUserPlaylists?limit=100&offset={offset}", null, active, token, verification: true);
            var page = document.RootElement.GetProperty("playlists");
            var items = page.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").ToString();
                if (!seen.Add(id)) throw new JsonException("Qobuz repeated a playlist page.");
                if (item.GetProperty("owner").GetProperty("id").ToString() == active.UserId)
                    playlists.Add(new(id, item.GetProperty("name").GetString() ?? "Untitled playlist", new Uri("https://play.qobuz.com/playlist/" + id)));
            }
            offset += items.GetArrayLength();
            if (offset >= page.GetProperty("total").GetInt32()) break;
            if (items.GetArrayLength() == 0) throw new JsonException("Incomplete playlist listing.");
        }
        if (!ReferenceEquals(active.Identity, session()?.Identity)) throw new PlaylistWriteRejected("The Qobuz account changed. Refresh the playlist list.");
        return playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadTracks(string id, QobuzPlaylistSession active, CancellationToken token, bool requireOwner = false)
    {
        using var read = await Send("playlist/get?playlist_id=" + Uri.EscapeDataString(id) + "&extra=track_ids", null, active, token, verification: true);
        if (requireOwner && (string.IsNullOrEmpty(active.UserId) || read.RootElement.GetProperty("owner").GetProperty("id").ToString() != active.UserId))
            throw new PlaylistWriteRejected("Only playlists owned by the current Qobuz account can receive tracks.");
        var ids = read.RootElement.GetProperty("track_ids");
        var actual = ids.ValueKind switch
        {
            JsonValueKind.Array => ids.EnumerateArray().Select(item => item.ToString()).ToArray(),
            JsonValueKind.Object => ids.EnumerateObject().OrderBy(p => int.Parse(p.Name)).Select(p => p.Value.ToString()).ToArray(),
            _ => throw new JsonException("Unknown playlist track ID format."),
        };
        if (read.RootElement.TryGetProperty("tracks_count", out var count) && count.GetInt32() != actual.Length)
            throw new JsonException("Incomplete playlist read-back.");
        return actual;
    }

    internal async Task<IReadOnlyList<string>> GetOwnedTrackIdsAsync(string id, CancellationToken token)
    {
        if (!long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit))
            throw new PlaylistWriteRejected("Choose a valid Qobuz playlist.");
        var active = session() ?? throw new PlaylistWriteRejected("Sign in to Qobuz in Settings first.");
        var ids = await ReadTracks(id, active, token, requireOwner: true);
        if (!ReferenceEquals(active.Identity, session()?.Identity)) throw new PlaylistWriteRejected("The Qobuz account changed. Refresh the playlist list.");
        return ids;
    }

    private async Task<JsonDocument> Send(string endpoint, Dictionary<string, string>? body, QobuzPlaylistSession account, CancellationToken token, bool verification = false)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, BaseUrl + endpoint);
        request.Headers.Add("X-App-Id", account.AppId);
        request.Headers.Add("X-User-Auth-Token", account.UserToken);
        if (body != null) request.Content = new FormUrlEncodedContent(body);
        using var response = await http.SendAsync(request, token);
        var content = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            if (verification) throw new HttpRequestException("Playlist verification failed.", null, response.StatusCode);
            throw new PlaylistWriteRejected(response.StatusCode == HttpStatusCode.TooManyRequests ? "Qobuz is rate limiting requests. Wait before trying again."
                : $"Qobuz refused the request, HTTP {(int)response.StatusCode}. Check your sign-in before trying again.", (int)response.StatusCode >= 500);
        }
        var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("status", out var status) && status.GetString() == "error")
        {
            document.Dispose();
            if (verification) throw new JsonException("Qobuz could not verify the playlist.");
            throw new PlaylistWriteRejected("Qobuz refused the playlist request. Check your sign-in and tracks before trying again.");
        }
        return document;
    }
}
