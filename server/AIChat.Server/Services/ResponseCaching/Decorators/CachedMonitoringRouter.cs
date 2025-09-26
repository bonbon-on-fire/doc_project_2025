using AIChat.Orleans.Contracts;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services.ResponseCaching.Decorators;

/// <summary>
/// Cached decorator for IMonitoringRouter that adds intelligent response caching capabilities.
/// Uses decorator pattern to transparently add caching to existing monitoring router functionality.
/// Follows SOLID principles with comprehensive error handling and performance monitoring.
/// </summary>
public class CachedMonitoringRouter : CachedRouterBase<IMonitoringRouter>, IMonitoringRouter
{
    /// <inheritdoc/>
    protected override string RouterTypeName => "Monitoring";

    /// <summary>
    /// Initializes a new instance of the CachedMonitoringRouter class.
    /// </summary>
    /// <param name="innerRouter">The underlying monitoring router to decorate with caching</param>
    /// <param name="cacheManager">The response cache manager for cache operations</param>
    /// <param name="keyGenerator">The cache key generator for consistent key creation</param>
    /// <param name="logger">Logger for diagnostic and performance information</param>
    public CachedMonitoringRouter(
        IMonitoringRouter innerRouter,
        IResponseCacheManager cacheManager,
        ICacheKeyGenerator keyGenerator,
        ILogger<CachedMonitoringRouter> logger)
        : base(innerRouter, cacheManager, keyGenerator, logger)
    {
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: null, // Generic monitoring operation without specific parameters
            userContext: null, // System-wide monitoring operation
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteAsync(orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<ISessionMonitoringGrain, Task> orleansOperation,
        Func<ProductionMonitoringService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        await ExecuteVoidWithCacheAsync(
            parameters: null,
            userContext: null,
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteAsync(orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteSystemOperationAsync<T>(
        Func<IHealthCheckGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: new { operationType = "system" }, // Mark as system operation
            userContext: "system", // System-wide operation
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteSystemOperationAsync(orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteSessionOperationAsync<T>(
        string sessionId,
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: new { sessionId }, // Include sessionId in cache parameters
            userContext: sessionId, // Session-specific caching
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteSessionOperationAsync(sessionId, orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Pass through to underlying router - this is a simple boolean check that doesn't benefit from caching
        return await InnerRouter.IsOrleansEnabledAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RouterHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return await CreateEnhancedHealthStatusAsync(
            originalHealthCheck: InnerRouter.CheckHealthAsync,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await CreateEnhancedMetricsAsync(
            originalMetricsCheck: InnerRouter.GetMetricsAsync,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets invalidation patterns for monitoring-specific operations that affect cached data.
    /// </summary>
    protected override IEnumerable<string> GetInvalidationPatterns(string operationName, object? parameters, string? userContext)
    {
        return operationName.ToLowerInvariant() switch
        {
            "updatesystemhealth" => ["Monitoring:GetSystemHealth:*", "Monitoring:GetMetrics:*"],
            "updatecapacitymetrics" => ["Monitoring:GetCapacityMetrics:*", "Monitoring:GetMetrics:*"],
            "clearsessionmetrics" => ["Monitoring:*:*"], // Clear all monitoring cache
            _ => base.GetInvalidationPatterns(operationName, parameters, userContext)
        };
    }
}