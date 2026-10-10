using System.Collections.ObjectModel;
using System.Text.Json;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Models;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// TIDAL through its official API, signed in with the client id and secret from the secrets file. New code, so the
/// baselines are recorded from it, not from the pre-refactor build.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class TidalTests
{
    [TestInitialize]
    public Task SignIn() => LiveSources.EnsureTidalAsync();

    [TestMethod]
    public Task Search() => Snapshot.MatchLive("Tidal.Search", async () =>
    {
        var token = CancellationToken.None;
        var tracks = await Live.Collect(items => TestSources.Tidal.SearchAsync(items, "daft punk get lucky", new(0, 5), albumMode: false, token));
        return new
        {
            Tracks = tracks,
            TracksSecondPage = await Live.CollectNextPage(items => TestSources.Tidal.SearchAsync(items, "daft punk get lucky", new(5, 5), albumMode: false, token), tracks),
            Albums = await Live.Collect(items => TestSources.Tidal.SearchAsync(items, "random access memories", new(0, 3), albumMode: true, token)),
            AdvancedArtistTrack = await Live.Collect(items => TestSources.Tidal.AdvancedSearchAsync(items, new("Daft Punk", "Get Lucky", ""), new(0, 5), token)),
        };
    });

    // TIDAL pages by cursor, 20 a page; the Search page asks by offset (20 first, then 10 at a time as it scrolls).
    [TestMethod]
    public async Task PagingByOffsetMatchesOneLongRequest()
    {
        var token = CancellationToken.None;
        var scrolled = new List<SongSearchObject>();
        foreach (var page in new PageRequest[] { new(0, 20), new(20, 10), new(30, 10) })
        {
            await TestSources.Tidal.SearchAsync(scrolled, "daft punk", page, albumMode: false, token);
        }
        var atOnce = new List<SongSearchObject>();
        await TestSources.Tidal.SearchAsync(atOnce, "daft punk", new(0, 40), albumMode: false, token);

        Assert.AreEqual(40, scrolled.Count);
        Assert.AreEqual(40, scrolled.Select(s => s.Id).Distinct().Count(), "a page repeated tracks");
        CollectionAssert.AreEqual(atOnce.Select(s => s.Id).ToList(), scrolled.Select(s => s.Id).ToList());
    }

    // The fields the app shows and matches on, for one known track.
    [TestMethod]
    public async Task ATrackHasItsDetails()
    {
        var results = new List<SongSearchObject>();
        await TestSources.Tidal.SearchAsync(results, "daft punk get lucky random access memories", new(0, 10), albumMode: false, CancellationToken.None);
        var track = results.FirstOrDefault(r => r.Isrc == Fixtures.Tracks[0].Isrc);

        Assert.IsNotNull(track, "Get Lucky (album version) is not in the first 10 results");
        Assert.AreEqual("tidal", track.Source);
        Assert.AreEqual("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", track.Title);
        Assert.AreEqual("Daft Punk, Pharrell Williams, Nile Rodgers", track.Artists);
        Assert.AreEqual("Random Access Memories", track.AlbumName);
        Assert.AreEqual("2013-05-20", track.ReleaseDate);
        Assert.AreEqual("370", track.Duration);
        StringAssert.EndsWith(track.ImageLocation, "/320x320.jpg");
        StringAssert.EndsWith(track.AdditionalFields?["cover_max"] as string, "/1280x1280.jpg");
    }
}

/// <summary>Parsing TIDAL's responses. No network.</summary>
[TestClass]
public class TidalParsingTests
{
    [TestMethod]
    [DataRow("PT6M10S", "370")]
    [DataRow("PT4M8S", "248")]
    [DataRow("PT1H2M3S", "3723")]
    [DataRow("PT45.6S", "46")]
    [DataRow("", "0")]
    [DataRow("not a duration", "0")]
    public void DurationsBecomeSeconds(string iso, string seconds) => Assert.AreEqual(seconds, TidalSource.Seconds(iso));

    [TestMethod]
    [DataRow("GB", "GB")]
    [DataRow("us", "US")]
    [DataRow("001", "US")] // Windows' "World" region
    [DataRow("", "US")]
    public void RegionsBecomeCountryCodes(string region, string code) => Assert.AreEqual(code, TidalApi.RegionCode(region));

    // A relationship lists references in TIDAL's order (the ranking, or the credit order); "included" can be in any
    // order and can miss some.
    [TestMethod]
    public void RelatedResourcesFollowTheRelationshipOrder()
    {
        var json = """
        {
          "data": { "id": "t1", "type": "tracks", "attributes": { "title": "Song", "version": "Live" },
                    "relationships": { "artists": { "data": [ { "id": "a2", "type": "artists" }, { "id": "a1", "type": "artists" }, { "id": "a9", "type": "artists" } ] } } },
          "included": [
            { "id": "a1", "type": "artists", "attributes": { "name": "First" } },
            { "id": "a2", "type": "artists", "attributes": { "name": "Second" } }
          ]
        }
        """;
        var document = new JsonApiDocument(JsonDocument.Parse(json).RootElement);
        var track = document.Data.Single();

        var names = document.Related(track, "artists").Select(a => JsonApiDocument.Attribute(a, "name")).ToList();

        CollectionAssert.AreEqual(new[] { "Second", "First" }, names); // a9 isn't included, so it's skipped
        Assert.AreEqual("Song (Live)", TidalSource.ConvertTrack(document, track).Title);
        Assert.AreEqual("Second, First", TidalSource.ConvertTrack(document, track).Artists);
    }

    [TestMethod]
    [DataRow("Song", "Live", "Song (Live)")]
    [DataRow("Random Access Memories (10th Anniversary Edition)", "10th Anniversary Edition", "Random Access Memories (10th Anniversary Edition)")]
    [DataRow("Song", "", "Song")]
    public void AVersionIsAddedOnce(string title, string version, string expected)
    {
        var json = $$"""{ "data": { "id": "x", "type": "albums", "attributes": { "title": {{JsonSerializer.Serialize(title)}}, "version": {{JsonSerializer.Serialize(version)}} } } }""";
        var document = new JsonApiDocument(JsonDocument.Parse(json).RootElement);

        Assert.AreEqual(expected, TidalSource.ConvertAlbum(document, document.Data.Single()).Title);
    }
}
