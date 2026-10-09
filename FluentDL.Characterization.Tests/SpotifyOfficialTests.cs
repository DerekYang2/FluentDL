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
        // Bug 17: the official search reads every page of results and ignores the limit, so only the first five
        // results and the fact that more were loaded are pinned. Page 2's head is skipped, since lower ranks reorder.
        return new
        {
            Tracks = await Live.CollectHead(items => SpotifyApi.GeneralSearch(items, "daft punk get lucky", token, 0, 5), 5),
            TracksSecondPage = await Live.CollectHead(items => SpotifyApi.GeneralSearch(items, "daft punk get lucky", token, 5, 5), 0),
            Albums = await Live.Collect(items => SpotifyApi.GeneralSearch(items, "random access memories", token, 0, 3, albumMode: true)),
            AdvancedArtistTrack = await Live.Collect(items => SpotifyApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "", token, 0, 5)),
            AdvancedAll = await Live.Collect(items => SpotifyApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "Random Access Memories", token, 0, 5)),
        };
    });

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("SpotifyOfficial.Lookups", async () =>
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

        return new
        {
            Tracks = tracks,
            Album = await Live.Capture(async () =>
            {
                var album = await SpotifyApi.GetAlbum(Fixtures.SpotifyAlbumId);
                return album == null ? null : SpotifyApi.ConvertAlbumSearchObject(album).ToShape();
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
            var result = await Live.Collect(items => SpotifyApi.AddTracksFromLink(items, url, CancellationToken.None, log.Callback, userLogin: false, albumMode));
            rows.Add(new { Url = url, AlbumMode = albumMode, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
