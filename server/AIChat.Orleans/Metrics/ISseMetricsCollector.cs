using Orleans;

namespace AIChat.Orleans.Metrics;

/// <summary>
/// Interface for collecting and monitoring SSE (Server-Sent Events) specific metrics.
/// Provides comprehensive monitoring for stream health, performance, and reliability.
/// </summary>
public interface ISseMetricsCollector
{
    /// <summary>
    /// Records SSE stream connection event.
    /// </summary>
    /// <param name="grainId">User grain ID handling the stream</param>
    /// <param name="connectionId">Unique SSE connection identifier</param>
    /// <param name="established">Whether connection was established or closed</param>
    Task RecordSseConnectionAsync(string grainId, string connectionId, bool established);

    /// <summary>
    /// Records SSE stream chunk processing metrics.
    /// </summary>
    /// <param name="grainId">User grain ID processing the chunk</param>
    /// <param name="chunkType">Type of chunk (text, reasoning, tool, etc.)</param>
    /// <param name="chunkSize">Size of the chunk in bytes</param>
    /// <param name="processingTime">Time to process in milliseconds</param>
    /// <param name="success">Whether processing succeeded</param>
    Task RecordSseStreamChunkAsync(
        string grainId,
        string chunkType,
        int chunkSize,
        double processingTime,
        bool success
    );

    /// <summary>
    /// Records SSE buffer utilization metrics.
    /// </summary>
    /// <param name="grainId">User grain ID</param>
    /// <param name="bufferSize">Current buffer size</param>
    /// <param name="bufferCapacity">Maximum buffer capacity</param>
    /// <param name="overflowOccurred">Whether buffer overflow occurred</param>
    Task RecordSseBufferMetricsAsync(
        string grainId,
        int bufferSize,
        int bufferCapacity,
        bool overflowOccurred
    );

    /// <summary>
    /// Records SSE stream completion metrics.
    /// </summary>
    /// <param name="grainId">User grain ID</param>
    /// <param name="streamDuration">Total stream duration in milliseconds</param>
    /// <param name="totalChunks">Total number of chunks sent</param>
    /// <param name="totalBytes">Total bytes transmitted</param>
    /// <param name="completedSuccessfully">Whether stream completed successfully</param>
    Task RecordSseStreamCompletionAsync(
        string grainId,
        double streamDuration,
        int totalChunks,
        long totalBytes,
        bool completedSuccessfully
    );

    /// <summary>
    /// Records SSE stream failure event.
    /// </summary>
    /// <param name="grainId">User grain ID</param>
    /// <param name="connectionId">Connection that failed</param>
    /// <param name="failureReason">Reason for failure</param>
    /// <param name="chunksLost">Number of chunks that couldn't be delivered</param>
    Task RecordSseStreamFailureAsync(
        string grainId,
        string connectionId,
        string failureReason,
        int chunksLost
    );

    /// <summary>
    /// Gets SSE-specific metrics summary.
    /// </summary>
    /// <returns>SSE streaming metrics summary</returns>
    Task<SseMetricsSummary> GetSseMetricsSummaryAsync();

    /// <summary>
    /// Gets SSE stream health indicators.
    /// </summary>
    /// <returns>Current health status of SSE streams</returns>
    Task<SseHealthIndicators> GetSseHealthIndicatorsAsync();

    /// <summary>
    /// Gets historical SSE metrics for trend analysis.
    /// </summary>
    /// <param name="metricType">Type of metric to retrieve</param>
    /// <param name="timeRange">Time range to query</param>
    /// <returns>Historical metric data points</returns>
    Task<List<SseMetricDataPoint>> GetSseHistoricalMetricsAsync(
        SseMetricType metricType,
        TimeSpan timeRange
    );
}

/// <summary>
/// Summary of SSE streaming metrics for dashboard display.
/// </summary>
public class SseMetricsSummary
{
    /// <summary>
    /// Total number of active SSE connections.
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// Total SSE connections established in the last hour.
    /// </summary>
    public int ConnectionsEstablishedLastHour { get; set; }

    /// <summary>
    /// Total SSE connections closed in the last hour.
    /// </summary>
    public int ConnectionsClosedLastHour { get; set; }

    /// <summary>
    /// Average stream duration in milliseconds.
    /// </summary>
    public double AverageStreamDuration { get; set; }

    /// <summary>
    /// Average chunk processing time in milliseconds.
    /// </summary>
    public double AverageChunkProcessingTime { get; set; }

    /// <summary>
    /// Total chunks processed in the last hour.
    /// </summary>
    public long ChunksProcessedLastHour { get; set; }

    /// <summary>
    /// Total bytes transmitted in the last hour.
    /// </summary>
    public long BytesTransmittedLastHour { get; set; }

    /// <summary>
    /// Stream success rate as percentage (0-100).
    /// </summary>
    public double StreamSuccessRate { get; set; }

    /// <summary>
    /// Number of stream failures in the last hour.
    /// </summary>
    public int StreamFailuresLastHour { get; set; }

    /// <summary>
    /// Buffer overflow events in the last hour.
    /// </summary>
    public int BufferOverflowsLastHour { get; set; }

    /// <summary>
    /// Average buffer utilization percentage (0-100).
    /// </summary>
    public double AverageBufferUtilization { get; set; }

    /// <summary>
    /// Metrics breakdown by chunk type.
    /// </summary>
    public Dictionary<string, ChunkTypeMetrics> ChunkTypeBreakdown { get; set; } = [];

