using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

/// <summary>
/// SQLite implementation of mode storage for persisting user-created custom modes.
/// Handles all database operations for mode CRUD operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the SqliteModeStorage class.
/// </remarks>
/// <param name="factory">Factory for creating SQLite connections</param>
public sealed class SqliteModeStorage(ISqliteConnectionFactory factory) : IModeStorage
{
    public async Task<(bool Success, string? Error, ModeRecord? Mode)> CreateModeAsync(
        ModeRecord mode,
        CancellationToken ct = default
    )
    {
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(ct);
            const string sql =
                @"INSERT INTO user_modes (Id, UserId, Name, Description, Prompt, Tools, DefaultModel, Category, CreatedAtUtc, UpdatedAtUtc)
VALUES ($id, $userId, $name, $description, $prompt, $tools, $defaultModel, $category, $createdAtUtc, $updatedAtUtc);";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$id", mode.Id);
            _ = cmd.Parameters.AddWithValue("$userId", mode.UserId);
            _ = cmd.Parameters.AddWithValue("$name", mode.Name);
            _ = cmd.Parameters.AddWithValue("$description", mode.Description);
            _ = cmd.Parameters.AddWithValue("$prompt", mode.Prompt);
            _ = cmd.Parameters.AddWithValue("$tools", mode.Tools);
            _ = cmd.Parameters.AddWithValue(
                "$defaultModel",
                (object?)mode.DefaultModel ?? DBNull.Value
            );
            _ = cmd.Parameters.AddWithValue("$category", mode.Category);
            _ = cmd.Parameters.AddWithValue("$createdAtUtc", mode.CreatedAtUtc.ToString("o"));
            _ = cmd.Parameters.AddWithValue("$updatedAtUtc", mode.UpdatedAtUtc.ToString("o"));

            _ = await cmd.ExecuteNonQueryAsync(ct);
            return (true, null, mode);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            return (false, "Mode with this ID already exists", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error, ModeRecord? Mode)> GetModeByIdAsync(
        string modeId,
        string userId,
        CancellationToken ct = default
    )
    {
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(ct);
            const string sql =
                @"SELECT Id, UserId, Name, Description, Prompt, Tools, DefaultModel, Category, CreatedAtUtc, UpdatedAtUtc
FROM user_modes WHERE Id=$id AND UserId=$userId LIMIT 1";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$id", modeId);
            _ = cmd.Parameters.AddWithValue("$userId", userId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var mode = new ModeRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Description = reader.GetString(3),
                    Prompt = reader.GetString(4),
                    Tools = reader.GetString(5),
                    DefaultModel = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Category = reader.GetString(7),
                    CreatedAtUtc = DateTime.Parse(reader.GetString(8)),
                    UpdatedAtUtc = DateTime.Parse(reader.GetString(9)),
                };
                return (true, null, mode);
            }
            return (false, "NotFound", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(
        bool Success,
        string? Error,
        IReadOnlyList<ModeRecord> Modes
    )> GetModesByUserAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(ct);
            const string sql =
                @"SELECT Id, UserId, Name, Description, Prompt, Tools, DefaultModel, Category, CreatedAtUtc, UpdatedAtUtc
FROM user_modes WHERE UserId=$userId ORDER BY CreatedAtUtc DESC";

            var modes = new List<ModeRecord>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$userId", userId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                modes.Add(
                    new ModeRecord
                    {
                        Id = reader.GetString(0),
                        UserId = reader.GetString(1),
                        Name = reader.GetString(2),
                        Description = reader.GetString(3),
                        Prompt = reader.GetString(4),
                        Tools = reader.GetString(5),
                        DefaultModel = reader.IsDBNull(6) ? null : reader.GetString(6),
                        Category = reader.GetString(7),
                        CreatedAtUtc = DateTime.Parse(reader.GetString(8)),
                        UpdatedAtUtc = DateTime.Parse(reader.GetString(9)),
                    }
                );
            }
            return (true, null, modes);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, new List<ModeRecord>());
        }
    }

    public async Task<(bool Success, string? Error, ModeRecord? Mode)> UpdateModeAsync(
        string modeId,
        string userId,
        ModeRecord updatedMode,
        CancellationToken ct = default
    )
    {
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(ct);
            const string sql =
                @"UPDATE user_modes
SET Name=$name, Description=$description, Prompt=$prompt, Tools=$tools, DefaultModel=$defaultModel, Category=$category, UpdatedAtUtc=$updatedAtUtc
WHERE Id=$id AND UserId=$userId";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$id", modeId);
            _ = cmd.Parameters.AddWithValue("$userId", userId);
            _ = cmd.Parameters.AddWithValue("$name", updatedMode.Name);
            _ = cmd.Parameters.AddWithValue("$description", updatedMode.Description);
            _ = cmd.Parameters.AddWithValue("$prompt", updatedMode.Prompt);
            _ = cmd.Parameters.AddWithValue("$tools", updatedMode.Tools);
            _ = cmd.Parameters.AddWithValue(
                "$defaultModel",
                (object?)updatedMode.DefaultModel ?? DBNull.Value
            );
            _ = cmd.Parameters.AddWithValue("$category", updatedMode.Category);
            _ = cmd.Parameters.AddWithValue(
                "$updatedAtUtc",
                updatedMode.UpdatedAtUtc.ToString("o")
            );

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            if (rows > 0)
            {
                return (true, null, updatedMode);
            }
            return (false, "NotFound", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error)> DeleteModeAsync(
        string modeId,
        string userId,
        CancellationToken ct = default
    )
    {
        try
        {
            await using var conn = await factory.CreateOpenConnectionAsync(ct);
            const string sql = "DELETE FROM user_modes WHERE Id=$id AND UserId=$userId";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            _ = cmd.Parameters.AddWithValue("$id", modeId);
            _ = cmd.Parameters.AddWithValue("$userId", userId);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return (rows > 0, rows > 0 ? null : "NotFound");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
