namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for managing resilient streaming with automatic recovery, buffering, and circuit breaking.
/// Provides fault tolerance for Orleans grain streaming operations.
/// </summary>
public interface IResilientStreamManager : IAsyncDisposable
{
    /// <summary>
    /// Processes a streaming request with resilience patterns including reconnection, buffering, and circuit breaking.
    /// </summary>
    /// <typeparam name="T">The type of data being streamed</typeparam>
    /// <param name="streamId">Unique identifier for the stream</param>
    /// <param name="grainStream">The source stream from an Orleans grain</param>
    /// <param name="httpResponse">The HTTP response to write SSE events to</param>
    /// <param name="formatter">Function to format grain data as SSE events</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async streaming operation with resilience</returns>
    Task ProcessResilientStreamAsync<T>(
        string streamId,
        IAsyncEnumerable<T> grainStream,
        HttpResponse httpResponse,
        Func<T, string> formatter,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Attempts to recover a failed stream using buffered messages and partial recovery.
    /// </summary>
    /// <param name="streamId">Unique identifier for the stream to recover</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>True if recovery was successful, false otherwise</returns>
    Task<bool> RecoverStreamAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the resilient streaming system.
    /// </summary>
    /// <returns>Health status including stream metrics and circuit breaker state</returns>
    Task<StreamHealthStatus> GetHealthStatusAsync();

    /// <summary>
    /// Gets detailed metrics for a specific stream.
    /// </summary>
    /// <param name="streamId">Unique identifier for the stream</param>
    /// <returns>Stream metrics or null if stream not found</returns>
    Task<StreamMetrics?> GetStreamMetricsAsync(string streamId);

    /// <summary>
    /// Manually triggers a circuit breaker reset for a specific stream.
    /// </summary>
    /// <param name="streamId">Unique identifier for the stream</param>
    /// <returns>True if reset was successful, false if stream not found</returns>
    Task<bool> ResetCircuitBreakerAsync(string streamId);

    /// <summary>
    /// Clears buffered messages for a specific stream.
    /// </summary>
    /// <param name="streamId">Unique identifier for the stream</param>
    /// <returns>Number of messages cleared</returns>
    Task<int> ClearBufferedMessagesAsync(string streamId);

    /// <summary>
    /// Gets all active stream identifiers.
    /// </summary>
    /// <returns>Collection of active stream IDs</returns>
    Task<IReadOnlyList<string>> GetActiveStreamIdsAsync();
}

/// <summary>
/// Represents the health status of the resilient streaming system.
/// </summary>
public record StreamHealthStatus
{
    /// <summary>
    /// Gets whether the streaming system is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the total number of active streams.
    /// </summary>
    public required int ActiveStreams { get; init; }

    /// <summary>
    /// Gets the number of streams currently in recovery.
    /// </summary>
    public required int StreamsInRecovery { get; init; }

    /// <summary>
    /// Gets the number of open circuit breakers.
    /// </summary>
    public required int OpenCircuitBreakers { get; init; }

    /// <summary>
    /// Gets the total buffered message count across all streams.
    /// </summary>
    public required int TotalBufferedMessages { get; init; }

    /// <summary>
    /// Gets the average recovery time in milliseconds.
    /// </summary>
    public required double AverageRecoveryTimeMs { get; init; }

    /// <summary>
    /// Gets the success rate percentage (0-100).
    /// </summary>
    public required double SuccessRatePercentage { get; init; }

    /// <summary>
    /// Gets detailed status messages.
    /// </summary>
    public List<string> StatusMessages { get; init; } = [];

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Metrics for an individual stream.
/// </summary>
public record StreamMetrics
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the current state of the stream.
    /// </summary>
    public required StreamState State { get; init; }

    /// <summary>
    /// Gets the circuit breaker state for this stream.
    /// </summary>
    public required CircuitState CircuitState { get; init; }

    /// <summary>
    /// Gets the number of messages processed.
    /// </summary>
    public required long MessagesProcessed { get; init; }

    /// <summary>
    /// Gets the number of messages currently buffered.
    /// </summary>
    public required int BufferedMessages { get; init; }

    /// <summary>
    /// Gets the number of reconnection attempts.
    /// </summary>
    public required int ReconnectionAttempts { get; init; }

    /// <summary>
    /// Gets the number of failures encountered.
    /// </summary>
    public required int FailureCount { get; init; }

    /// <summary>
    /// Gets the last error message if any.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the stream start time.
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// Gets the last activity time.
    /// </summary>
    public required DateTime LastActivityTime { get; init; }

    /// <summary>
    /// Gets the total processing time in milliseconds.
    /// </summary>
    public required double TotalProcessingTimeMs { get; init; }

    /// <summary>
    /// Gets partial message recovery statistics.
    /// </summary>
    public PartialRecoveryStats? PartialRecovery { get; init; }
}

/// <summary>
/// Statistics for partial message recovery.
/// </summary>
public record PartialRecoveryStats
{
    /// <summary>
    /// Gets the number of partial messages stored.
    /// </summary>
    public required int PartialMessagesStored { get; init; }

    /// <summary>
    /// Gets the number of successful recoveries.
    /// </summary>
    public required int SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Gets the total size of partial messages in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the last chunk sequence number received.
    /// </summary>
    public int? LastChunkSequence { get; init; }
}

/// <summary>
/// Represents the state of a stream.
/// </summary>
public enum StreamState
{
    /// <summary>
    /// Stream is active and processing normally.
    /// </summary>
    Active,

    /// <summary>
    /// Stream is attempting to reconnect.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Stream is in recovery mode.
    /// </summary>
    Recovering,

    /// <summary>
    /// Stream has failed and cannot recover.
    /// </summary>
    Failed,

    /// <summary>
    /// Stream has completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Stream was cancelled.
    /// </summary>
    Cancelled,
}

/// <summary>
/// Represents the state of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed and allowing operations.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open and rejecting operations.
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open and testing recovery.
    /// </summary>
    HalfOpen,
}
