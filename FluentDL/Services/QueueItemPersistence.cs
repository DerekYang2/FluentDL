using Microsoft.Data.Sqlite;

namespace FluentDL.Services;

/// <summary>All queue-row writes. Each public method is one transaction or one statement.</summary>
internal static class QueueItemPersistence
{
    internal const string CoverExpression = "CASE WHEN json_valid(JsonText) THEN json_extract(JsonText, '$.ImageLocation') END";
    private const string CounterExpression = "CASE WHEN json_valid(JsonText) THEN json_extract(JsonText, '$.QueueCounter') END";

    internal static Task SaveMetadataAsync(SqliteConnection connection, string hash, string json) =>
        UpsertAsync(connection, null, hash, json, keepImage: false);

    /// <summary>Upserts visible rows. An incomplete restore is not a deletion list, so rows absent from it stay.</summary>
    internal static async Task SaveSnapshotAsync(SqliteConnection connection, IReadOnlyList<(string Hash, string Json)> snapshot)
    {
        using var transaction = connection.BeginTransaction();
        // A restored model can normalize a missing cover URL to the placeholder, so keep the saved image.
        foreach (var item in snapshot) await UpsertAsync(connection, transaction, item.Hash, item.Json, keepImage: true);
        transaction.Commit();
    }

    internal static async Task ReplaceAsync(SqliteConnection connection, string? removedHash, string hash, string json)
    {
        using var transaction = connection.BeginTransaction();
        await UpsertAsync(connection, transaction, hash, json, keepImage: false);
        if (removedHash != null && removedHash != hash) await DeleteAsync(connection, transaction, removedHash);
        transaction.Commit();
    }

    /// <summary>
    /// Replaces a saved album with its tracks, which already have consecutive positions starting at the album's. Rows
    /// after the album move down by <paramref name="shift"/> so every row keeps its own position.
    /// </summary>
    internal static async Task ReplaceAlbumAsync(SqliteConnection connection, string albumHash, int albumPosition, int shift,
        IReadOnlyList<(string Hash, string Json)> tracks)
    {
        if (tracks.Any(track => track.Hash == albumHash)) throw new ArgumentException("An album cannot replace itself.", nameof(tracks));
        using var transaction = connection.BeginTransaction();
        byte[]? cover;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Image FROM Items WHERE Hash=@album";
            select.Parameters.AddWithValue("@album", albumHash);
            await using var reader = await select.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The saved album is no longer in the queue.");
            cover = reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0);
        }
        if (shift > 0)
        {
            using var move = connection.CreateCommand();
            move.Transaction = transaction;
            move.CommandText = $"UPDATE Items SET JsonText=json_set(JsonText, '$.QueueCounter', ({CounterExpression}) + @shift) "
                + $"WHERE Hash<>@album AND ({CounterExpression}) > @position";
            move.Parameters.AddWithValue("@shift", shift);
            move.Parameters.AddWithValue("@album", albumHash);
            move.Parameters.AddWithValue("@position", albumPosition);
            await move.ExecuteNonQueryAsync();
        }
        foreach (var track in tracks) await UpsertAsync(connection, transaction, track.Hash, track.Json, keepImage: true, cover);
        await DeleteAsync(connection, transaction, albumHash);
        transaction.Commit();
    }

    internal static Task RemoveAsync(SqliteConnection connection, string hash) => DeleteAsync(connection, null, hash);

    internal static async Task ClearAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Items";
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task SetArtworkAsync(SqliteConnection connection, string hash, string url, byte[]? bytes)
    {
        using var command = connection.CreateCommand();
        command.CommandText = bytes != null
            ? $"UPDATE Items SET Image=@b WHERE Hash=@h AND ({CoverExpression})=@u"
            : $"UPDATE Items SET Image=(SELECT Image FROM Items WHERE Image IS NOT NULL AND ({CoverExpression})=@u LIMIT 1) "
                + $"WHERE Hash=@h AND Image IS NULL AND ({CoverExpression})=@u";
        command.Parameters.AddWithValue("@h", hash);
        command.Parameters.AddWithValue("@u", url);
        if (bytes != null) command.Parameters.AddWithValue("@b", bytes);
        await command.ExecuteNonQueryAsync();
    }

    // keepImage keeps an existing image, or uses image for a new row. Otherwise an image survives only while its cover URL is unchanged.
    private static async Task UpsertAsync(SqliteConnection connection, SqliteTransaction? transaction, string hash, string json,
        bool keepImage, byte[]? image = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Items (Hash, JsonText, Image, CreatedUtc) VALUES (@h, @j, @b, @t) "
            + "ON CONFLICT(Hash) DO UPDATE SET JsonText=excluded.JsonText, Image="
            + (keepImage ? "COALESCE(Items.Image, excluded.Image)"
                : $"CASE WHEN ({CoverExpression}) IS json_extract(excluded.JsonText, '$.ImageLocation') THEN Items.Image ELSE NULL END");
        command.Parameters.AddWithValue("@h", hash);
        command.Parameters.AddWithValue("@j", json);
        command.Parameters.Add("@b", SqliteType.Blob).Value = image ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteAsync(SqliteConnection connection, SqliteTransaction? transaction, string hash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Items WHERE Hash=@h";
        command.Parameters.AddWithValue("@h", hash);
        await command.ExecuteNonQueryAsync();
    }
}
