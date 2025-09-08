namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for collecting and reporting streaming metrics.
/// Provides thread-safe operations for tracking performance and statistics.
/// </summary>
public interface IStreamingMetrics
{
    /// <summary>
    /// Records the processing of an item.
    /// </summary>
    /// <param name="processingTimeMs">Time taken to process the item in milliseconds</param>
    void RecordItemProcessed(long processingTimeMs);

    /// <summary>
    /// Records a backpressure event.
    /// </summary>
    /// <param name="delayMs">The delay applied for backpressure in milliseconds</param>
    void RecordBackpressureEvent(int delayMs);

    /// <summary>
    /// Records an error occurrence.
    /// </summary>
    /// <param name="errorType">The type of error that occurred</param>
    void RecordError(string errorType);

    /// <summary>
    /// Records bytes written to the stream.
    /// </summary>
    /// <param name="bytes">Number of bytes written</param>
    void RecordBytesWritten(long bytes);

    /// <summary>
    /// Gets the current statistics snapshot.
    /// </summary>
    /// <returns>Current streaming statistics</returns>
    StreamingStatistics GetStatistics();

    /// <summary>
    /// Resets all metrics to their initial state.
    /// </summary>
    void Reset();
}

/// <summary>
/// Comprehensive statistics for streaming operations.
/// </summary>
public record StreamingStatistics
{
    /// <summary>
    /// Total number of items processed.
    /// </summary>
    public long ItemsProcessed { get; init; }

    /// <summary>
    /// Average processing time per item in milliseconds.
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }

    /// <summary>
    /// Maximum processing time for a single item in milliseconds.
    /// </summary>
    public long MaxProcessingTimeMs { get; init; }

    /// <summary>
    /// Minimum processing time for a single item in milliseconds.
    /// </summary>
    public long MinProcessingTimeMs { get; init; }

    /// <summary>
    /// Total number of backpressure events triggered.
    /// </summary>
    public long BackpressureEvents { get; init; }

    /// <summary>
    /// Total time spent in backpressure delays in milliseconds.
    /// </summary>
    public long TotalBackpressureDelayMs { get; init; }

    /// <summary>
    /// Total number of errors encountered.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Error counts by type.
    /// </summary>
    public IReadOnlyDictionary<string, long> ErrorsByType { get; init; } =
        new Dictionary<string, long>();

    /// <summary>
    /// Total bytes written to the stream.
    /// </summary>
    public long TotalBytesWritten { get; init; }

    /// <summary>
    /// Current throughput in items per second.
    /// </summary>
    public double ThroughputPerSecond { get; init; }

    /// <summary>
    /// Timestamp when metrics collection started.
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    /// Duration of metrics collection.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
