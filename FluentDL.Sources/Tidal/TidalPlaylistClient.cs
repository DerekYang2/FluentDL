using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Services.Playlists;

namespace FluentDL.Services.Tidal;

/// <summary>Official playlist writes using the existing download account. Never replays mutations.</summary>
public sealed class TidalPlaylistClient
{
    private const string BaseUrl = "https://openapi.tidal.com/v2/";
    private readonly TidalDownloadClient accounts;
    private readonly HttpClient http;

    public TidalPlaylistClient(TidalDownloadClient accounts) : this(accounts,
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false })) { }

    internal TidalPlaylistClient(TidalDownloadClient accounts, HttpClient http) => (this.accounts, this.http) = (accounts, http);
    public async Task<bool> CanCreateAsync(CancellationToken token) => await accounts.GetApiSessionAsync(token) != null;

    public async Task<IReadOnlyList<OwnedPlaylist>> GetOwnedAsync(CancellationToken token)
    {
        var account = await RequireAccount(null, token);
        var playlists = new List<OwnedPlaylist>();
        await Pages("playlists?filter%5Bowners.id%5D=me&include=owners", account, document =>
        {
            foreach (var item in document.GetProperty("data").EnumerateArray())
            {
                if (!IsOwner(item, account)) continue;
                var id = item.GetProperty("id").GetString()!;
                if (!Guid.TryParse(id, out _)) throw new JsonException("Invalid playlist ID.");
                playlists.Add(new(id, item.GetProperty("attributes").GetProperty("name").GetString() ?? "Untitled playlist", Url(id)));
            }
        }, token);
        await RequireAccount(account, token);
        return playlists.DistinctBy(p => p.Id).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task<PlaylistCreationResult> CreateAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token)
    {
        var id = request.Target?.Id;
        if (id != null && !Guid.TryParse(id, out _))
            return Task.FromResult(new PlaylistCreationResult(null, 0, request.TrackIds.Count, PlaylistCreationStatus.Failed, "Choose a valid TIDAL playlist."));
        TidalDownloadClient.Session? original = null;
        async Task<TidalDownloadClient.Session> Current(CancellationToken ct)
        {
            var current = await RequireAccount(original, ct);
            original ??= current;
            return current;
        }
        return PlaylistUpload.RunAsync("TIDAL", request, [PlaylistVisibility.Unlisted, PlaylistVisibility.Public],
            async (created, ct) =>
            {
                var account = await Current(ct);
                using var result = await Send(HttpMethod.Post, "playlists", account, new
                {
                    data = new { type = "playlists", attributes = new { name = request.Name.Trim(), description = request.Description.Trim(), accessType = request.Visibility == PlaylistVisibility.Public ? "PUBLIC" : "UNLISTED" } },
                }, ct);
                id = result.RootElement.GetProperty("data").GetProperty("id").GetString();
                if (!Guid.TryParse(id, out _)) throw new JsonException("Create did not return a playlist UUID.");
                created(Url(id!));
            },
            async (batch, expected, ct) =>
            {
                var account = await Current(ct);
                if (request.Target != null)
                {
                    await RequireOwner(id!, account, ct);
                    PlaylistUpload.EnsureUnchanged(await ReadTracks(id!, account, ct), expected.Take(expected.Count - batch.Count).ToArray());
                }
                using var result = await Send(HttpMethod.Post, $"playlists/{id}/relationships/items?countryCode={Uri.EscapeDataString(account.Country)}", account, new
                {
                    data = batch.Select(trackId => new { type = "tracks", id = trackId }).ToArray(),
                    meta = new { onDuplicates = "SKIP" },
                }, ct);
                var acknowledged = result.RootElement.GetProperty("data").GetArrayLength();
                if (acknowledged > batch.Count) throw new JsonException("Invalid acknowledgement count.");
                return PlaylistUpload.Confirm(await ReadTracks(id!, account, ct), expected, batch.Count);
            }, progress, token, async ct =>
            {
                var account = await Current(ct);
                await RequireOwner(id!, account, ct);
                return new(Url(id!), await ReadTracks(id!, account, ct));
            });
    }

    private async Task<TidalDownloadClient.Session> RequireAccount(TidalDownloadClient.Session? original, CancellationToken token)
    {
        var current = await accounts.GetApiSessionAsync(token);
        if (current == null) throw new PlaylistWriteRejected("Sign in at Settings > Authentication > TIDAL first.");
        if (original != null && (current.UserId != original.UserId || current.ClientId != original.ClientId
            || (original.UserId == null && current.AccessToken != original.AccessToken)))
            throw new PlaylistWriteRejected("The TIDAL account changed. No further tracks were sent.");
        return current;
    }

    private static Uri Url(string id) => new("https://tidal.com/browse/playlist/" + id);
    public async Task<IReadOnlyList<string>> GetOwnedTrackIdsAsync(string id, CancellationToken token)
    {
        if (!Guid.TryParse(id, out _)) throw new PlaylistWriteRejected("Choose a valid TIDAL playlist.");
        var account = await RequireAccount(null, token);
        await RequireOwner(id, account, token);
        var ids = await ReadTracks(id, account, token);
        await RequireAccount(account, token);
        return ids;
    }
    private static bool IsOwner(JsonElement playlist, TidalDownloadClient.Session account) => !string.IsNullOrEmpty(account.UserId)
        && playlist.GetProperty("relationships").GetProperty("owners").GetProperty("data").EnumerateArray()
            .Any(owner => owner.GetProperty("id").GetString() == account.UserId);

    private async Task RequireOwner(string id, TidalDownloadClient.Session account, CancellationToken token)
    {
        using var document = await Send(HttpMethod.Get, $"playlists/{id}?include=owners", account, null, token);
        if (!IsOwner(document.RootElement.GetProperty("data"), account))
            throw new PlaylistWriteRejected("Only playlists owned by the current TIDAL account can receive tracks.");
    }

    private async Task<IReadOnlyList<string>> ReadTracks(string id, TidalDownloadClient.Session account, CancellationToken token)
    {
        var tracks = new List<string>();
        await Pages($"playlists/{id}/relationships/items?sort=itemIndex", account, document =>
        {
            foreach (var item in document.GetProperty("data").EnumerateArray())
                tracks.Add(item.GetProperty("type").GetString() == "tracks" ? item.GetProperty("id").GetString()! : "video:" + item.GetProperty("id").GetString());
        }, token);
        return tracks;
    }

    private async Task Pages(string path, TidalDownloadClient.Session account, Action<JsonElement> receive, CancellationToken token)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? next = path;
        while (next != null)
        {
            if (!visited.Add(next)) throw new JsonException("TIDAL repeated a pagination link.");
            using var document = await Send(HttpMethod.Get, next, account, null, token);
            receive(document.RootElement);
            next = document.RootElement.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var link)
                ? link.ValueKind == JsonValueKind.String ? link.GetString()
                    : link.ValueKind == JsonValueKind.Object && link.TryGetProperty("href", out var href) ? href.GetString() : null
                : null;
        }
    }

    private async Task<JsonDocument> Send(HttpMethod method, string path, TidalDownloadClient.Session account, object? payload, CancellationToken token)
    {
        // TIDAL next links can be relative to the API base, with a leading slash but without /v2.
        if (path.StartsWith("//", StringComparison.Ordinal)) throw new JsonException("Unexpected TIDAL pagination URL.");
        var uri = new Uri(new Uri(BaseUrl), path.StartsWith("/v2/", StringComparison.Ordinal) ? path : path.TrimStart('/'));
        if (uri.Scheme != "https" || uri.Host != "openapi.tidal.com" || !uri.IsDefaultPort || !uri.AbsolutePath.StartsWith("/v2/", StringComparison.Ordinal) || uri.UserInfo.Length != 0)
            throw new JsonException("Unexpected TIDAL pagination URL.");
        if (method == HttpMethod.Get && !uri.Query.Contains("countryCode=", StringComparison.Ordinal))
            uri = new Uri(uri.AbsoluteUri + (uri.Query.Length == 0 ? "?" : "&") + "countryCode=" + Uri.EscapeDataString(account.Country));
        using var request = new HttpRequestMessage(method, uri);
        if (payload != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.api+json");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode && method == HttpMethod.Get)
            throw new HttpRequestException("TIDAL could not verify the playlist.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
            throw new PlaylistWriteRejected(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "TIDAL rejected the sign-in. Sign in again at Settings > Authentication > TIDAL.",
                HttpStatusCode.Forbidden => "TIDAL did not allow this account to change the playlist.",
                HttpStatusCode.TooManyRequests => "TIDAL is rate limiting requests. Wait before trying again.",
                _ => $"TIDAL returned HTTP {(int)response.StatusCode}. Check the playlist before trying again.",
            }, method != HttpMethod.Get && (int)response.StatusCode >= 500);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }
}
