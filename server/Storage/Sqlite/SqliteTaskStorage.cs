using AchieveAi.LmDotnetTools.Misc.Utils;

namespace AIChat.Server.Storage.Sqlite;

public sealed class SqliteTaskStorage : ITaskStorage
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteTaskStorage(ISqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(TaskManager, int, DateTime)?> GetTasksManagerAsync(string chatId, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"
            SELECT ChatId, TaskData, Version, UpdatedAtUtc 
            FROM chat_tasks 
            WHERE ChatId = $chatId 
            LIMIT 1";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$chatId", chatId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var taskDataJson = reader.GetString(1);
            return (TaskManager.DeserializeTasks(taskDataJson), reader.GetInt32(2), reader.GetDateTime(3));
        }

        return null;
    }

    public async Task<ChatTaskState?> GetTasksAsync(string chatId, CancellationToken ct = default)
    {
        var tup = await GetTasksManagerAsync(chatId, ct);
        if (tup != null)
        {
            var (taskManager, version, lastUpdated) = tup.Value;
            return new ChatTaskState
            {
                ChatId = chatId,
                TaskManager = taskManager,
                Version = version,
                LastUpdatedUtc = lastUpdated
            };
        }

        return null;
    }

    public async Task<ChatTaskState> SaveTasksAsync(string chatId, TaskManager taskManager, int expectedVersion, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var taskDataJson = taskManager.JsonSerializeTasks();

        // First, try to update existing record with version check
        const string updateSql = @"
            UPDATE chat_tasks 
            SET TaskData = $taskData, 
                Version = Version + 1, 
                UpdatedAtUtc = $updatedAt
            WHERE ChatId = $chatId AND Version = $expectedVersion";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = updateSql;
            cmd.Parameters.AddWithValue("$taskData", taskDataJson);
            cmd.Parameters.AddWithValue("$updatedAt", nowUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$chatId", chatId);
            cmd.Parameters.AddWithValue("$expectedVersion", expectedVersion);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

            if (rowsAffected > 0)
            {
                // Update succeeded, return new state
                return new ChatTaskState
                {
                    ChatId = chatId,
                    TaskManager = taskManager,
                    Version = expectedVersion + 1,
                    LastUpdatedUtc = nowUtc
                };
            }
        }

        // Update failed, check if it's because record doesn't exist or version conflict
        const string checkSql = @"
            SELECT Version 
            FROM chat_tasks 
            WHERE ChatId = $chatId 
            LIMIT 1";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = checkSql;
            cmd.Parameters.AddWithValue("$chatId", chatId);

            var result = await cmd.ExecuteScalarAsync(ct);

            if (result != null)
            {
                // Record exists but version doesn't match
                var currentVersion = Convert.ToInt32(result);
                throw new InvalidOperationException($"Version conflict: expected {expectedVersion}, but current version is {currentVersion}");
            }
        }

        // Record doesn't exist, insert new one (only if expectedVersion is 0)
        if (expectedVersion != 0)
        {
            throw new InvalidOperationException($"Version conflict: expected version {expectedVersion}, but no tasks exist for this chat");
        }

        const string insertSql = @"
            INSERT INTO chat_tasks (ChatId, TaskData, Version, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($chatId, $taskData, 1, $createdAt, $updatedAt)";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = insertSql;
            cmd.Parameters.AddWithValue("$chatId", chatId);
            cmd.Parameters.AddWithValue("$taskData", taskDataJson);
            cmd.Parameters.AddWithValue("$createdAt", nowUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$updatedAt", nowUtc.ToString("o"));

            await cmd.ExecuteNonQueryAsync(ct);
        }

        return new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = taskManager,
            Version = 1,
            LastUpdatedUtc = nowUtc
        };
    }

    public async Task DeleteTasksAsync(string chatId, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = "DELETE FROM chat_tasks WHERE ChatId = $chatId";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$chatId", chatId);
        
        await cmd.ExecuteNonQueryAsync(ct);
    }
}