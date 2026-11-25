using System.Net;
using System.Text.Json;
using System.Web;
using FluentDL.Contracts.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class DeezerIsrcConversionTests
{
    internal const string Isrc = "USDMG1260805";
    private const string IsrcLookupPath = "/track/isrc:" + Isrc;
    private const string Artist = "The Lumineers";
    private const string Title = "Ho Hey";
    private static readonly Release Single = new("2864019852", "606848212", Title, Artist, 824699);
    private static readonly Release Deluxe = new("2865751052", "607331412", "The Lumineers (Deluxe Edition)", Artist, 487421);
    private static readonly Release Now = new("69549556", "6816837", "NOW: The Hits Of Spring 2013", "Various Artists", 0, Readable: false);

    [TestMethod]
    public async Task AReadableDirectMatchOnTheNamedAlbumNeedsOnlyTheIsrcLookup()
    {
        var catalog = new FakeCatalog { Lookup = Deluxe };

        var match = await Source(catalog).FindEquivalentAsync(Song("The Lumineers"), Isrc, CancellationToken.None);

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(Deluxe.Id, match.Track?.Id);
        CollectionAssert.AreEqual(new[] { IsrcLookupPath }, catalog.Paths);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AReadableDirectMatchOnAnotherAlbumIsUsedOnlyWhenSearchFindsNoMatch(bool searchFindsMatch)
    {
        var catalog = new FakeCatalog { Lookup = Now with { Readable = true }, Search = searchFindsMatch ? [Single] : [] };

        var match = await Source(catalog).FindEquivalentAsync(Song(), Isrc, CancellationToken.None, isrcOnly: true);

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(searchFindsMatch ? Single.Id : Now.Id, match.Track?.Id);
        var expectedPaths = searchFindsMatch
            ? new[] { IsrcLookupPath, "/search", "/track/" + Single.Id }
            : new[] { IsrcLookupPath, "/search" };
        CollectionAssert.AreEqual(expectedPaths, catalog.Paths);
    }

    [TestMethod]
    [DataRow("Ho Hey", "2864019852")]
    [DataRow("The Lumineers", "2865751052")]
    [DataRow("Another Album", "2864019852")]
    public async Task SearchFindsTheNamedAlbumOrTheArtistsHighestRankedRelease(string albumName, string expected)
    {
        var catalog = new FakeCatalog { Search = [Now, Deluxe, Single], Lookup = Now };

        var match = await Source(catalog).FindEquivalentAsync(Song(albumName), Isrc, CancellationToken.None, isrcOnly: true);

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(expected, match.Track?.Id);
        Assert.AreEqual("The Lumineers ho hey", catalog.Queries.Single());
        Assert.IsFalse(catalog.Paths.Contains("/track/" + Now.Id));
        CollectionAssert.AreEqual(new[] { IsrcLookupPath, "/search" }, catalog.Paths.Take(2).ToArray());
        Assert.AreEqual(1, catalog.Paths.Count(path => path == IsrcLookupPath));
    }

    [TestMethod]
    public async Task AReadableCompilationRanksBelowTheArtistsOwnReleaseEvenWithAHigherRank()
    {
        var catalog = new FakeCatalog { Search = [Now with { Readable = true, Rank = 999999 }, Single] };

        var match = await Source(catalog).FindEquivalentAsync(Song("Another Album"), Isrc, CancellationToken.None);

        Assert.AreEqual(Single.Id, match.Track?.Id);
        CollectionAssert.Contains(catalog.Paths, "/album/" + Now.AlbumId);
    }

    [TestMethod]
    public async Task TheTrackDetailsMustConfirmReadabilityEvenForTheNamedAlbum()
    {
        var catalog = new FakeCatalog { Search = [Single with { Readable = false, PreviewReadable = true }, Deluxe], Lookup = Now };

        var match = await Source(catalog).FindEquivalentAsync(Song(), Isrc, CancellationToken.None, isrcOnly: true);

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual(Deluxe.Id, match.Track?.Id);
        CollectionAssert.Contains(catalog.Paths, "/track/" + Single.Id);
    }

    [TestMethod]
    [DataRow(true, true, "69549556", MatchKind.Exact)]
    [DataRow(false, true, null, MatchKind.NotFound)]
    [DataRow(false, false, "2864019852", MatchKind.Close)]
    public async Task TheDirectLookupMustBeReadableOrConversionFallsBackToMetadata(bool readable, bool isrcOnly, string? expected, MatchKind kind)
    {
        var catalog = new FakeCatalog { Search = [Single with { Isrc = "OTHER" }], Lookup = Now with { Readable = readable } };

        var match = await Source(catalog).FindEquivalentAsync(Song(), Isrc, CancellationToken.None, isrcOnly);

        Assert.AreEqual(kind, match.Kind);
        Assert.AreEqual(expected, match.Track?.Id);
        Assert.AreEqual(IsrcLookupPath, catalog.Paths[0]);
        Assert.AreEqual(isrcOnly ? 1 : 3, catalog.Queries.Count);
    }

    [TestMethod]
    public async Task TrackDetailRequestsAreLimitedToFiveDistinctMatchingTitlesAndArtists()
    {
        var wrongArtist = Single with { Id = "cover", TrackArtist = "Another Artist" };
        var wrongTitle = Single with { Id = "live", TrackTitle = "Ho Hey (Live)" };
        var sameTitle = Enumerable.Range(1, 6).Select(i => Single with { Id = $"{i}", Isrc = "OTHER" }).ToArray();
        var catalog = new FakeCatalog { Search = [wrongArtist, wrongTitle, sameTitle[0], .. sameTitle], Lookup = Now };

        var match = await Source(catalog).FindEquivalentAsync(Song(), Isrc, CancellationToken.None, isrcOnly: true);

        Assert.AreEqual(MatchKind.NotFound, match.Kind);
        CollectionAssert.AreEqual(new[] { IsrcLookupPath, "/search", "/track/1", "/track/2", "/track/3", "/track/4", "/track/5" }, catalog.Paths);
    }

    [TestMethod]
    public async Task CancellationDuringTrackDetailsStopsFurtherRequests()
    {
        using var cancellation = new CancellationTokenSource();
        var catalog = new FakeCatalog { Lookup = Now, Search = [Single, Deluxe], AfterRequest = path =>
        {
            if (path == "/track/" + Single.Id) cancellation.Cancel();
        } };

        try
        {
            await Source(catalog).FindEquivalentAsync(Song(), Isrc, cancellation.Token);
            Assert.Fail("Cancellation should propagate.");
        }
        catch (OperationCanceledException) { }

        CollectionAssert.AreEqual(new[] { IsrcLookupPath, "/search", "/track/" + Single.Id }, catalog.Paths);
    }

    internal static SongSearchObject Song(string albumName = Title) =>
        new() { Title = Title, Artists = Artist + ", Wesley Schultz", AlbumName = albumName, Isrc = Isrc };

    private static DeezerSource Source(FakeCatalog catalog) =>
        new(new TidalParsingTests.DictionarySettings([]), null!) { restClient = new RestHelper("https://catalog.test", handler: catalog) };

    private sealed record Release(string Id, string AlbumId, string Album, string AlbumArtist, int Rank, bool Readable = true,
        string Isrc = Isrc, string TrackArtist = Artist, string TrackTitle = Title, bool? PreviewReadable = null)
    {
        public object Preview() => new { id = Id, title = TrackTitle, readable = PreviewReadable ?? Readable, rank = Rank,
            artist = new { name = TrackArtist }, album = new { id = AlbumId, title = Album } };

        public object Detail() => new { id = Id, title = TrackTitle, readable = Readable, rank = Rank, isrc = Isrc.ToLowerInvariant(),
            artist = new { name = TrackArtist }, album = new { id = AlbumId, title = Album } };
    }

    private sealed class FakeCatalog : HttpMessageHandler
    {
        public Release[] Search { get; init; } = [];
        public Release? Lookup { get; init; }
        public Action<string>? AfterRequest { get; init; }
        public List<string> Paths { get; } = [];
        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            Paths.Add(path);
            if (path == "/search") Queries.Add(HttpUtility.ParseQueryString(uri.Query)["q"]!);
            object? body = path switch
            {
                "/search" => new { data = Search.Select(r => r.Preview()) },
                IsrcLookupPath => Lookup?.Detail(),
                _ when path.StartsWith("/track/") => Search.FirstOrDefault(r => path == "/track/" + r.Id)?.Detail(),
                _ when path.StartsWith("/album/") => new { artist = new { name = Search.First(r => path == "/album/" + r.AlbumId).AlbumArtist } },
                _ => null,
            };
            AfterRequest?.Invoke(path);
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body ?? new { error = new { code = 800 } })),
            });
        }
    }
}

[TestClass]
[TestCategory("Live")]
public class DeezerIsrcConversionLiveTests
{
    [TestMethod]
    public async Task HoHeyConvertsToTheSingleWithoutAnAccount()
    {
        var source = new DeezerSource(new TidalParsingTests.DictionarySettings([]), null!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var match = await source.FindEquivalentAsync(DeezerIsrcConversionTests.Song(), DeezerIsrcConversionTests.Isrc, timeout.Token, isrcOnly: true);

        Assert.AreEqual(MatchKind.Exact, match.Kind);
        Assert.AreEqual("2864019852", match.Track?.Id);
    }
}
