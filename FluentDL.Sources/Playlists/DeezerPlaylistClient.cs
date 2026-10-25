using System.Net;
using System.Text;
using System.Text.Json;
using FluentDL.Contracts.Sources;

namespace FluentDL.Services.Playlists;

internal sealed record DeezerPlaylistSession(string Arl, string Sid, string ApiToken, object Identity, string UserId = "");
internal sealed record DeezerPlaylistContents(string Name, IReadOnlyList<string> TrackIds, object SessionIdentity);

/// <summary>Uses DeezNET's signed-in session, but does not replay gateway writes after gateway/token errors.</summary>
internal sealed class DeezerPlaylistClient(Func<DeezerPlaylistSession?> session, HttpClient? transport = null)
{
    private readonly HttpClient http = transport ?? new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
    internal static IReadOnlyList<PlaylistVisibility> Visibilities { get; } = [PlaylistVisibility.Private, PlaylistVisibility.Public];
    internal bool CanRead => session() is { Arl.Length: > 0, Sid.Length: > 0, ApiToken.Length: > 0 };
    internal object? ReadSessionIdentity => session() is { Arl.Length: > 0, Sid.Length: > 0, ApiToken.Length: > 0 } active ? active.Identity : null;
    internal bool CanCreate => CanRead;
    private sealed record ReadCheck(object Identity, bool Available);
    private ReadCheck? readCheck;
    internal bool? ReadAccess => readCheck is { } check && ReferenceEquals(check.Identity, session()?.Identity)
        ? check.Available : null;

