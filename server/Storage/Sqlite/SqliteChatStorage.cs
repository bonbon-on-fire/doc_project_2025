using System.Text.Json;
using AIChat.Server.Services;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

public sealed class SqliteChatStorage : IChatStorage
{
    private readonly ISqliteConnectionFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = MessageSerializationOptions.Default;

    public SqliteChatStorage(ISqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<(bool Success, string? Error, ChatRecord? Chat)> CreateChatAsync(string userId, string title, DateTime createdAtUtc, DateTime updatedAtUtc, string? chatJson, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"INSERT INTO chats (Id, UserId, Title, CreatedAtUtc, UpdatedAtUtc, ChatJson)
VALUES ($id, $userId, $title, $createdAtUtc, $updatedAtUtc, $chatJson);";
        var id = Guid.NewGuid().ToString();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$createdAtUtc", createdAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$updatedAtUtc", updatedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$chatJson", (object?)chatJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var record = new ChatRecord
        {
            Id = id,
            UserId = userId,
            Title = title,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            ChatJson = chatJson
        };
        return (true, null, record);
    }

    public async Task<(bool Success, string? Error, ChatRecord? Chat)> GetChatByIdAsync(string chatId, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        const string sql = @"SELECT Id, UserId, Title, CreatedAtUtc, UpdatedAtUtc, ChatJson FROM chats WHERE Id=$id LIMIT 1";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", chatId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var chat = new ChatRecord
            {
                Id = reader.GetString(0),
                UserId = reader.GetString(1),
                Title = reader.GetString(2),
                CreatedAtUtc = DateTime.Parse(reader.GetString(3)),
                UpdatedAtUtc = DateTime.Parse(reader.GetString(4)),
                ChatJson = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
            return (true, null, chat);
        }
        return (false, "NotFound", null);
    }

    public async Task<(bool Success, string? Error, IReadOnlyList<ChatRecord> Chats, int TotalCount)> GetChatHistoryByUserAsync(string userId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        int offset = Math.Max(0, (page - 1) * pageSize);
        const string countSql = "SELECT COUNT(*) FROM chats WHERE UserId=$userId";
        const string pageSql = @"SELECT Id, UserId, Title, CreatedAtUtc, UpdatedAtUtc, ChatJson
FROM chats WHERE UserId=$userId ORDER BY UpdatedAtUtc DESC LIMIT $limit OFFSET $offset";

        int total;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = countSql;
            cmd.Parameters.AddWithValue("$userId", userId);
            total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        var list = new List<ChatRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pageSql;
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", offset);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new ChatRecord
                {
                    Id = reader.GetString(0),
                    UserId = reader.GetString(1),
                    Title = reader.GetString(2),
                    CreatedAtUtc = DateTime.Parse(reader.GetString(3)),
                    UpdatedAtUtc = DateTime.Parse(reader.GetString(4)),
                    ChatJson = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }
        }
        return (true, null, list, total);
    }

    public async Task<(bool Success, string? Error)> DeleteChatAsync(string chatId, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chats WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", chatId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return (rows > 0, rows > 0 ? null : "NotFound");
    }

