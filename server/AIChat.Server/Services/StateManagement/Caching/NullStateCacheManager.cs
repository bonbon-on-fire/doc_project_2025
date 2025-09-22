namespace AIChat.Server.Services.StateManagement.Caching;

/// <summary>
/// Null object pattern implementation of IStateCacheManager.
/// Provides a no-operation cache that doesn't cache anything.
/// Useful for testing, debugging, or when caching should be disabled.
/// </summary>
/// <typeparam name="T">The type of entity that would be cached</typeparam>
public class NullStateCacheManager<T> : IStateCacheManager<T> where T : class
{
    /// <summary>
    /// Always returns null as no caching is performed.
    /// </summary>
    public Task<T?> GetFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<T?>(null);
    }

    /// <summary>
    /// No-operation implementation that doesn't cache anything.
    /// </summary>
    public Task SetCacheAsync(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-operation implementation as nothing is cached.
    /// </summary>
    public Task RemoveFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-operation implementation as nothing is cached.
    /// </summary>
    public Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Always returns false as no caching is performed.
    /// </summary>
    public Task<bool> ExistsInCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <summary>
    /// No-operation implementation as nothing is cached.
    /// </summary>
    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-operation implementation as nothing is cached.
    /// </summary>
    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns empty cache statistics since no caching is performed.
    /// </summary>
    public Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new CacheStatistics
        {
            HitCount = 0,
            MissCount = 0,
            EntryCount = 0,
            EstimatedMemoryUsage = 0,
            CollectedAt = DateTime.UtcNow,
            AdditionalMetrics = new Dictionary<string, object>
            {
                ["CacheType"] = "Null",
                ["EntityType"] = typeof(T).Name,
                ["Description"] = "No-operation cache implementation"
            }
        };

        return Task.FromResult(statistics);
    }
}