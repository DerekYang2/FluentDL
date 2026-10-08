using FluentDL.Characterization.Tests.Infrastructure;
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
        return new
        {
            Tracks = await Live.Collect(items => QobuzApi.GeneralSearch(items, "daft punk get lucky", token, 0, 5)),
            TracksSecondPage = await Live.Collect(items => QobuzApi.GeneralSearch(items, "daft punk get lucky", token, 5, 5)),
            Albums = await Live.Collect(items => QobuzApi.GeneralSearch(items, "random access memories", token, 0, 3, albumMode: true)),
            AdvancedArtistTrack = await Live.Collect(items => QobuzApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "", token, 0, 5)),
            AdvancedAll = await Live.Collect(items => QobuzApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "Random Access Memories", token, 0, 5)),
            AdvancedArtistOnly = await Live.Collect(items => QobuzApi.AdvancedSearch(items, "Avicii", "", "", token, 0, 5)),
            AdvancedTrackOnly = await Live.Collect(items => QobuzApi.AdvancedSearch(items, "", "Levels", "", token, 0, 5)),
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
                ById = await Live.Capture(async () => (await QobuzApi.GetTrackAsync(f.QobuzId!)).ToShape()),
                ByIsrc = await Live.Capture(async () =>
                {
                    var track = await QobuzApi.SearchISRC(f.Isrc!);
                    return track == null ? null : QobuzApi.ConvertSongSearchObject(track).ToShape();
                }),
            });
        }

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () =>
            {
                var album = await QobuzApi.GetInternalAlbum(Fixtures.QobuzAlbumId);
                if (album == null) return null;
                return new
                {
                    Converted = QobuzApi.ConvertAlbumSearchObject(album).ToShape(),
                    Tracks = album.Tracks?.Items.Select(t => QobuzApi.CreateSongSearchObject(t, album).ToShape()).ToArray(),
                };
            }),
            PreviewUriAvailable = await Live.Capture(() => Task.FromResult<object?>(QobuzApi.GetPreviewUri("9140031") != null)),
            IsPremium = QobuzApi.IsPremium(),
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
            var result = await Live.Collect(items => QobuzApi.AddTracksFromLink(items, url, CancellationToken.None, log.Callback, albumMode));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }

        foreach (var url in new[]
        {
            $"https://www.qobuz.com/us-en/label/columbia/download-streaming-albums/{Fixtures.QobuzLabelId}",
            $"https://www.qobuz.com/us-en/interpreter/daft-punk/{Fixtures.QobuzArtistId}",
        })
        {
            var isLabelOrArtist = QobuzApi.IsLinkLabelOrArtist(url, out var linkType, out var id);
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => QobuzApi.AddTracksFromLabelOrArtist(items, url, CancellationToken.None, log.Callback, 0, 5, albumMode: true));
            rows.Add(new { Url = url, IsLabelOrArtist = isLabelOrArtist, LinkType = linkType.ToString(), Id = id, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
