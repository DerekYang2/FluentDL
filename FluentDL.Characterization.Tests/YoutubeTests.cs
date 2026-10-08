using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
public class YoutubeTests
{
    [TestInitialize]
    public Task CheckAvailability() => LiveSources.EnsureYoutubeMusicAvailableAsync();

    // YouTube reorders lower-ranked results between runs, so only the top 3 are pinned.
    [TestMethod]
    public Task Search() => Snapshot.MatchLive("Youtube.Search", async () =>
    {
        var token = CancellationToken.None;
        return new
        {
            Tracks = await Live.Collect(items => YoutubeApi.GeneralSearch(items, "daft punk get lucky", token, 0, 3)),
            Albums = await Live.Collect(items => YoutubeApi.GeneralSearch(items, "daft punk random access memories", token, 0, 2, albumMode: true)),
            AdvancedArtistTrack = await Live.Collect(items => YoutubeApi.AdvancedSearch(items, "Daft Punk", "Get Lucky", "", token, 0, 3)),
        };
    });

    [TestMethod]
    public Task Lookups() => Snapshot.MatchLive("Youtube.Lookups", async () =>
    {
        var tracks = new List<object>();
        foreach (var f in Fixtures.Tracks.Where(f => f.YoutubeId != null))
        {
            tracks.Add(new
            {
                f.Key,
                ById = await Live.Capture(async () => (await YoutubeApi.GetTrack(f.YoutubeId!)).ToShape()),
            });
        }
        return tracks;
    });

    [TestMethod]
    public Task Links() => Snapshot.MatchLive("Youtube.Links", async () =>
    {
        var rows = new List<object>();
        foreach (var url in new[]
        {
            "https://www.youtube.com/watch?v=jNQXAC9IVRw",
            "https://music.youtube.com/watch?v=jNQXAC9IVRw",
        })
        {
            var log = new Live.StatusLog();
            var result = await Live.Collect(items => YoutubeApi.AddTracksFromLink(items, url, CancellationToken.None, log.Callback));
            rows.Add(new { Url = url, Result = result, Status = log.Messages });
        }
        return rows;
    });
}
