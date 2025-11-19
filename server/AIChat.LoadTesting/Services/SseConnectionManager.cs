using System.Collections.Concurrent;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;


/// <summary>
/// Implementation of SSE connection manager.
/// Manages the lifecycle of multiple SSE connections with proper resource management.
/// </summary>
public class SseConnectionManager : ISseConnectionManager
{
    private readonly ConcurrentDictionary<string, ISseConnection> _connections = new();
    private readonly ISseConnectionFactory _connectionFactory;
    private readonly IOptions<LoadTestingConfiguration> _loadTestConfig;
    private readonly ILogger<SseConnectionManager> _logger;
    private bool _disposed;

    // Connection events
    public event EventHandler<ConnectionEventArgs>? ConnectionEstablished;
    public event EventHandler<ConnectionEventArgs>? ConnectionClosed;
    public event EventHandler<ConnectionErrorEventArgs>? ConnectionError;

    public int ActiveConnectionCount => _connections.Count(c => c.Value.IsConnected);
    public int TotalConnectionCount => _connections.Count;

    public SseConnectionManager(
        ISseConnectionFactory connectionFactory,
        IOptions<LoadTestingConfiguration> loadTestConfig,
        ILogger<SseConnectionManager> logger
    )
    {
        _connectionFactory = connectionFactory;
        _loadTestConfig = loadTestConfig;
        _logger = logger;
    }

    public async Task<ISseConnection> CreateConnectionAsync(
        string connectionId,
        string endpoint,
        CancellationToken cancellationToken = default
    )
    {
        if (_connections.ContainsKey(connectionId))
        {
            throw new InvalidOperationException(
                $"Connection with ID {connectionId} already exists"
            );
        }

        var fullUrl = _loadTestConfig.Value.ServerBaseUrl.TrimEnd('/') + endpoint;
        var connection = _connectionFactory.CreateConnection(connectionId, fullUrl);

        if (!_connections.TryAdd(connectionId, connection))
        {
            throw new InvalidOperationException($"Failed to add connection {connectionId}");
        }

        try
        {
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

            // Raise connection established event
            ConnectionEstablished?.Invoke(this, new ConnectionEventArgs(connectionId, connection));

            _logger.LogInformation(
                "Created SSE connection {ConnectionId}. Total active: {ActiveCount}",
                connectionId,
                ActiveConnectionCount
            );
        }
        catch (Exception ex)
        {
            _ = _connections.TryRemove(connectionId, out var failedConnection);

            // Raise connection error event
            if (failedConnection != null)
            {
                ConnectionError?.Invoke(this, new ConnectionErrorEventArgs(
                    connectionId, failedConnection, ex, false));
            }

            _logger.LogError(ex, "Failed to create SSE connection {ConnectionId}", connectionId);
            throw;
        }

        return connection;
    }

    public async Task CloseConnectionAsync(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            await connection.DisconnectAsync().ConfigureAwait(false);
            if (connection is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                connection.Dispose();
            }

            // Raise connection closed event
            ConnectionClosed?.Invoke(this, new ConnectionEventArgs(connectionId, connection));

            _logger.LogInformation(
                "Closed SSE connection {ConnectionId}. Remaining active: {ActiveCount}",
                connectionId,
                ActiveConnectionCount
            );
        }
    }

    public async Task CloseAllConnectionsAsync()
    {
        _logger.LogInformation(
            "Closing all {Count} SSE connections",
            _connections.Count
        );

        var tasks = _connections.Select(kvp => CloseConnectionAsync(kvp.Key));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        _connections.Clear();
    }

    public ISseConnection? GetConnection(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    public IEnumerable<ISseConnection> GetAllConnections()
    {
        return _connections.Values;
    }

    public IEnumerable<ISseConnection> GetConnections(Func<ISseConnection, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _connections.Values.Where(predicate);
    }

    public bool ConnectionExists(string connectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        return _connections.ContainsKey(connectionId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CloseAllConnectionsAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
