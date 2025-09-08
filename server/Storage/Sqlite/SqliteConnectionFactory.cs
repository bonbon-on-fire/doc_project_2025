using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

public interface ISqliteConnectionFactory
{
    ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
    SqliteConnection? RootConnection { get; }
}

public sealed class SqliteConnectionFactory(string connectionString, bool keepRootOpen)
    : ISqliteConnectionFactory,
        IAsyncDisposable
{
    public SqliteConnection? RootConnection { get; private set; }

    public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(
        CancellationToken ct = default
    )
    {
        if (keepRootOpen)
        {
            // In shared cache in-memory mode, keep one root open and return new pooled connections
            if (RootConnection == null)
            {
                RootConnection = new SqliteConnection(connectionString);
                await RootConnection.OpenAsync(ct);
                await SchemaHelper.EnsurePragmasAsync(RootConnection, ct);
                await SchemaHelper.EnsureSchemaAsync(RootConnection, ct);
            }
        }

        var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await SchemaHelper.EnsurePragmasAsync(conn, ct);
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (RootConnection != null)
        {
            await RootConnection.DisposeAsync();
            RootConnection = null;
        }
    }
}
