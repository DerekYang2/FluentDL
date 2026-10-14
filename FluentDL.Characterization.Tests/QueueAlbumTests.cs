using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;

namespace FluentDL.Characterization.Tests;

/// <summary>Albums in the queue: saving and restoring, conversion, download ordering, the count text and the ETA.</summary>
[TestClass]
public class QueueAlbumTests
{
    // Rows as DatabaseService stores them: QueueObject JSON. Albums carry AlbumTracksCount, expanded tracks QueueSubOrder.
    [TestMethod]
    public void SavedAlbumRowsRestoreAsAlbumsAndOldTrackRowsStayTracks()
    {
        var album = QueueObject.ParseSaved("""
            {"Title":"Random Access Memories","Id":"6575789","Source":"deezer","Artists":"Daft Punk","Isrc":"886443927087",
             "QueueCounter":4,"AlbumTracksCount":13,"QueueSubOrder":null}
            """)!.Value;
        var albumObject = album.Song as AlbumSearchObject;
        Assert.IsNotNull(albumObject, "An album row must restore as an album.");
        Assert.AreEqual(13, albumObject.TracksCount);
        Assert.AreEqual("886443927087", albumObject.Upc);
        Assert.AreEqual(4, albumObject.QueueCounter);

        // Rows saved before this change have neither field.
        var track = QueueObject.ParseSaved("""{"Title":"Get Lucky","Id":"67238735","Source":"deezer","QueueCounter":5}""")!.Value;
        Assert.IsNotInstanceOfType(track.Song, typeof(AlbumSearchObject));
        Assert.AreEqual(0, track.SubOrder);

        var expanded = QueueObject.ParseSaved("""{"Title":"Track 3","Id":"3","Source":"qobuz","QueueCounter":4,"AlbumTracksCount":null,"QueueSubOrder":2}""")!.Value;
        Assert.IsNotInstanceOfType(expanded.Song, typeof(AlbumSearchObject));
        Assert.AreEqual(2, expanded.SubOrder);

        Assert.IsNull(QueueObject.ParseSaved("{not json"), "A corrupt row is skipped, not the whole queue.");
    }

    [TestMethod]
    public void ExpandedTracksKeepAlbumOrder()
    {
        var tracks = new List<SongSearchObject>
        {
            new() { Id = "1", Title = "Give Life Back to Music", Isrc = "USQX91300101" },
            new() { Id = "2", Title = "The Game of Love", Isrc = "USQX91300102" },
            new() { Id = "3", Title = "Giorgio by Moroder", Isrc = "USQX91300103" },
        };
        // By ISRC, then by title when the tag has none, then by track number after the listed tracks.
        Assert.AreEqual(1, Views.QueuePage.AlbumOrder(tracks, new SongSearchObject { Title = "anything", Isrc = "usqx91300102" }));
        Assert.AreEqual(2, Views.QueuePage.AlbumOrder(tracks, new SongSearchObject { Title = "Giorgio By Moroder", TrackPosition = "9" }));
        Assert.AreEqual(3 + 7, Views.QueuePage.AlbumOrder(tracks, new SongSearchObject { Title = "Bonus", TrackPosition = "7/14" }));
    }

    [TestMethod]
    [DataRow(0, 0, "No tracks in queue")]
    [DataRow(1, 0, "1 track in queue")]
    [DataRow(12, 0, "12 tracks in queue")]
    [DataRow(0, 1, "1 album in queue")]
    [DataRow(3, 2, "3 tracks, 2 albums in queue")]
    public void CountTextNamesTracksAndAlbums(int tracks, int albums, string expected) =>
        Assert.AreEqual(expected, Views.QueuePage.FormatQueueCount(tracks, albums));

    [TestMethod]
    public void EtaOnlyShowsForLongOperations()
    {
        var eta = new EtaEstimator();
        Assert.IsNull(eta.Update(0.5, TimeSpan.FromSeconds(4)), "Too early.");
        Assert.IsNull(eta.Update(0.02, TimeSpan.FromSeconds(60)), "Too little progress to estimate.");
        Assert.IsNull(eta.Update(0.5, TimeSpan.FromSeconds(20)), "20 s left is short; skip it.");
        Assert.AreEqual("about 2 min left", eta.Update(0.2, TimeSpan.FromSeconds(30)));
        Assert.AreEqual("about 20 s left", eta.Update(0.6, TimeSpan.FromSeconds(30)), "Once shown, it stays until under 10 s.");
        Assert.IsNull(eta.Update(0.9, TimeSpan.FromSeconds(30)));
        Assert.AreEqual("about 1 h 30 min left", EtaEstimator.Format(5400));
    }

