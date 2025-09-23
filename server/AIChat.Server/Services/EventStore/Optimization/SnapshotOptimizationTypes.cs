namespace AIChat.Server.Services.EventStore.Optimization;

/// <summary>
/// Configuration for snapshot performance optimization.
/// </summary>
public record SnapshotOptimizationConfiguration
{
    /// <summary>
    /// Gets whether caching is enabled.
    /// </summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>
    /// Gets whether batching is enabled.
    /// </summary>
    public bool EnableBatching { get; init; } = true;

    /// <summary>
    /// Gets the cache expiration time.
    /// </summary>
    public TimeSpan CacheExpirationTime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets the maximum number of entries to cache.
    /// </summary>
    public int MaxCacheEntries { get; init; } = 1000;

    /// <summary>
    /// Gets the maximum batch size for operations.
    /// </summary>
    public int MaxBatchSize { get; init; } = 50;

    /// <summary>
    /// Gets the batch processing interval.
    /// </summary>
    public TimeSpan BatchProcessingInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the priority threshold below which operations are batched.
    /// </summary>
    public int BatchingPriorityThreshold { get; init; } = 5;

    /// <summary>
    /// Gets whether to enable prefetching of related snapshots.
    /// </summary>
    public bool EnablePrefetching { get; init; } = false;

    /// <summary>
    /// Gets the maximum memory usage for caching in bytes.
    /// </summary>
    public long MaxCacheMemoryBytes { get; init; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// Creates a default optimization configuration.
    /// </summary>
    /// <returns>Default configuration</returns>
    public static SnapshotOptimizationConfiguration CreateDefault()
    {
        return new SnapshotOptimizationConfiguration();
    }

    /// <summary>
    /// Creates a high-performance configuration optimized for throughput.
    /// </summary>
    /// <returns>High-performance configuration</returns>
    public static SnapshotOptimizationConfiguration CreateHighPerformance()
    {
        return new SnapshotOptimizationConfiguration
        {
            EnableCaching = true,
            EnableBatching = true,
            CacheExpirationTime = TimeSpan.FromHours(2),
            MaxCacheEntries = 5000,
            MaxBatchSize = 100,
            BatchProcessingInterval = TimeSpan.FromSeconds(2),
            BatchingPriorityThreshold = 8,
            EnablePrefetching = true,
            MaxCacheMemoryBytes = 500 * 1024 * 1024 // 500MB
        };
    }

    /// <summary>
    /// Creates a memory-conservative configuration.
    /// </summary>
    /// <returns>Memory-conservative configuration</returns>
    public static SnapshotOptimizationConfiguration CreateMemoryConservative()
    {
        return new SnapshotOptimizationConfiguration
        {
            EnableCaching = true,
            EnableBatching = false,
            CacheExpirationTime = TimeSpan.FromMinutes(10),
            MaxCacheEntries = 100,
            MaxBatchSize = 10,
            EnablePrefetching = false,
            MaxCacheMemoryBytes = 10 * 1024 * 1024 // 10MB
        };
    }
}

/// <summary>
/// Represents a cached snapshot entry.
/// </summary>
internal record CachedSnapshot
{
    /// <summary>
    /// Gets the cached snapshot data.
    /// </summary>
    public required object Data { get; init; }

    /// <summary>
    /// Gets the cached snapshot metadata.
    /// </summary>
    public required SnapshotMetadata Metadata { get; init; }

    /// <summary>
    /// Gets when this snapshot was cached.
    /// </summary>
    public required DateTimeOffset CachedAt { get; init; }

    /// <summary>
    /// Gets the time taken to retrieve this snapshot.
    /// </summary>
    public required TimeSpan RetrievalTime { get; init; }

    /// <summary>
    /// Gets the estimated size of this cached entry in bytes.
    /// </summary>
    public required long EstimatedSize { get; init; }
}

/// <summary>
/// Represents cached metadata entry.
/// </summary>
internal record CachedMetadata
{
    /// <summary>
    /// Gets the cached metadata.
    /// </summary>
    public required SnapshotMetadata Metadata { get; init; }

    /// <summary>
    /// Gets when this metadata was cached.
    /// </summary>
    public required DateTimeOffset CachedAt { get; init; }
}

/// <summary>
/// Represents a batched operation.
/// </summary>
internal class BatchedOperation
{
    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public required BatchOperationType Type { get; set; }

    /// <summary>
    /// Gets or sets the stream identifier.
    /// </summary>
    public required string StreamId { get; set; }

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    public required long Version { get; set; }

    /// <summary>
    /// Gets or sets the state to snapshot.
    /// </summary>
    public object? State { get; set; }

    /// <summary>
    /// Gets or sets the metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the operation priority.
    /// </summary>
    public required int Priority { get; set; }

    /// <summary>
    /// Gets or sets when this operation was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion source for this operation.
    /// </summary>
    public required TaskCompletionSource<SnapshotWriteResult> CompletionSource { get; set; }
}

/// <summary>
/// Types of batched operations.
/// </summary>
internal enum BatchOperationType
{
    Create,
    Delete,
    Update
}

/// <summary>
/// Performance metrics for a stream.
/// </summary>
public class PerformanceMetrics
{
    private long _totalRetrievalTimeMs;
    private long _totalCreationTimeMs;