    public async Task<(bool Success, string? Error)> UpdateChatUpdatedAtAsync(
        string chatId,
        DateTime updatedAtUtc,
        CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE chats SET UpdatedAtUtc=$u WHERE Id=$id";
        cmd.Parameters.AddWithValue("$u", updatedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", chatId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return (rows > 0, rows > 0 ? null : "NotFound");
    }

    public async Task<(bool Success, string? Error, int NextSequence)> AllocateSequenceAsync(
        string chatId,
        CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "SELECT IFNULL(MAX(SequenceNumber), -1) + 1 FROM messages WHERE ChatId=$chatId";
            cmd.Parameters.AddWithValue("$chatId", chatId);
            var next = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            await tx.CommitAsync(ct);
            return (true, null, next);
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(ct); } catch { }
            return (false, ex.Message, -1);
        }
    }

    public async Task<(bool Success, string? Error, MessageRecord? Message)> InsertMessageAsync(
        MessageRecord message,
        CancellationToken ct = default)
    {
        async Task<bool> TryInsertAsync(MessageRecord m)
        {
            await using var conn = await _factory.CreateOpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = @"INSERT INTO messages (Id, ChatId, Role, Kind, TimestampUtc, SequenceNumber, MessageJson)
VALUES ($id, $chatId, $role, $kind, $timestampUtc, $seq, $json)";
                cmd.Parameters.AddWithValue("$id", m.Id);
                cmd.Parameters.AddWithValue("$chatId", m.ChatId);
                cmd.Parameters.AddWithValue("$role", m.Role);
                cmd.Parameters.AddWithValue("$kind", m.Kind);
                cmd.Parameters.AddWithValue("$timestampUtc", m.TimestampUtc.ToString("o"));
                cmd.Parameters.AddWithValue("$seq", m.SequenceNumber);
                cmd.Parameters.AddWithValue("$json", m.MessageJson);
                await cmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19 /* constraint violation */)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                return false;
            }
        }

        // First attempt
        if (await TryInsertAsync(message))
        {
            return (true, null, message);
        }

        // Conflict: compute next sequence and retry once
        var alloc = await AllocateSequenceAsync(message.ChatId, ct);
        if (!alloc.Success)
        {
            return (false, alloc.Error, null);
        }
        var retried = new MessageRecord
        {
            Id = message.Id,
            ChatId = message.ChatId,
            Role = message.Role,
            Kind = message.Kind,
            TimestampUtc = message.TimestampUtc,
            SequenceNumber = alloc.NextSequence,
            MessageJson = message.MessageJson
        };
        if (await TryInsertAsync(retried))
        {
            return (true, null, retried);
        }
        return (false, "UniqueConflict", null);
    }

    public async Task<(bool Success, string? Error, IReadOnlyList<MessageRecord> Messages)> ListChatMessagesOrderedAsync(
        string chatId,
        CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, ChatId, Role, Kind, TimestampUtc, SequenceNumber, MessageJson FROM messages WHERE ChatId=$chatId ORDER BY SequenceNumber ASC";
        cmd.Parameters.AddWithValue("$chatId", chatId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MessageRecord>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new MessageRecord
            {
                Id = reader.GetString(0),
                ChatId = reader.GetString(1),
                Role = reader.GetString(2),
                Kind = reader.GetString(3),
                TimestampUtc = DateTime.Parse(reader.GetString(4)),
                SequenceNumber = reader.GetInt32(5),
                MessageJson = reader.GetString(6)
            });
        }
        return (true, null, list);
    }

    public async Task<(bool Success, string? Error, MessageRecord? Message)> GetMessageByIdAsync(
        string messageId,
        CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Id, ChatId, Role, Kind, TimestampUtc, SequenceNumber, MessageJson FROM messages WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", messageId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var m = new MessageRecord
            {
                Id = reader.GetString(0),
                ChatId = reader.GetString(1),
                Role = reader.GetString(2),
                Kind = reader.GetString(3),
                TimestampUtc = DateTime.Parse(reader.GetString(4)),
                SequenceNumber = reader.GetInt32(5),
                MessageJson = reader.GetString(6)
            };
            return (true, null, m);
        }
        return (false, "NotFound", null);
    }

    public async Task<(bool Success, string? Error, string? Content)> GetMessageContentAsync(string messageId, CancellationToken ct = default)
    {
        var res = await GetMessageByIdAsync(messageId, ct);
        if (!res.Success || res.Message == null)
        {
            return (false, res.Error ?? "NotFound", null);
        }
        try
        {
            var dto = JsonSerializer.Deserialize<AIChat.Server.Services.MessageDto>(res.Message.MessageJson, JsonOptions);
            if (dto is AIChat.Server.Services.TextMessageDto text)
            {
                return (true, null, text.Text);
            }
            if (dto is AIChat.Server.Services.ReasoningMessageDto reasoning)
            {
                return (true, null, reasoning.GetText());
            }

            // Fallback: parse raw JSON for common fields without relying on polymorphism
            using var doc = JsonDocument.Parse(res.Message.MessageJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                return (true, null, textProp.GetString());
            }
            if (root.TryGetProperty("Text", out var textProp2) && textProp2.ValueKind == JsonValueKind.String)
            {
                return (true, null, textProp2.GetString());
            }
            if (root.TryGetProperty("reasoning", out var reasProp) && reasProp.ValueKind == JsonValueKind.String)
            {
                // Check visibility if present
                if (root.TryGetProperty("visibility", out var vis) && vis.ValueKind == JsonValueKind.String)
                {
                    var visVal = vis.GetString();
                    if (string.Equals(visVal, "Encrypted", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, null, null);
                    }
                }
                return (true, null, reasProp.GetString());
            }
            if (root.TryGetProperty("Reasoning", out var reasProp2) && reasProp2.ValueKind == JsonValueKind.String)
            {
                return (true, null, reasProp2.GetString());
            }
            return (true, null, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error)> UpdateMessageJsonAsync(string messageId, string newMessageJson, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE messages SET MessageJson=$json WHERE Id=$id";
        cmd.Parameters.AddWithValue("$json", newMessageJson);
        cmd.Parameters.AddWithValue("$id", messageId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return (rows > 0, rows > 0 ? null : "NotFound");
    }
}


