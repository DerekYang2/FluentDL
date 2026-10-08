using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Spotify in web player mode, which is what the app uses when no client id and secret are set.
/// Some methods use the official client even in this mode and fail (bug 9 in the refactor plan);
/// the baseline records those failures as they are.
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
        var tracks = await Live.Collect(items => SpotifyApi.GeneralSearch(items, "daft punk get lucky", token, 0, 5));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => SpotifyApi.GeneralSearch(items, "daft punk get lucky", token, 5, 5), tracks),
            AlbumMode = await Live.Collect(items => SpotifyApi.GeneralSearch(items, "random access memories", token, 0, 3, albumMode: true)),
            Advanced = await Live.Collect(items => SpotifyApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "", token, 0, 5)),
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
                ById = await Live.Capture(async () => (await SpotifyApi.GetTrack(f.SpotifyId!)).ToShape()),
                IsrcFromId = await Live.Capture(async () => await SpotifyApi.GetIsrcFromId(f.SpotifyId!)),
                ByIsrc = await Live.Capture(async () => (await SpotifyApi.GetTrackFromISRC(f.Isrc!)).ToShape()),
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
            var result = await Live.Collect(items => SpotifyApi.AddTracksFromLink(items, url, CancellationToken.None, log.Callback, userLogin: false));
            rows.Add(new { Url = url, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
