namespace AIChat.Orleans.Metrics.Storage;

/// <summary>
/// Interface for SSE metrics storage operations.
/// Provides abstraction over the underlying storage mechanism.
/// </summary>
public interface ISseMetricsStorage
{
    /// <summary>
    /// Stores or updates connection metrics.
    /// </summary>
    Task StoreConnectionAsync(string connectionId, SseConnectionMetrics connection);

    /// <summary>
    /// Removes connection metrics.
    /// </summary>
    Task<SseConnectionMetrics?> RemoveConnectionAsync(string connectionId);

    /// <summary>
    /// Gets all active connections.
    /// </summary>
    Task<IEnumerable<KeyValuePair<string, SseConnectionMetrics>>> GetActiveConnectionsAsync();

    /// <summary>
    /// Stores a stream event.
    /// </summary>
    Task StoreStreamEventAsync(SseStreamEvent streamEvent);

    /// <summary>
    /// Gets stream events within a time range.
    /// </summary>
    Task<IEnumerable<SseStreamEvent>> GetStreamEventsAsync(DateTime since);

    /// <summary>
    /// Stores chunk metrics.
    /// </summary>
    Task StoreChunkMetricsAsync(SseChunkMetrics chunk);

    /// <summary>
    /// Gets chunk metrics within a time range.
    /// </summary>
    Task<IEnumerable<SseChunkMetrics>> GetChunkMetricsAsync(DateTime since);

    /// <summary>
    /// Stores buffer event.
    /// </summary>
    Task StoreBufferEventAsync(SseBufferEvent bufferEvent);

    /// <summary>
    /// Gets buffer events within a time range.
    /// </summary>
    Task<IEnumerable<SseBufferEvent>> GetBufferEventsAsync(DateTime since);

    /// <summary>
    /// Stores failure event.
    /// </summary>
    Task StoreFailureEventAsync(SseFailureEvent failure);

    /// <summary>
    /// Gets failure events within a time range.
    /// </summary>
    Task<IEnumerable<SseFailureEvent>> GetFailureEventsAsync(DateTime since);

    /// <summary>
    /// Stores stream session metrics.
    /// </summary>
    Task StoreStreamSessionAsync(string sessionKey, StreamSessionMetrics session);

    /// <summary>
    /// Gets stream sessions within a time range.
    /// </summary>
    Task<IEnumerable<KeyValuePair<string, StreamSessionMetrics>>> GetStreamSessionsAsync(DateTime since);

    /// <summary>
    /// Removes old data based on retention policy.
    /// </summary>
    Task<int> CleanupOldDataAsync(DateTime cutoffTime, CancellationToken cancellationToken);

    /// <summary>
    /// Gets storage statistics.
    /// </summary>
    Task<StorageStatistics> GetStatisticsAsync();
}

/// <summary>
/// Storage statistics for monitoring.
/// </summary>
public class StorageStatistics
{
    /// <summary>
    /// Total number of active connections.
    /// </summary>
    public int ActiveConnectionCount { get; set; }

    /// <summary>
    /// Total number of stream events.
    /// </summary>
    public int StreamEventCount { get; set; }

    /// <summary>
    /// Total number of chunk metrics.
    /// </summary>
    public int ChunkMetricsCount { get; set; }

    /// <summary>
    /// Total number of buffer events.
    /// </summary>
    public int BufferEventCount { get; set; }

    /// <summary>
    /// Total number of failure events.
    /// </summary>
    public int FailureEventCount { get; set; }

    /// <summary>
    /// Total number of stream sessions.
    /// </summary>
    public int StreamSessionCount { get; set; }

    /// <summary>
    /// Estimated memory usage in bytes.
    /// </summary>
    public long EstimatedMemoryUsage { get; set; }
}

/// <summary>
/// Connection metrics data.
/// </summary>
public class SseConnectionMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public DateTime EstablishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsActive { get; set; }
    public double Duration { get; set; }
}

/// <summary>
/// Stream event data.
/// </summary>
public class SseStreamEvent
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Duration { get; set; }
}

/// <summary>
/// Chunk metrics data.
/// </summary>
public class SseChunkMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public int Size { get; set; }
    public double ProcessingTime { get; set; }
    public bool Success { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Buffer event data.
/// </summary>
public class SseBufferEvent
{
    public string GrainId { get; set; } = string.Empty;
    public int BufferSize { get; set; }
    public int BufferCapacity { get; set; }
    public double Utilization { get; set; }
    public bool OverflowOccurred { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Failure event data.
/// </summary>
public class SseFailureEvent
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public int ChunksLost { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Stream session metrics data.
/// </summary>
public class StreamSessionMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public int TotalChunks { get; set; }
    public int FailedChunks { get; set; }
    public long TotalBytes { get; set; }
    public bool CompletedSuccessfully { get; set; }
    public DateTime CompletedAt { get; set; }
}
