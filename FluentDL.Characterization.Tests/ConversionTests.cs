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
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertToTidal(string origin) => RunAsync(origin, "tidal", spotifyOfficial: false);

    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertFromTidal(string target) => RunAsync("tidal", target, spotifyOfficial: false);

    [TestMethod]
    [TestCategory("YouTube")]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertFromYoutube(string target) => RunAsync("youtube", target, spotifyOfficial: false);

    // About a minute per origin: every match makes several YouTube Music requests, because YouTube can't look up an
    // ISRC. Normal runs convert from Deezer only; the other origins feed the same matcher nearly the same names, so they
    // run with the Slow category (a full run).
    [TestMethod]
    [TestCategory("YouTube")]
    public Task ConvertToYoutube() => RunAsync("deezer", "youtube", spotifyOfficial: false);

    [TestMethod]
    [TestCategory("YouTube")]
    [TestCategory("Slow")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public Task ConvertToYoutubeFromOtherSources(string origin) => RunAsync(origin, "youtube", spotifyOfficial: false);

    // Spotify with a client id and secret takes different paths: its tracks carry an ISRC, and converting to Spotify
    // searches by ISRC first, then by metadata.
    [TestMethod]
    [DataRow("deezer", "spotify")]
    [DataRow("qobuz", "spotify")]
    [DataRow("spotify", "deezer")]
    [DataRow("spotify", "qobuz")]
    public Task ConvertWithSpotifyOfficial(string origin, string target) => RunAsync(origin, target, spotifyOfficial: true);

    [TestMethod]
    [TestCategory("YouTube")]
    [TestCategory("Slow")]
    public Task ConvertWithSpotifyOfficialToYoutube() => RunAsync("spotify", "youtube", spotifyOfficial: true);

    // Bugs 15 and 1: a song with no ISRC (a local file, a YouTube video) is matched on Deezer by artist, title and
    // album. That search used to find nothing. The artist check on its results is covered by Core's AnyArtistMatches tests.
    [TestMethod]
    public async Task DeezerMatchesByMetadataWithoutAnIsrc()
    {
        await LiveSources.EnsureDeezerAsync();
        var song = new SongSearchObject
        {
            Source = "local", Id = "get-lucky.flac", Title = "Get Lucky (feat. Pharrell Williams and Nile Rodgers)",
            Artists = "Daft Punk, Pharrell Williams, Nile Rodgers", AlbumName = "Random Access Memories",
        };
        var match = await TestSources.Conversions.FindEquivalentAsync(song, "deezer", CancellationToken.None);
        Assert.AreEqual(MatchKind.Close, match.Kind);
        Assert.AreEqual(Fixtures.Tracks[0].DeezerId, match.Track!.Id);
    }

    // With no ISRC, TIDAL matches by a search for the artist and title. The title check ignores the featuring credit,
    // and the closest album name picks the album release over compilations.
    [TestMethod]
    public async Task TidalMatchesByMetadataWithoutAnIsrc()
    {
        await LiveSources.EnsureTidalAsync();
        var song = new SongSearchObject
        {
            Source = "local", Id = "get-lucky.flac", Title = "Get Lucky (feat. Pharrell Williams and Nile Rodgers)",
            Artists = "Daft Punk, Pharrell Williams, Nile Rodgers", AlbumName = "Random Access Memories",
        };
        var match = await TestSources.Tidal.FindEquivalentAsync(song, null, CancellationToken.None);
        Assert.AreEqual(MatchKind.Close, match.Kind);
        Assert.AreEqual("Random Access Memories", match.Track!.AlbumName);
        Assert.AreEqual(Fixtures.Tracks[0].Isrc, match.Track.Isrc);
    }

    // A local file tagged in its own language, whose names the target lists differently (Deezer: "Spring Day" by
    // BTS, "Gurenge"; Qobuz: "Sunny Day"). ConversionService finds it on Spotify (web player mode) by search and
    // length, and the target finds it by that ISRC. Before, name matching found nothing.
    [TestMethod]
    [DataRow("\ubc29\ud0c4\uc18c\ub144\ub2e8", "\ubd04\ub0a0", "274", "deezer", "KRA381700253")] // 방탄소년단 봄날
    [DataRow("LiSA", "\u7d05\u84ee\u83ef", "236", "deezer", "JPU901900905")] // 紅蓮華
    [DataRow("\u5468\u6770\u502b", "\u6674\u5929", "270", "qobuz", "TWK970300503")] // 周杰倫 晴天
    public async Task NativeNamesConvertThroughSpotify(string artist, string title, string duration, string target, string isrc)
    {
        await LiveSources.EnsureSpotifyWebPlayerAsync();
        await SignInAsync(target, spotifyOfficial: false);
        var song = new SongSearchObject { Source = SourceIds.Local, Id = "native.flac", Title = title, Artists = artist, AlbumName = "", Duration = duration };

        var match = await TestSources.Conversions.FindEquivalentAsync(song, target, CancellationToken.None);

        Assert.AreEqual(MatchKind.Close, match.Kind);
        Assert.AreEqual(isrc, match.Track!.Isrc, $"{match.Track.Title} ({match.Track.Artists})");
    }

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
        "tidal" => fixture.TidalId,
        _ => fixture.YoutubeId,
    };

    private static Task SignInAsync(string source, bool spotifyOfficial) => source switch
    {
        "deezer" => LiveSources.EnsureDeezerAsync(),
        "qobuz" => LiveSources.EnsureQobuzAsync(),
        "spotify" => spotifyOfficial ? LiveSources.EnsureSpotifyOfficialAsync() : LiveSources.EnsureSpotifyWebPlayerAsync(),
        "tidal" => LiveSources.EnsureTidalAsync(),
        _ => LiveSources.EnsureYoutubeMusicAvailableAsync(),
    };

    private static async Task<SongSearchObject?> LoadAsync(string source, string id) => source switch
    {
        "deezer" => await TestSources.Deezer.GetTrack(id),
        "qobuz" => await TestSources.Qobuz.GetTrackAsync(id),
        "spotify" => await TestSources.Spotify.GetTrack(id),
        "tidal" => await TestSources.Tidal.GetTrackAsync(id),
        _ => await TestSources.Youtube.GetTrack(id),
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
