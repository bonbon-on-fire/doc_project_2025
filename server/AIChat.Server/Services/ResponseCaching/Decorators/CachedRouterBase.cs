using System.Diagnostics;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services.ResponseCaching.Decorators;

/// <summary>
/// Abstract base class for cached router decorators that provides common caching functionality.
/// Implements shared caching logic to reduce code duplication across different router types.
/// Follows the Template Method pattern with SOLID principles.
/// </summary>
/// <typeparam name="TRouter">The type of router being decorated</typeparam>
public abstract class CachedRouterBase<TRouter> : IDisposable where TRouter : class
{
    private readonly TRouter _innerRouter;
    private readonly IResponseCacheManager _cacheManager;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger _logger;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the inner router instance being decorated with caching functionality.
    /// </summary>
    protected TRouter InnerRouter => _innerRouter;

    /// <summary>
    /// Gets the response cache manager for cache operations.
    /// </summary>
    protected IResponseCacheManager CacheManager => _cacheManager;

    /// <summary>
    /// Gets the cache key generator for consistent key creation.
    /// </summary>
    protected ICacheKeyGenerator KeyGenerator => _keyGenerator;

    /// <summary>
    /// Gets the logger for diagnostic and performance information.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Gets the router type name for cache key generation.
    /// Must be implemented by derived classes to specify the specific router type.
    /// </summary>
    protected abstract string RouterTypeName { get; }

    /// <summary>
    /// Initializes a new instance of the CachedRouterBase class.
    /// </summary>
    /// <param name="innerRouter">The underlying router to decorate with caching</param>
    /// <param name="cacheManager">The response cache manager for cache operations</param>
    /// <param name="keyGenerator">The cache key generator for consistent key creation</param>
    /// <param name="logger">Logger for diagnostic and performance information</param>
    protected CachedRouterBase(
        TRouter innerRouter,
        IResponseCacheManager cacheManager,
        ICacheKeyGenerator keyGenerator,
        ILogger logger)
    {
        _innerRouter = innerRouter ?? throw new ArgumentNullException(nameof(innerRouter));
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("Cached{RouterType} initialized with caching capabilities", RouterTypeName);
    }

    /// <summary>
    /// Executes an operation with intelligent caching based on operation type and policy.
    /// This is the core template method that handles the caching logic.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="parameters">Operation parameters for cache key generation</param>
    /// <param name="userContext">User context for cache key generation</param>
    /// <param name="operationName">Name of the operation being executed</param>
    /// <param name="executeOperation">Function to execute the actual operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation, either from cache or from execution</returns>
    protected async Task<T> ExecuteWithCacheAsync<T>(
        object? parameters,
        string? userContext,
        string operationName,
        Func<Task<T>> executeOperation,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        Logger.LogTrace("Starting cached {RouterType} operation {OperationName} with ID {OperationId}",
            RouterTypeName, operationName, operationId);

        try
        {
            // Determine cache policy for this operation
            var cachePolicy = RouterCachePolicies.GetPolicy(RouterTypeName, operationName);

            if (cachePolicy == null || cachePolicy.OperationType == CacheOperationType.WriteThrough)
            {
                Logger.LogTrace("No caching for {RouterType} operation {OperationName} (ID: {OperationId}) - executing directly",
                    RouterTypeName, operationName, operationId);

                var result = await executeOperation();

                // For write operations, invalidate related cache entries
                if (cachePolicy?.OperationType == CacheOperationType.WriteThrough)
                {
                    await InvalidateRelatedCacheEntriesAsync(operationName, parameters, userContext);
                }

                return result;
            }

            // Generate cache key
            var cacheKey = KeyGenerator.GenerateKey(RouterTypeName, operationName, parameters, userContext);

            // Try to get from cache first
            try
            {
                var cachedResult = await CacheManager.GetCachedResponseAsync<T>(cacheKey, cancellationToken);
                if (cachedResult != null)
                {
                    Logger.LogDebug("Cache hit for {RouterType} operation {OperationName} (ID: {OperationId})",
                        RouterTypeName, operationName, operationId);
                    return cachedResult;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error retrieving from cache for {RouterType} operation {OperationName} (ID: {OperationId}) - proceeding with execution",
                    RouterTypeName, operationName, operationId);
            }

            // Cache miss - execute the operation
            Logger.LogDebug("Cache miss for {RouterType} operation {OperationName} (ID: {OperationId}) - executing operation",
                RouterTypeName, operationName, operationId);

            var operationResult = await executeOperation();

            // Cache the result
            try
            {
                await CacheManager.SetCachedResponseAsync(cacheKey, operationResult, cachePolicy, cancellationToken);

                Logger.LogDebug("Cached result for {RouterType} operation {OperationName} (ID: {OperationId})",
                    RouterTypeName, operationName, operationId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error caching result for {RouterType} operation {OperationName} (ID: {OperationId})",
                    RouterTypeName, operationName, operationId);
                // Don't fail the operation if caching fails
            }

            return operationResult;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in cached {RouterType} operation {OperationName} (ID: {OperationId})",
                RouterTypeName, operationName, operationId);
            throw;
        }
        finally
        {
            Logger.LogTrace("Completed cached {RouterType} operation {OperationName} in {ElapsedMs}ms",
                RouterTypeName, operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Executes a void operation with cache invalidation for write operations.
    /// </summary>
    /// <param name="parameters">Operation parameters for invalidation pattern generation</param>
    /// <param name="userContext">User context for invalidation pattern generation</param>
    /// <param name="operationName">Name of the operation being executed</param>
    /// <param name="executeOperation">Function to execute the actual operation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    protected async Task ExecuteVoidWithCacheAsync(
        object? parameters,
        string? userContext,
        string operationName,
        Func<Task> executeOperation,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var operationId = Guid.NewGuid().ToString("N")[..8];

        Logger.LogTrace("Starting cached {RouterType} void operation {OperationName} with ID {OperationId}",
            RouterTypeName, operationName, operationId);

        try
        {
            // Execute the operation
            await executeOperation();

            // Invalidate related cache entries for write operations
            await InvalidateRelatedCacheEntriesAsync(operationName, parameters, userContext);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in cached {RouterType} void operation {OperationName} (ID: {OperationId})",
                RouterTypeName, operationName, operationId);
            throw;
        }
    }

