using AIChat.LoadTesting.Models;

namespace AIChat.LoadTesting.Services.Interfaces;

/// <summary>
/// Records and aggregates metrics for SSE connections during load testing.
/// Provides a separation of concerns for metrics collection.
/// </summary>
public interface ISseMetricsRecorder
{
    /// <summary>
    /// Records the start of a connection attempt.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="timestamp">The timestamp of the attempt</param>
    void RecordConnectionAttempt(string connectionId, DateTime timestamp);

    /// <summary>
    /// Records a successful connection establishment.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="latencyMs">Connection establishment latency in milliseconds</param>
    /// <param name="timestamp">The timestamp of successful connection</param>
    void RecordConnectionSuccess(string connectionId, double latencyMs, DateTime timestamp);

    /// <summary>
    /// Records a failed connection attempt.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="error">The error that occurred</param>
    /// <param name="timestamp">The timestamp of the failure</param>
    void RecordConnectionFailure(string connectionId, Exception error, DateTime timestamp);

    /// <summary>
    /// Records a connection disconnection.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="reason">The reason for disconnection</param>
    /// <param name="timestamp">The timestamp of disconnection</param>
    void RecordDisconnection(string connectionId, string reason, DateTime timestamp);

    /// <summary>
    /// Records message reception metrics.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="message">The received message</param>
    /// <param name="processingTimeMs">Time taken to process the message in milliseconds</param>
    void RecordMessageReceived(string connectionId, SseMessage message, double processingTimeMs);

    /// <summary>
    /// Records chunk reception metrics.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="chunkSizeBytes">Size of the chunk in bytes</param>
    /// <param name="latencyMs">Chunk latency in milliseconds</param>
    void RecordChunkReceived(string connectionId, int chunkSizeBytes, double latencyMs);

    /// <summary>
    /// Records buffer state metrics.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="bufferSize">Current buffer size</param>
    /// <param name="bufferCapacity">Maximum buffer capacity</param>
    /// <param name="overflow">Whether a buffer overflow occurred</param>
    void RecordBufferState(string connectionId, int bufferSize, int bufferCapacity, bool overflow);

    /// <summary>
    /// Records reconnection attempt metrics.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="attemptNumber">The reconnection attempt number</param>
    /// <param name="success">Whether the reconnection was successful</param>
    /// <param name="latencyMs">Reconnection latency in milliseconds</param>
    void RecordReconnection(string connectionId, int attemptNumber, bool success, double latencyMs);

    /// <summary>
    /// Records an error event.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="error">The error that occurred</param>
    /// <param name="context">Additional context about where the error occurred</param>
    void RecordError(string connectionId, Exception error, string context);

    /// <summary>
    /// Gets metrics for a specific connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <returns>Connection metrics, or null if not found</returns>
    SseConnectionMetrics? GetConnectionMetrics(string connectionId);

    /// <summary>
    /// Gets aggregated metrics across all connections.
    /// </summary>
    /// <returns>Aggregated metrics snapshot</returns>
    AggregatedSseMetrics GetAggregatedMetrics();

    /// <summary>
    /// Exports metrics in a format suitable for reporting.
    /// </summary>
    /// <param name="format">The export format (e.g., "json", "csv", "prometheus")</param>
    /// <returns>Exported metrics as a string</returns>
    string ExportMetrics(string format = "json");

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    void Reset();

    /// <summary>
    /// Flushes any pending metrics to storage or external systems.
    /// </summary>
    /// <returns>Task representing the asynchronous flush operation</returns>
    Task FlushAsync();
}

/// <summary>
/// Aggregated metrics across all SSE connections.
/// </summary>
public class AggregatedSseMetrics
{
    /// <summary>
    /// Gets or sets the total number of connection attempts.
    /// </summary>
    public long TotalConnectionAttempts { get; set; }

    /// <summary>
    /// Gets or sets the number of successful connections.
    /// </summary>
    public long SuccessfulConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of failed connections.
    /// </summary>
    public long FailedConnections { get; set; }

    /// <summary>
    /// Gets or sets the number of currently active connections.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages received.
    /// </summary>
    public long TotalMessagesReceived { get; set; }

    /// <summary>
    /// Gets or sets the total bytes received.
    /// </summary>
    public long TotalBytesReceived { get; set; }

    /// <summary>
    /// Gets or sets the total number of errors.
    /// </summary>
    public long TotalErrors { get; set; }

    /// <summary>
    /// Gets or sets the total number of reconnection attempts.
    /// </summary>
    public long TotalReconnectionAttempts { get; set; }

    /// <summary>
    /// Gets or sets the number of successful reconnections.
    /// </summary>
    public long SuccessfulReconnections { get; set; }

    /// <summary>
    /// Gets or sets the average connection establishment latency.
    /// </summary>
    public double AverageConnectionLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the average message processing latency.
    /// </summary>
    public double AverageMessageLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets latency percentiles.
    /// </summary>
    public LatencyPercentiles? LatencyPercentiles { get; set; }

    /// <summary>
    /// Gets or sets the messages per second throughput.
    /// </summary>
    public double MessagesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the bytes per second throughput.
    /// </summary>
    public double BytesPerSecond { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when metrics collection started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last metric update.
    /// </summary>
    public DateTime LastUpdateTime { get; set; }

    /// <summary>
    /// Gets or sets error distribution by type.
    /// </summary>
    public Dictionary<string, long> ErrorsByType { get; set; } = new();

    /// <summary>
    /// Gets or sets message distribution by event type.
    /// </summary>
    public Dictionary<string, long> MessagesByEventType { get; set; } = new();
}