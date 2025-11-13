using AIChat.Server.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Tests.Services.EventStore;

/// <summary>
/// Test implementation of ISqliteConnectionFactory that creates fresh connections for each operation.
/// This resolves transaction isolation issues that occur when sharing a single connection across operations.
/// </summary>
internal sealed class TestSqliteConnectionFactory : ISqliteConnectionFactory, IDisposable
{
    private readonly string _connectionString;
    private readonly List<SqliteConnection> _openConnections = [];
    private bool _disposed;

    /// <summary>
    /// Gets the root connection (not used in test scenarios).
    /// </summary>
    public SqliteConnection? RootConnection => null;

    /// <summary>
    /// Initializes a new instance of the TestSqliteConnectionFactory class.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public TestSqliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Creates and opens a new SQLite connection.
    /// Each call returns a fresh connection to ensure proper transaction isolation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new, opened SQLite connection.</returns>
    public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Track connection for cleanup
        lock (_openConnections)
        {
            _openConnections.Add(connection);
        }

        return connection;
    }

    /// <summary>
    /// Disposes all tracked connections.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_openConnections)
        {
            foreach (var connection in _openConnections)
            {
                try
                {
                    connection?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors in tests
                }
            }
            _openConnections.Clear();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
