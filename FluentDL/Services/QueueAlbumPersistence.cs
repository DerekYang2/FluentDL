using Microsoft.Data.Sqlite;

namespace FluentDL.Services;

internal static class QueueAlbumPersistence
{
    internal static async Task ReplaceAsync(SqliteConnection connection, string albumHash, IReadOnlyList<(string Hash, string Json)> tracks)
    {
        if (tracks.Any(track => track.Hash == albumHash)) throw new ArgumentException("An album cannot replace itself.", nameof(tracks));
        using var transaction = connection.BeginTransaction();
        byte[]? cover;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Image FROM Items WHERE Hash = @album";
            select.Parameters.AddWithValue("@album", albumHash);
            await using var reader = await select.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The saved album is no longer in the queue.");
            cover = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
        }
        foreach (var track in tracks)
        {
            using var save = connection.CreateCommand();
            save.Transaction = transaction;
            save.CommandText = "INSERT INTO Items (Hash, JsonText, Image, CreatedUtc) VALUES (@h, @j, @b, @t) "
                + "ON CONFLICT(Hash) DO UPDATE SET JsonText = excluded.JsonText, Image = COALESCE(Items.Image, excluded.Image)";
            save.Parameters.AddWithValue("@h", track.Hash);
            save.Parameters.AddWithValue("@j", track.Json);
            save.Parameters.Add("@b", SqliteType.Blob).Value = cover ?? (object)DBNull.Value;
            save.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
            await save.ExecuteNonQueryAsync();
        }
        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM Items WHERE Hash = @album";
        remove.Parameters.AddWithValue("@album", albumHash);
        await remove.ExecuteNonQueryAsync();
        transaction.Commit();
    }
}
