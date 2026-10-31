using FluentDL.Core.Sources;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Data.Sqlite;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class QueueTrackReplacementTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConvergingConversionsKeepTheEarliestPositionAcrossSaveRestoreAndRemoval(bool laterFinishesFirst)
    {
        var first = Row(SourceIds.Qobuz, "qobuz", 2);
        var middle = Row(SourceIds.Deezer, "unrelated", 3);
        var last = Row(SourceIds.Tidal, "tidal", 4);
        var rows = new List<QueueObject> { first, middle, last };
        var keys = rows.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        await using var connection = await Database(rows);
        var earlierMatch = Row(SourceIds.Spotify, "same-id", 2);
        var laterMatch = Row(SourceIds.Spotify, "same-id", 4);
        var completed = new[] { (first, earlierMatch), (last, laterMatch) };
        if (laterFinishesFirst) Array.Reverse(completed);
        foreach (var (original, match) in completed)
        {
            var result = QueueTrackReplacement.Apply(rows, keys, rows.IndexOf(original), match)!;
            await QueueItemPersistence.ReplaceAsync(connection, result.RemovedHash, QueueTrackReplacement.KeyOf(result.Row), result.Row.ToString());
        }

        CollectionAssert.AreEqual(new[] { earlierMatch, middle }, rows);
        CollectionAssert.AreEquivalent(rows.Select(QueueTrackReplacement.KeyOf).ToArray(), keys.ToArray());
        var restored = await Restore(connection);
        CollectionAssert.AreEqual(new[] { "same-id", "unrelated" }, restored.Select(row => row.Id).ToArray());
        Assert.AreEqual(2, restored[0].QueueCounter);

        // Both source outcomes remain in the conversion summary even though the queue has one target row.
        var outcomes = new Dictionary<SongSearchObject, (int Queue, int Track)>
        { [earlierMatch] = (0, 0), [laterMatch] = (2, 0) };
        Assert.AreEqual(2, QueuePage.OrderedConversionResults(outcomes).Count);

        var retainedKey = QueueTrackReplacement.KeyOf(rows[0]);
        rows.RemoveAt(0);
        keys.Remove(retainedKey);
        using (var remove = connection.CreateCommand())
        {
            remove.CommandText = "DELETE FROM Items WHERE Hash=@h";
            remove.Parameters.AddWithValue("@h", retainedKey);
            await remove.ExecuteNonQueryAsync();
        }
        CollectionAssert.AreEqual(new[] { "unrelated" }, (await Restore(connection)).Select(row => row.Id).ToArray());
        Assert.IsFalse(keys.Contains(retainedKey));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AnExistingTargetKeepsWhicheverQueuePositionWasEarlier(bool targetFirst)
    {
        var original = Row(SourceIds.Qobuz, "input", targetFirst ? 4 : 1);
        var existing = Row(SourceIds.Spotify, "target", targetFirst ? 1 : 4);
        var rows = targetFirst ? new List<QueueObject> { existing, original } : [original, existing];
        var keys = rows.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        var replacement = Row(SourceIds.Spotify, "target", original.QueueCounter!.Value);
        var result = QueueTrackReplacement.Apply(rows, keys, rows.IndexOf(original), replacement)!;
        Assert.AreEqual(1, rows.Count);
        Assert.AreSame(targetFirst ? existing : replacement, result.Row);
        Assert.AreEqual(1, result.Row.QueueCounter);
        CollectionAssert.AreEquivalent(new[] { QueueTrackReplacement.KeyOf(result.Row) }, keys.ToArray());
    }

    [TestMethod]
    public void SameTrackMetadataWithDifferentProviderIdsDoesNotMerge()
    {
        var original = Row(SourceIds.Tidal, "input", 0);
        var existing = Row(SourceIds.Spotify, "one", 1);
        var replacement = Row(SourceIds.Spotify, "two", 0);
        var rows = new List<QueueObject> { original, existing };
        var keys = rows.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        QueueTrackReplacement.Apply(rows, keys, 0, replacement);
        CollectionAssert.AreEqual(new[] { replacement, existing }, rows);
        Assert.AreEqual(2, keys.Count);
    }

    [TestMethod]
    public void RemovedInputDoesNotInsertOrScheduleAnOrphanedReplacement()
    {
        var rows = new List<QueueObject>();
        var keys = new HashSet<string>();
        Assert.IsNull(QueueTrackReplacement.Apply(rows, keys, -1, Row(SourceIds.Spotify, "target", 0)));
        Assert.IsNull(QueueTrackReplacement.Apply(rows, keys, 0, Row(SourceIds.Spotify, "target", 0)));
        Assert.AreEqual(0, rows.Count);
        Assert.AreEqual(0, keys.Count);
    }

    [TestMethod]
    public void ExpandedTrackKeepsItsPositionAndDoesNotCollideWithAnAlbumId()
    {
        var original = Row(SourceIds.Tidal, "input", 3);
        var album = new QueueObject(new AlbumSearchObject { Source = SourceIds.Spotify, Id = "target", TracksCount = 5 });
        var replacement = Row(SourceIds.Spotify, "target", 3);
        var rows = new List<QueueObject> { original, album };
        var keys = rows.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        var result = QueueTrackReplacement.Apply(rows, keys, 0, replacement)!;
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(3, result.Row.QueueCounter);
        Assert.AreEqual(2, keys.Count);
    }

    [TestMethod]
    public async Task SameKeyReplacementDoesNotDeleteItselfOrLoseCachedArtwork()
    {
        var existing = Row(SourceIds.Spotify, "same", 0);
        var rows = new List<QueueObject> { existing };
        var keys = rows.Select(QueueTrackReplacement.KeyOf).ToHashSet();
        await using var connection = await Database(rows);
        var key = QueueTrackReplacement.KeyOf(existing);
        await QueueItemPersistence.SetArtworkAsync(connection, key, existing.ImageLocation, [1, 2, 3]);
        var result = QueueTrackReplacement.Apply(rows, keys, 0, Row(SourceIds.Spotify, "same", 0))!;
        await QueueItemPersistence.ReplaceAsync(connection, result.RemovedHash, key, result.Row.ToString());
        Assert.AreEqual(1, (await Restore(connection)).Count);
        using var image = connection.CreateCommand();
        image.CommandText = "SELECT Image FROM Items";
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (byte[])(await image.ExecuteScalarAsync())!);
    }

    [TestMethod]
    public async Task AFailedReplacementRollsBackBothTheTargetSaveAndSourceRemoval()
    {
        var original = Row(SourceIds.Qobuz, "input", 0);
        await using var connection = await Database([original]);
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "CREATE TRIGGER reject_remove BEFORE DELETE ON Items BEGIN SELECT RAISE(ABORT, 'fixture failure'); END";
            await trigger.ExecuteNonQueryAsync();
        }
        var target = Row(SourceIds.Spotify, "target", 0);
        await Assert.ThrowsExceptionAsync<SqliteException>(() => QueueItemPersistence.ReplaceAsync(connection,
            QueueTrackReplacement.KeyOf(original), QueueTrackReplacement.KeyOf(target), target.ToString()));
        CollectionAssert.AreEqual(new[] { "input" }, (await Restore(connection)).Select(row => row.Id).ToArray());
    }

    private static QueueObject Row(string source, string id, int order) => new(new SongSearchObject
    {
        Source = source, Id = id, Title = "Same song", Artists = "Artist", Isrc = "USABC2400001",
        ImageLocation = "https://covers.example.test/shared.jpg", QueueCounter = order,
    });

    private static async Task<SqliteConnection> Database(IEnumerable<QueueObject> rows)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Items (Hash TEXT PRIMARY KEY, JsonText TEXT NOT NULL, Image BLOB, CreatedUtc TEXT NOT NULL)";
        await command.ExecuteNonQueryAsync();
        foreach (var row in rows) await QueueItemPersistence.SaveMetadataAsync(connection, QueueTrackReplacement.KeyOf(row), row.ToString());
        return connection;
    }

    private static async Task<List<SongSearchObject>> Restore(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JsonText FROM Items ORDER BY json_extract(JsonText, '$.QueueCounter')";
        using var reader = await command.ExecuteReaderAsync();
        var rows = new List<SongSearchObject>();
        while (await reader.ReadAsync()) rows.Add(QueueObject.ParseSaved(reader.GetString(0))!.Value.Song);
        return rows;
    }
}
