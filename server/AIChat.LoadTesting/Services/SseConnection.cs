using System.Threading.Channels;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Represents a single SSE connection with proper separation of concerns.
/// Delegates protocol handling, metrics recording, and HTTP operations to specialized services.
/// </summary>
public class SseConnection : ISseConnection
{
    private readonly IHttpStreamClient _httpStreamClient;
    private readonly ISseProtocolHandler _protocolHandler;
    private readonly ISseMetricsRecorder _metricsRecorder;
    private readonly ISseReconnectionStrategy _reconnectionStrategy;
    private readonly ILogger<SseConnection> _logger;

    private readonly Channel<SseMessage> _messageChannel;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _receiveTask;
    private StreamReader? _currentStream;
    private bool _disposed;
    private readonly object _stateLock = new();

    public string ConnectionId { get; }
    public string Endpoint { get; }
    public DateTime ConnectedAt { get; private set; }
    public DateTime? DisconnectedAt { get; private set; }
    public bool IsConnected { get; private set; }
    public int MessageCount { get; private set; }
    public long BytesReceived { get; private set; }
    public int ChunksReceived { get; private set; }
    public int ReconnectAttempts { get; private set; }
    public ChannelReader<SseMessage> Messages => _messageChannel.Reader;

    public SseConnection(
        string connectionId,
        string endpoint,
        IHttpStreamClient httpStreamClient,
        ISseProtocolHandler protocolHandler,
        ISseMetricsRecorder metricsRecorder,
        ISseReconnectionStrategy reconnectionStrategy,
        ILogger<SseConnection> logger)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _httpStreamClient = httpStreamClient ?? throw new ArgumentNullException(nameof(httpStreamClient));
        _protocolHandler = protocolHandler ?? throw new ArgumentNullException(nameof(protocolHandler));
        _metricsRecorder = metricsRecorder ?? throw new ArgumentNullException(nameof(metricsRecorder));
        _reconnectionStrategy = reconnectionStrategy ?? throw new ArgumentNullException(nameof(reconnectionStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cancellationTokenSource = new CancellationTokenSource();
        _messageChannel = Channel.CreateUnbounded<SseMessage>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
            AllowSynchronousContinuations = false
        });

