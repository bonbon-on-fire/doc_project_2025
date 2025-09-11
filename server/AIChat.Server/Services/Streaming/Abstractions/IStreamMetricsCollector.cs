namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Collects and manages metrics for streaming operations.
/// Provides thread-safe metric collection and aggregation.
/// </summary>
public interface IStreamMetricsCollector
{
    /// <summary>
    /// Records the start of a new stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="metadata">Optional metadata about the stream</param>
    void RecordStreamStart(string streamId, StreamMetadata? metadata = null);

    /// <summary>
    /// Records the successful completion of a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="duration">The total duration of the stream</param>
    void RecordStreamComplete(string streamId, TimeSpan? duration = null);

    /// <summary>
    /// Records a stream failure.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="isRecoverable">Whether the failure is recoverable</param>
    void RecordStreamFailure(string streamId, Exception exception, bool isRecoverable = true);

    /// <summary>
    /// Records a message being processed.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="messageSize">Size of the message in bytes</param>
    /// <param name="processingTime">Time taken to process the message</param>
    void RecordMessageProcessed(
        string streamId,
        long messageSize = 0,
        TimeSpan? processingTime = null
    );

    /// <summary>
    /// Records a reconnection attempt.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="attemptNumber">The attempt number</param>
    /// <param name="success">Whether the reconnection was successful</param>
    void RecordReconnectionAttempt(string streamId, int attemptNumber, bool success);

    /// <summary>
    /// Records a circuit breaker state change.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="newState">The new circuit state</param>
    /// <param name="reason">Optional reason for the state change</param>
    void RecordCircuitBreakerStateChange(
        string streamId,
        CircuitState newState,
        string? reason = null
    );

    /// <summary>
    /// Records a recovery operation.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="recoveryType">Type of recovery performed</param>
    /// <param name="success">Whether recovery was successful</param>
    /// <param name="duration">Duration of the recovery operation</param>
    void RecordRecoveryOperation(
        string streamId,
        RecoveryType recoveryType,
        bool success,
        TimeSpan duration
    );

    /// <summary>
    /// Gets metrics for a specific stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Stream metrics or null if not found</returns>
    StreamMetrics? GetStreamMetrics(string streamId);

    /// <summary>
    /// Gets aggregated metrics for all streams.
    /// </summary>
    /// <returns>Aggregated metrics</returns>
    AggregatedMetrics GetAggregatedMetrics();

    /// <summary>
    /// Gets metrics for streams matching a filter.
    /// </summary>
    /// <param name="filter">Filter predicate</param>
    /// <returns>Collection of matching stream metrics</returns>
    IReadOnlyList<StreamMetrics> GetStreamMetrics(Func<StreamMetrics, bool> filter);

    /// <summary>
    /// Resets metrics for a specific stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>True if metrics were reset, false if not found</returns>
    bool ResetStreamMetrics(string streamId);

    /// <summary>
    /// Exports metrics in a specific format.
    /// </summary>
    /// <param name="format">The export format</param>
    /// <returns>Exported metrics as string</returns>
    Task<string> ExportMetricsAsync(MetricsExportFormat format = MetricsExportFormat.Json);
}

/// <summary>
/// Metadata about a stream.
/// </summary>
public class StreamMetadata
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets or sets the stream type.
    /// </summary>
    public string? StreamType { get; init; }

    /// <summary>
    /// Gets or sets custom tags.
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = [];
}

/// <summary>
/// Types of recovery operations.
/// </summary>
public enum RecoveryType
{
    /// <summary>
    /// Message replay recovery.
    /// </summary>
    MessageReplay,

    /// <summary>
    /// Partial message recovery.
    /// </summary>
    PartialMessage,

    /// <summary>
    /// Full stream recovery.
    /// </summary>
    FullStream,

    /// <summary>
    /// Circuit breaker reset.
    /// </summary>
    CircuitReset,
}

/// <summary>
/// Aggregated metrics across all streams.
/// </summary>
public class AggregatedMetrics
{
    /// <summary>
    /// Gets or sets the total number of streams.
    /// </summary>
    public long TotalStreams { get; init; }

    /// <summary>
    /// Gets or sets the number of active streams.
    /// </summary>
    public int ActiveStreams { get; init; }

    /// <summary>
    /// Gets or sets the number of completed streams.
    /// </summary>
    public long CompletedStreams { get; init; }

    /// <summary>
    /// Gets or sets the number of failed streams.
    /// </summary>
    public long FailedStreams { get; init; }

    /// <summary>
    /// Gets or sets the total messages processed.
    /// </summary>
    public long TotalMessagesProcessed { get; init; }

    /// <summary>
    /// Gets or sets the total bytes processed.
    /// </summary>
    public long TotalBytesProcessed { get; init; }

    /// <summary>
    /// Gets or sets the average stream duration.
    /// </summary>
    public TimeSpan AverageStreamDuration { get; init; }

    /// <summary>
    /// Gets or sets the success rate percentage.
    /// </summary>
    public double SuccessRatePercentage { get; init; }

    /// <summary>
    /// Gets or sets the average recovery time.
    /// </summary>
    public TimeSpan AverageRecoveryTime { get; init; }

    /// <summary>
    /// Gets or sets the total reconnection attempts.
    /// </summary>
    public long TotalReconnectionAttempts { get; init; }

    /// <summary>
    /// Gets or sets the successful reconnections.
    /// </summary>
    public long SuccessfulReconnections { get; init; }

    /// <summary>
    /// Gets or sets the number of open circuit breakers.
    /// </summary>
    public int OpenCircuitBreakers { get; init; }

    /// <summary>
    /// Gets or sets the timestamp of the metrics.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the uptime duration.
    /// </summary>
    public TimeSpan Uptime { get; init; }
}

/// <summary>
/// Formats for exporting metrics.
/// </summary>
public enum MetricsExportFormat
{
    /// <summary>
    /// JSON format.
    /// </summary>
    Json,

    /// <summary>
    /// Prometheus format.
    /// </summary>
    Prometheus,

    /// <summary>
    /// CSV format.
    /// </summary>
    Csv,

    /// <summary>
    /// Plain text format.
    /// </summary>
    PlainText,
}
