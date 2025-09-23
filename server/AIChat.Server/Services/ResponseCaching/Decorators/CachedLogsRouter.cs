using System.Diagnostics;
using System.Text.Json;
using AIChat.Server.Services.Routing;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Services.ResponseCaching.Decorators;

/// <summary>
/// Cached decorator for ILogsRouter that adds intelligent response caching capabilities.
/// Uses decorator pattern to transparently add caching to existing logs router functionality.
/// Follows SOLID principles with comprehensive error handling and performance monitoring.
/// </summary>
public class CachedLogsRouter : CachedRouterBase<ILogsRouter>, ILogsRouter
{
    /// <inheritdoc/>
    protected override string RouterTypeName => "Logs";

    /// <summary>
    /// Initializes a new instance of the CachedLogsRouter class.
    /// </summary>
    /// <param name="innerRouter">The underlying logs router to decorate with caching</param>
    /// <param name="cacheManager">The response cache manager for cache operations</param>
    /// <param name="keyGenerator">The cache key generator for consistent key creation</param>
    /// <param name="logger">Logger for diagnostic and performance information</param>
    public CachedLogsRouter(
        ILogsRouter innerRouter,
        IResponseCacheManager cacheManager,
        ICacheKeyGenerator keyGenerator,
        ILogger<CachedLogsRouter> logger)
        : base(innerRouter, cacheManager, keyGenerator, logger)
    {
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> orleansOperation,
        Func<Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return await ExecuteWithCacheAsync<T>(
            parameters: null, // Generic log operation without specific parameters
            userContext: null, // System-wide logging operation
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteAsync(orleansOperation, directOperation, operationName, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<Task> orleansOperation,
        Func<Task> directOperation,
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
    public async Task<IActionResult> ExecuteLogEntryAsync(
        JsonElement logEntry,
        Func<JsonElement, Task<IActionResult>>? orleansOperation,
        Func<JsonElement, Task<IActionResult>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directOperation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        // Extract log entry details for cache key generation
        var logLevel = logEntry.TryGetProperty("level", out var levelElement) ? levelElement.GetString() : "unknown";
        var timestamp = logEntry.TryGetProperty("timestamp", out var timestampElement) ? timestampElement.GetString() : null;
        var source = logEntry.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : "unknown";

        return await ExecuteWithCacheAsync<IActionResult>(
            parameters: new { logLevel, source, operationType = "logEntry" }, // Include log metadata
            userContext: null, // Log entries are typically system-wide
            operationName: operationName,
            executeOperation: async () => await InnerRouter.ExecuteLogEntryAsync(logEntry, orleansOperation, directOperation, operationName, cancellationToken),
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
    /// Gets invalidation patterns for logs-specific operations that affect cached data.
    /// Note: Log operations are typically write-heavy and may not benefit significantly from caching,
    /// but search and export operations can benefit from intelligent caching.
    /// </summary>
    protected override IEnumerable<string> GetInvalidationPatterns(string operationName, object? parameters, string? userContext)
    {
        return operationName.ToLowerInvariant() switch
        {
            "clearlogfiles" => new[] { "Logs:*:*:*:*" }, // Clear all log cache
            "rotatelogs" => new[] { "Logs:GetLogs:*", "Logs:SearchLogs:*" }, // Invalidate read operations
            "purgeoldlogs" => new[] { "Logs:GetLogs:*", "Logs:SearchLogs:*", "Logs:ExportLogs:*" }, // Invalidate read and export operations
            _ => base.GetInvalidationPatterns(operationName, parameters, userContext)
        };
    }
}