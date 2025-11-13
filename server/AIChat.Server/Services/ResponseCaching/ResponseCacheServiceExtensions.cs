using AIChat.Server.Configuration;
using AIChat.Server.Services.StateManagement;
using AIChat.Server.Services.StateManagement.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.ResponseCaching;

/// <summary>
/// Extension methods for registering response caching services in the dependency injection container.
/// Provides comprehensive service registration with health checks and configuration options.
/// </summary>
public static class ResponseCacheServiceExtensions
{
    private static readonly string[] tags = ["cache", "performance"];
    /// <summary>
    /// Adds intelligent response caching services to the service collection with default configuration.
    /// This is the recommended way to add response caching for most applications.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddIntelligentResponseCaching(this IServiceCollection services)
    {
        return services.AddIntelligentResponseCaching(options => { });
    }

    /// <summary>
    /// Adds intelligent response caching services to the service collection with custom configuration.
    /// Provides full control over caching behavior and performance characteristics.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure response caching options</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddIntelligentResponseCaching(
        this IServiceCollection services,
        Action<ResponseCacheOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        // Configure cache-specific configurations
        services.AddOptions<MemoryStateCacheConfiguration>()
            .BindConfiguration(MemoryStateCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ResponseCacheConfiguration>()
            .BindConfiguration(ResponseCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure legacy options for backward compatibility
        services.Configure(configureOptions);

        // Register memory cache if not already registered
        services.AddMemoryCache();

        // Register cache key generator
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();

        // Register state cache manager for cached responses with configuration
        services.AddSingleton<IStateCacheManager<CachedResponse>>(serviceProvider =>
        {
            var memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
            var logger = serviceProvider.GetRequiredService<ILogger<MemoryStateCacheManager<CachedResponse>>>();
            var configuration = serviceProvider.GetRequiredService<IOptions<MemoryStateCacheConfiguration>>();
            return new MemoryStateCacheManager<CachedResponse>(memoryCache, logger, configuration);
        });

        // Register response cache manager
        services.AddSingleton<IResponseCacheManager, ResponseCacheManager>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<ResponseCacheHealthCheck>("response_cache", HealthStatus.Degraded, tags);

        return services;
    }

    /// <summary>
    /// Adds distributed response caching using Redis for scalability across multiple instances.
    /// Recommended for production environments with multiple application instances.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="connectionString">Redis connection string</param>
    /// <param name="configureOptions">Optional action to configure response caching options</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddDistributedResponseCaching(
        this IServiceCollection services,
        string connectionString,
        Action<ResponseCacheOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        // Register cache-specific configurations
        services.AddOptions<MemoryStateCacheConfiguration>()
            .BindConfiguration(MemoryStateCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ResponseCacheConfiguration>()
            .BindConfiguration(ResponseCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure legacy options for backward compatibility
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register Redis distributed cache (placeholder - requires StackExchange.Redis package)
        // TODO: Add StackExchange.Redis package reference to enable distributed caching
        // services.AddStackExchangeRedisCache(options =>
        // {
        //     options.Configuration = connectionString;
        //     options.InstanceName = "ResponseCache";
        // });

        // Register underlying state cache infrastructure
        // State cache infrastructure will be registered directly

        // Register cache key generator
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();

        // Register distributed state cache manager for cached responses
        services.AddSingleton<IStateCacheManager<CachedResponse>, DistributedStateCacheManager<CachedResponse>>();

        // Register response cache manager
        services.AddSingleton<IResponseCacheManager, ResponseCacheManager>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<ResponseCacheHealthCheck>("response_cache", HealthStatus.Degraded, tags)
            .AddCheck<DistributedCacheHealthCheck>("distributed_cache", HealthStatus.Degraded, tagsArray);

        return services;
    }

    private static readonly string[] tagsArray = ["cache", "redis"];

    /// <summary>
    /// Adds response caching with custom state cache manager implementation.
    /// Provides maximum flexibility for advanced caching scenarios.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="stateCacheManagerFactory">Factory function to create the state cache manager</param>
    /// <param name="configureOptions">Optional action to configure response caching options</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddResponseCachingWithCustomStorage<TCacheManager>(
        this IServiceCollection services,
        Func<IServiceProvider, TCacheManager> stateCacheManagerFactory,
        Action<ResponseCacheOptions>? configureOptions = null)
        where TCacheManager : class, IStateCacheManager<CachedResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(stateCacheManagerFactory);

        // Register cache-specific configurations
        services.AddOptions<MemoryStateCacheConfiguration>()
            .BindConfiguration(MemoryStateCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ResponseCacheConfiguration>()
            .BindConfiguration(ResponseCacheConfiguration.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure legacy options for backward compatibility
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register underlying state cache infrastructure
        // State cache infrastructure will be registered directly

        // Register cache key generator
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();

        // Register custom state cache manager
        services.AddSingleton<IStateCacheManager<CachedResponse>>(stateCacheManagerFactory);

        // Register response cache manager
        services.AddSingleton<IResponseCacheManager, ResponseCacheManager>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<ResponseCacheHealthCheck>("response_cache", HealthStatus.Degraded, tags);

        return services;
    }
}

/// <summary>
/// Configuration options for response caching behavior.
/// </summary>
public class ResponseCacheOptions
{
    /// <summary>
    /// Gets or sets whether response caching is enabled globally.
    /// When disabled, all cache operations become no-ops.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the default cache expiration for operations without specific policies.
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the maximum memory usage for the cache in bytes.
    /// When exceeded, least recently used entries will be evicted.
    /// </summary>
    public long MaxMemoryUsageBytes { get; set; } = 100_000_000; // 100MB

    /// <summary>
    /// Gets or sets the maximum size for individual cache entries in bytes.
    /// Entries larger than this will not be cached.
    /// </summary>
    public int MaxEntrySize { get; set; } = 1_000_000; // 1MB

    /// <summary>
    /// Gets or sets whether cache warming should be performed on application startup.
    /// </summary>
    public bool EnableCacheWarming { get; set; }

    /// <summary>
    /// Gets or sets the cache warm-up operations to perform on startup.
    /// </summary>
    public List<CacheWarmupConfiguration> WarmupOperations { get; set; } = [];

    /// <summary>
    /// Gets or sets whether detailed operation metrics should be collected.
    /// Disable for high-traffic scenarios to reduce overhead.
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the cleanup interval for stale metrics and entries.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets custom cache policies for specific operations.
    /// </summary>
    public Dictionary<string, CachePolicy> CustomPolicies { get; set; } = [];
}

/// <summary>
/// Configuration for cache warming operations.
/// </summary>
public class CacheWarmupConfiguration
{
    /// <summary>
    /// Gets or sets the router type for the warmup operation.
    /// </summary>
    public required string RouterType { get; init; }

    /// <summary>
    /// Gets or sets the operation name for the warmup operation.
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// Gets or sets the parameters for the warmup operation.
    /// </summary>
    public object? Parameters { get; init; }

    /// <summary>
    /// Gets or sets the user context for the warmup operation.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// Gets or sets the priority of this warmup operation.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Gets or sets custom metadata for the warmup operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Health check for response cache system status.
/// </summary>
public class ResponseCacheHealthCheck : IHealthCheck
{
    private readonly IResponseCacheManager _cacheManager;
    private readonly ILogger<ResponseCacheHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the ResponseCacheHealthCheck class.
    /// </summary>
    /// <param name="cacheManager">The response cache manager to check</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public ResponseCacheHealthCheck(
        IResponseCacheManager cacheManager,
        ILogger<ResponseCacheHealthCheck> logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs health check of the response cache system.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthStatus = await _cacheManager.CheckHealthAsync(cancellationToken);

            var data = new Dictionary<string, object>
            {
                ["IsHealthy"] = healthStatus.IsHealthy,
                ["PerformanceScore"] = healthStatus.PerformanceScore,
                ["HitRatio"] = healthStatus.OverallHitRatio,
                ["MemoryUsage"] = $"{healthStatus.MemoryUsagePercentage:F1}%"
            };

            if (healthStatus.Warnings.Count > 0)
            {
                data["Warnings"] = healthStatus.Warnings;
            }

            var status = healthStatus.IsHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;
            var description = healthStatus.Message ?? "Response cache health check completed";

            return new HealthCheckResult(status, description, data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Response cache health check failed");

            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                "Response cache health check failed",
                ex,
                new Dictionary<string, object> { ["Exception"] = ex.Message });
        }
    }
}

/// <summary>
/// Health check for distributed cache connectivity.
/// </summary>
public class DistributedCacheHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<DistributedCacheHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the DistributedCacheHealthCheck class.
    /// </summary>
    /// <param name="distributedCache">The distributed cache to check</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public DistributedCacheHealthCheck(
        IDistributedCache distributedCache,
        ILogger<DistributedCacheHealthCheck> logger)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs health check of the distributed cache connectivity.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testKey = $"health_check_{Guid.NewGuid():N}";
            const string testValue = "health_check_value";

            // Test write operation
            await _distributedCache.SetStringAsync(testKey, testValue, cancellationToken);

            // Test read operation
            var retrievedValue = await _distributedCache.GetStringAsync(testKey, cancellationToken);

            // Test delete operation
            await _distributedCache.RemoveAsync(testKey, cancellationToken);

            var isHealthy = retrievedValue == testValue;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Degraded;
            var description = isHealthy
                ? "Distributed cache connectivity verified"
                : "Distributed cache connectivity issues detected";

            return new HealthCheckResult(status, description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Distributed cache health check failed");

            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                "Distributed cache connectivity failed",
                ex,
                new Dictionary<string, object> { ["Exception"] = ex.Message });
        }
    }
}

/// <summary>
/// Placeholder for distributed state cache manager - would be implemented if Redis integration is needed.
/// Currently using in-memory cache manager as primary implementation.
/// </summary>
public class DistributedStateCacheManager<T> : IStateCacheManager<T> where T : class
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<DistributedStateCacheManager<T>> _logger;

    public DistributedStateCacheManager(
        IDistributedCache distributedCache,
        ILogger<DistributedStateCacheManager<T>> logger)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Implementation would use JSON serialization with IDistributedCache
    /// For now, throwing NotImplementedException as this is a placeholder
    /// </summary>
    /// <param name="key"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<T?> GetFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task SetCacheAsync(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task RemoveFromCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task<bool> ExistsInCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }

    public Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Distributed caching requires Redis implementation");
    }
}
