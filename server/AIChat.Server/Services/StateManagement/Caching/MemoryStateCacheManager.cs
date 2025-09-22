using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace AIChat.Server.Services.StateManagement.Caching;

/// <summary>
/// In-memory cache implementation for state management.
/// Uses IMemoryCache with additional features like pattern-based invalidation and metrics.
/// </summary>
/// <typeparam name="T">The type of entity being cached</typeparam>
public class MemoryStateCacheManager<T> : IStateCacheManager<T>, IDisposable where T : class
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryStateCacheManager<T>> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _keyTracker;
    private readonly CacheMetricsCollector _metrics;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MemoryStateCacheManager class.
    /// </summary>
    /// <param name="memoryCache">The underlying memory cache</param>
    /// <param name="logger">The logger instance</param>
    public MemoryStateCacheManager(
        IMemoryCache memoryCache,
        ILogger<MemoryStateCacheManager<T>> logger)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyTracker = new ConcurrentDictionary<string, DateTime>();
        _metrics = new CacheMetricsCollector();
    }

    /// <summary>
    /// Gets an entity from the cache.
    /// </summary>
    public Task<T?> GetFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
            var cached = _memoryCache.Get<T>(cacheKey);

            if (cached != null)
            {
                _metrics.RecordHit();
                _logger.LogDebug("Cache hit for {EntityType} key {Key}", typeof(T).Name, key);
            }
            else
            {
                _metrics.RecordMiss();
                _logger.LogDebug("Cache miss for {EntityType} key {Key}", typeof(T).Name, key);
            }

            return Task.FromResult(cached);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {EntityType} from cache with key {Key}", typeof(T).Name, key);
            _metrics.RecordMiss(); // Treat errors as misses
            return Task.FromResult<T?>(null);
        }
    }

    /// <summary>
    /// Sets an entity in the cache.
    /// </summary>
    public Task SetCacheAsync(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }
        ArgumentNullException.ThrowIfNull(value);

        ThrowIfDisposed();

        try
        {
            var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
            var options = new MemoryCacheEntryOptions();

            // Set expiration
            if (expiry.HasValue)
            {
                _ = options.SetAbsoluteExpiration(expiry.Value);
            }
            else
            {
                // Default expiration of 30 minutes
                _ = options.SetSlidingExpiration(TimeSpan.FromMinutes(30));
            }

            // Set eviction callback to clean up tracking
            _ = options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
            {
                var originalKey = MemoryStateCacheManager<T>.ExtractOriginalKey(evictedKey.ToString()!);
                _ = _keyTracker.TryRemove(originalKey, out _);
                _logger.LogDebug("Cache entry evicted for {EntityType} key {Key}, reason: {Reason}",
                    typeof(T).Name, originalKey, reason);
            });

            _ = _memoryCache.Set(cacheKey, value, options);
            _keyTracker[key] = DateTime.UtcNow;

            _logger.LogDebug("Cached {EntityType} with key {Key}, expiry: {Expiry}",
                typeof(T).Name, key, expiry?.ToString() ?? "sliding 30m");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting {EntityType} in cache with key {Key}", typeof(T).Name, key);
            return Task.CompletedTask; // Don't throw on cache errors
        }
    }

    /// <summary>
    /// Removes an entity from the cache.
    /// </summary>
    public Task RemoveFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
            _memoryCache.Remove(cacheKey);
            _ = _keyTracker.TryRemove(key, out _);

            _logger.LogDebug("Removed {EntityType} from cache with key {Key}", typeof(T).Name, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing {EntityType} from cache with key {Key}", typeof(T).Name, key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes multiple entities from the cache.
    /// </summary>
    public async Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        ThrowIfDisposed();

        var keyList = keys.ToList();
        _logger.LogDebug("Removing {Count} {EntityType} entries from cache", keyList.Count, typeof(T).Name);

        foreach (var key in keyList)
        {
            await RemoveFromCacheAsync(key, cancellationToken);
        }
    }

    /// <summary>
    /// Checks if an entity exists in the cache.
    /// </summary>
    public Task<bool> ExistsInCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        ThrowIfDisposed();

        try
        {
            var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
            var exists = _memoryCache.TryGetValue(cacheKey, out _);
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of {EntityType} in cache with key {Key}", typeof(T).Name, key);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Invalidates all cache entries for the entity type.
    /// </summary>
    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var keysToRemove = _keyTracker.Keys.ToList();
            _logger.LogDebug("Invalidating all {Count} {EntityType} cache entries", keysToRemove.Count, typeof(T).Name);

            foreach (var key in keysToRemove)
            {
                var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
                _memoryCache.Remove(cacheKey);
            }

            _keyTracker.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating all {EntityType} cache entries", typeof(T).Name);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Invalidates cache entries matching a specific pattern.
    /// </summary>
    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        ThrowIfDisposed();

        try
        {
            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var matchingKeys = _keyTracker.Keys.Where(key => regex.IsMatch(key)).ToList();

            _logger.LogDebug("Invalidating {Count} {EntityType} cache entries matching pattern {Pattern}",
                matchingKeys.Count, typeof(T).Name, pattern);

            foreach (var key in matchingKeys)
            {
                var cacheKey = MemoryStateCacheManager<T>.GetCacheKey(key);
                _memoryCache.Remove(cacheKey);
                _ = _keyTracker.TryRemove(key, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating {EntityType} cache entries by pattern {Pattern}",
                typeof(T).Name, pattern);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets cache statistics for monitoring and observability.
    /// </summary>
    public Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var statistics = new CacheStatistics
            {
                HitCount = _metrics.HitCount,
                MissCount = _metrics.MissCount,
                EntryCount = _keyTracker.Count,
                EstimatedMemoryUsage = EstimateMemoryUsage(),
                CollectedAt = DateTime.UtcNow,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["CacheType"] = "Memory",
                    ["EntityType"] = typeof(T).Name,
                    ["OldestEntryAge"] = GetOldestEntryAge(),
                    ["AverageEntryAge"] = GetAverageEntryAge()
                }
            };

            return Task.FromResult(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics for {EntityType}", typeof(T).Name);
            return Task.FromResult(CacheStatistics.Empty());
        }
    }

    /// <summary>
    /// Disposes the cache manager and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the cache manager.
    /// </summary>
    /// <param name="disposing">Whether this is being called from Dispose()</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            lock (_disposeLock)
            {
                if (!_disposed)
                {
                    _logger.LogDebug("Disposing MemoryStateCacheManager for {EntityType}", typeof(T).Name);

                    // Clear tracking data
                    _keyTracker.Clear();

                    _disposed = true;
                }
            }
        }
    }

    private static string GetCacheKey(string key)
    {
        return $"{typeof(T).Name}:{key}";
    }

    private static string ExtractOriginalKey(string cacheKey)
    {
        var prefix = $"{typeof(T).Name}:";
        return cacheKey.StartsWith(prefix, StringComparison.Ordinal) ? cacheKey[prefix.Length..] : cacheKey;
    }

    private long EstimateMemoryUsage()
    {
        // Rough estimation: assume each entry takes about 1KB on average
        // This is a simplification - in reality it would depend on the actual object size
        return _keyTracker.Count * 1024L;
    }

    private TimeSpan GetOldestEntryAge()
    {
        if (_keyTracker.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var oldestTime = _keyTracker.Values.Min();
        return DateTime.UtcNow - oldestTime;
    }

    private TimeSpan GetAverageEntryAge()
    {
        if (_keyTracker.IsEmpty)
        {
            return TimeSpan.Zero;
        }

        var now = DateTime.UtcNow;
        var totalAge = _keyTracker.Values.Sum(time => (now - time).TotalSeconds);
        return TimeSpan.FromSeconds(totalAge / _keyTracker.Count);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// Simple metrics collector for cache operations.
/// </summary>
public class CacheMetricsCollector
{
    private long _hitCount;
    private long _missCount;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long HitCount
    {
        get
        {
            lock (_lock)
            {
                return _hitCount;
            }
        }
    }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long MissCount
    {
        get
        {
            lock (_lock)
            {
                return _missCount;
            }
        }
    }

    /// <summary>
    /// Records a cache hit.
    /// </summary>
    public void RecordHit()
    {
        lock (_lock)
        {
            _hitCount++;
        }
    }

    /// <summary>
    /// Records a cache miss.
    /// </summary>
    public void RecordMiss()
    {
        lock (_lock)
        {
            _missCount++;
        }
    }

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _hitCount = 0;
            _missCount = 0;
        }
    }
}