    [TestMethod]
    public async Task AlbumConversionUsesTheUpcThenFallsBackToAnAlbumSearch()
    {
        var album = new AlbumSearchObject
        {
            Source = SourceIds.Qobuz, Id = "q1", Title = "Random Access Memories", Artists = "Daft Punk", TracksCount = 13,
            Upc = "886443927087", // 12 digits; Deezer stores 13
        };
        var deezer = new FakeTarget(SourceIds.Deezer) { UpcAlbum = ("0886443927087", Album(SourceIds.Deezer, "d1", 13)) };
        var conversions = new ConversionService(new SourceRegistry([deezer]));
        var byUpc = await conversions.FindEquivalentAsync(album, SourceIds.Deezer, CancellationToken.None);
        Assert.AreEqual(MatchKind.Exact, byUpc.Kind);
        Assert.AreEqual("d1", byUpc.Track!.Id);
        Assert.IsInstanceOfType(byUpc.Track, typeof(AlbumSearchObject));
        Assert.AreEqual(0, deezer.Searches.Count, "A UPC hit needs no search.");

        // No UPC lookup on the target: search albums and prefer the same edition.
        var spotify = new FakeTarget(SourceIds.Spotify)
        {
            SearchResults = [Album(SourceIds.Spotify, "deluxe", 22, " (10th Anniversary Edition)"), Album(SourceIds.Spotify, "same", 13)],
        };
        var bySearch = await new ConversionService(new SourceRegistry([spotify])).FindEquivalentAsync(album, SourceIds.Spotify, CancellationToken.None);
        Assert.AreEqual(MatchKind.Exact, bySearch.Kind);
        Assert.AreEqual("same", bySearch.Track!.Id);
        Assert.IsTrue(spotify.Searches.Single().AlbumMode);
        StringAssert.Contains(spotify.Searches.Single().Query, "Daft Punk");

        spotify.SearchResults = [Album(SourceIds.Spotify, "deluxe", 22, " (10th Anniversary Edition)")];
        Assert.AreEqual(MatchKind.Close, (await new ConversionService(new SourceRegistry([spotify])).FindEquivalentAsync(album, SourceIds.Spotify, CancellationToken.None)).Kind);

        spotify.SearchResults = [Album(SourceIds.Spotify, "other", 13, "", "Somebody Else")];
        var none = await new ConversionService(new SourceRegistry([spotify])).FindEquivalentAsync(album, SourceIds.Spotify, CancellationToken.None);
        Assert.AreEqual(MatchKind.NotFound, none.Kind);
        Assert.IsNull(none.Track);

        // A target with a UPC lookup that does not know the UPC still searches.
        var tidal = new FakeTarget(SourceIds.Tidal) { SearchResults = [Album(SourceIds.Tidal, "t1", 13)] };
        var tidalMatch = await new ConversionService(new SourceRegistry([tidal])).FindEquivalentAsync(album, SourceIds.Tidal, CancellationToken.None);
        Assert.AreEqual("t1", tidalMatch.Track!.Id);
        CollectionAssert.AreEqual(new[] { "886443927087", "0886443927087" }, tidal.UpcLookups);
    }

    [TestMethod]
    public void TracksStillUseTrackConversion()
    {
        Assert.IsFalse(QueueObject.IsAlbumItem(new SongSearchObject()));
        Assert.IsTrue(QueueObject.IsAlbumItem(new AlbumSearchObject()));
    }

    private static AlbumSearchObject Album(string source, string id, int tracks, string suffix = "", string artists = "Daft Punk") => new()
    {
        Source = source, Id = id, Title = "Random Access Memories" + suffix, Artists = artists, TracksCount = tracks,
    };

    private sealed class FakeTarget(string id) : IConversionTarget, ISearchSource, IAlbumUpcSource
    {
        public SourceDescriptor Descriptor { get; } = SourceCatalog.Find(id)!;
        public (string Upc, AlbumSearchObject Album)? UpcAlbum;
        public List<SongSearchObject> SearchResults = [];
        public List<(string Query, bool AlbumMode)> Searches = [];
        public List<string> UpcLookups = [];

        public Task<AlbumSearchObject?> GetAlbumByUpcAsync(string upc, CancellationToken token)
        {
            UpcLookups.Add(upc);
            return Task.FromResult(UpcAlbum is { } hit && hit.Upc == upc ? hit.Album : null);
        }

        public Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token)
        {
            Searches.Add((query, albumMode));
            foreach (var result in SearchResults) results.Add(result);
            return Task.CompletedTask;
        }

        public Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false) =>
            throw new AssertFailedException("Albums must not go through track matching.");
    }
}
