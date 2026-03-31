using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

public static class SchemaHelper
{
    public static async Task EnsurePragmasAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        // Detect existing schema and reset if mismatched (new project rule: nuke and recreate on mismatch)
        var needsReset = await NeedsSchemaResetAsync(connection, ct);

        using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            if (needsReset)
            {
                using (var drop = connection.CreateCommand())
                {
                    drop.Transaction = (SqliteTransaction)tx;
                    drop.CommandText = @"
DROP INDEX IF EXISTS idx_chat_tasks_chat_id;
DROP INDEX IF EXISTS idx_chats_user_updated;
DROP INDEX IF EXISTS ux_messages_chat_sequence;
DROP INDEX IF EXISTS idx_messages_chat_ts;
DROP TABLE IF EXISTS chat_tasks;
DROP TABLE IF EXISTS messages;
DROP TABLE IF EXISTS chats;";
                    await drop.ExecuteNonQueryAsync(ct);
                }
            }

            const string ddl = @"
CREATE TABLE IF NOT EXISTS chats (
  Id TEXT PRIMARY KEY,
  UserId TEXT NOT NULL,
  Title TEXT NOT NULL,
  CreatedAtUtc TEXT NOT NULL,
  UpdatedAtUtc TEXT NOT NULL,
  ChatJson TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_chats_user_updated ON chats (UserId, UpdatedAtUtc DESC);

CREATE TABLE IF NOT EXISTS messages (
  Id TEXT PRIMARY KEY,
  ChatId TEXT NOT NULL,
  Role TEXT NOT NULL,
  Kind TEXT NOT NULL,
  TimestampUtc TEXT NOT NULL,
  SequenceNumber INTEGER NOT NULL,
  MessageJson TEXT NOT NULL,
  FOREIGN KEY (ChatId) REFERENCES chats (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_chat_sequence ON messages (ChatId, SequenceNumber);
CREATE INDEX IF NOT EXISTS idx_messages_chat_ts ON messages (ChatId, TimestampUtc);

CREATE TABLE IF NOT EXISTS chat_tasks (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  ChatId TEXT NOT NULL,
  TaskData TEXT NOT NULL,
  Version INTEGER NOT NULL DEFAULT 1,
  CreatedAtUtc TEXT NOT NULL,
  UpdatedAtUtc TEXT NOT NULL,
  FOREIGN KEY (ChatId) REFERENCES chats (Id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_chat_tasks_chat_id ON chat_tasks (ChatId);";

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // Optionally set user_version for future migrations
            using (var ver = connection.CreateCommand())
            {
                ver.Transaction = (SqliteTransaction)tx;
                ver.CommandText = "PRAGMA user_version = 2;";
                await ver.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public static async Task SeedUsersAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        // Seed two known users if they do not exist
        const string systemUserId = "system-user-id";
        const string demoUserId = "user-123";

        const string createUsersTable = @"CREATE TABLE IF NOT EXISTS users (
  Id TEXT PRIMARY KEY,
  Email TEXT NOT NULL,
  Name TEXT NOT NULL,
  Provider TEXT NOT NULL,
  ProviderUserId TEXT NULL,
  ProfileImageUrl TEXT NULL,
  CreatedAt TEXT NOT NULL,
  UpdatedAt TEXT NOT NULL,
  UNIQUE (Email)
);";

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = createUsersTable;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await UpsertUserAsync(connection, systemUserId, "system@aichat.com", "System", "internal", null, null, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
        await UpsertUserAsync(connection, demoUserId, "demo@aichat.com", "Demo User", "demo", null, null, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), ct);
    }

    private static async Task UpsertUserAsync(
        SqliteConnection connection,
        string id,
        string email,
        string name,
        string provider,
        string? providerUserId,
        string? profileImageUrl,
        DateTime createdAtUtc,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO users (Id, Email, Name, Provider, ProviderUserId, ProfileImageUrl, CreatedAt, UpdatedAt)
VALUES ($id, $email, $name, $provider, $providerUserId, $profileImageUrl, $createdAt, $updatedAt)
ON CONFLICT(Email) DO NOTHING;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$email", email);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$provider", provider);
        cmd.Parameters.AddWithValue("$providerUserId", (object?)providerUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$profileImageUrl", (object?)profileImageUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", createdAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$updatedAt", createdAtUtc.ToString("o"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> NeedsSchemaResetAsync(SqliteConnection connection, CancellationToken ct)
    {
        // If no chats/messages tables, no reset needed
        var hasChats = await TableExistsAsync(connection, "chats", ct);
        var hasMessages = await TableExistsAsync(connection, "messages", ct);
        var hasChatTasks = await TableExistsAsync(connection, "chat_tasks", ct);
        if (!hasChats && !hasMessages && !hasChatTasks)
        {
            return false;
        }

        // Validate required columns for chats
        if (hasChats)
        {
            var cols = await GetColumnsAsync(connection, "chats", ct);
            if (!cols.Contains("UpdatedAtUtc") || !cols.Contains("CreatedAtUtc") || !cols.Contains("Title") || !cols.Contains("UserId"))
            {
                return true;
            }
        }

        // Validate required columns for messages
        if (hasMessages)
        {
            var cols = await GetColumnsAsync(connection, "messages", ct);
            var required = new[] { "ChatId", "Role", "Kind", "TimestampUtc", "SequenceNumber", "MessageJson" };
            foreach (var col in required)
            {
                if (!cols.Contains(col))
                {
                    return true;
                }
            }
        }

        // Validate required columns for chat_tasks
        if (hasChatTasks)
        {
            var cols = await GetColumnsAsync(connection, "chat_tasks", ct);
            var required = new[] { "ChatId", "TaskData", "Version", "CreatedAtUtc", "UpdatedAtUtc" };
            foreach (var col in required)
            {
                if (!cols.Contains(col))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string tableName, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            columns.Add(reader.GetString(1)); // name column
        }
        return columns;
    }
}



