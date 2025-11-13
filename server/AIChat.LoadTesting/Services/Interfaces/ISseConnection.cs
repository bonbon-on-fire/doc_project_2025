using System.Threading.Channels;
using AIChat.LoadTesting.Models;

namespace AIChat.LoadTesting.Services.Interfaces;

/// <summary>
/// Interface for SSE (Server-Sent Events) connections.
/// Provides abstraction for connection lifecycle and message handling.
/// </summary>
public interface ISseConnection : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this connection.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets the SSE endpoint URL.
    /// </summary>
    string Endpoint { get; }

    /// <summary>
    /// Gets the timestamp when the connection was established.
    /// </summary>
    DateTime ConnectedAt { get; }

    /// <summary>
    /// Gets the timestamp when the connection was closed, if applicable.
    /// </summary>
    DateTime? DisconnectedAt { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the total number of messages received.
    /// </summary>
    int MessageCount { get; }

    /// <summary>
    /// Gets the total bytes received through this connection.
    /// </summary>
    long BytesReceived { get; }

    /// <summary>
    /// Gets the number of chunks received.
    /// </summary>
    int ChunksReceived { get; }

    /// <summary>
    /// Gets the number of reconnection attempts made.
    /// </summary>
    int ReconnectAttempts { get; }

    /// <summary>
    /// Gets the channel reader for consuming SSE messages.
    /// </summary>
    ChannelReader<SseMessage> Messages { get; }

    /// <summary>
    /// Establishes the SSE connection and starts receiving messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the asynchronous connection operation.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the SSE connection gracefully.
    /// </summary>
    /// <returns>Task representing the asynchronous disconnection operation.</returns>
    Task DisconnectAsync();

    /// <summary>
    /// Gets the current metrics for this connection.
    /// </summary>
    /// <returns>Connection metrics snapshot.</returns>
    SseConnectionMetrics GetMetrics();
}

/// <summary>
/// Factory interface for creating SSE connections.
/// </summary>
public interface ISseConnectionFactory
{
    /// <summary>
    /// Creates a new SSE connection instance.
    /// </summary>
    /// <param name="connectionId">Unique identifier for the connection.</param>
    /// <param name="endpoint">SSE endpoint URL.</param>
    /// <returns>New SSE connection instance.</returns>
    ISseConnection CreateConnection(string connectionId, string endpoint);
}

/// <summary>
/// Interface for SSE reconnection strategy.
/// </summary>
public interface ISseReconnectionStrategy
{
    /// <summary>
    /// Determines if reconnection should be attempted.
    /// </summary>
    /// <param name="attemptNumber">Current reconnection attempt number.</param>
    /// <param name="lastError">The last error that caused disconnection.</param>
    /// <returns>True if reconnection should be attempted; otherwise, false.</returns>
    bool ShouldReconnect(int attemptNumber, Exception? lastError);

    /// <summary>
    /// Gets the delay before the next reconnection attempt.
    /// </summary>
    /// <param name="attemptNumber">Current reconnection attempt number.</param>
    /// <returns>Delay duration before next attempt.</returns>
    TimeSpan GetReconnectDelay(int attemptNumber);

    /// <summary>
    /// Handles reconnection logic for an SSE connection.
    /// </summary>
    /// <param name="connection">The connection to reconnect.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the asynchronous reconnection operation.</returns>
    Task HandleReconnectionAsync(ISseConnection connection, CancellationToken cancellationToken);
}

/// <summary>
/// Interface for SSE message processing.
/// </summary>
public interface ISseMessageProcessor
{
    /// <summary>
    /// Processes an incoming SSE message.
    /// </summary>
    /// <param name="message">The SSE message to process.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the asynchronous processing operation.</returns>
    Task ProcessMessageAsync(SseMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Processes a batch of SSE messages.
    /// </summary>
    /// <param name="messages">Collection of SSE messages to process.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Task representing the asynchronous batch processing operation.</returns>
    Task ProcessBatchAsync(IEnumerable<SseMessage> messages, CancellationToken cancellationToken);
}

/// <summary>
/// Interface for SSE buffer management.
/// </summary>
public interface ISseBufferManager
{
    /// <summary>
    /// Gets the current buffer size in bytes.
    /// </summary>
    int CurrentBufferSize { get; }

    /// <summary>
    /// Gets the maximum buffer capacity in bytes.
    /// </summary>
    int BufferCapacity { get; }

    /// <summary>
    /// Gets the buffer utilization percentage.
    /// </summary>
    double UtilizationPercentage { get; }

    /// <summary>
    /// Adds a message to the buffer.
    /// </summary>
    /// <param name="message">The message to buffer.</param>
    /// <returns>True if the message was buffered; false if the buffer is full.</returns>
    bool TryAddToBuffer(SseMessage message);

    /// <summary>
    /// Drains messages from the buffer.
    /// </summary>
    /// <param name="count">Number of messages to drain. If not specified, drains all.</param>
    /// <returns>Collection of drained messages.</returns>
    IEnumerable<SseMessage> DrainBuffer(int? count = null);

    /// <summary>
    /// Clears all messages from the buffer.
    /// </summary>
    void ClearBuffer();
}

/// <summary>
/// Interface for HTTP stream client operations.
/// </summary>
public interface IHttpStreamClient : IDisposable
{
    /// <summary>
    /// Opens a stream to the specified URL for SSE.
    /// </summary>
    /// <param name="url">The URL to connect to.</param>
    /// <param name="headers">Optional headers to include in the request.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Stream reader for the SSE stream.</returns>
    Task<StreamReader> OpenStreamAsync(
        string url,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the timeout for the HTTP client.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    void SetTimeout(TimeSpan timeout);
}
