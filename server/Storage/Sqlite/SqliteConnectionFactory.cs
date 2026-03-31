using Microsoft.Data.Sqlite;

namespace AIChat.Server.Storage.Sqlite;

public interface ISqliteConnectionFactory
{
    ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
    SqliteConnection? RootConnection { get; }
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly bool _useRootConnection;
    private SqliteConnection? _rootConnection;

    public SqliteConnectionFactory(string connectionString, bool keepRootOpen)
    {
        _connectionString = connectionString;
        _useRootConnection = keepRootOpen;
    }

    public SqliteConnection? RootConnection => _rootConnection;

    public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        if (_useRootConnection)
        {
            // In shared cache in-memory mode, keep one root open and return new pooled connections
            if (_rootConnection == null)
            {
                _rootConnection = new SqliteConnection(_connectionString);
                await _rootConnection.OpenAsync(ct);
                await SchemaHelper.EnsurePragmasAsync(_rootConnection, ct);
                await SchemaHelper.EnsureSchemaAsync(_rootConnection, ct);
            }
        }

        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await SchemaHelper.EnsurePragmasAsync(conn, ct);
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_rootConnection != null)
        {
            await _rootConnection.DisposeAsync();
            _rootConnection = null;
        }
    }
}



