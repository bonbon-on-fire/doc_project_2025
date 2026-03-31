using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

public static class TestDatabaseInitializer
{
    public static async Task InitializeAsync(SqliteConnectionFactory factory, CancellationToken ct = default)
    {
        // Ensure root connection exists and schema is recreated for a clean state
        var root = await factory.CreateOpenConnectionAsync(ct);
        // Explicitly drop and recreate schema for tests
        using (var dropCmd = root.CreateCommand())
        {
            dropCmd.CommandText = @"DROP TABLE IF EXISTS messages; DROP TABLE IF EXISTS chats;";
            await dropCmd.ExecuteNonQueryAsync(ct);
        }

        await SchemaHelper.EnsureSchemaAsync((SqliteConnection)root, ct);
        await SchemaHelper.SeedUsersAsync((SqliteConnection)root, ct);
    }
}


