using AIChat.LoadTesting.Services.Interfaces;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Manages SSE (Server-Sent Events) connections for load testing.
/// Handles connection lifecycle, tracking, and coordination.
/// </summary>
public interface ISseConnectionManager : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Creates a new SSE connection.
    /// </summary>
    /// <param name="connectionId">Unique identifier for the connection</param>
    /// <param name="endpoint">SSE endpoint URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SSE connection instance</returns>
    Task<ISseConnection> CreateConnectionAsync(
        string connectionId,
        string endpoint,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Closes a specific SSE connection.
    /// </summary>
    /// <param name="connectionId">Connection identifier to close</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task CloseConnectionAsync(string connectionId);

    /// <summary>
    /// Closes all active SSE connections.
    /// </summary>
    /// <returns>Task representing the asynchronous operation</returns>
    Task CloseAllConnectionsAsync();

    /// <summary>
    /// Gets the count of active connections.
    /// </summary>
    int ActiveConnectionCount { get; }

    /// <summary>
    /// Gets the total count of all connections (active and inactive).
    /// </summary>
    int TotalConnectionCount { get; }

    /// <summary>
    /// Gets a specific connection by ID.
    /// </summary>
    /// <param name="connectionId">Connection identifier</param>
    /// <returns>SSE connection if found, null otherwise</returns>
    ISseConnection? GetConnection(string connectionId);

    /// <summary>
    /// Gets all active connections.
    /// </summary>
    /// <returns>Collection of active connections</returns>
    IEnumerable<ISseConnection> GetAllConnections();

    /// <summary>
    /// Gets all connections matching the specified predicate.
    /// </summary>
    /// <param name="predicate">Filter predicate</param>
    /// <returns>Collection of matching connections</returns>
    IEnumerable<ISseConnection> GetConnections(Func<ISseConnection, bool> predicate);

    /// <summary>
    /// Checks if a connection with the specified ID exists.
    /// </summary>
    /// <param name="connectionId">Connection identifier</param>
    /// <returns>True if the connection exists; otherwise, false</returns>
    bool ConnectionExists(string connectionId);

    /// <summary>
    /// Event raised when a connection is established.
    /// </summary>
    event EventHandler<ConnectionEventArgs>? ConnectionEstablished;

    /// <summary>
    /// Event raised when a connection is closed.
    /// </summary>
    event EventHandler<ConnectionEventArgs>? ConnectionClosed;

    /// <summary>
    /// Event raised when a connection encounters an error.
    /// </summary>
    event EventHandler<ConnectionErrorEventArgs>? ConnectionError;
}

/// <summary>
/// Event arguments for connection events.
/// </summary>
public class ConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the connection ID.
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// Gets the connection instance.
    /// </summary>
    public ISseConnection Connection { get; }

    /// <summary>
    /// Gets the timestamp of the event.
    /// </summary>
    public DateTime Timestamp { get; }

    public ConnectionEventArgs(string connectionId, ISseConnection connection)
    {
        ConnectionId = connectionId;
        Connection = connection;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments for connection error events.
/// </summary>
public class ConnectionErrorEventArgs : ConnectionEventArgs
{
    /// <summary>
    /// Gets the error that occurred.
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    /// Gets whether the error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; }

    public ConnectionErrorEventArgs(
        string connectionId,
        ISseConnection connection,
        Exception error,
        bool isRecoverable = true)
        : base(connectionId, connection)
    {
        Error = error;
        IsRecoverable = isRecoverable;
    }
}