    /// <summary>
    /// Gets the number of cache hits.
    /// </summary>
    public long CacheHits;

    /// <summary>
    /// Gets the number of cache misses.
    /// </summary>
    public long CacheMisses;

    /// <summary>
    /// Gets the number of create operations.
    /// </summary>
    public long CreateOperations;

    /// <summary>
    /// Gets the number of batched operations.
    /// </summary>
    public long BatchedOperations;

    /// <summary>
    /// Gets the error count.
    /// </summary>
    public long ErrorCount;

    /// <summary>
    /// Gets the average retrieval time.
    /// </summary>
    public TimeSpan AverageRetrievalTime =>
        CacheHits + CacheMisses > 0
            ? TimeSpan.FromMilliseconds(_totalRetrievalTimeMs / (CacheHits + CacheMisses))
            : TimeSpan.Zero;

    /// <summary>
    /// Gets the average creation time.
    /// </summary>
    public TimeSpan AverageCreationTime =>
        CreateOperations > 0
            ? TimeSpan.FromMilliseconds(_totalCreationTimeMs / CreateOperations)
            : TimeSpan.Zero;

    /// <summary>
    /// Records a retrieval time.
    /// </summary>
    /// <param name="time">The retrieval time</param>
    public void RecordRetrievalTime(TimeSpan time)
    {
        Interlocked.Add(ref _totalRetrievalTimeMs, (long)time.TotalMilliseconds);
    }

    /// <summary>
    /// Records a creation time.
    /// </summary>
    /// <param name="time">The creation time</param>
    public void RecordCreationTime(TimeSpan time)
    {
        Interlocked.Add(ref _totalCreationTimeMs, (long)time.TotalMilliseconds);
    }
}

/// <summary>
/// Result of snapshot preloading operation.
/// </summary>
public record SnapshotPreloadResult
{
    /// <summary>
    /// Gets the total number of streams processed.
    /// </summary>
    public required int TotalStreams { get; init; }

    /// <summary>
    /// Gets the number of successful preloads.
    /// </summary>
    public required int SuccessfulPreloads { get; init; }

    /// <summary>
    /// Gets the number of failed preloads.
    /// </summary>
    public required int FailedPreloads { get; init; }

    /// <summary>
    /// Gets the total preload time.
    /// </summary>
    public required TimeSpan PreloadTime { get; init; }

    /// <summary>
    /// Gets the preload errors.
    /// </summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalStreams > 0 ? (double)SuccessfulPreloads / TotalStreams * 100 : 0;
}

/// <summary>
/// Statistics for the snapshot optimizer.
/// </summary>
public record SnapshotOptimizerStatistics
{
    /// <summary>
    /// Gets the cache statistics.
    /// </summary>
    public required CacheStatistics CacheStatistics { get; init; }

    /// <summary>
    /// Gets the batch statistics.
    /// </summary>
    public required BatchStatistics BatchStatistics { get; init; }

    /// <summary>
    /// Gets the performance metrics per stream.
    /// </summary>
    public required IReadOnlyList<PerformanceMetrics> PerformanceMetrics { get; init; }

    /// <summary>
    /// Gets the configuration snapshot.
    /// </summary>
    public required SnapshotOptimizationConfiguration ConfigurationSnapshot { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Cache performance statistics.
/// </summary>
public record CacheStatistics
{
    /// <summary>
    /// Gets the total cache hits.
    /// </summary>
    public required long CacheHits { get; init; }

    /// <summary>
    /// Gets the total cache misses.
    /// </summary>
    public required long CacheMisses { get; init; }

    /// <summary>
    /// Gets the cache hit ratio (0.0 to 1.0).
    /// </summary>
    public required double HitRatio { get; init; }

    /// <summary>
    /// Gets the number of cached entries.
    /// </summary>
    public required int CachedEntries { get; init; }

    /// <summary>
    /// Gets the estimated memory usage in bytes.
    /// </summary>
    public required long EstimatedMemoryUsage { get; init; }
}

/// <summary>
/// Batch processing statistics.
/// </summary>
public record BatchStatistics
{
    /// <summary>
    /// Gets the number of operations currently queued.
    /// </summary>
    public required int QueuedOperations { get; init; }

    /// <summary>
    /// Gets the total number of operations processed in batches.
    /// </summary>
    public required long TotalBatchedOperations { get; init; }

    /// <summary>
    /// Gets the average batch size.
    /// </summary>
    public required double AverageBatchSize { get; init; }
}

/// <summary>
/// Result of cache optimization operation.
/// </summary>
public record CacheOptimizationResult
{
    /// <summary>
    /// Gets the number of expired entries removed.
    /// </summary>
    public required int ExpiredEntriesRemoved { get; init; }

    /// <summary>
    /// Gets the amount of memory reclaimed in bytes.
    /// </summary>
    public required long MemoryReclaimed { get; init; }

    /// <summary>
    /// Gets the time taken for optimization.
    /// </summary>
    public required TimeSpan OptimizationTime { get; init; }
}