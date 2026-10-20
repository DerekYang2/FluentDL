using FluentDL.Contracts.Sources;
using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.Data.Sqlite;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueAlbumExpansionTests
{
    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    [DataRow("youtube")]
    [DataRow("tidal")]
    public async Task ExpansionListsTheOriginalSourcesTracksWithoutDownloading(string source)
    {
        var album = Album(source);
        var catalog = new FakeCatalog(source, [Track(source, "3"), Track(source, "1"), Track(source, "2")]);
        var tracks = await new QueueAlbumExpansion(new SourceRegistry([catalog])).LoadAsync(album);
        Assert.AreEqual(1, catalog.Calls);
        Assert.AreNotSame<object>(album, catalog.Received);
        CollectionAssert.AreEqual(new[] { "3", "1", "2" }, tracks.Select(track => track.Id).ToArray());
        Assert.AreEqual(3, album.AlbumTracksCount);
        Assert.IsNull(album.TrackList, "Lookup must not replace the queued album before commit.");
    }

    [TestMethod]
    [DataRow("spotify")]
    [DataRow("youtube")]
    public async Task CachedAlbumTracksArePassedToSourcesThatCannotReloadByAlbumId(string source)
    {
        var cached = new List<SongSearchObject> { Track(source, "a"), Track(source, "b"), Track(source, "c") };
        var album = Album(source);
        album.TrackList = cached;
        var catalog = new FakeCatalog(source, null);
        var tracks = await new QueueAlbumExpansion(new SourceRegistry([catalog])).LoadAsync(album);
        CollectionAssert.AreEqual(cached, tracks.ToList());
        Assert.AreNotSame(cached, catalog.Received!.TrackList);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("incomplete")]
    [DataRow("wrong-source")]
    [DataRow("missing-id")]
    [DataRow("nested-album")]
    public async Task InvalidOrIncompleteResultsLeaveTheAlbumIntact(string failure)
    {
        var album = Album("tidal");
        var tracks = new List<SongSearchObject> { Track("tidal", "1"), Track("tidal", "2"), Track("tidal", "3") };
        switch (failure)
        {
            case "empty": tracks.Clear(); break;
            case "incomplete": tracks.RemoveAt(2); break;
            case "wrong-source": tracks[0].Source = "deezer"; break;
            case "missing-id": tracks[0].Id = ""; break;
            case "nested-album": tracks[0] = new AlbumSearchObject { Source = "tidal", Id = "nested" }; break;
        }
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new QueueAlbumExpansion(new SourceRegistry([new FakeCatalog("tidal", tracks)])).LoadAsync(album));
        Assert.IsTrue(album.IsAlbum);
        Assert.AreEqual("album", album.Id);
        Assert.IsNull(album.TrackList);
    }

    [TestMethod]
    public void PlanSkipsExistingAndRepeatedTracksWithoutMovingExistingRows()
    {
        var album = Album("tidal");
        var existing = new QueueObject(Track("tidal", "2")) { QueueCounter = 20 };
        var tracks = new[] { Track("tidal", "3"), Track("tidal", "2"), Track("tidal", "3"), Track("tidal", "1") };
        var plan = QueueAlbumExpansion.Plan(album, tracks, [album, existing, new(Track("deezer", "1"))]);
        CollectionAssert.AreEqual(new[] { "3", "1" }, plan.Rows.Select(row => row.Id).ToArray());
        CollectionAssert.AreEqual(new int?[] { 0, 3 }, plan.Rows.Select(row => row.QueueSubOrder).ToArray());
        Assert.IsTrue(plan.Rows.All(row => row.QueueCounter == 7 && !row.IsAlbum));
        Assert.AreEqual(2, plan.Skipped);
        Assert.AreEqual(20, existing.QueueCounter);
        Assert.IsNull(existing.QueueSubOrder);
        Assert.AreEqual(0, tracks[0].QueueCounter.GetValueOrDefault(), "Planning must not mutate the source's track objects.");
    }

    [TestMethod]
    public void ExpandedRowsPreserveSavedOrderAndProviderMetadata()
    {
        var album = Album("spotify");
        var cover = new object();
        album.LocalBitmapImage = cover;
        var tracks = new[] { Track("spotify", "b"), Track("spotify", "a"), Track("spotify", "c") };
        tracks[0].AdditionalFields = new() { ["preview_url"] = "https://preview.example.test/audio" };
        var plan = QueueAlbumExpansion.Plan(album, tracks, [album]);
        var restored = plan.Rows.Select(row => QueueObject.ParseSaved(row.ToString())!.Value).ToArray();
        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, restored.OrderBy(row => row.SubOrder).Select(row => row.Song.Id).ToArray());
        Assert.AreEqual("https://preview.example.test/audio", restored[0].Song.AdditionalFields!["preview_url"]);
        Assert.IsTrue(plan.Rows.All(row => ReferenceEquals(cover, row.LocalBitmapImage)));
        Assert.IsTrue(restored.All(row => row.Song.QueueCounter == 7));
    }

    [TestMethod]
    public void AllDuplicateTracksProduceNoNewRows()
    {
        var album = Album("tidal");
        var tracks = new[] { Track("tidal", "1"), Track("tidal", "2"), Track("tidal", "3") };
        var plan = QueueAlbumExpansion.Plan(album, tracks, tracks.Select(track => new QueueObject(track)).Append(album));
        Assert.AreEqual(0, plan.Rows.Count);
        Assert.AreEqual(3, plan.Skipped);
    }

    [TestMethod]
    public async Task PersistenceReplacesTheAlbumAndCopiesItsCachedCover()
    {
        using var connection = await Database();
        await QueueAlbumPersistence.ReplaceAsync(connection, "album:tidalalbum", [("tidal1", "track-one"), ("tidal2", "track-two")]);
        CollectionAssert.AreEquivalent(new[] { "outside", "tidal1", "tidal2" }, (await Keys(connection)).ToArray());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Image FROM Items WHERE Hash = 'tidal1'";
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await command.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task FailedPersistenceRollsBackEveryInsertedTrackAndKeepsTheAlbum()
    {
        using var connection = await Database();
        using (var reject = connection.CreateCommand())
        {
            reject.CommandText = "CREATE TRIGGER reject_second BEFORE INSERT ON Items WHEN NEW.Hash = 'tidal2' BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;";
            await reject.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsExceptionAsync<SqliteException>(() => QueueAlbumPersistence.ReplaceAsync(connection, "album:tidalalbum", [("tidal1", "track-one"), ("tidal2", "track-two")]));
        CollectionAssert.AreEquivalent(new[] { "album:tidalalbum", "outside" }, (await Keys(connection)).ToArray());
    }

    [TestMethod]
    public async Task MissingSavedAlbumCannotInsertOrphanTracks()
    {
        using var connection = await Database();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => QueueAlbumPersistence.ReplaceAsync(connection, "missing", [("tidal1", "track-one")]));
        CollectionAssert.AreEquivalent(new[] { "album:tidalalbum", "outside" }, (await Keys(connection)).ToArray());
    }

    private static QueueObject Album(string source) => new(new AlbumSearchObject { Source = source, Id = "album", Title = "Album", TracksCount = 3 }) { QueueCounter = 7 };
    private static SongSearchObject Track(string source, string id) => new() { Source = source, Id = id, Title = id, TrackPosition = id };
    private static async Task<SqliteConnection> Database()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL); "
            + "INSERT INTO Items VALUES ('album:tidalalbum', 'album', X'010203', 'now'), ('outside', 'outside', NULL, 'now');";
        await command.ExecuteNonQueryAsync();
        return connection;
    }
    private static async Task<List<string>> Keys(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Hash FROM Items";
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new List<string>();
        while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
        return keys;
    }
    private sealed class FakeCatalog(string id, List<SongSearchObject>? tracks) : ICatalogSource
    {
        public SourceDescriptor Descriptor => SourceCatalog.Find(id)!;
        public int Calls;
        public AlbumSearchObject? Received;
        public Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album)
        {
            Calls++;
            Received = album;
            return Task.FromResult(tracks ?? album.TrackList ?? []);
        }
        public Task<SongSearchObject?> GetTrackAsync(string id) => throw new AssertFailedException("Expansion must only list album tracks.");
        public Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album) => throw new AssertFailedException("Expansion reuses cached artwork.");
    }
}
