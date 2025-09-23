using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services.Routing;

namespace AIChat.Server.Services.ResponseCaching.Decorators;

/// <summary>
/// Cached decorator for IModeRouter that adds intelligent response caching capabilities.
/// Uses decorator pattern to transparently add caching to existing mode router functionality.
/// Follows SOLID principles with comprehensive error handling and performance monitoring.
/// </summary>
public class CachedModeRouter : CachedRouterBase<IModeRouter>, IModeRouter
{
    /// <inheritdoc/>
    protected override string RouterTypeName => "Mode";

    /// <summary>
    /// Initializes a new instance of the CachedModeRouter class.
    /// </summary>
    /// <param name="innerRouter">The underlying mode router to decorate with caching</param>
    /// <param name="cacheManager">The response cache manager for cache operations</param>
    /// <param name="keyGenerator">The cache key generator for consistent key creation</param>
    /// <param name="logger">Logger for diagnostic and performance information</param>
    public CachedModeRouter(
        IModeRouter innerRouter,
        IResponseCacheManager cacheManager,
        ICacheKeyGenerator keyGenerator,
        ILogger<CachedModeRouter> logger)
        : base(innerRouter, cacheManager, keyGenerator, logger)
    {
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: null, // Generic operation without specific parameters
            userContext: null, // System-wide operation
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteAsync(orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<IModeGrain, Task> orleansOperation,
        Func<IModeService, Task> directOperation,
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
    public async Task<T> ExecuteUserOperationAsync<T>(
        string userId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: new { userId }, // Include userId in cache parameters
            userContext: userId,
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteUserOperationAsync(userId, orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteModeOperationAsync<T>(
        string modeId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(modeId);
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: new { modeId }, // Include modeId in cache parameters
            userContext: null, // Mode operations are typically system-wide
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteModeOperationAsync(modeId, orleansOperation, directOperation, operationName, cancellationToken),
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
            originalHealthCheck: cancellationToken => InnerRouter.CheckHealthAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await CreateEnhancedMetricsAsync(
            originalMetricsCheck: cancellationToken => InnerRouter.GetMetricsAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets invalidation patterns for mode-specific operations that affect cached data.
    /// </summary>
    protected override IEnumerable<string> GetInvalidationPatterns(string operationName, object? parameters, string? userContext)
    {
        return operationName.ToLowerInvariant() switch
        {
            "createmode" => new[] { "Mode:GetModes:*", "Mode:GetMode:*" },
            "updatemode" => new[] { "Mode:GetModes:*", "Mode:GetMode:*" },
            "deletemode" => new[] { "Mode:GetModes:*", "Mode:GetMode:*" },
            _ => base.GetInvalidationPatterns(operationName, parameters, userContext)
        };
    }
}