        // Subscribe to protocol errors
        _protocolHandler.ProtocolError += OnProtocolError;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(SseConnection));

        lock (_stateLock)
        {
            if (IsConnected)
            {
                _logger.LogWarning("Connection {ConnectionId} is already connected", ConnectionId);
                return;
            }
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cancellationTokenSource.Token);

        var attemptStartTime = DateTime.UtcNow;
        _metricsRecorder.RecordConnectionAttempt(ConnectionId, attemptStartTime);

        try
        {
            _logger.LogInformation("Establishing SSE connection {ConnectionId} to {Endpoint}",
                ConnectionId, Endpoint);

            // Open the HTTP stream
            var headers = new Dictionary<string, string>
            {
                ["X-Connection-Id"] = ConnectionId,
                ["X-Client-Type"] = "LoadTest"
            };

            _currentStream = await _httpStreamClient.OpenStreamAsync(
                Endpoint,
                headers,
                linkedCts.Token).ConfigureAwait(false);

            // Update state
            lock (_stateLock)
            {
                IsConnected = true;
                ConnectedAt = DateTime.UtcNow;
            }

            var connectionLatency = (DateTime.UtcNow - attemptStartTime).TotalMilliseconds;
            _metricsRecorder.RecordConnectionSuccess(ConnectionId, connectionLatency, DateTime.UtcNow);

            _logger.LogInformation("SSE connection {ConnectionId} established successfully in {Latency}ms",
                ConnectionId, connectionLatency);

            // Start receiving messages in background
            _receiveTask = ReceiveMessagesAsync(_currentStream.BaseStream, linkedCts.Token);

            // Don't await the receive task here - it runs for the lifetime of the connection
            _ = MonitorConnectionAsync(_receiveTask, linkedCts.Token);
        }
        catch (Exception ex)
        {
            _metricsRecorder.RecordConnectionFailure(ConnectionId, ex, DateTime.UtcNow);
            _logger.LogError(ex, "Failed to establish SSE connection {ConnectionId}", ConnectionId);

            // Let the reconnection strategy handle retry logic
            if (_reconnectionStrategy.ShouldReconnect(ReconnectAttempts, ex))
            {
                ReconnectAttempts++;
                await _reconnectionStrategy.HandleReconnectionAsync(this, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                lock (_stateLock)
                {
                    IsConnected = false;
                    DisconnectedAt = DateTime.UtcNow;
                }
                throw;
            }
        }
    }

    public async Task DisconnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(SseConnection));

        lock (_stateLock)
        {
            if (!IsConnected)
            {
                _logger.LogDebug("Connection {ConnectionId} is already disconnected", ConnectionId);
                return;
            }
        }

        _logger.LogInformation("Disconnecting SSE connection {ConnectionId}", ConnectionId);

        // Signal cancellation
        _cancellationTokenSource.Cancel();

        // Wait for receive task to complete
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during graceful disconnection of {ConnectionId}", ConnectionId);
            }
        }

        // Clean up stream
        _currentStream?.Dispose();
        _currentStream = null;

        // Update state
        lock (_stateLock)
        {
            IsConnected = false;
            DisconnectedAt = DateTime.UtcNow;
        }

        // Complete the message channel
        _messageChannel.Writer.TryComplete();

        _metricsRecorder.RecordDisconnection(ConnectionId, "Manual disconnect", DateTime.UtcNow);
        _logger.LogInformation("SSE connection {ConnectionId} disconnected", ConnectionId);
    }

    public SseConnectionMetrics GetMetrics()
    {
        var connectionMetrics = _metricsRecorder.GetConnectionMetrics(ConnectionId);
        if (connectionMetrics != null)
        {
            return connectionMetrics;
        }

        // Return basic metrics if detailed metrics not available
        lock (_stateLock)
        {
            return new SseConnectionMetrics
            {
                ConnectionId = ConnectionId,
                IsConnected = IsConnected,
                ConnectedAt = ConnectedAt,
                DisconnectedAt = DisconnectedAt,
                Duration = IsConnected
                    ? DateTime.UtcNow - ConnectedAt
                    : (DisconnectedAt ?? DateTime.UtcNow) - ConnectedAt,
                MessagesReceived = MessageCount,
                BytesReceived = BytesReceived,
                ChunksReceived = ChunksReceived,
                ReconnectAttempts = ReconnectAttempts
            };
        }
    }

    private async Task ReceiveMessagesAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting message reception for connection {ConnectionId}", ConnectionId);

            // Use a separate channel for protocol processing
            var protocolChannel = Channel.CreateUnbounded<SseMessage>();

            // Start protocol processing
            var protocolTask = _protocolHandler.ProcessStreamAsync(
                stream,
                protocolChannel.Writer,
                cancellationToken);

            // Process messages from protocol handler
            await foreach (var message in protocolChannel.Reader.ReadAllAsync(cancellationToken))
            {
                var processingStart = DateTime.UtcNow;

                // Update metrics
                MessageCount++;
                BytesReceived += message.Data.Length;
                ChunksReceived++;

                // Record metrics
                var processingTime = (DateTime.UtcNow - processingStart).TotalMilliseconds;
                _metricsRecorder.RecordMessageReceived(ConnectionId, message, processingTime);
                _metricsRecorder.RecordChunkReceived(ConnectionId, message.Data.Length, message.LatencyMs);

                // Forward to consumer channel
                await _messageChannel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);

                _logger.LogTrace("Received message on {ConnectionId}: Type={EventType}, Size={Size}",
                    ConnectionId, message.Event, message.Data.Length);
            }

            await protocolTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message reception cancelled for connection {ConnectionId}", ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving messages for connection {ConnectionId}", ConnectionId);
            _metricsRecorder.RecordError(ConnectionId, ex, "Message reception");
            throw;
        }
    }

    private async Task MonitorConnectionAsync(Task receiveTask, CancellationToken cancellationToken)
    {
        try
        {
            await receiveTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Connection {ConnectionId} terminated unexpectedly", ConnectionId);

            lock (_stateLock)
            {
                IsConnected = false;
                DisconnectedAt = DateTime.UtcNow;
            }

            _metricsRecorder.RecordDisconnection(ConnectionId, $"Error: {ex.Message}", DateTime.UtcNow);

            // Attempt reconnection if appropriate
            if (_reconnectionStrategy.ShouldReconnect(ReconnectAttempts, ex))
            {
                ReconnectAttempts++;
                try
                {
                    await _reconnectionStrategy.HandleReconnectionAsync(this, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception reconnectEx)
                {
                    _logger.LogError(reconnectEx, "Reconnection failed for {ConnectionId}", ConnectionId);
                }
            }
        }
    }

    private void OnProtocolError(object? sender, ProtocolErrorEventArgs e)
    {
        _logger.LogWarning("Protocol error on connection {ConnectionId}: {Error}",
            ConnectionId, e.ErrorMessage);
        _metricsRecorder.RecordError(ConnectionId,
            new InvalidOperationException(e.ErrorMessage),
            "Protocol parsing");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);

        _protocolHandler.ProtocolError -= OnProtocolError;
        _cancellationTokenSource?.Dispose();
        _currentStream?.Dispose();
        _httpStreamClient?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}