namespace AIChat.Server.Services.Translation;

/// <summary>
/// Main service for protocol translation operations.
/// Orchestrates multiple translators to provide seamless conversion between different protocol formats.
/// </summary>
public interface IProtocolTranslationService
{
    /// <summary>
    /// Translates a message from one protocol format to another.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <param name="source">Source message to translate</param>
    /// <param name="context">Translation context containing additional information</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Translation result containing the converted message or error information</returns>
    Task<TranslationResult<TTarget>> TranslateAsync<TSource, TTarget>(
        TSource source,
        TranslationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates multiple messages in a batch operation for improved performance.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <param name="sources">Source messages to translate</param>
    /// <param name="context">Translation context containing additional information</param>
    /// <param name="cancellationToken">Token to cancel the translation operation</param>
    /// <returns>Collection of translation results</returns>
    Task<IEnumerable<TranslationResult<TTarget>>> TranslateBatchAsync<TSource, TTarget>(
        IEnumerable<TSource> sources,
        TranslationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a translation from the source type to target type is supported.
    /// </summary>
    /// <typeparam name="TSource">Source message type</typeparam>
    /// <typeparam name="TTarget">Target message type</typeparam>
    /// <returns>True if translation is supported, false otherwise</returns>
    bool CanTranslate<TSource, TTarget>();

    /// <summary>
    /// Determines whether a translation from the source type to target type is supported.
    /// </summary>
    /// <param name="sourceType">Source message type</param>
    /// <param name="targetType">Target message type</param>
    /// <returns>True if translation is supported, false otherwise</returns>
    bool CanTranslate(Type sourceType, Type targetType);

    /// <summary>
    /// Gets all supported translation pairs.
    /// </summary>
    /// <returns>Collection of supported source-to-target type pairs</returns>
    IEnumerable<(Type SourceType, Type TargetType)> GetSupportedTranslations();

    /// <summary>
    /// Gets the names of all registered translators.
    /// </summary>
    /// <returns>Collection of translator names</returns>
    IEnumerable<string> GetRegisteredTranslators();

    /// <summary>
    /// Gets comprehensive metrics for all translation operations.
    /// </summary>
    /// <returns>Translation metrics aggregated across all translators</returns>
    Task<TranslationMetrics> GetMetricsAsync();

    /// <summary>
    /// Gets metrics for a specific translator.
    /// </summary>
    /// <param name="translatorName">Name of the translator</param>
    /// <returns>Translation metrics for the specified translator</returns>
    Task<TranslationMetrics?> GetTranslatorMetricsAsync(string translatorName);

    /// <summary>
    /// Resets metrics for all translators.
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    Task ResetMetricsAsync();

    /// <summary>
    /// Validates the health of all registered translators.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the health check operation</param>
    /// <returns>Health check result</returns>
    Task<TranslationHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Caching layer for translation results to improve performance.
/// </summary>
public interface ITranslationCache : IDisposable
{
    /// <summary>
    /// Gets a cached translation result.
    /// </summary>
    /// <typeparam name="T">Type of the cached result</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached result or null if not found</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a translation result in the cache.
    /// </summary>
    /// <typeparam name="T">Type of the result to cache</typeparam>
    /// <param name="key">Cache key</param>
    /// <param name="value">Value to cache</param>
    /// <param name="expiration">Cache expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes cached results matching the specified pattern.
    /// </summary>
    /// <param name="pattern">Pattern to match for cache invalidation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task InvalidateAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>Cache statistics</returns>
    Task<CacheStatistics> GetStatisticsAsync();
}

/// <summary>
/// Health check result for translation services.
/// </summary>
public sealed class TranslationHealthCheckResult
{
    /// <summary>
    /// Overall health status.
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// Description of the health check result.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Duration of the health check operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Additional data about the health check.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = [];

    /// <summary>
    /// Individual translator health results.
    /// </summary>
    public Dictionary<string, TranslatorHealthResult> TranslatorResults { get; set; } = [];

    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    /// <param name="description">Health check description</param>
    /// <param name="duration">Check duration</param>
    /// <returns>Healthy result</returns>
    public static TranslationHealthCheckResult Healthy(string description = "All translators are healthy", TimeSpan duration = default)
    {
        return new TranslationHealthCheckResult
        {
            Status = HealthStatus.Healthy,
            Description = description,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    /// <param name="description">Health check description</param>
    /// <param name="duration">Check duration</param>
    /// <returns>Unhealthy result</returns>
    public static TranslationHealthCheckResult Unhealthy(string description, TimeSpan duration = default)
    {
        return new TranslationHealthCheckResult
        {
            Status = HealthStatus.Unhealthy,
            Description = description,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates a degraded result.
    /// </summary>
    /// <param name="description">Health check description</param>
    /// <param name="duration">Check duration</param>
    /// <returns>Degraded result</returns>
    public static TranslationHealthCheckResult Degraded(string description, TimeSpan duration = default)
    {
        return new TranslationHealthCheckResult
        {
            Status = HealthStatus.Degraded,
            Description = description,
            Duration = duration
        };
    }
}

/// <summary>
/// Health status enumeration.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Service is healthy and functioning normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// Service is functioning but with reduced performance or capabilities.
    /// </summary>
    Degraded,

    /// <summary>
    /// Service is not functioning properly.
    /// </summary>
    Unhealthy
}

/// <summary>
/// Health result for an individual translator.
/// </summary>
public sealed class TranslatorHealthResult
{
    /// <summary>
    /// Health status of the translator.
    /// </summary>
    public HealthStatus Status { get; set; }

    /// <summary>
    /// Description of the translator health.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Last successful translation time.
    /// </summary>
    public DateTime? LastSuccessfulTranslation { get; set; }

    /// <summary>
    /// Error count in the recent period.
    /// </summary>
    public int RecentErrorCount { get; set; }

    /// <summary>
    /// Additional health data.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = [];
}

/// <summary>
/// Cache statistics for monitoring cache performance.
/// </summary>
public sealed class CacheStatistics
{
    /// <summary>
    /// Total number of cache requests.
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of cache hits.
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses.
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Cache hit rate as a percentage.
    /// </summary>
    public double HitRate => TotalRequests > 0 ? (CacheHits * 100.0) / TotalRequests : 0;

    /// <summary>
    /// Number of items currently in the cache.
    /// </summary>
    public long ItemCount { get; set; }

    /// <summary>
    /// Estimated memory usage in bytes.
    /// </summary>
    public long EstimatedMemoryUsage { get; set; }

    /// <summary>
    /// Last cache cleanup time.
    /// </summary>
    public DateTime LastCleanupTime { get; set; }
}