    internal async Task CheckReadAccessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var active = session();
        readCheck = null;
        if (!CanRead || active == null) return;
        try
        {
            if (!long.TryParse(active.UserId, out var user) || user <= 0) throw new JsonException("The Deezer account could not be identified.");
            using var account = await Call("deezer.getUserData", new { }, active, token, verification: true);
            if (account.RootElement.GetProperty("USER").GetProperty("USER_ID").ToString() != active.UserId)
                throw new JsonException("The Deezer session does not match the signed-in account.");
            if (!ReferenceEquals(active.Identity, session()?.Identity)) return;
            using var page = await Call("deezer.pageProfile", new { USER_ID = user, tab = "playlists", nb = 1, start = 0 }, active, token, verification: true);
            var list = page.RootElement.GetProperty("TAB").GetProperty("playlists");
            if (list.GetProperty("data").ValueKind != JsonValueKind.Array || list.GetProperty("total").GetInt32() < 0)
                throw new JsonException("Invalid playlist listing.");
            readCheck = new(active.Identity, true);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            readCheck = new(active.Identity, false);
        }
    }

    internal async Task<DeezerPlaylistContents> ReadPlaylistAsync(string id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit))
            throw new PlaylistWriteRejected("Choose a valid Deezer playlist link.");
        var active = session();
        if (!CanRead || active == null) throw new PlaylistWriteRejected("Sign in to Deezer with your ARL in Settings to load private playlists.");
        using var page = await Call("deezer.pagePlaylist", new { PLAYLIST_ID = number, lang = "en" }, active, token, verification: true);
        var name = page.RootElement.GetProperty("DATA").GetProperty("TITLE").GetString() ?? "Untitled playlist";
        // Import is read-only. Deezer decides which public or private playlists this account can read.
        var ids = await ReadTracks(id, active, token);
        if (!ReferenceEquals(active.Identity, session()?.Identity))
            throw new PlaylistWriteRejected("The Deezer sign-in changed. Load the playlist again.");
        return new(name, ids, active.Identity);
    }

    internal const int MetadataBatchSize = 50;

    internal DeezerPlaylistSession RequireReadSession(object identity)
    {
        var active = session();
        if (active is not { Arl.Length: > 0, Sid.Length: > 0, ApiToken.Length: > 0 } || !ReferenceEquals(identity, active.Identity))
            throw new PlaylistWriteRejected("The Deezer sign-in changed. Load the playlist again.");
        return active;
    }

    internal async Task<IReadOnlyList<JsonElement>> ReadTrackBatchAsync(IReadOnlyList<string> ids, object identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var active = RequireReadSession(identity);
        if (ids.Count is < 1 or > MetadataBatchSize || ids.Any(id => !long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit)))
            throw new ArgumentException("Invalid Deezer metadata batch.", nameof(ids));
        using var result = await Call("song.getListData", new { SNG_IDS = ids.Select(long.Parse).ToArray() }, active, token, verification: true);
        RequireReadSession(identity);
        return result.RootElement.GetProperty("data").EnumerateArray().Select(track => track.Clone()).ToArray();
    }

    internal Task<PlaylistCreationResult> CreateAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token)
    {
        var ids = request.TrackIds.ToArray();
        var account = session();
        string? playlistId = request.Target?.Id;
        if (playlistId != null && (!long.TryParse(playlistId, out var targetId) || targetId <= 0 || !playlistId.All(char.IsAsciiDigit)))
            return Task.FromResult(new PlaylistCreationResult(null, 0, ids.Length, PlaylistCreationStatus.Failed, "Choose a valid Deezer playlist."));
        DeezerPlaylistSession Current()
        {
            var current = session();
            if (account == null || current == null || !ReferenceEquals(account.Identity, current.Identity))
                throw new PlaylistWriteRejected("The Deezer sign-in changed or expired. Check Settings before trying again.");
            return current;
        }
        return PlaylistUpload.RunAsync("Deezer", request with { TrackIds = ids }, Visibilities,
            async (created, ct) =>
            {
                var active = Current();
                if (!CanCreate) throw new PlaylistWriteRejected("Sign in to Deezer with your ARL in Settings first.");
                using var result = await Call("playlist.create", new { title = request.Name.Trim(), description = request.Description.Trim(),
                    status = request.Visibility == PlaylistVisibility.Private ? 1 : 0, songs = Array.Empty<object>() }, active, ct);
                if (!long.TryParse(result.RootElement.ToString(), out var id) || id <= 0) throw new JsonException("No playlist ID returned.");
                playlistId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                created(new Uri("https://www.deezer.com/playlist/" + playlistId));
                using var page = await Call("deezer.pagePlaylist", new { PLAYLIST_ID = id, lang = "en" }, active, ct, verification: true);
                var visibility = page.RootElement.GetProperty("DATA").GetProperty("STATUS").ToString();
                if (visibility != (request.Visibility == PlaylistVisibility.Private ? "1" : "0"))
                    throw new PlaylistWriteRejected("Deezer did not confirm the requested visibility. No tracks were added.");
            },
            async (batch, expected, ct) =>
            {
                var active = Current();
                if (request.Target != null)
                {
                    await RequireOwner(playlistId!, active, ct);
                    PlaylistUpload.EnsureUnchanged(await ReadTracks(playlistId!, active, ct), expected.Take(expected.Count - batch.Count).ToArray());
                }
                using var result = await Call("playlist.addSongs", new { PLAYLIST_ID = long.Parse(playlistId!),
                    songs = batch.Select(id => new object[] { id, 0 }).ToArray(), offset = -1 }, active, ct);
                if (result.RootElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException("Unknown append acknowledgement.");
                var actual = await ReadTracks(playlistId!, active, ct);
                return PlaylistUpload.Confirm(actual, expected, batch.Count);
            }, progress, token, async ct =>
            {
                var active = Current();
                await RequireOwner(playlistId!, active, ct);
                return new(Url(playlistId!), await ReadTracks(playlistId!, active, ct));
            }, nameLimit: 50);
    }

    internal async Task<IReadOnlyList<OwnedPlaylist>> GetOwnedAsync(CancellationToken token)
    {
        var active = session() ?? throw new PlaylistWriteRejected("Sign in to Deezer in Settings first.");
        if (!long.TryParse(active.UserId, out var user) || user <= 0) throw new PlaylistWriteRejected("The Deezer account could not be identified. Sign in again.");
        var result = new List<OwnedPlaylist>();
        var start = 0;
        var seen = new HashSet<string>();
        while (true)
        {
            using var page = await Call("deezer.pageProfile", new { USER_ID = user, tab = "playlists", nb = 100, start }, active, token, verification: true);
            var list = page.RootElement.GetProperty("TAB").GetProperty("playlists");
            var data = list.GetProperty("data");
            foreach (var item in data.EnumerateArray())
            {
                var id = item.GetProperty("PLAYLIST_ID").ToString();
                if (!seen.Add(id)) throw new JsonException("Deezer repeated a playlist page. The list may be incomplete.");
                if (item.GetProperty("PARENT_USER_ID").ToString() == active.UserId)
                    result.Add(new(id, item.GetProperty("TITLE").GetString() ?? "Untitled playlist", Url(id)));
            }
            start += data.GetArrayLength();
            if (start >= list.GetProperty("total").GetInt32()) break;
            if (data.GetArrayLength() == 0) throw new JsonException("Incomplete playlist listing.");
        }
        if (!ReferenceEquals(active.Identity, session()?.Identity)) throw new PlaylistWriteRejected("The Deezer account changed. Refresh the playlist list.");
        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static Uri Url(string id) => new("https://www.deezer.com/playlist/" + id);

    internal async Task<IReadOnlyList<string>> GetOwnedTrackIdsAsync(string id, CancellationToken token)
    {
        if (!long.TryParse(id, out var number) || number <= 0 || !id.All(char.IsAsciiDigit))
            throw new PlaylistWriteRejected("Choose a valid Deezer playlist.");
        var active = session() ?? throw new PlaylistWriteRejected("Sign in to Deezer in Settings first.");
        await RequireOwner(id, active, token);
        var ids = await ReadTracks(id, active, token);
        if (!ReferenceEquals(active.Identity, session()?.Identity)) throw new PlaylistWriteRejected("The Deezer account changed. Refresh the playlist list.");
        return ids;
    }

    private async Task RequireOwner(string id, DeezerPlaylistSession active, CancellationToken token)
    {
        using var page = await Call("deezer.pagePlaylist", new { PLAYLIST_ID = long.Parse(id), lang = "en" }, active, token, verification: true);
        if (string.IsNullOrEmpty(active.UserId) || page.RootElement.GetProperty("DATA").GetProperty("PARENT_USER_ID").ToString() != active.UserId)
            throw new PlaylistWriteRejected("Only playlists owned by the current Deezer account can receive tracks.");
    }

    private async Task<IReadOnlyList<string>> ReadTracks(string id, DeezerPlaylistSession active, CancellationToken token)
    {
        var actual = new List<string>();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(active.Identity, session()?.Identity))
                throw new PlaylistWriteRejected("The Deezer sign-in changed. Load the playlist again.");
            using var page = await Call("playlist.getSongs", new { PLAYLIST_ID = long.Parse(id), nb = 1000, start = actual.Count }, active, token, verification: true);
            var data = page.RootElement.GetProperty("data");
            actual.AddRange(data.EnumerateArray().Select(track => track.GetProperty("SNG_ID").ToString()));
            if (actual.Count >= page.RootElement.GetProperty("total").GetInt32()) return actual;
            if (data.GetArrayLength() == 0) throw new JsonException("Incomplete playlist read-back.");
        }
    }

    private async Task<JsonDocument> Call(string method, object body, DeezerPlaylistSession account, CancellationToken token, bool verification = false)
    {
        var url = "https://www.deezer.com/ajax/gw-light.php?api_version=1.0&input=3&method=" + Uri.EscapeDataString(method)
            + "&api_token=" + Uri.EscapeDataString(account.ApiToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("Cookie", "arl=" + account.Arl + "; sid=" + account.Sid);
        using var response = await http.SendAsync(request, token);
        var json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            if (verification) throw new HttpRequestException("Playlist verification failed.", null, response.StatusCode);
            throw new PlaylistWriteRejected(response.StatusCode == HttpStatusCode.TooManyRequests ? "Deezer is rate limiting requests. Wait before trying again."
                : $"Deezer refused the request, HTTP {(int)response.StatusCode}. Check your sign-in before trying again.", (int)response.StatusCode >= 500);
        }
        using var envelope = JsonDocument.Parse(json);
        var root = envelope.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.EnumerateObject().Any())
        {
            if (verification) throw new JsonException("Deezer could not verify the playlist.");
            var known = new[] { "VALID_TOKEN_REQUIRED", "GATEWAY_ERROR", "DATA_ERROR", "PERMISSION_ERROR", "VALID_PARAMETER_REQUIRED", "VALID_PLAYLIST_REQUIRED", "TOO_MANY_SONGS", "UNKNOWN_ERROR" };
            var code = known.FirstOrDefault(name => error.TryGetProperty(name, out _))
                ?? error.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => name.Contains('_') && name.Length <= 64 && name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'));
            throw new PlaylistWriteRejected((code == null ? "Deezer refused the playlist request." : $"Deezer returned {code} for the playlist request.")
                + " Check your sign-in and tracks before trying again.", error.TryGetProperty("GATEWAY_ERROR", out _));
        }
        return JsonDocument.Parse(root.GetProperty("results").GetRawText());
    }
}
