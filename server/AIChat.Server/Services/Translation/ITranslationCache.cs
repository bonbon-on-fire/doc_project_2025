using System.Collections.Concurrent;

namespace AIChat.Server.Services.Translation;

/// <summary>
/// Simple in-memory implementation of translation cache for improved performance.
/// </summary>
public class MemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<string, CacheItem> _cache = new();
    private readonly Timer _cleanupTimer;
    private readonly TranslationOptions _options;
    private readonly ILogger<MemoryTranslationCache> _logger;
    private long _totalRequests;
    private long _cacheHits;
    private long _cacheMisses;

    /// <summary>
    /// Initializes a new instance of the MemoryTranslationCache.
    /// </summary>
    /// <param name="options">Translation options</param>
    /// <param name="logger">Logger instance</param>
    public MemoryTranslationCache(TranslationOptions options, ILogger<MemoryTranslationCache> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set up cleanup timer to run every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredItems, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableCaching)
        {
            return default;
        }

        Interlocked.Increment(ref _totalRequests);

        if (_cache.TryGetValue(key, out var item))
        {
            if (item.ExpiresAt > DateTime.UtcNow)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("Cache hit for key: {Key}", key);

                if (item.Value is T typedValue)
                {
                    return typedValue;
                }

                try
                {
                    return (T)Convert.ChangeType(item.Value, typeof(T));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert cached value to type {Type} for key: {Key}", typeof(T).Name, key);
                }
            }
            else
            {
                // Item expired, remove it
                _cache.TryRemove(key, out _);
                _logger.LogDebug("Cache item expired and removed for key: {Key}", key);
            }
        }

        Interlocked.Increment(ref _cacheMisses);
        _logger.LogDebug("Cache miss for key: {Key}", key);
        return default;
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableCaching || value == null)
        {
            return;
        }

        // Check cache size limit
        if (_cache.Count >= _options.MaxCacheSize)
        {
            // Remove some old entries to make room
            RemoveOldestEntries(_options.MaxCacheSize / 4); // Remove 25% to make room
        }

        var expiresAt = DateTime.UtcNow.Add(expiration);
        var cacheItem = new CacheItem(value, expiresAt);

        _cache.AddOrUpdate(key, cacheItem, (_, _) => cacheItem);
        _logger.LogDebug("Cached value for key: {Key}, expires at: {ExpiresAt}", key, expiresAt);
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var keysToRemove = new List<string>();

        foreach (var key in _cache.Keys)
        {
            if (IsPatternMatch(key, pattern))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        _logger.LogInformation("Invalidated {Count} cache entries matching pattern: {Pattern}", keysToRemove.Count, pattern);
    }

    /// <inheritdoc />
    public async Task<CacheStatistics> GetStatisticsAsync()
    {
        return new CacheStatistics
        {
            TotalRequests = _totalRequests,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            ItemCount = _cache.Count,
            EstimatedMemoryUsage = EstimateMemoryUsage(),
            LastCleanupTime = DateTime.UtcNow // We don't track this separately in this simple implementation
        };
    }

    private void CleanupExpiredItems(object? state)
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAt <= now)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired cache entries", keysToRemove.Count);
        }
    }

    private void RemoveOldestEntries(int countToRemove)
    {
        var itemsToRemove = _cache
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(countToRemove)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in itemsToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        _logger.LogDebug("Removed {Count} oldest cache entries to make room", itemsToRemove.Count);
    }

    private static bool IsPatternMatch(string text, string pattern)
    {
        // Simple wildcard matching (supports * at the end)
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return text.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private long EstimateMemoryUsage()
    {
        // Rough estimation: each cache item takes approximately 100 bytes plus content size
        const int overheadPerItem = 100;
        long totalSize = _cache.Count * overheadPerItem;

        // This is a very rough estimate since we can't easily calculate object sizes
        // In a production system, you might want to use a more sophisticated approach
        return totalSize;
    }

    /// <summary>
    /// Disposes the cache and cleanup timer.
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cache.Clear();
    }

    private sealed class CacheItem
    {
        public object Value { get; }
        public DateTime ExpiresAt { get; }
        public DateTime CreatedAt { get; }

        public CacheItem(object value, DateTime expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
            CreatedAt = DateTime.UtcNow;
        }
    }
}

/// <summary>
/// Null object pattern implementation of translation cache.
/// Used when caching is disabled.
/// </summary>
public class NullTranslationCache : ITranslationCache
{
    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateAsync(string pattern, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CacheStatistics> GetStatisticsAsync()
    {
        return Task.FromResult(new CacheStatistics());
    }

    /// <summary>
    /// No-op dispose for null cache.
    /// </summary>
    public void Dispose()
    {
        // No resources to dispose
    }
}