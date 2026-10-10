using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Spotify with a client id and secret (the official Web API), which takes different code paths from web player
/// mode: album search, advanced search, ISRC search and album links only work here.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class SpotifyOfficialTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureSpotifyOfficialAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("SpotifyOfficial.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Spotify.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Spotify.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            Albums = await Live.Collect(items => TestSources.Spotify.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Spotify.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
            AdvancedAll = await Live.Collect(items => TestSources.Spotify.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", "Random Access Memories"), new(0, 5), token)),
        };
    });

    // Bug 17: the official search used to read every page of results (610 for a limit of 5, about 100 s).
    [TestMethod]
    public async Task SearchReturnsAtMostTheLimit()
    {
        var tracks = new List<Models.SongSearchObject>();
        await TestSources.Spotify.SearchAsync(tracks, "daft punk get lucky", new(0, 5), albumMode: false, CancellationToken.None);
        var albums = new List<Models.SongSearchObject>();
        await TestSources.Spotify.SearchAsync(albums, "random access memories", new(0, 3), albumMode: true, CancellationToken.None);

        Assert.AreEqual(5, tracks.Count);
        Assert.AreEqual(3, albums.Count);
    }

    // Bugs 3 and 19: when the ISRC search found nothing, the conversion returned null right away, without trying the
    // metadata or reporting an error. The fake ISRC finds nothing, so only the metadata can match.
    [TestMethod]
    public async Task ConversionFallsBackToMetadataWhenTheIsrcFindsNothing()
    {
        var song = new Models.SongSearchObject
        {
            Source = "deezer", Id = "67238735", Isrc = "XX0000000000", Title = "Get Lucky (feat. Pharrell Williams and Nile Rodgers)",
            Artists = "Daft Punk, Pharrell Williams, Nile Rodgers", AlbumName = "Random Access Memories",
        };
        var match = await TestSources.Spotify.FindEquivalentAsync(song, song.Isrc, CancellationToken.None);

        Assert.AreEqual(Contracts.Sources.MatchKind.Close, match.Kind);
        Assert.AreEqual(Fixtures.Tracks[0].SpotifyId, match.Track!.Id);
    }

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("SpotifyOfficial.Lookups", async () =>
    {
        var tracks = new List<object>();
        foreach (var f in Fixtures.Tracks.Where(f => f.SpotifyId != null))
        {
            tracks.Add(new
            {
                f.Key,
                ById = await Live.Capture(async () => (await TestSources.Spotify.GetTrack(f.SpotifyId!)).ToShape()),
                IsrcFromId = await Live.Capture(async () => await TestSources.Spotify.GetIsrcFromId(f.SpotifyId!)),
                ByIsrc = await Live.Capture(async () => (await TestSources.Spotify.GetTrackFromISRC(f.Isrc!)).ToShape()),
            });
        }

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () =>
            {
                var album = await TestSources.Spotify.GetAlbum(Fixtures.SpotifyAlbumId);
                return album == null ? null : TestSources.Spotify.ConvertAlbumSearchObject(album).ToShape();
            }),
        };
    });

    [TestMethod]
    public Task Links() => Snapshot.MatchLive("SpotifyOfficial.Links", async () =>
    {
        var getLucky = Fixtures.Tracks.First(f => f.Key == "get-lucky-album").SpotifyId;
        var rows = new List<object>();
        foreach (var (url, albumMode) in new[]
        {
            ($"https://open.spotify.com/track/{getLucky}", false),
            ($"https://open.spotify.com/album/{Fixtures.SpotifyAlbumId}", false),
            ($"https://open.spotify.com/album/{Fixtures.SpotifyAlbumId}", true),
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => TestSources.Spotify.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: albumMode), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
