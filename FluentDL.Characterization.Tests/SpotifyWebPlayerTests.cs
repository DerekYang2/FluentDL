using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Spotify in web player mode, which is what the app uses when no client id and secret are set.
/// The web player has no field search, ISRC search or album loading (bug 9): advanced search runs a normal search,
/// ISRC lookups return null, and album links report an error.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class SpotifyWebPlayerTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureSpotifyWebPlayerAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("SpotifyWeb.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Spotify.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Spotify.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            AlbumMode = await Live.Collect(items => TestSources.Spotify.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            Advanced = await Live.Collect(items => TestSources.Spotify.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
        };
    });

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("SpotifyWeb.Lookups", async () =>
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
        return tracks;
    });

    [TestMethod]
    public Task Links() => Snapshot.MatchLive("SpotifyWeb.Links", async () =>
    {
        var getLucky = Fixtures.Tracks.First(f => f.Key == "get-lucky-album").SpotifyId;
        var rows = new List<object>();
        foreach (var url in new[]
        {
            $"https://open.spotify.com/track/{getLucky}",
            $"https://open.spotify.com/track/{getLucky}?si=abc123",
            $"https://open.spotify.com/album/{Fixtures.SpotifyAlbumId}",
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => TestSources.Spotify.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: false), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
