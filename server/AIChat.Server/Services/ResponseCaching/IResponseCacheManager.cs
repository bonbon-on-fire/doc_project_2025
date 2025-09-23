using AIChat.Server.Services.StateManagement;

namespace AIChat.Server.Services.ResponseCaching;

/// <summary>
/// Interface for intelligent response caching that integrates with the Orleans router pattern.
/// Provides high-level caching operations for router responses with built-in invalidation strategies.
/// Follows the Interface Segregation Principle by focusing on response-specific cache operations.
/// </summary>
public interface IResponseCacheManager : IDisposable
{
    /// <summary>
    /// Gets a cached response for the specified cache key.
    /// </summary>
    /// <typeparam name="T">The type of response being cached</typeparam>
    /// <param name="cacheKey">The cache key for the response</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The cached response if found, null otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when cacheKey is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<T?> GetCachedResponseAsync<T>(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a response in the cache with the specified policy.
    /// </summary>
    /// <typeparam name="T">The type of response being cached</typeparam>
    /// <param name="cacheKey">The cache key for the response</param>
    /// <param name="response">The response to cache</param>
    /// <param name="policy">The cache policy defining expiration and invalidation behavior</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when cacheKey, response, or policy is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task SetCachedResponseAsync<T>(string cacheKey, T response, CachePolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a specific cached response.
    /// </summary>
    /// <param name="cacheKey">The cache key to invalidate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when cacheKey is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task InvalidateResponseAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached responses matching the specified pattern.
    /// Useful for bulk invalidation when related data changes.
    /// </summary>
    /// <param name="pattern">The pattern to match against cache keys (supports wildcards)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when pattern is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task InvalidateResponsePatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive statistics about response cache performance.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Cache statistics including hit ratios, operation metrics, and performance data</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ResponseCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check of the response cache system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status including performance metrics and system state</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ResponseCacheHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Warms the cache with frequently accessed responses to improve initial performance.
    /// </summary>
    /// <param name="warmupOperations">Collection of operations to pre-cache</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when warmupOperations is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task WarmCacheAsync(IEnumerable<CacheWarmupOperation> warmupOperations, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents comprehensive statistics for response cache performance monitoring.
/// </summary>
public record ResponseCacheStatistics : CacheStatistics
{
    /// <summary>
    /// Gets per-operation cache metrics for detailed analysis.
    /// </summary>
    public required Dictionary<string, CacheOperationMetrics> OperationMetrics { get; init; }

    /// <summary>
    /// Gets per-router cache metrics for router-specific analysis.
    /// </summary>
    public required Dictionary<string, RouterCacheMetrics> RouterMetrics { get; init; }

    /// <summary>
    /// Gets the average latency for cache operations in milliseconds.
    /// </summary>
    public double AverageCacheLatencyMs { get; init; }

    /// <summary>
    /// Gets the total bytes stored in the response cache.
    /// </summary>
    public long TotalBytesStored { get; init; }

    /// <summary>
    /// Gets the cache efficiency score (0.0 to 1.0) based on hit ratio and performance.
    /// </summary>
    public double EfficiencyScore { get; init; }

    /// <summary>
    /// Gets the number of cache invalidations performed.
    /// </summary>
    public long InvalidationCount { get; init; }

    /// <summary>
    /// Gets the number of cache evictions due to memory pressure.
    /// </summary>
    public long EvictionCount { get; init; }
}

/// <summary>
/// Represents cache metrics for a specific operation type.
/// </summary>
public record CacheOperationMetrics
{
    /// <summary>
    /// Gets the operation name (e.g., "GetModes", "GetChatHistory").
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// Gets the number of cache hits for this operation.
    /// </summary>
    public long HitCount { get; init; }

    /// <summary>
    /// Gets the number of cache misses for this operation.
    /// </summary>
    public long MissCount { get; init; }

    /// <summary>
    /// Gets the cache hit ratio for this operation (0.0 to 1.0).
    /// </summary>
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0.0;

    /// <summary>
    /// Gets the average response time improvement when cache hits (in milliseconds).
    /// </summary>
    public double AverageTimeSavedMs { get; init; }

    /// <summary>
    /// Gets the average cache operation latency for this operation (in milliseconds).
    /// </summary>
    public double AverageLatencyMs { get; init; }

    /// <summary>
    /// Gets the timestamp of last cache access for this operation.
    /// </summary>
    public DateTime LastAccessTime { get; init; }
}

/// <summary>
/// Represents cache metrics for a specific router type.
/// </summary>
public record RouterCacheMetrics
{
    /// <summary>
    /// Gets the router type name (e.g., "ModeRouter", "ChatRouter").
    /// </summary>
    public required string RouterType { get; init; }

    /// <summary>
    /// Gets the total number of cached operations for this router.
    /// </summary>
    public long TotalOperations { get; init; }

    /// <summary>
    /// Gets the number of cache hits for this router.
    /// </summary>
    public long HitCount { get; init; }

    /// <summary>
    /// Gets the number of cache misses for this router.
    /// </summary>
    public long MissCount { get; init; }

    /// <summary>
    /// Gets the cache hit ratio for this router (0.0 to 1.0).
    /// </summary>
    public double HitRatio => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0.0;

    /// <summary>
    /// Gets the estimated memory usage for this router's cache entries (in bytes).
    /// </summary>
    public long EstimatedMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of invalidations performed for this router.
    /// </summary>
    public long InvalidationCount { get; init; }
}

/// <summary>
/// Represents the health status of the response cache system.
/// </summary>
public record ResponseCacheHealthStatus
{
    /// <summary>
    /// Gets whether the cache system is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the current cache performance score (0.0 to 1.0).
    /// </summary>
    public double PerformanceScore { get; init; }

    /// <summary>
    /// Gets the cache hit ratio across all operations.
    /// </summary>
    public double OverallHitRatio { get; init; }

    /// <summary>
    /// Gets the current memory usage as a percentage of available memory.
    /// </summary>
    public double MemoryUsagePercentage { get; init; }

    /// <summary>
    /// Gets any health warnings or issues.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets detailed health status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a cache warmup operation for pre-loading frequently accessed data.
/// </summary>
public record CacheWarmupOperation
{
    /// <summary>
    /// Gets the cache key for the operation to warm up.
    /// </summary>
    public required string CacheKey { get; init; }

    /// <summary>
    /// Gets the operation to execute to generate the cached value.
    /// </summary>
    public required Func<CancellationToken, Task<object?>> Operation { get; init; }

    /// <summary>
    /// Gets the cache policy to use for the warmed-up entry.
    /// </summary>
    public required CachePolicy Policy { get; init; }

    /// <summary>
    /// Gets the priority of this warmup operation (higher values are warmed first).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Gets optional metadata about this warmup operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}