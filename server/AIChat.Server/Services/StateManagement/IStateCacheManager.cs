namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Interface for cache management operations in state management.
/// Follows the Interface Segregation Principle by providing only cache-related operations.
/// </summary>
/// <typeparam name="T">The type of entity being cached</typeparam>
public interface IStateCacheManager<T> where T : class
{
    /// <summary>
    /// Gets an entity from the cache.
    /// </summary>
    /// <param name="key">The cache key for the entity</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The cached entity if found, null otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<T?> GetFromCacheAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets an entity in the cache.
    /// </summary>
    /// <param name="key">The cache key for the entity</param>
    /// <param name="value">The entity to cache</param>
    /// <param name="expiry">Optional expiration time for the cache entry</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty, or value is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task SetCacheAsync(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an entity from the cache.
    /// </summary>
    /// <param name="key">The cache key for the entity to remove</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task RemoveFromCacheAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes multiple entities from the cache.
    /// </summary>
    /// <param name="keys">The collection of cache keys to remove</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when keys is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an entity exists in the cache.
    /// </summary>
    /// <param name="key">The cache key to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the entity exists in cache, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when key is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> ExistsInCacheAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cache entries for the entity type.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task InvalidateAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cache entries matching a specific pattern.
    /// </summary>
    /// <param name="pattern">The pattern to match against cache keys</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when pattern is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics for monitoring and observability.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Cache statistics information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents cache statistics for monitoring and observability.
/// </summary>
public record CacheStatistics
{
    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long HitCount { get; init; }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long MissCount { get; init; }

    /// <summary>
    /// Gets the cache hit ratio (between 0.0 and 1.0).
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)HitCount / TotalRequests : 0.0;

    /// <summary>
    /// Gets the total number of cache requests.
    /// </summary>
    public long TotalRequests => HitCount + MissCount;

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    public long EntryCount { get; init; }

    /// <summary>
    /// Gets the estimated memory usage of the cache in bytes.
    /// </summary>
    public long EstimatedMemoryUsage { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets additional cache-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Creates cache statistics with zero values.
    /// </summary>
    /// <returns>Empty cache statistics</returns>
    public static CacheStatistics Empty()
    {
        return new CacheStatistics
        {
            HitCount = 0,
            MissCount = 0,
            EntryCount = 0,
            EstimatedMemoryUsage = 0
        };
    }
}
