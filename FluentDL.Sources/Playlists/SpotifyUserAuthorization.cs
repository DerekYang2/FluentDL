using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;

namespace FluentDL.Services.Playlists;

// The SDK read client and the playlist transport share one refresh lock and one access token.
internal sealed class SpotifyUserAuthorization(AuthorizationCodeTokenResponse initial,
    Func<CancellationToken, Task<AuthorizationCodeRefreshResponse>> refresh,
    Func<AuthorizationCodeTokenResponse, Task>? save = null,
    Func<Task>? invalidate = null) : IAuthenticator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool savePending;
    internal static readonly string[] PlaylistScopes = [Scopes.PlaylistReadPrivate, Scopes.PlaylistModifyPrivate, Scopes.PlaylistModifyPublic];
    internal static bool HasPlaylistScopes(string? granted) => PlaylistScopes.All(scope =>
        (granted ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal));
    internal bool CanWritePlaylists => HasPlaylistScopes(initial.Scope);
    internal static bool IsRejected(APIException error) => error.Response?.StatusCode is
        System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    internal async Task<string> GetAccessTokenAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (initial.IsExpired)
            {
                AuthorizationCodeRefreshResponse updated;
                try { updated = await refresh(token); }
                catch (APIException error) when (IsRejected(error))
                {
                    if (invalidate != null) await invalidate();
                    throw new PlaylistWriteRejected("Spotify user authorization expired. Sign in again at Settings > Authentication > Spotify.");
                }
                initial.AccessToken = updated.AccessToken;
                initial.CreatedAt = updated.CreatedAt;
                initial.ExpiresIn = updated.ExpiresIn;
                initial.Scope = updated.Scope ?? initial.Scope;
                initial.TokenType = updated.TokenType;
                if (!string.IsNullOrEmpty(updated.RefreshToken)) initial.RefreshToken = updated.RefreshToken;
                if (!CanWritePlaylists)
                {
                    if (invalidate != null) await invalidate();
                    throw new PlaylistWriteRejected("Spotify playlist permissions changed. Sign in again at Settings > Authentication > Spotify.");
                }
                savePending = save != null;
            }
            if (savePending && save != null)
            {
                await save(initial);
                savePending = false;
            }
            if (string.IsNullOrWhiteSpace(initial.AccessToken)) throw new PlaylistWriteRejected("Spotify did not provide a user access token.");
            return initial.AccessToken;
        }
        finally { gate.Release(); }
    }

    public async Task Apply(IRequest request, IAPIConnector apiConnector) =>
        request.Headers["Authorization"] = "Bearer " + await GetAccessTokenAsync(CancellationToken.None);
}
