using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
[TestCategory("YouTube")]
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
            Tracks = await Live.Collect(items => TestSources.Youtube.SearchAsync(items, "daft punk get lucky", new(0, 3), albumMode: false, token)),
            Albums = await Live.Collect(items => TestSources.Youtube.SearchAsync(items, "daft punk random access memories", new(0, 2), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Youtube.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 3), token)),
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
                ById = await Live.Capture(async () => (await TestSources.Youtube.GetTrack(f.YoutubeId!)).ToShape()),
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
            var result = await Live.Collect(items => TestSources.Youtube.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: false), log.Callback, CancellationToken.None));
            rows.Add(new { Url = url, Result = result, Status = log.Messages });
        }
        return rows;
    });
}

/// <summary>
/// Bug 20: a YouTube link whose lookup fails used to throw out of the link import, which crashed the app. A video that
/// doesn't exist fails the same way as YouTube's bot check, so it stands in for it. Needs no YouTube Music, so it skips
/// that availability check.
/// </summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("YouTube")]
public class YoutubeLinkErrorTests
{
    [TestMethod]
    public async Task AFailedLookupIsReportedNotThrown()
    {
        var url = "https://www.youtube.com/watch?v=aaaaaaaaaaa"; // a well-formed id with no video behind it
        var items = new List<Models.SongSearchObject>();
        var log = new Live.StatusLog();

        await TestSources.Youtube.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: false), log.Callback, CancellationToken.None);

        Assert.AreEqual(0, items.Count);
        Assert.AreEqual(1, log.Messages.Count);
        Assert.AreEqual("Error", log.Messages[0].Severity);
        // The new error path ("Error loading <link>: <reason>"), not the older "Error loading track from" one.
        StringAssert.StartsWith(log.Messages[0].Message, $"<b>YouTube</b>   Error loading <a href='{url}'>");
    }
}
