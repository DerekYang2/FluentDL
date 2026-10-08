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
        var tracks = await Live.Collect(items => DeezerApi.GeneralSearch(items, "daft punk get lucky", token, 0, 5));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => DeezerApi.GeneralSearch(items, "daft punk get lucky", token, 5, 5), tracks),
            Albums = await Live.Collect(items => DeezerApi.GeneralSearch(items, "random access memories", token, 0, 3, albumMode: true)),
            AdvancedArtistTrack = await Live.Collect(items => DeezerApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "", token, 0, 5)),
            AdvancedAll = await Live.Collect(items => DeezerApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "Random Access Memories", token, 0, 5)),
            AdvancedArtistOnly = await Live.Collect(items => DeezerApi.AdvancedSearch(items, "Avicii", "", "", token, 0, 5)),
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
                ById = await Live.Capture(async () => (await DeezerApi.GetTrack(f.DeezerId!)).ToShape()),
                ByIsrc = await Live.Capture(async () => (await DeezerApi.GetTrackFromISRC(f.Isrc!)).ToShape()),
            });
        }

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () => (await DeezerApi.GetAlbum(Fixtures.DeezerAlbumId)).ToShape()),
            AlbumByUpc = await Live.Capture(async () => (await DeezerApi.GetAlbumFromUPC(Fixtures.AlbumUpc)).ToShape()),
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
            var result = await Live.Collect(items => DeezerApi.AddTracksFromLink(items, url, CancellationToken.None, log.Callback, albumMode));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
