
namespace AIChat.Orleans.Services;

/// <summary>
/// Interface for managing mode-specific caching operations.
/// Provides bounded caching with LRU eviction and memory management.
/// </summary>
public interface IModeCacheManager
{
    /// <summary>
    /// Sets a value in the cache with optional time-to-live.
    /// </summary>
    /// <typeparam name="T">Type of the value to cache</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to cache</param>
    /// <param name="ttl">Optional time-to-live. If null, uses default TTL.</param>
    void Set<T>(string key, T value, TimeSpan? ttl = null);

    /// <summary>
    /// Gets a value from the cache.
    /// </summary>
    /// <typeparam name="T">Type of the value to retrieve</typeparam>
    /// <param name="key">Cache key</param>
    /// <returns>Cached value or default if not found/expired</returns>
    T? Get<T>(string key);

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    /// <param name="key">Cache key to remove</param>
    void Remove(string key);

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    /// <returns>Cache statistics including hit rate, memory usage, etc.</returns>
    CacheStatistics GetStatistics();

    /// <summary>
    /// Performs cache maintenance (eviction of expired items).
    /// </summary>
    void PerformMaintenance();
}

/// <summary>
/// Cache statistics for monitoring and diagnostics.
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of items in cache.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Estimated memory usage in bytes.
    /// </summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>
    /// Cache hit rate percentage.
    /// </summary>
    public double HitRatePercentage { get; set; }

    /// <summary>
    /// Total number of cache hits.
    /// </summary>
    public long TotalHits { get; set; }

    /// <summary>
    /// Total number of cache misses.
    /// </summary>
    public long TotalMisses { get; set; }

    /// <summary>
    /// Total number of evictions performed.
    /// </summary>
    public long TotalEvictions { get; set; }

    /// <summary>
    /// Maximum memory usage configured.
    /// </summary>
    public long MaxMemoryBytes { get; set; }

    /// <summary>
    /// Maximum item count configured.
    /// </summary>
    public int MaxItems { get; set; }
}

/// <summary>
/// Internal cache entry structure.
/// </summary>
internal sealed class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public object Value { get; set; } = null!;
    public long SizeBytes { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
}
