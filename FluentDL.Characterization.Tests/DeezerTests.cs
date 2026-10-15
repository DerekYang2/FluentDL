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

    // Bug 10: clearing the ARL kept the signed-in client, so downloads still ran on the old account. A fresh instance,
    // so the shared signed-in one stays as it is.
    [TestMethod]
    public async Task ClearingTheArlSignsOut()
    {
        var deezer = new Services.Sources.DeezerSource(TestSources.Settings, TestSources.AudioConverter);
        await deezer.ConnectAsync();
        Assert.IsTrue(deezer.IsInitialized);
        var song = await deezer.GetTrack(Fixtures.Tracks[0].DeezerId!);

        await deezer.InitDeezerClient("");

        var directory = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Exception? error = null;
            try
            {
                await deezer.DownloadTrack(Path.Combine(directory, "track"), song, DeezNET.Data.Bitrate.MP3_128);
            }
            catch (Exception e)
            {
                error = e;
            }
            Assert.IsNotNull(error, "the download ran after the ARL was cleared");
            Assert.AreEqual(0, Directory.GetFiles(directory).Length);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
