using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Services;

/// <summary>
/// Bounded cache manager implementation with LRU eviction and memory management.
/// Prevents memory leaks by enforcing both item count and memory size limits.
/// Thread-safe implementation using concurrent collections and locks.
/// </summary>
public class BoundedModeCacheManager : IModeCacheManager
{
    private readonly int _maxItems;
    private readonly long _maxMemoryBytes;
    private readonly TimeSpan _defaultTtl;
    private readonly LinkedList<CacheEntry> _lruList;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cache;
    private readonly object _lock = new();
    private readonly ILogger<BoundedModeCacheManager> _logger;

    // Statistics tracking
    private long _currentMemoryBytes;
    private long _totalHits;
    private long _totalMisses;
    private long _totalEvictions;

    // JSON serializer options for size estimation
    private static readonly JsonSerializerOptions SizeEstimationOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the BoundedModeCacheManager.
    /// </summary>
    /// <param name="maxItems">Maximum number of items to store (default: 1000)</param>
    /// <param name="maxMemoryBytes">Maximum memory usage in bytes (default: 100MB)</param>
    /// <param name="defaultTtl">Default time-to-live for cached items (default: 30 minutes)</param>
    /// <param name="logger">Logger for diagnostics</param>
    public BoundedModeCacheManager(
        int maxItems = 1000,
        long maxMemoryBytes = 100_000_000, // 100MB
        TimeSpan? defaultTtl = null,
        ILogger<BoundedModeCacheManager>? logger = null)
    {
        _maxItems = maxItems;
        _maxMemoryBytes = maxMemoryBytes;
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(30);
        _lruList = new();
        _cache = [];
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BoundedModeCacheManager>.Instance;

        _logger.LogInformation(
            "Initialized BoundedModeCacheManager with MaxItems: {MaxItems}, MaxMemory: {MaxMemoryMB}MB, DefaultTTL: {DefaultTtl}",
            _maxItems, _maxMemoryBytes / 1024 / 1024, _defaultTtl);
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_lock)
        {
            try
            {
                // Remove existing entry if present
                if (_cache.TryGetValue(key, out var existingNode))
                {
                    _currentMemoryBytes -= existingNode.Value.SizeBytes;
                    _lruList.Remove(existingNode);
                    _cache.Remove(key);
                }

                // Create new cache entry
                var entry = new CacheEntry
                {
                    Key = key,
                    Value = value!,
                    SizeBytes = EstimateSize(value),
                    ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : DateTime.UtcNow.Add(_defaultTtl),
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow
                };

                // Evict items if necessary to make space
                EvictIfNecessary(entry.SizeBytes);

                // Add new entry
                var node = _lruList.AddFirst(entry);
                _cache[key] = node;
                _currentMemoryBytes += entry.SizeBytes;

                _logger.LogDebug("Cached item with key '{Key}', size {Size} bytes. Total items: {Count}, memory: {Memory} bytes",
                    key, entry.SizeBytes, _cache.Count, _currentMemoryBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cache item with key '{Key}'", key);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public T? Get<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_lock)
        {
            try
            {
                if (!_cache.TryGetValue(key, out var node))
                {
                    _totalMisses++;
                    return default;
                }

                var entry = node.Value;

                // Check if expired
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
                {
                    // Remove expired entry
                    _currentMemoryBytes -= entry.SizeBytes;
                    _lruList.Remove(node);
                    _cache.Remove(key);
                    _totalMisses++;
                    _totalEvictions++;

                    _logger.LogDebug("Removed expired cache entry with key '{Key}'", key);
                    return default;
                }

                // Move to front of LRU list (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                entry.LastAccessedAt = DateTime.UtcNow;

                _totalHits++;

                _logger.LogDebug("Cache hit for key '{Key}'", key);
                return (T)entry.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve cached item with key '{Key}'", key);
                _totalMisses++;
                return default;
            }
        }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_lock)
        {
            try
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _currentMemoryBytes -= node.Value.SizeBytes;
                    _lruList.Remove(node);
                    _cache.Remove(key);

                    _logger.LogDebug("Removed cache entry with key '{Key}'", key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove cached item with key '{Key}'", key);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                var itemCount = _cache.Count;
                var memoryUsage = _currentMemoryBytes;

                _cache.Clear();
                _lruList.Clear();
                _currentMemoryBytes = 0;

                _logger.LogInformation("Cleared cache. Removed {ItemCount} items, freed {MemoryMB}MB",
                    itemCount, memoryUsage / 1024 / 1024);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache");
                throw;
            }
        }
    }

    /// <inheritdoc />
    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            var totalRequests = _totalHits + _totalMisses;
            var hitRate = totalRequests > 0 ? (_totalHits * 100.0) / totalRequests : 0.0;

            return new CacheStatistics
            {
                ItemCount = _cache.Count,
                MemoryUsageBytes = _currentMemoryBytes,
                HitRatePercentage = hitRate,
                TotalHits = _totalHits,
                TotalMisses = _totalMisses,
                TotalEvictions = _totalEvictions,
                MaxMemoryBytes = _maxMemoryBytes,
                MaxItems = _maxItems
            };
        }
    }

    /// <inheritdoc />
    public void PerformMaintenance()
    {
        lock (_lock)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = new List<string>();

                // Find expired entries
                foreach (var kvp in _cache)
                {
                    var entry = kvp.Value.Value;
                    if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < now)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                // Remove expired entries
                foreach (var key in expiredKeys)
                {
                    if (_cache.TryGetValue(key, out var node))
                    {
                        _currentMemoryBytes -= node.Value.SizeBytes;
                        _lruList.Remove(node);
                        _cache.Remove(key);
                        _totalEvictions++;
                    }
                }

                if (expiredKeys.Count > 0)
                {
                    _logger.LogDebug("Cache maintenance removed {ExpiredCount} expired items", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform cache maintenance");
                throw;
            }
        }
    }

    /// <summary>
    /// Evicts items if necessary to make space for a new entry.
    /// Uses LRU eviction strategy.
    /// </summary>
    /// <param name="newItemSize">Size of the new item to be added</param>
    private void EvictIfNecessary(long newItemSize)
    {
        // Evict based on item count limit
        while (_cache.Count >= _maxItems && _lruList.Count > 0)
        {
            EvictOldest("item count limit");
        }

        // Evict based on memory limit
        while (_currentMemoryBytes + newItemSize > _maxMemoryBytes && _lruList.Count > 0)
        {
            EvictOldest("memory limit");
        }
    }

    /// <summary>
    /// Evicts the oldest (least recently used) item from the cache.
    /// </summary>
    /// <param name="reason">Reason for eviction (for logging)</param>
    private void EvictOldest(string reason)
    {
        var oldest = _lruList.Last;
        if (oldest != null)
        {
            _cache.Remove(oldest.Value.Key);
            _currentMemoryBytes -= oldest.Value.SizeBytes;
            _lruList.RemoveLast();
            _totalEvictions++;

            _logger.LogDebug("Evicted cache entry '{Key}' due to {Reason}",
                oldest.Value.Key, reason);
        }
    }

    /// <summary>
    /// Estimates the memory size of an object using JSON serialization.
    /// This is an approximation and could be enhanced for better accuracy.
    /// </summary>
    /// <param name="obj">Object to estimate size for</param>
    /// <returns>Estimated size in bytes</returns>
    private static long EstimateSize(object? obj)
    {
        if (obj == null)
        {
            return 0;
        }

        try
        {
            // Serialize to JSON and estimate size
            var json = JsonSerializer.Serialize(obj, SizeEstimationOptions);
            return json.Length * 2; // Approximate bytes (UTF-16 encoding)
        }
        catch
        {
            // Fallback to a basic estimation
            return obj.ToString()?.Length * 2 ?? 0;
        }
    }
}