using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Views;

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
    [TestMethod]
    [DataRow("deezer", "qobuz")]
    [DataRow("deezer", "spotify")]
    [DataRow("deezer", "youtube")]
    [DataRow("qobuz", "deezer")]
    [DataRow("qobuz", "spotify")]
    [DataRow("qobuz", "youtube")]
    [DataRow("spotify", "deezer")]
    [DataRow("spotify", "qobuz")]
    [DataRow("spotify", "youtube")]
    [DataRow("youtube", "deezer")]
    [DataRow("youtube", "qobuz")]
    [DataRow("youtube", "spotify")]
    public async Task Convert(string origin, string target)
    {
        await SignInAsync(origin);
        await SignInAsync(target);

        if (!Fixtures.Tracks.Any(f => OriginId(f, origin) != null)) Assert.Inconclusive($"No fixture has a {origin} id yet.");

        await Snapshot.MatchLive($"Conversion.{origin}-to-{target}", async () =>
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

    private static Task SignInAsync(string source) => source switch
    {
        "deezer" => LiveSources.EnsureDeezerAsync(),
        "qobuz" => LiveSources.EnsureQobuzAsync(),
        "spotify" => LiveSources.EnsureSpotifyWebPlayerAsync(),
        _ => LiveSources.EnsureYoutubeMusicAvailableAsync(),
    };

    private static async Task<SongSearchObject?> LoadAsync(string source, string id) => source switch
    {
        "deezer" => await DeezerApi.GetTrack(id),
        "qobuz" => await QobuzApi.GetTrackAsync(id),
        "spotify" => await SpotifyApi.GetTrack(id),
        _ => await YoutubeApi.GetTrack(id),
    };

    // Same calls as QueuePage.xaml.cs:1140-1147.
    private static async Task<SongSearchObject?> ConvertAsync(string target, SongSearchObject song, ConversionUpdateCallback callback) => target switch
    {
        "deezer" => await DeezerApi.GetDeezerTrack(song, CancellationToken.None, callback),
        "qobuz" => await QobuzApi.GetQobuzTrack(song, CancellationToken.None, callback),
        "spotify" => await SpotifyApi.GetSpotifyTrack(song, CancellationToken.None, callback),
        _ => await YoutubeApi.GetYoutubeTrack(song, CancellationToken.None, callback),
    };
}
