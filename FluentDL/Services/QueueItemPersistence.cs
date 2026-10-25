using Microsoft.Data.Sqlite;

namespace FluentDL.Services;

internal static class QueueItemPersistence
{
    internal const string CoverExpression = "CASE WHEN json_valid(JsonText) THEN json_extract(JsonText, '$.ImageLocation') END";

    internal static async Task SaveMetadataAsync(SqliteConnection connection, string hash, string json, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO Items (Hash, JsonText, Image, CreatedUtc) VALUES (@h, @j, NULL, @t) "
            + $"ON CONFLICT(Hash) DO UPDATE SET JsonText=excluded.JsonText, Image=CASE WHEN ({CoverExpression})=json_extract(excluded.JsonText, '$.ImageLocation') THEN Items.Image ELSE NULL END";
        command.Parameters.AddWithValue("@h", hash);
        command.Parameters.AddWithValue("@j", json);
        command.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    internal static async Task ReplaceAsync(SqliteConnection connection, string? removedHash, string hash, string json)
    {
        using var transaction = connection.BeginTransaction();
        await SaveMetadataAsync(connection, hash, json, transaction);
        if (removedHash != null && removedHash != hash)
        {
            using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = "DELETE FROM Items WHERE Hash=@h";
            remove.Parameters.AddWithValue("@h", removedHash);
            await remove.ExecuteNonQueryAsync();
        }
        transaction.Commit();
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
}
