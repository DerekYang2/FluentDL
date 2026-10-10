using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
public class DeezerTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureDeezerAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("Deezer.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Deezer.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Deezer.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            Albums = await Live.Collect(items => TestSources.Deezer.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Deezer.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
            AdvancedAll = await Live.Collect(items => TestSources.Deezer.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", "Random Access Memories"), new(0, 5), token)),
            AdvancedArtistOnly = await Live.Collect(items => TestSources.Deezer.AdvancedSearchAsync(items, new("Avicii", "", ""), new(0, 5), token)),
        };
    });

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("Deezer.Lookups", async () =>
    {
        var tracks = new List<object>();
        foreach (var f in Fixtures.Tracks.Where(f => f.DeezerId != null))
        {
            tracks.Add(new
            {
                f.Key,
                ById = await Live.Capture(async () => (await TestSources.Deezer.GetTrack(f.DeezerId!)).ToShape()),
                ByIsrc = await Live.Capture(async () => (await TestSources.Deezer.GetTrackFromISRC(f.Isrc!)).ToShape()),
            });
        }

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () => (await TestSources.Deezer.GetAlbum(Fixtures.DeezerAlbumId)).ToShape()),
            AlbumByUpc = await Live.Capture(async () => (await TestSources.Deezer.GetAlbumFromUPC(Fixtures.AlbumUpc)).ToShape()),
        };
    });

    [TestMethod]
    public Task Links() => Snapshot.MatchLive("Deezer.Links", async () =>
    {
        var rows = new List<object>();
        foreach (var (url, albumMode) in new[]
        {
            ("https://www.deezer.com/track/67238735", false),
            ("https://www.deezer.com/en/track/67238735?utm_source=share", false),
            ("https://www.deezer.com/album/6575789", false),
            ("https://www.deezer.com/album/6575789", true),
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => TestSources.Deezer.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: albumMode), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
