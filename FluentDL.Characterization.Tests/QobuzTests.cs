using System.Collections.ObjectModel;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Models;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
public class QobuzTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureQobuzAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("Qobuz.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Qobuz.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Qobuz.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            Albums = await Live.Collect(items => TestSources.Qobuz.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Qobuz.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
            AdvancedAll = await Live.Collect(items => TestSources.Qobuz.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", "Random Access Memories"), new(0, 5), token)),
            AdvancedArtistOnly = await Live.Collect(items => TestSources.Qobuz.AdvancedSearchAsync(items, new("Avicii", "", ""), new(0, 5), token)),
            AdvancedTrackOnly = await Live.Collect(items => TestSources.Qobuz.AdvancedSearchAsync(items, new("", "Levels", ""), new(0, 5), token)),
        };
    });

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("Qobuz.Lookups", async () =>
    {
        var tracks = new List<object>();
        foreach (var f in Fixtures.Tracks.Where(f => f.QobuzId != null))
        {
            tracks.Add(new
            {
                f.Key,
                ById = await Live.Capture(async () => (await TestSources.Qobuz.GetTrackAsync(f.QobuzId!)).ToShape()),
                ByIsrc = await Live.Capture(async () =>
                {
                    var track = await TestSources.Qobuz.SearchISRC(f.Isrc!);
                    return track == null ? null : TestSources.Qobuz.ConvertSongSearchObject(track).ToShape();
                }),
            });
        }

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () =>
            {
                var album = await TestSources.Qobuz.GetInternalAlbum(Fixtures.QobuzAlbumId);
                if (album == null) return null;
                return new
                {
                    Converted = TestSources.Qobuz.ConvertAlbumSearchObject(album).ToShape(),
                    Tracks = album.Tracks?.Items.Select(t => TestSources.Qobuz.CreateSongSearchObject(t, album).ToShape()).ToArray(),
                };
            }),
            PreviewUriAvailable = await Live.Capture(() => Task.FromResult<object?>(TestSources.Qobuz.GetPreviewUri("9140031") != null)),
            IsPremium = TestSources.Qobuz.IsPremium(),
        };
    });

    [TestMethod]
    public Task Links() => Snapshot.MatchLive("Qobuz.Links", async () =>
    {
        var rows = new List<object>();
        foreach (var (url, albumMode) in new[]
        {
            ("https://open.qobuz.com/track/9140031", false),
            ("https://play.qobuz.com/track/9140031", false),
            ("https://open.qobuz.com/album/0886443927087", false),
            ("https://open.qobuz.com/album/0886443927087", true),
            ("https://www.qobuz.com/us-en/album/random-access-memories-daft-punk/0886443927087", false),
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => TestSources.Qobuz.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: albumMode), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }

        foreach (var (url, newestFirst) in new[]
        {
            // A label lists its newest albums first, so new releases change the list: pin the count and layout only.
            ($"https://www.qobuz.com/us-en/label/columbia/download-streaming-albums/{Fixtures.QobuzLabelId}", true),
            ($"https://www.qobuz.com/us-en/interpreter/daft-punk/{Fixtures.QobuzArtistId}", false),
        })
        {
            var isLabelOrArtist = TestSources.Qobuz.IsLinkLabelOrArtist(url, out var linkType, out var id);
            var log = new Live.StatusLog();
            Func<ObservableCollection<SongSearchObject>, Task> fill =
                items => TestSources.Qobuz.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 5), AlbumMode: true), log.Callback, CancellationToken.None);
            var result = newestFirst ? await Live.CollectShapeOnly(fill) : await Live.Collect(fill);
            rows.Add(new { Url = url, IsLabelOrArtist = isLabelOrArtist, LinkType = linkType.ToString(), Id = id, Result = result, Status = log.Messages });
        }
        return rows;
    });

    // Bug 8: the ISRC search is a text search, and the top result was returned without checking its ISRC.
    [TestMethod]
    [DataRow("USQX91300108", "USQX91300108")]
    [DataRow("USQX91300199", null)] // one digit off a real ISRC, not a Qobuz track
    [DataRow("daft punk", null)] // not an ISRC at all
    public async Task SearchIsrcOnlyReturnsThatIsrc(string isrc, string? expectedIsrc)
    {
        var track = await TestSources.Qobuz.SearchISRC(isrc);
        Assert.AreEqual(expectedIsrc, track?.Isrc, $"{track?.Title} / {track?.Performer?.Name}");
    }

    [TestMethod]
    [DataRow("TCACK1581189")]
    [DataRow("TCADA1796141")]
    [DataRow("USZ4V1600115")]
    [DataRow("USQX91300108")]
    public async Task SearchIsrcOnlyReturnsAvailableExactMatches(string isrc)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var track = await TestSources.Qobuz.SearchISRC(isrc, timeout.Token);
        if (track == null) return;
        Assert.AreEqual(isrc, track.Isrc, ignoreCase: true);
        Assert.AreEqual(true, track.Streamable);
        Assert.AreEqual(true, track.Displayable);
        Assert.IsNotNull(await TestSources.Qobuz.GetTrackAsync(track.Id, timeout.Token), "The selected track must be retrievable.");
    }
}
