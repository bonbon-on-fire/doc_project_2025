using AIChat.Server.Storage.Sqlite;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests.Storage;

public class SqliteConnectionFactoryTests
{
    [Fact]
    public async Task RootConnectionIsHeldWhenKeepRootOpenTrue()
    {
        var factory = new SqliteConnectionFactory(
            "Data Source=:memory:;Cache=Shared",
            keepRootOpen: true
        );
        var conn = await factory.CreateOpenConnectionAsync();
        _ = factory.RootConnection.Should().NotBeNull();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task ConnectionsOpenWithPragmaForeignKeysOn()
    {
        var factory = new SqliteConnectionFactory(
            "Data Source=:memory:;Cache=Shared",
            keepRootOpen: true
        );
        var conn = await factory.CreateOpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys";
        var val = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        _ = val.Should().Be(1);
        await factory.DisposeAsync();
    }
}
