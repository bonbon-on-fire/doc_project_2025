using AIChat.Server.Storage.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIChat.Server.Tests.Storage;

public class SchemaHelperTests
{
    private static SqliteConnection CreateInMemoryShared()
        => new SqliteConnection("Data Source=File:schematest?mode=memory&cache=shared");

    [Fact]
    public async Task Schema_Creation_Is_Idempotent()
    {
        await using var conn = CreateInMemoryShared();
        await conn.OpenAsync();
        await SchemaHelper.EnsurePragmasAsync(conn);
        await SchemaHelper.EnsureSchemaAsync(conn);
        await SchemaHelper.EnsureSchemaAsync(conn);

        // Validate tables exist
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('chats','messages') ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync();
        var names = Enumerable.Empty<string>().ToList();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        names.Should().Contain(new[] { "chats", "messages" });
    }

    [Fact]
    public async Task Users_Seeded_Once()
    {
        await using var conn = CreateInMemoryShared();
        await conn.OpenAsync();
        await SchemaHelper.EnsurePragmasAsync(conn);
        await SchemaHelper.EnsureSchemaAsync(conn);
        await SchemaHelper.SeedUsersAsync(conn);
        await SchemaHelper.SeedUsersAsync(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        count.Should().BeGreaterThanOrEqualTo(2);
        count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task InMemory_Shared_Persists_While_Root_Open()
    {
        var factory = new SqliteConnectionFactory("Data Source=File:schematest2?mode=memory&cache=shared", keepRootOpen: true);
        await TestDatabaseInitializer.InitializeAsync(factory);

        // Insert a row via a non-root connection
        await using (var conn = await factory.CreateOpenConnectionAsync())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO chats (Id, UserId, Title, CreatedAtUtc, UpdatedAtUtc) VALUES ('c1','user-123','t', '2025-01-01T00:00:00Z', '2025-01-01T00:00:00Z')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Read it back via another connection
        await using (var conn2 = await factory.CreateOpenConnectionAsync())
        {
            using var cmd2 = conn2.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM chats WHERE Id='c1'";
            var count = (long)(await cmd2.ExecuteScalarAsync() ?? 0L);
            count.Should().Be(1);
        }

        await factory.DisposeAsync();
    }
}


