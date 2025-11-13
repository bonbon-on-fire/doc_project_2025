using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AIChat.Server.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

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
    private readonly MemoryStateCacheConfiguration _configuration;
    private readonly ConcurrentDictionary<string, DateTime> _keyTracker;
    private readonly CacheMetricsCollector? _metrics;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MemoryStateCacheManager class.
    /// </summary>
    /// <param name="memoryCache">The underlying memory cache</param>
    /// <param name="logger">The logger instance</param>
    /// <param name="configuration">Configuration options for cache behavior</param>
    public MemoryStateCacheManager(
        IMemoryCache memoryCache,
        ILogger<MemoryStateCacheManager<T>> logger,
        IOptions<MemoryStateCacheConfiguration> configuration)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _keyTracker = new ConcurrentDictionary<string, DateTime>();
        _metrics = _configuration.EnableMetricsCollection ? new CacheMetricsCollector() : null;
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
                _metrics?.RecordHit();
                if (_configuration.EnableDetailedLogging)
                {
                    _logger.LogDebug("Cache hit for {EntityType} key {Key}", typeof(T).Name, key);
                }
            }
            else
            {
                _metrics?.RecordMiss();
                if (_configuration.EnableDetailedLogging)
                {
                    _logger.LogDebug("Cache miss for {EntityType} key {Key}", typeof(T).Name, key);
                }
            }

            return Task.FromResult(cached);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {EntityType} from cache with key {Key}", typeof(T).Name, key);
            _metrics?.RecordMiss(); // Treat errors as misses
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
                // Use configured default expiration
                _ = options.SetSlidingExpiration(_configuration.GetDefaultSlidingExpiration());
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

            if (_configuration.EnableDetailedLogging)
            {
                _logger.LogDebug("Cached {EntityType} with key {Key}, expiry: {Expiry}",
                    typeof(T).Name, key, expiry?.ToString() ?? $"sliding {_configuration.DefaultSlidingExpirationMinutes}m");
            }

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

            if (_configuration.EnableDetailedLogging)
            {
                _logger.LogDebug("Removed {EntityType} from cache with key {Key}", typeof(T).Name, key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing {EntityType} from cache with key {Key}", typeof(T).Name, key);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes multiple entities from the cache using optimized bulk operations.
    /// </summary>
    public async Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        ThrowIfDisposed();

        var keyList = keys.ToList();
        if (_configuration.EnableDetailedLogging)
        {
            _logger.LogDebug("Bulk removing {Count} {EntityType} entries from cache", keyList.Count, typeof(T).Name);
        }

        if (keyList.Count == 0)
        {
            return;
        }

        try
        {
            // Optimize for bulk removal: batch operations and reduce logging overhead
            var removedCount = 0;
            var failedCount = 0;

            // Process in parallel for better performance on large batches
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _configuration.GetEffectiveMaxDegreeOfParallelism()
            };

            await Parallel.ForEachAsync(keyList, parallelOptions, async (key, ct) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        Interlocked.Increment(ref failedCount);
                        return;
                    }

                    var cacheKey = GetCacheKey(key);
                    _memoryCache.Remove(cacheKey);
                    _keyTracker.TryRemove(key, out _);

                    Interlocked.Increment(ref removedCount);
                    await Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove {EntityType} cache entry with key {Key}", typeof(T).Name, key);
                    Interlocked.Increment(ref failedCount);
                }
            });

            if (_configuration.EnableDetailedLogging)
            {
                _logger.LogDebug("Bulk removal completed for {EntityType}: {RemovedCount} successful, {FailedCount} failed",
                    typeof(T).Name, removedCount, failedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk cache removal for {EntityType}", typeof(T).Name);
            // Fall back to sequential removal on parallel processing errors
            foreach (var key in keyList)
            {
                try
                {
                    await RemoveFromCacheAsync(key, cancellationToken);
                }
                catch (Exception seqEx)
                {
                    _logger.LogWarning(seqEx, "Sequential fallback failed for key {Key}", key);
                }
            }
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
            if (_configuration.EnableDetailedLogging)
            {
                _logger.LogDebug("Invalidating all {Count} {EntityType} cache entries", keysToRemove.Count, typeof(T).Name);
            }

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

            if (_configuration.EnableDetailedLogging)
            {
                _logger.LogDebug("Invalidating {Count} {EntityType} cache entries matching pattern {Pattern}",
                    matchingKeys.Count, typeof(T).Name, pattern);
            }

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
                HitCount = _metrics?.HitCount ?? 0,
                MissCount = _metrics?.MissCount ?? 0,
                EntryCount = _keyTracker.Count,
                EstimatedMemoryUsage = EstimateMemoryUsage(),
                CollectedAt = DateTime.UtcNow,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["CacheType"] = "Memory",
                    ["EntityType"] = typeof(T).Name,
                    ["OldestEntryAge"] = GetOldestEntryAge(),
                    ["AverageEntryAge"] = GetAverageEntryAge(),
                    ["MetricsEnabled"] = _configuration.EnableMetricsCollection,
                    ["DetailedLoggingEnabled"] = _configuration.EnableDetailedLogging
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
                    if (_configuration.EnableDetailedLogging)
                    {
                        _logger.LogDebug("Disposing MemoryStateCacheManager for {EntityType}", typeof(T).Name);
                    }

                    // Clear tracking data
                    if (_configuration.EnableKeyTrackerCleanup)
                    {
                        _keyTracker.Clear();
                    }

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
        try
        {
            // Improved memory estimation based on actual object analysis
            var entryCount = _keyTracker.Count;
            if (entryCount == 0)
            {
                return 0L;
            }

            var totalMemory = 0L;
            var sampleCount = 0;
            // Use configured sample size for estimation

            // Sample cache entries to estimate average size using configured sample size
            var maxSamples = _configuration.MemoryEstimationSampleSize;
            foreach (var kvp in _keyTracker.Take(maxSamples))
            {
                var cacheKey = GetCacheKey(kvp.Key);
                if (_memoryCache.TryGetValue(cacheKey, out var cachedValue) && cachedValue is T value)
                {
                    totalMemory += EstimateObjectSize(value);
                    totalMemory += EstimateStringSize(kvp.Key); // Key overhead
                    totalMemory += 64; // DateTime and dictionary overhead per entry
                    sampleCount++;
                }
            }

            if (sampleCount == 0)
            {
                // Fallback to configured conservative estimate if no samples available
                return entryCount * _configuration.FallbackMemoryEstimationBytes;
            }

            // Calculate average size and apply to total entries
            var averageEntrySize = totalMemory / sampleCount;
            return averageEntrySize * entryCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error estimating memory usage, falling back to conservative estimate");
            // Fallback to configured conservative estimate on any error
            return _keyTracker.Count * _configuration.FallbackMemoryEstimationBytes;
        }
    }

    /// <summary>
    /// Estimates the memory size of an object using JSON serialization as a proxy.
    /// This provides a reasonable approximation of in-memory object size.
    /// </summary>
    private long EstimateObjectSize(T obj)
    {
        try
        {
            // Use JSON serialization as a reasonable proxy for object complexity
            var json = System.Text.Json.JsonSerializer.Serialize(obj);
            var jsonBytes = System.Text.Encoding.UTF8.GetByteCount(json);

            // Apply configured object overhead factor and base overhead
            return (long)(jsonBytes * _configuration.ObjectOverheadFactor) + _configuration.BaseObjectOverheadBytes;
        }
        catch
        {
            // If serialization fails, use conservative estimate based on type
            return typeof(T).IsValueType ? 32L : 256L;
        }
    }

    /// <summary>
    /// Estimates the memory size of a string including overhead.
    /// </summary>
    private static long EstimateStringSize(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return 24; // Empty string overhead
        }

        // String overhead: 2 bytes per char + object header + length field
        return (str.Length * 2) + 24;
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
/// Optimized with atomic operations for high-performance concurrent access.
/// </summary>
public class CacheMetricsCollector
{
    private long _hitCount;
    private long _missCount;

    /// <summary>
    /// Gets the total number of cache hits.
    /// Uses atomic read operation for thread safety without locking.
    /// </summary>
    public long HitCount => Interlocked.Read(ref _hitCount);

    /// <summary>
    /// Gets the total number of cache misses.
    /// Uses atomic read operation for thread safety without locking.
    /// </summary>
    public long MissCount => Interlocked.Read(ref _missCount);

    /// <summary>
    /// Records a cache hit using atomic increment for optimal performance.
    /// </summary>
    public void RecordHit()
    {
        Interlocked.Increment(ref _hitCount);
    }

    /// <summary>
    /// Records a cache miss using atomic increment for optimal performance.
    /// </summary>
    public void RecordMiss()
    {
        Interlocked.Increment(ref _missCount);
    }

    /// <summary>
    /// Resets all metrics to zero using atomic exchange operations.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _hitCount, 0);
        Interlocked.Exchange(ref _missCount, 0);
    }
}
