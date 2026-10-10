using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// The queue's "convert to another service" feature: each fixture, starting on one service, matched on another,
/// called the same way QueuePage.xaml.cs does (Get{Target}Track with its default options).
/// The badge each call reports (Success = ISRC match, Warning = metadata match, Error = none) is part of the baseline.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class ConversionTests
{
    // One method per target, so tools/Run-LiveTests.ps1 can run the targets in parallel processes. Everything that
    // calls YouTube is in the YouTube category, which Run-LiveTests.ps1 -Quick skips: YouTube is slow and bot-checks
    // heavy use.
    [TestMethod]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertToDeezer(string origin) => RunAsync(origin, "deezer", spotifyOfficial: false);

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("spotify")]
    public Task ConvertToQobuz(string origin) => RunAsync(origin, "qobuz", spotifyOfficial: false);

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public Task ConvertToSpotify(string origin) => RunAsync(origin, "spotify", spotifyOfficial: false);

    [TestMethod]
    [TestCategory("YouTube")]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertFromYoutube(string target) => RunAsync("youtube", target, spotifyOfficial: false);

    // About a minute per origin: every match makes several YouTube Music requests.
    [TestMethod]
    [TestCategory("YouTube")]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertToYoutube(string origin) => RunAsync(origin, "youtube", spotifyOfficial: false);

    // Spotify with a client id and secret takes different paths: its tracks carry an ISRC, and converting to Spotify
    // searches by ISRC first (bug 3: when the ISRC search finds nothing, it never falls back to metadata).
    [TestMethod]
    [DataRow("deezer", "spotify")]
    [DataRow("qobuz", "spotify")]
    [DataRow("spotify", "deezer")]
    [DataRow("spotify", "qobuz")]
    public Task ConvertWithSpotifyOfficial(string origin, string target) => RunAsync(origin, target, spotifyOfficial: true);

    [TestMethod]
    [TestCategory("YouTube")]
    public Task ConvertWithSpotifyOfficialToYoutube() => RunAsync("spotify", "youtube", spotifyOfficial: true);

    private static async Task RunAsync(string origin, string target, bool spotifyOfficial)
    {
        var baselineName = $"{(spotifyOfficial ? "ConversionSpotifyOfficial" : "Conversion")}.{origin}-to-{target}";
        await SignInAsync(origin, spotifyOfficial);
        await SignInAsync(target, spotifyOfficial);

        if (!Fixtures.Tracks.Any(f => OriginId(f, origin) != null)) Assert.Inconclusive($"No fixture has a {origin} id yet.");

        await Snapshot.MatchLive(baselineName, async () =>
        {
            var rows = new List<object>();
            foreach (var fixture in Fixtures.Tracks)
            {
                var originId = OriginId(fixture, origin);
                if (originId == null) continue;

                var song = await LoadAsync(origin, originId);
                if (song == null)
                {
                    rows.Add(new { fixture.Key, Origin = "could not load the origin track" });
                    continue;
                }

                var log = new Live.ConversionLog();
                var result = await Live.Capture(async () => (await ConvertAsync(target, song, log.Callback)).ToShape());
                rows.Add(new { fixture.Key, OriginId = originId, OriginIsrc = song.Isrc, Result = result, Callbacks = log.Calls });
            }
            return rows;
        });
    }

    private static string? OriginId(Fixtures.Track fixture, string origin) => origin switch
    {
        "deezer" => fixture.DeezerId,
        "qobuz" => fixture.QobuzId,
        "spotify" => fixture.SpotifyId,
        _ => fixture.YoutubeId,
    };

    private static Task SignInAsync(string source, bool spotifyOfficial) => source switch
    {
        "deezer" => LiveSources.EnsureDeezerAsync(),
        "qobuz" => LiveSources.EnsureQobuzAsync(),
        "spotify" => spotifyOfficial ? LiveSources.EnsureSpotifyOfficialAsync() : LiveSources.EnsureSpotifyWebPlayerAsync(),
        _ => LiveSources.EnsureYoutubeMusicAvailableAsync(),
    };

    private static async Task<SongSearchObject?> LoadAsync(string source, string id) => source switch
    {
        "deezer" => await TestSources.Deezer.GetTrack(id),
        "qobuz" => await TestSources.Qobuz.GetTrackAsync(id),
        "spotify" => await SpotifyApi.GetTrack(id),
        _ => await YoutubeApi.GetTrack(id),
    };

    // Through the conversion adapters, the way the queue will call them. The queue's callback is rebuilt from the
    // MatchResult: a match reports the found track, NotFound reports the original song, NotReported reports nothing.
    private static async Task<SongSearchObject?> ConvertAsync(string target, SongSearchObject song, ConversionUpdateCallback callback)
    {
        var result = await TestSources.Conversions.FindEquivalentAsync(song, target, CancellationToken.None);
        switch (result.Kind)
        {
            case MatchKind.Exact:
                callback(StatusLevel.Success, result.Track!);
                break;
            case MatchKind.Close:
                callback(StatusLevel.Warning, result.Track!);
                break;
            case MatchKind.NotFound:
                callback(StatusLevel.Error, song);
                break;
        }
        return result.Track;
    }
}
