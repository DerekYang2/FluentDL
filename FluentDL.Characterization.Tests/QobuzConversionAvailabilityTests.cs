using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentDL.Contracts.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QobuzConversionAvailabilityTests
{
    private const string Isrc = "GBTEST000001";

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(null, true)]
    [DataRow(true, null)]
    [DataRow(null, null)]
    public async Task IsrcSearchSkipsUnavailableResultsAndFindsAnAvailableExactMatch(bool? streamable, bool? displayable)
    {
        using var http = new HttpClient(new Handler(_ => Reply(new { tracks = new { items = new[]
        {
            Track(1, streamable, displayable), Track(2, isrc: "OTHER"), Track(3, isrc: Isrc.ToLowerInvariant()),
        } } })));
        var source = Source(http);
        var match = await source.FindEquivalentAsync(Input(), Isrc, CancellationToken.None, isrcOnly: true);
        Assert.AreEqual("3", match.Track?.Id);
        Assert.AreEqual(MatchKind.Exact, match.Kind);
    }

    [TestMethod]
    public async Task IsrcOnlyDoesNotReturnUnavailableTracksOrUseMetadataFallback()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            StringAssert.EndsWith(request.RequestUri!.AbsolutePath, "/track/search");
            return Reply(new { tracks = new { items = new[] { Track(1, false, false) } } });
        }));
        var source = Source(http);
        var match = await source.FindEquivalentAsync(Input(), Isrc, CancellationToken.None, isrcOnly: true);
        Assert.IsNull(match.Track);
        Assert.AreEqual(MatchKind.NotFound, match.Kind);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MetadataSearchSkipsUnavailableTracksAndStartsAtTheFirstPage(bool availableAlternative)
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            calls.Add(path);
            if (path.EndsWith("album/search")) return Reply(new { albums = new { items = Array.Empty<object>() } });
            Assert.AreEqual("0", query["offset"]);
            Assert.AreEqual("28", query["limit"]);
            return Reply(new { tracks = new { items = new[] { Track(1, false, false), Track(2, availableAlternative, true) } } });
        }));
        var match = await Source(http).FindEquivalentAsync(Input(), null, CancellationToken.None);
        Assert.AreEqual(availableAlternative ? "2" : null, match.Track?.Id);
        Assert.AreEqual(availableAlternative ? MatchKind.Close : MatchKind.NotFound, match.Kind);
        Assert.AreEqual(2, calls.Count);
    }

    [TestMethod]
    public async Task UnavailableIsrcResultsCanFallBackToAnAvailableMetadataMatch()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("album/search"))
                return Reply(new { albums = new { items = Array.Empty<object>() } });
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["query"];
            return Reply(new { tracks = new { items = new[] { query == Isrc ? Track(1, false, false) : Track(2) } } });
        }));
        var match = await Source(http).FindEquivalentAsync(Input(), Isrc, CancellationToken.None);
        Assert.AreEqual("2", match.Track?.Id);
        Assert.AreEqual(MatchKind.Close, match.Kind);
    }

    [TestMethod]
    [DataRow(200, false, true)]
    [DataRow(200, true, false)]
    [DataRow(200, null, null)]
    [DataRow(404, true, true)]
    public async Task AlbumMatchingChecksTrackDetailsAndContinuesAfterAnUnavailableCandidate(int status, bool? streamable, bool? displayable)
    {
        var detailIds = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("album/search")) return Reply(new { albums = new { items = new[]
            {
                new { id = "test-album", title = "Album", artists = new[] { new { name = "Artist" } } },
            } } });
            if (path.EndsWith("album/get")) return Reply(new { tracks = new { items = new[] { Track(1), Track(2) } } });
            StringAssert.EndsWith(path, "/track/get");
            var id = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["track_id"]!;
            detailIds.Add(id);
            return id == "1" ? Reply(Track(1, streamable, displayable), (HttpStatusCode)status) : Reply(Track(2));
        }));
        var match = await Source(http).FindEquivalentAsync(Input(), null, CancellationToken.None);
        Assert.AreEqual("2", match.Track?.Id);
        Assert.AreEqual(MatchKind.Close, match.Kind);
        CollectionAssert.AreEqual(new[] { "1", "2" }, detailIds);
    }

    [TestMethod]
    public async Task CancellationDuringIsrcSearchDoesNotBecomeANoMatch()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }));
        try
        {
            await Source(http).SearchISRC(Isrc, cancellation.Token);
            Assert.Fail("Cancellation should propagate.");
        }
        catch (OperationCanceledException) { }
    }

    private static SongSearchObject Input() => new() { Title = "Song", Artists = "Artist", AlbumName = "Album" };

    private static object Track(int id, bool? streamable = true, bool? displayable = true, string isrc = Isrc) => new
    {
        id, title = "Song", isrc, streamable, displayable, performers = "Artist, MainArtist", performer = new { name = "Artist" },
    };

    private static QobuzSource Source(HttpClient http)
    {
        var source = new QobuzSource(new TidalParsingTests.DictionarySettings([]))
        {
            IsInitialized = true,
            CatalogRequests = new ProviderRequestPolicy("Qobuz test", interval: TimeSpan.Zero, transientRetries: 0),
        };
        typeof(QobuzSource).GetField("QobuzHttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, http);
        typeof(QobuzSource).GetField("HttpClientInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, true);
        return source;
    }

    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
