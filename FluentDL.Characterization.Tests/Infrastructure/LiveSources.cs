using System.Reflection;
using FluentDL.Services;
using FluentDL.Services.CustomSpotify;
using FluentDL.Services.Sources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDL.Characterization.Tests.Infrastructure;

/// <summary>
/// Signs the sources in once per test run, through the same adapters the app registers.
/// </summary>
internal static class LiveSources
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool deezerTried, qobuzTried, spotifyTried, youtubeTried;
    private static string? youtubeBlockedReason;

    /// <summary>
    /// YouTube answers some YouTubeMusicAPI song detail requests with a bot check and no videoDetails, and the library
    /// then throws ArgumentNullException('videoDetails.title') (bug 16). Since 3.0.11 about half of them fail at random,
    /// and YoutubeSource retries them, so YouTube counts as blocked only when most fixture videos still fail after the
    /// same retries. One fixture video (wDuoOapZ9Z0) never returns details. A real block is an outside outage, not a
    /// regression, so YouTube tests end as Inconclusive while it lasts.
    /// </summary>
    public static async Task EnsureYoutubeMusicAvailableAsync()
    {
        await RunOnceAsync(() => youtubeTried, () => youtubeTried = true, async () =>
        {
            var client = new YouTubeMusicAPI.Client.YouTubeMusicClient();
            var ids = Fixtures.Tracks.Where(f => f.YoutubeId != null && f.Isrc != null).Select(f => f.YoutubeId!).ToList();
            var failed = new List<string>();
            foreach (var id in ids)
            {
                try
                {
                    await FluentDL.Services.Sources.YoutubeSource.RetryBotCheckAsync(() => client.GetSongVideoInfoAsync(id));
                }
                catch (ArgumentNullException e) when (e.ParamName == "videoDetails.title")
                {
                    failed.Add(id);
                    if (failed.Count * 2 > ids.Count)
                    {
                        youtubeBlockedReason = $"YouTube is bot-checking YouTubeMusicAPI requests from this machine (no videoDetails for {string.Join(", ", failed)}, after retries). Try again later.";
                        return;
                    }
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
        await RunOnceAsync(() => deezerTried, () => deezerTried = true, () => TestSources.Deezer.ConnectAsync());

        if (!TestSources.Deezer.IsInitialized) Assert.Inconclusive("Deezer did not sign in with the ARL from the secrets file.");
    }

    public static async Task EnsureQobuzAsync()
    {
        await RunOnceAsync(() => qobuzTried, () => qobuzTried = true, () => TestSources.Qobuz.ConnectAsync());

        if (!TestSources.Qobuz.IsInitialized) Assert.Inconclusive("Qobuz did not sign in with the values from the secrets file.");
    }

    /// <summary>
    /// Puts Spotify in web player mode by setting the private fields that the web player branch of
    /// SpotifySource.Initialize sets, with the web services TestSources gave the adapter. It also clears the official
    /// client, which the app never does in this mode (bug 9), in case an official-mode test ran first in the same
    /// process.
    /// </summary>
    public static async Task EnsureSpotifyWebPlayerAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!spotifyTried)
            {
                spotifyTried = true;
                spotifyWebAuthenticated = await TestSources.SpotifyWebService.IsAuthenticated(10000);
            }

            SetPrivateField("spotify", null);
            SetPrivateField("spotifyUser", null);
            SetPrivateField("spotifyWebService", TestSources.SpotifyWebService);
            SetPrivateField("spotifyISRCService", TestSources.SpotifyIsrcService);
            TestSources.Spotify.IsInitialized = spotifyWebAuthenticated;
        }
        finally
        {
            Gate.Release();
        }

        if (!TestSources.Spotify.IsInitialized) Assert.Inconclusive("Spotify web player authentication failed.");
    }

    /// <summary>
    /// Puts Spotify in official API mode through SpotifySource.ConnectAsync, which reads the client id and secret
    /// (from the secrets file here). Runs every time, because web player tests switch the mode back.
    /// </summary>
    public static async Task EnsureSpotifyOfficialAsync()
    {
        Secrets.Require("spotify_client_id");
        Secrets.Require("spotify_client_secret");

        await Gate.WaitAsync();
        try
        {
            await TestSources.Spotify.ConnectAsync();
        }
        finally
        {
            Gate.Release();
        }

        if (!TestSources.Spotify.IsInitialized) Assert.Inconclusive("Spotify did not sign in with the client id and secret from the secrets file.");
    }

    private static bool spotifyWebAuthenticated;

    private static void SetPrivateField(string name, object? value)
    {
        var field = typeof(SpotifySource).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(nameof(SpotifySource), name);
        field.SetValue(TestSources.Spotify, value);
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
