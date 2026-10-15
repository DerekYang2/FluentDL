using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// ConversionService for songs with no ISRC (local files, YouTube videos): it finds the recording on Spotify by
/// search and length, then hands its ISRC to the target. Fake sources, no network.
/// </summary>
[TestClass]
public class ConversionBridgeTests
{
    private static readonly SongSearchObject SpringDay = new() { Source = "spotify", Id = "spring-day", Title = "Spring Day", Artists = "BTS", Duration = "274" };

    private static SongSearchObject LocalSong(string duration = "275") =>
        new() { Source = SourceIds.Local, Id = "bom-nal.flac", Title = "봄날", Artists = "방탄소년단", Duration = duration };

    [TestMethod]
    public async Task AnIsrcFoundThroughSpotifyGoesToTheTargetAsACloseMatch()
    {
        var bridge = new FakeBridge(SpringDay, "KRA381700253");
        var target = new FakeTarget(MatchKind.Exact);

        var match = await new ConversionService(new([bridge, target])).FindEquivalentAsync(LocalSong(), SourceIds.Deezer, CancellationToken.None);

        Assert.AreEqual("방탄소년단 봄날", bridge.Queries.Single());
        Assert.AreEqual("KRA381700253", target.ReceivedIsrc);
        Assert.AreEqual(MatchKind.Close, match.Kind); // found by a search, so yellow, not green
    }

    [TestMethod]
    [DataRow("280")] // 6 s longer: a different version, cover or karaoke track
    [DataRow("0")] // no length to compare
    [DataRow("")]
    public async Task WithoutTheSameLengthTheTargetMatchesByNameAsBefore(string duration)
    {
        var target = new FakeTarget(MatchKind.NotFound);

        await new ConversionService(new([new FakeBridge(SpringDay, "KRA381700253"), target])).FindEquivalentAsync(LocalSong(duration), SourceIds.Deezer, CancellationToken.None);

        Assert.IsTrue(target.Called);
        Assert.IsNull(target.ReceivedIsrc);
    }

    [TestMethod]
    public async Task AClockLengthIsComparedToo()
    {
        var target = new FakeTarget(MatchKind.Exact);

        await new ConversionService(new([new FakeBridge(SpringDay, "KRA381700253"), target])).FindEquivalentAsync(LocalSong("4:35"), SourceIds.Deezer, CancellationToken.None);

        Assert.AreEqual("KRA381700253", target.ReceivedIsrc);
    }

    [TestMethod]
    public async Task ASongWithAnIsrcSkipsTheSearch()
    {
        var bridge = new FakeBridge(SpringDay, "KRA381700253");
        var target = new FakeTarget(MatchKind.Exact);
        var song = LocalSong();
        song.Isrc = "KRA381700253";

        var match = await new ConversionService(new([bridge, target])).FindEquivalentAsync(song, SourceIds.Deezer, CancellationToken.None);

        Assert.AreEqual(0, bridge.Queries.Count);
        Assert.AreEqual(MatchKind.Exact, match.Kind); // the song's own ISRC stays green
    }

    [TestMethod]
    public async Task ConvertingToSpotifyReturnsTheTrackItFound()
    {
        var bridge = new FakeBridge(SpringDay, "KRA381700253");

        var match = await new ConversionService(new([bridge])).FindEquivalentAsync(LocalSong(), SourceIds.Spotify, CancellationToken.None);

        Assert.AreEqual(MatchKind.Close, match.Kind);
        Assert.AreSame(SpringDay, match.Track);
        Assert.AreEqual(0, bridge.ConversionCalls);
    }

    [TestMethod]
    public async Task WhenSpotifyFailsTheTargetMatchesByNameAsBefore()
    {
        var target = new FakeTarget(MatchKind.Close);

        var match = await new ConversionService(new([new FakeBridge(SpringDay, "KRA381700253") { Fails = true }, target])).FindEquivalentAsync(LocalSong(), SourceIds.Deezer, CancellationToken.None);

        Assert.IsNull(target.ReceivedIsrc);
        Assert.AreEqual(MatchKind.Close, match.Kind);
    }

    // Spotify: searches, gives ISRCs, and is a conversion target.
    private sealed class FakeBridge(SongSearchObject top, string isrc) : ISearchSource, IIsrcSource, IConversionTarget
    {
        public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Spotify)!;
        public List<string> Queries { get; } = [];
        public int ConversionCalls { get; private set; }
        public bool Fails { get; init; }

        public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token)
        {
            Queries.Add(query);
            if (Fails) throw new HttpRequestException("Spotify is down");
            results.Add(top);
            return Task.CompletedTask;
        }

        public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) => Task.CompletedTask;
        public Task<string?> GetIsrcAsync(string trackId) => Task.FromResult<string?>(trackId == top.Id ? isrc : null);

        public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false)
        {
            ConversionCalls++;
            return Task.FromResult(new MatchResult(null, MatchKind.NotFound));
        }
    }

    // Deezer as a conversion target: answers with the given kind and remembers the ISRC it got.
    private sealed class FakeTarget(MatchKind kind) : IConversionTarget
    {
        public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(SourceIds.Deezer)!;
        public bool Called { get; private set; }
        public string? ReceivedIsrc { get; private set; }

        public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false)
        {
            Called = true;
            ReceivedIsrc = isrc;
            return Task.FromResult(new MatchResult(kind == MatchKind.NotFound ? null : new SongSearchObject { Source = "deezer", Id = "1783253587" }, kind));
        }
    }
}
