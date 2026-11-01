using System.Text.Json;
using FluentDL.Services;
using Microsoft.Data.Sqlite;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueItemPersistenceTests
{
    private static string Metadata(string url, int order = 3) => JsonSerializer.Serialize(new { ImageLocation = url, Isrc = "USABC2400001", QueueCounter = order });

    [TestMethod]
    public async Task MetadataAndOrderAreSavedWithoutWaitingForArtwork()
    {
        await using var connection = await Database();
        var json = Metadata("https://example.test/cover.jpg");
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", json);
        Assert.AreEqual(json, await Value(connection, "SELECT JsonText FROM Items WHERE Hash='one'"));
        Assert.AreEqual(DBNull.Value, await Value(connection, "SELECT Image FROM Items WHERE Hash='one'"));
    }

    [TestMethod]
    public async Task UpdatingMetadataKeepsTheExistingCachedCoverForTheSameUrl()
    {
        await using var connection = await Database();
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/cover.jpg"));
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/cover.jpg", [1, 2, 3]);
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/cover.jpg", 9));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await Value(connection, "SELECT Image FROM Items WHERE Hash='one'"))!);
        StringAssert.Contains((string)(await Value(connection, "SELECT JsonText FROM Items WHERE Hash='one'"))!, "\"QueueCounter\":9");
    }

    [TestMethod]
    public async Task AChangedCoverUrlDropsTheOldImageAndRejectsItsLateCompletion()
    {
        await using var connection = await Database();
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/old.jpg"));
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/old.jpg", [1]);
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/new.jpg"));
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/old.jpg", [2]);
        Assert.AreEqual(DBNull.Value, await Value(connection, "SELECT Image FROM Items WHERE Hash='one'"));
    }

    [TestMethod]
    public async Task LateArtworkNeverRecreatesARemovedQueueEntry()
    {
        await using var connection = await Database();
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/cover.jpg"));
        using (var delete = connection.CreateCommand()) { delete.CommandText = "DELETE FROM Items"; await delete.ExecuteNonQueryAsync(); }
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/cover.jpg", [1]);
        Assert.AreEqual(0L, await Value(connection, "SELECT COUNT(*) FROM Items"));
    }

    [TestMethod]
    public async Task SharedArtworkCopiesThePersistedJpegWithoutReplacingMetadata()
    {
        await using var connection = await Database();
        const string url = "https://example.test/shared.jpg";
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata(url, 1));
        await QueueItemPersistence.SaveMetadataAsync(connection, "two", Metadata(url, 2));
        await QueueItemPersistence.SetArtworkAsync(connection, "one", url, [1, 2, 3]);
        await QueueItemPersistence.SetArtworkAsync(connection, "two", url, null);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await Value(connection, "SELECT Image FROM Items WHERE Hash='two'"))!);
        Assert.AreEqual(Metadata(url, 2), await Value(connection, "SELECT JsonText FROM Items WHERE Hash='two'"));
    }

    [TestMethod]
    public async Task SharedArtworkIgnoresMalformedLegacyMetadata()
    {
        await using var connection = await Database();
        using (var legacy = connection.CreateCommand())
        {
            legacy.CommandText = "INSERT INTO Items VALUES ('bad', 'not-json', X'09', 'now')";
            await legacy.ExecuteNonQueryAsync();
        }
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", Metadata("https://example.test/cover.jpg"));
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/cover.jpg", null);
        Assert.AreEqual(DBNull.Value, await Value(connection, "SELECT Image FROM Items WHERE Hash='one'"));
    }

    [TestMethod]
    public async Task PartialSnapshotKeepsUnloadedRowsAndTheirCachedArtwork()
    {
        await using var connection = await Database();
        var unloaded = Metadata("https://example.test/unloaded.jpg", 2);
        await QueueItemPersistence.SaveMetadataAsync(connection, "unloaded", unloaded);
        await QueueItemPersistence.SetArtworkAsync(connection, "unloaded", "https://example.test/unloaded.jpg", [1, 2, 3]);
        await QueueItemPersistence.SaveSnapshotAsync(connection, new[] { ("loaded", Metadata("https://example.test/loaded.jpg", 1)) });
        Assert.AreEqual(unloaded, await Value(connection, "SELECT JsonText FROM Items WHERE Hash='unloaded'"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await Value(connection, "SELECT Image FROM Items WHERE Hash='unloaded'"))!);
        Assert.AreEqual(2L, await Value(connection, "SELECT COUNT(*) FROM Items"));
    }

    [TestMethod]
    public async Task EmptySnapshotDoesNotDeleteSavedRows()
    {
        await using var connection = await Database();
        await QueueItemPersistence.SaveMetadataAsync(connection, "unavailable", Metadata("https://example.test/cover.jpg"));
        await QueueItemPersistence.SaveSnapshotAsync(connection, Array.Empty<(string, string)>());
        Assert.AreEqual(1L, await Value(connection, "SELECT COUNT(*) FROM Items"));
    }

    [TestMethod]
    public async Task AFailedSnapshotRollsBackMetadataAndKeepsUnloadedArtwork()
    {
        await using var connection = await Database();
        var original = Metadata("https://example.test/cover.jpg", 3);
        await QueueItemPersistence.SaveMetadataAsync(connection, "one", original);
        await QueueItemPersistence.SetArtworkAsync(connection, "one", "https://example.test/cover.jpg", [1, 2, 3]);
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "CREATE TRIGGER fail_snapshot BEFORE INSERT ON Items WHEN NEW.Hash='fail' BEGIN SELECT RAISE(ABORT, 'Synthetic snapshot failure'); END";
            await trigger.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsExceptionAsync<SqliteException>(() => QueueItemPersistence.SaveSnapshotAsync(connection,
            new[] { ("one", Metadata("https://example.test/changed.jpg", 9)), ("fail", original) }));
        Assert.AreEqual(original, await Value(connection, "SELECT JsonText FROM Items WHERE Hash='one'"));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await Value(connection, "SELECT Image FROM Items WHERE Hash='one'"))!);
        Assert.AreEqual(1L, await Value(connection, "SELECT COUNT(*) FROM Items"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SnapshotKeepsLegacyArtworkWhenTheRestoredCoverIsMissingOrNormalized(bool normalized)
    {
        await using var connection = await Database();
        const string json = "{\"Source\":\"local\",\"Id\":\"Z:\\\\missing.flac\",\"QueueCounter\":3}";
        using (var seed = connection.CreateCommand())
        {
            seed.CommandText = "INSERT INTO Items VALUES ('local',@j,X'010203','now')";
            seed.Parameters.AddWithValue("@j", json);
            await seed.ExecuteNonQueryAsync();
        }
        var restored = normalized ? "{\"Source\":\"local\",\"ImageLocation\":\"ms-appx:///Assets/Unloaded.jpg\"}" : json;
        await QueueItemPersistence.SaveSnapshotAsync(connection, new[] { ("local", restored) });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await Value(connection, "SELECT Image FROM Items WHERE Hash='local'"))!);
    }

    private static async Task<SqliteConnection> Database()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL)";
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<object?> Value(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
