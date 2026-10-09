using System.Reflection;
using FluentDL.Services;
using FluentDL.Services.CustomSpotify;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// Signs the static source classes in once per test run, with the same calls the app makes at startup.
/// </summary>
internal static class LiveSources
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool deezerTried, qobuzTried, spotifyTried, youtubeTried;
    private static string? youtubeBlockedReason;

    /// <summary>
    /// YouTube sometimes answers YouTubeMusicAPI's ANDROID_VR player requests with "Sign in to confirm you're not a bot"
    /// and no videoDetails, and the library then throws ArgumentNullException('videoDetails.title'). Heavy test runs can
    /// trigger this for hours. It is an outside outage, not a regression, so YouTube tests end as Inconclusive while it lasts.
    /// </summary>
    public static async Task EnsureYoutubeMusicAvailableAsync()
    {
        await RunOnceAsync(() => youtubeTried, () => youtubeTried = true, async () =>
        {
            // The check hits some requests and not others, so probe every fixture video.
            var client = new YouTubeMusicAPI.Client.YouTubeMusicClient();
            var ids = Fixtures.Tracks.Where(f => f.YoutubeId != null && f.Isrc != null).Select(f => f.YoutubeId!);
            foreach (var id in ids)
            {
                try
                {
                    await client.GetSongVideoInfoAsync(id);
                }
                catch (ArgumentNullException e) when (e.ParamName == "videoDetails.title")
                {
                    youtubeBlockedReason = $"YouTube is bot-checking YouTubeMusicAPI requests from this machine (no videoDetails for {id}). Try again later.";
                    return;
                }
                catch (HttpRequestException e)
                {
                    youtubeBlockedReason = $"YouTube Music request failed for {id}: {e.Message}. Try again later.";
                    return;
                }
            }
        });

        if (youtubeBlockedReason != null) Assert.Inconclusive(youtubeBlockedReason);
    }

    public static async Task EnsureDeezerAsync()
    {
        await RunOnceAsync(() => deezerTried, () => deezerTried = true, async () =>
            await DeezerApi.InitDeezerClient(Secrets.Get("deezer_arl")));

        if (!DeezerApi.IsInitialized) Assert.Inconclusive("Deezer did not sign in with the ARL from the secrets file.");
    }

    public static async Task EnsureQobuzAsync()
    {
        await RunOnceAsync(() => qobuzTried, () => qobuzTried = true, () =>
        {
            QobuzApi.Initialize(Secrets.Get("qobuz_id"), Secrets.Get("qobuz_token"), Secrets.Get("qobuz_app_id"), Secrets.Get("qobuz_app_secret"));
            return Task.CompletedTask;
        });

        if (!QobuzApi.IsInitialized) Assert.Inconclusive("Qobuz did not sign in with the values from the secrets file.");
    }

    /// <summary>
    /// Puts SpotifyApi in web player mode. In the app, SpotifyApi.Initialize gets these services from
    /// App.GetService, which needs the running WinUI app, so the test builds them and sets the same
    /// private fields that the web player branch of Initialize sets. It also clears the official client, which the
    /// app never sets in this mode (bug 9), in case an official-mode test ran first in the same process.
    /// </summary>
    public static async Task EnsureSpotifyWebPlayerAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!spotifyTried)
            {
                spotifyTried = true;
                var tokens = new ClientTokenService(new HttpClient(), NullLogger<ClientTokenService>.Instance, new MemoryCache(new MemoryCacheOptions()));
                spotifyWebService = new SpotifyWebService(new HttpClient(), tokens, NullLogger<SpotifyWebService>.Instance);
                spotifyIsrcService = new SpotifyISRCService(new HttpClient(), tokens, NullLogger<SpotifyISRCService>.Instance);
                spotifyWebAuthenticated = await spotifyWebService.IsAuthenticated(10000);
            }

            SetPrivateStatic("spotify", null);
            SetPrivateStatic("spotifyUser", null);
            SetPrivateStatic("spotifyWebService", spotifyWebService);
            SetPrivateStatic("spotifyISRCService", spotifyIsrcService);
            SpotifyApi.IsInitialized = spotifyWebAuthenticated;
        }
        finally
        {
            Gate.Release();
        }

        if (!SpotifyApi.IsInitialized) Assert.Inconclusive("Spotify web player authentication failed.");
    }

    /// <summary>
    /// Puts SpotifyApi in official API mode with the client id and secret from the secrets file, through the same
    /// SpotifyApi.Initialize call the app makes. Runs every time, because web player tests switch the mode back.
    /// </summary>
    public static async Task EnsureSpotifyOfficialAsync()
    {
        var clientId = Secrets.Require("spotify_client_id");
        var clientSecret = Secrets.Require("spotify_client_secret");

        await Gate.WaitAsync();
        try
        {
            await SpotifyApi.Initialize(clientId, clientSecret);
        }
        finally
        {
            Gate.Release();
        }

        if (!SpotifyApi.IsInitialized) Assert.Inconclusive("Spotify did not sign in with the client id and secret from the secrets file.");
    }

    private static SpotifyWebService? spotifyWebService;
    private static SpotifyISRCService? spotifyIsrcService;
    private static bool spotifyWebAuthenticated;

    private static void SetPrivateStatic(string name, object? value)
    {
        var field = typeof(SpotifyApi).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(nameof(SpotifyApi), name);
        field.SetValue(null, value);
    }

    private static async Task RunOnceAsync(Func<bool> tried, Action markTried, Func<Task> init)
    {
        await Gate.WaitAsync();
        try
        {
            if (tried()) return;
            markTried();
            await init();
        }
        finally
        {
            Gate.Release();
        }
    }
}
