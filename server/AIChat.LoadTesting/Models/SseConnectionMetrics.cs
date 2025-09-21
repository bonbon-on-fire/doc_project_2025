namespace AIChat.LoadTesting.Models;

/// <summary>
/// Metrics for an SSE connection, capturing performance and health data.
/// </summary>
public class SseConnectionMetrics
{
    /// <summary>
    /// Gets or sets the unique identifier for the connection.
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the connection is currently active.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the connection was established.
    /// </summary>
    public DateTime ConnectedAt { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the connection was closed, if applicable.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Gets or sets the duration of the connection.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the total number of messages received.
    /// </summary>
    public int MessagesReceived { get; set; }

    /// <summary>
    /// Gets or sets the total bytes received through this connection.
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Gets or sets the number of chunks received.
    /// </summary>
    public int ChunksReceived { get; set; }

    /// <summary>
    /// Gets or sets the average latency for chunks in milliseconds.
    /// </summary>
    public double AverageChunkLatency { get; set; }

    /// <summary>
    /// Gets or sets the number of reconnection attempts made.
    /// </summary>
    public int ReconnectAttempts { get; set; }

    /// <summary>
    /// Gets or sets the number of errors encountered.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the last error message, if any.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the current buffer utilization percentage.
    /// </summary>
    public double BufferUtilization { get; set; }

    /// <summary>
    /// Gets or sets the peak buffer utilization percentage.
    /// </summary>
    public double PeakBufferUtilization { get; set; }

    /// <summary>
    /// Gets or sets performance percentiles for latency.
    /// </summary>
    public LatencyPercentiles? LatencyPercentiles { get; set; }
}

/// <summary>
/// Latency percentiles for detailed performance analysis.
/// </summary>
public class LatencyPercentiles
{
    /// <summary>
    /// Gets or sets the 50th percentile (median) latency in milliseconds.
    /// </summary>
    public double P50 { get; set; }

    /// <summary>
    /// Gets or sets the 75th percentile latency in milliseconds.
    /// </summary>
    public double P75 { get; set; }

    /// <summary>
    /// Gets or sets the 90th percentile latency in milliseconds.
    /// </summary>
    public double P90 { get; set; }

    /// <summary>
    /// Gets or sets the 95th percentile latency in milliseconds.
    /// </summary>
    public double P95 { get; set; }

    /// <summary>
    /// Gets or sets the 99th percentile latency in milliseconds.
    /// </summary>
    public double P99 { get; set; }

    /// <summary>
    /// Gets or sets the maximum latency in milliseconds.
    /// </summary>
    public double Max { get; set; }

    /// <summary>
    /// Gets or sets the minimum latency in milliseconds.
    /// </summary>
    public double Min { get; set; }
}