    /// <summary>
    /// Timestamp when these metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Health indicators for SSE streaming.
/// </summary>
public class SseHealthIndicators
{
    /// <summary>
    /// Overall health status of SSE streaming.
    /// </summary>
    public HealthStatus OverallHealth { get; set; }

    /// <summary>
    /// Connection stability indicator.
    /// </summary>
    public HealthIndicator ConnectionStability { get; set; } = new();

    /// <summary>
    /// Stream throughput indicator.
    /// </summary>
    public HealthIndicator StreamThroughput { get; set; } = new();

    /// <summary>
    /// Buffer health indicator.
    /// </summary>
    public HealthIndicator BufferHealth { get; set; } = new();

    /// <summary>
    /// Error rate indicator.
    /// </summary>
    public HealthIndicator ErrorRate { get; set; } = new();

    /// <summary>
    /// Latency indicator.
    /// </summary>
    public HealthIndicator Latency { get; set; } = new();

    /// <summary>
    /// Recovery performance indicator.
    /// </summary>
    public HealthIndicator RecoveryPerformance { get; set; } = new();

    /// <summary>
    /// Active alerts related to SSE streaming.
    /// </summary>
    public List<SseAlert> ActiveAlerts { get; set; } = [];

    /// <summary>
    /// Timestamp of health check.
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual health indicator for a specific aspect of SSE streaming.
/// </summary>
public class HealthIndicator
{
    /// <summary>
    /// Current health status.
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// Current value of the metric.
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// Threshold for warning status.
    /// </summary>
    public double WarningThreshold { get; set; }

    /// <summary>
    /// Threshold for critical status.
    /// </summary>
    public double CriticalThreshold { get; set; }

    /// <summary>
    /// Human-readable description of the indicator.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Trend direction (increasing, stable, decreasing).
    /// </summary>
    public string Trend { get; set; } = "stable";
}

/// <summary>
/// Health status enumeration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.HealthStatus")]
public enum HealthStatus
{
    /// <summary>
    /// System is healthy.
    /// </summary>
    [Id(0)]
    Healthy,

    /// <summary>
    /// System is degraded but functional.
    /// </summary>
    [Id(1)]
    Degraded,

    /// <summary>
    /// System has critical issues.
    /// </summary>
    [Id(2)]
    Critical,

    /// <summary>
    /// Health status unknown.
    /// </summary>
    [Id(3)]
    Unknown
}

/// <summary>
/// SSE streaming alert.
/// </summary>
public class SseAlert
{
    /// <summary>
    /// Unique alert identifier.
    /// </summary>
    public string AlertId { get; set; } = string.Empty;

    /// <summary>
    /// Alert severity level.
    /// </summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>
    /// Alert message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// When the alert was triggered.
    /// </summary>
    public DateTime TriggeredAt { get; set; }

    /// <summary>
    /// Metric that triggered the alert.
    /// </summary>
    public string TriggeringMetric { get; set; } = string.Empty;

    /// <summary>
    /// Current value of the triggering metric.
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// Threshold that was exceeded.
    /// </summary>
    public double ThresholdValue { get; set; }

    /// <summary>
    /// Recommended action to resolve the alert.
    /// </summary>
    public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// Alert severity levels.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.AlertSeverity")]
public enum AlertSeverity
{
    /// <summary>
    /// Informational alert.
    /// </summary>
    [Id(0)]
    Info,

    /// <summary>
    /// Warning level alert.
    /// </summary>
    [Id(1)]
    Warning,

    /// <summary>
    /// Critical alert requiring immediate attention.
    /// </summary>
    [Id(2)]
    Critical
}

/// <summary>
/// Metrics for specific chunk types.
/// </summary>
public class ChunkTypeMetrics
{
    /// <summary>
    /// Type of chunk (text, reasoning, tool, etc.).
    /// </summary>
    public string ChunkType { get; set; } = string.Empty;

    /// <summary>
    /// Total count of this chunk type.
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    /// Average size in bytes.
    /// </summary>
    public double AverageSize { get; set; }

    /// <summary>
    /// Average processing time in milliseconds.
    /// </summary>
    public double AverageProcessingTime { get; set; }

    /// <summary>
    /// Success rate for this chunk type (0-100).
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Total bytes for this chunk type.
    /// </summary>
    public long TotalBytes { get; set; }
}

/// <summary>
/// Historical data point for SSE metrics.
/// </summary>
public class SseMetricDataPoint
{
    /// <summary>
    /// Timestamp of the data point.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Metric value.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Type of metric.
    /// </summary>
    public SseMetricType MetricType { get; set; }

    /// <summary>
    /// Optional tags for additional context.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// Types of SSE metrics available for historical queries.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.SseMetricType")]
public enum SseMetricType
{
    /// <summary>
    /// Number of active connections.
    /// </summary>
    [Id(0)]
    ActiveConnections,

    /// <summary>
    /// Chunks processed per minute.
    /// </summary>
    [Id(1)]
    ChunkThroughput,

    /// <summary>
    /// Average chunk processing latency.
    /// </summary>
    [Id(2)]
    ChunkLatency,

    /// <summary>
    /// Buffer utilization percentage.
    /// </summary>
    [Id(3)]
    BufferUtilization,

    /// <summary>
    /// Stream failure rate.
    /// </summary>
    [Id(4)]
    FailureRate,

    /// <summary>
    /// Bytes transmitted per minute.
    /// </summary>
    [Id(5)]
    ByteThroughput,

    /// <summary>
    /// Connection drop rate.
    /// </summary>
    [Id(6)]
    ConnectionDropRate,

    /// <summary>
    /// Stream recovery time.
    /// </summary>
    [Id(7)]
    RecoveryTime
}