    /// <summary>
    /// Invalidates cache entries related to write operations that may affect cached data.
    /// </summary>
    /// <param name="operationName">The name of the operation that triggered invalidation</param>
    /// <param name="parameters">Operation parameters for context-specific invalidation</param>
    /// <param name="userContext">User context for user-specific invalidation</param>
    protected async Task InvalidateRelatedCacheEntriesAsync(string operationName, object? parameters, string? userContext)
    {
        try
        {
            var invalidationPatterns = GetInvalidationPatterns(operationName, parameters, userContext);

            foreach (var pattern in invalidationPatterns)
            {
                await CacheManager.InvalidateResponsePatternAsync(pattern);

                Logger.LogDebug("Invalidated cache entries matching pattern {Pattern} for {RouterType} operation {OperationName}",
                    pattern, RouterTypeName, operationName);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error invalidating cache entries for {RouterType} operation {OperationName}",
                RouterTypeName, operationName);
            // Don't fail the operation if cache invalidation fails
        }
    }

    /// <summary>
    /// Gets invalidation patterns for specific operations that affect cached data.
    /// Can be overridden by derived classes to provide router-specific invalidation logic.
    /// </summary>
    /// <param name="operationName">The operation name</param>
    /// <param name="parameters">Operation parameters</param>
    /// <param name="userContext">User context</param>
    /// <returns>Collection of invalidation patterns</returns>
    protected virtual IEnumerable<string> GetInvalidationPatterns(string operationName, object? parameters, string? userContext)
    {
        // Default invalidation patterns - can be overridden by derived classes
        var operationLower = operationName.ToLowerInvariant();

        if (operationLower.Contains("create") || operationLower.Contains("update") || operationLower.Contains("delete"))
        {
            // For write operations, invalidate all related cached entries
            return [$"{RouterTypeName}:*:*:*:*"];
        }

        return [];
    }

    /// <summary>
    /// Creates enhanced router health status that includes cache health information.
    /// </summary>
    /// <param name="originalHealthCheck">Function to get the original router health status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enhanced health status including cache information</returns>
    protected async Task<RouterHealthStatus> CreateEnhancedHealthStatusAsync(
        Func<CancellationToken, Task<RouterHealthStatus>> originalHealthCheck,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get health status from underlying router
            var routerHealth = await originalHealthCheck(cancellationToken);

            // Get cache health status
            var cacheHealth = await CacheManager.CheckHealthAsync(cancellationToken);

            // Combine health statuses
            var overallHealth = routerHealth.IsHealthy && cacheHealth.IsHealthy;
            var message = $"Router: {routerHealth.Message}, Cache: {cacheHealth.Message}";

            return routerHealth with
            {
                IsHealthy = overallHealth,
                Message = message
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during cached {RouterType} router health check", RouterTypeName);

            return new RouterHealthStatus
            {
                IsHealthy = false,
                IsOrleansHealthy = false,
                IsDirectServiceHealthy = false,
                IsOrleansEnabled = false,
                CurrentMode = RouterMode.Degraded,
                Message = $"Cached {RouterTypeName} router health check failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Creates enhanced router metrics that include cache performance information.
    /// </summary>
    /// <param name="originalMetricsCheck">Function to get the original router metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enhanced metrics including cache performance data</returns>
    protected async Task<RouterMetrics> CreateEnhancedMetricsAsync(
        Func<CancellationToken, Task<RouterMetrics>> originalMetricsCheck,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get metrics from underlying router
            var routerMetrics = await originalMetricsCheck(cancellationToken);

            // Get cache statistics
            var cacheStats = await CacheManager.GetStatisticsAsync(cancellationToken);

            // Enhance router metrics with cache information
            // Adjust response times based on cache hit ratio (cache hits are much faster)
            var cacheSpeedup = cacheStats.HitRatio; // Higher hit ratio = more speedup

            return routerMetrics with
            {
                OrleansAverageExecutionTimeMs = routerMetrics.OrleansAverageExecutionTimeMs * (1.0 - cacheSpeedup * 0.8),
                DirectServiceAverageExecutionTimeMs = routerMetrics.DirectServiceAverageExecutionTimeMs * (1.0 - cacheSpeedup * 0.8)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting cached {RouterType} router metrics", RouterTypeName);

            // Return default metrics on error
            return new RouterMetrics
            {
                TotalOperations = 0,
                OrleansOperations = 0,
                DirectServiceOperations = 0,
                FallbackOperations = 0,
                FailedOperations = 1 // Count this error
            };
        }
    }

    /// <summary>
    /// Throws ObjectDisposedException if the instance has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes the cached router and underlying resources.
    /// </summary>
    public virtual void Dispose()
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                if (InnerRouter is IDisposable disposableRouter)
                {
                    disposableRouter.Dispose();
                }

                _disposed = true;
            }
        }
        GC.SuppressFinalize(this);
    }
}