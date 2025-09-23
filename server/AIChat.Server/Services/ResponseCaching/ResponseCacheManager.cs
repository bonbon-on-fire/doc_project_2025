using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AIChat.Server.Services.StateManagement;

namespace AIChat.Server.Services.ResponseCaching;

/// <summary>
/// Production implementation of intelligent response caching that integrates with the Orleans router pattern.
/// Leverages existing IStateCacheManager infrastructure while providing response-specific caching capabilities.
/// Follows SOLID principles with comprehensive monitoring, metrics collection, and health checking.
/// </summary>
public class ResponseCacheManager : IResponseCacheManager, IDisposable
{
    private readonly IStateCacheManager<CachedResponse> _stateCacheManager;
    private readonly ICacheKeyGenerator _keyGenerator;
    private readonly ILogger<ResponseCacheManager> _logger;
    private readonly ResponseCacheMetricsCollector _metricsCollector;
    private readonly ConcurrentDictionary<string, CacheOperationMetrics> _operationMetrics;
    private readonly ConcurrentDictionary<string, RouterCacheMetrics> _routerMetrics;
    private readonly SemaphoreSlim _healthCheckSemaphore;
    private readonly Timer _cleanupTimer;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ResponseCacheManager class.
    /// </summary>
    /// <param name="stateCacheManager">Underlying state cache manager for storage operations</param>
    /// <param name="keyGenerator">Cache key generator for consistent key creation</param>
    /// <param name="logger">Logger for diagnostic and monitoring information</param>
    public ResponseCacheManager(
        IStateCacheManager<CachedResponse> stateCacheManager,
        ICacheKeyGenerator keyGenerator,
        ILogger<ResponseCacheManager> logger)
    {
        _stateCacheManager = stateCacheManager ?? throw new ArgumentNullException(nameof(stateCacheManager));
        _keyGenerator = keyGenerator ?? throw new ArgumentNullException(nameof(keyGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _metricsCollector = new ResponseCacheMetricsCollector();
        _operationMetrics = new ConcurrentDictionary<string, CacheOperationMetrics>();
        _routerMetrics = new ConcurrentDictionary<string, RouterCacheMetrics>();
        _healthCheckSemaphore = new SemaphoreSlim(1, 1);

        // Configure JSON serialization for response caching
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Set up cleanup timer to run every 5 minutes
        _cleanupTimer = new Timer(PerformMaintenanceAsync, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        _logger.LogInformation("ResponseCacheManager initialized with cleanup timer");
    }

    /// <inheritdoc/>
    public async Task<T?> GetCachedResponseAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ThrowIfDisposed();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogTrace("Getting cached response for key {CacheKey}", cacheKey);

            var cachedResponse = await _stateCacheManager.GetFromCacheAsync(cacheKey, cancellationToken);

            if (cachedResponse != null)
            {
                var result = DeserializeResponse<T>(cachedResponse);
                RecordCacheHit(cacheKey, stopwatch.Elapsed);

                _logger.LogDebug("Cache hit for key {CacheKey}, type {ResponseType}",
                    cacheKey, typeof(T).Name);

                return result;
            }

            RecordCacheMiss(cacheKey, stopwatch.Elapsed);

            _logger.LogDebug("Cache miss for key {CacheKey}, type {ResponseType}",
                cacheKey, typeof(T).Name);

            return default;
        }
        catch (Exception ex)
        {
            RecordCacheMiss(cacheKey, stopwatch.Elapsed);

            _logger.LogError(ex, "Error getting cached response for key {CacheKey}", cacheKey);
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task SetCachedResponseAsync<T>(string cacheKey, T response, CachePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(policy);
        ThrowIfDisposed();

        // Don't cache write-through operations
        if (policy.OperationType == CacheOperationType.WriteThrough)
        {
            _logger.LogTrace("Skipping cache for write-through operation with key {CacheKey}", cacheKey);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cachedResponse = CreateCachedResponse(response, policy);

            // Check size limit if specified
            if (policy.MaxSizeBytes.HasValue && cachedResponse.SizeBytes > policy.MaxSizeBytes.Value)
            {
                _logger.LogWarning("Response too large to cache: {SizeBytes} bytes > {MaxSizeBytes} bytes for key {CacheKey}",
                    cachedResponse.SizeBytes, policy.MaxSizeBytes.Value, cacheKey);
                return;
            }

            var expiry = CalculateExpiry(policy);

            await _stateCacheManager.SetCacheAsync(cacheKey, cachedResponse, expiry, cancellationToken);

            RecordCacheSet(cacheKey, cachedResponse.SizeBytes, stopwatch.Elapsed);

            _logger.LogDebug("Cached response for key {CacheKey}, type {ResponseType}, size {SizeBytes} bytes",
                cacheKey, typeof(T).Name, cachedResponse.SizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching response for key {CacheKey}", cacheKey);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateResponseAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        ThrowIfDisposed();

        try
        {
            await _stateCacheManager.RemoveFromCacheAsync(cacheKey, cancellationToken);

            RecordInvalidation(cacheKey);

            _logger.LogDebug("Invalidated cache entry for key {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache entry for key {CacheKey}", cacheKey);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateResponsePatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        ThrowIfDisposed();

        try
        {
            await _stateCacheManager.InvalidateByPatternAsync(pattern, cancellationToken);

            RecordPatternInvalidation(pattern);

            _logger.LogDebug("Invalidated cache entries matching pattern {Pattern}", pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache entries for pattern {Pattern}", pattern);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ResponseCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var baseStats = await _stateCacheManager.GetCacheStatisticsAsync(cancellationToken);
            var metricsSnapshot = _metricsCollector.GetSnapshot();

            return new ResponseCacheStatistics
            {
                HitCount = baseStats.HitCount,
                MissCount = baseStats.MissCount,
                EntryCount = baseStats.EntryCount,
                EstimatedMemoryUsage = baseStats.EstimatedMemoryUsage,
                AdditionalMetrics = baseStats.AdditionalMetrics,
                OperationMetrics = new Dictionary<string, CacheOperationMetrics>(_operationMetrics),
                RouterMetrics = new Dictionary<string, RouterCacheMetrics>(_routerMetrics),
                AverageCacheLatencyMs = metricsSnapshot.AverageLatencyMs,
                TotalBytesStored = metricsSnapshot.TotalBytesStored,
                EfficiencyScore = CalculateEfficiencyScore(baseStats),
                InvalidationCount = metricsSnapshot.InvalidationCount,
                EvictionCount = metricsSnapshot.EvictionCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ResponseCacheHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!await _healthCheckSemaphore.WaitAsync(5000, cancellationToken))
        {
            return new ResponseCacheHealthStatus
            {
                IsHealthy = false,
                Message = "Health check timeout after 5 seconds",
                Warnings = { "Health check semaphore timeout - possible deadlock" }
            };
        }

        try
        {
            var stats = await GetStatisticsAsync(cancellationToken);
            var warnings = new List<string>();
            var isHealthy = true;
            var performanceScore = 1.0;

            // Check hit ratio health
            if (stats.HitRatio < 0.3)
            {
                warnings.Add($"Low cache hit ratio: {stats.HitRatio:P2}");
                performanceScore *= 0.7;
            }
            else if (stats.HitRatio < 0.5)
            {
                warnings.Add($"Moderate cache hit ratio: {stats.HitRatio:P2}");
                performanceScore *= 0.9;
            }

            // Check memory usage health
            var memoryUsagePercentage = CalculateMemoryUsagePercentage(stats.EstimatedMemoryUsage);
            if (memoryUsagePercentage > 90)
            {
                warnings.Add($"High memory usage: {memoryUsagePercentage:F1}%");
                isHealthy = false;
                performanceScore *= 0.5;
            }
            else if (memoryUsagePercentage > 75)
            {
                warnings.Add($"Elevated memory usage: {memoryUsagePercentage:F1}%");
                performanceScore *= 0.8;
            }

            // Check latency health
            if (stats.AverageCacheLatencyMs > 10)
            {
                warnings.Add($"High cache latency: {stats.AverageCacheLatencyMs:F2}ms");
                performanceScore *= 0.8;
            }

            var message = isHealthy
                ? $"Cache healthy - Hit ratio: {stats.HitRatio:P2}, Memory: {memoryUsagePercentage:F1}%"
                : $"Cache degraded - {warnings.Count} issues detected";

            return new ResponseCacheHealthStatus
            {
                IsHealthy = isHealthy,
                PerformanceScore = performanceScore,
                OverallHitRatio = stats.HitRatio,
                MemoryUsagePercentage = memoryUsagePercentage,
                Warnings = warnings,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache health check");

            return new ResponseCacheHealthStatus
            {
                IsHealthy = false,
                Message = $"Health check failed: {ex.Message}",
                Warnings = { "Exception during health check" }
            };
        }
        finally
        {
            _healthCheckSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task WarmCacheAsync(IEnumerable<CacheWarmupOperation> warmupOperations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(warmupOperations);
        ThrowIfDisposed();

        var operations = warmupOperations.OrderByDescending(op => op.Priority).ToList();

        _logger.LogInformation("Starting cache warmup with {OperationCount} operations", operations.Count);

        var successCount = 0;
        var failureCount = 0;

        foreach (var operation in operations)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var result = await operation.Operation(cancellationToken);
                if (result != null)
                {
                    await SetCachedResponseAsync(operation.CacheKey, result, operation.Policy, cancellationToken);
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache warmup failed for key {CacheKey}", operation.CacheKey);
                failureCount++;
            }
        }

        _logger.LogInformation("Cache warmup completed: {SuccessCount} successful, {FailureCount} failed",
            successCount, failureCount);
    }

    /// <summary>
    /// Creates a cached response wrapper with metadata.
    /// </summary>
    private CachedResponse CreateCachedResponse<T>(T response, CachePolicy policy)
    {
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var responseType = typeof(T).FullName ?? typeof(T).Name;

        return new CachedResponse
        {
            ResponseType = responseType,
            SerializedResponse = json,
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(json),
            CachedAt = DateTime.UtcNow,
            Policy = policy
        };
    }

    /// <summary>
    /// Deserializes a cached response to the requested type.
    /// </summary>
    private T? DeserializeResponse<T>(CachedResponse cachedResponse)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(cachedResponse.SerializedResponse, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing cached response of type {CachedType} to {RequestedType}",
                cachedResponse.ResponseType, typeof(T).Name);
            return default;
        }
    }

    /// <summary>
    /// Calculates expiry time based on cache policy.
    /// </summary>
    private static TimeSpan? CalculateExpiry(CachePolicy policy)
    {
        return policy.AbsoluteExpiration ?? policy.SlidingExpiration;
    }

    /// <summary>
    /// Calculates cache efficiency score based on statistics.
    /// </summary>
    private static double CalculateEfficiencyScore(CacheStatistics stats)
    {
        if (stats.TotalRequests == 0)
        {
            return 0.0;
        }

        // Base score on hit ratio
        var score = stats.HitRatio;

        // Adjust for memory efficiency (assuming 100MB baseline)
        var memoryEfficiency = Math.Min(1.0, 100_000_000.0 / Math.Max(stats.EstimatedMemoryUsage, 1));
        score *= memoryEfficiency;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    /// <summary>
    /// Calculates memory usage percentage (assuming 100MB budget).
    /// </summary>
    private static double CalculateMemoryUsagePercentage(long memoryUsage)
    {
        const long budgetBytes = 100_000_000; // 100MB budget
        return (double)memoryUsage / budgetBytes * 100.0;
    }

    /// <summary>
    /// Records cache hit metrics.
    /// </summary>
    private void RecordCacheHit(string cacheKey, TimeSpan latency)
    {
        _metricsCollector.RecordHit(latency);

        if (_keyGenerator.IsValidCacheKey(cacheKey))
        {
            var metadata = _keyGenerator.ExtractMetadata(cacheKey);
            UpdateOperationMetrics(metadata.OperationName, true, latency);
            UpdateRouterMetrics(metadata.RouterType, true);
        }
    }

    /// <summary>
    /// Records cache miss metrics.
    /// </summary>
    private void RecordCacheMiss(string cacheKey, TimeSpan latency)
    {
        _metricsCollector.RecordMiss(latency);

        if (_keyGenerator.IsValidCacheKey(cacheKey))
        {
            var metadata = _keyGenerator.ExtractMetadata(cacheKey);
            UpdateOperationMetrics(metadata.OperationName, false, latency);
            UpdateRouterMetrics(metadata.RouterType, false);
        }
    }

    /// <summary>
    /// Records cache set metrics.
    /// </summary>
    private void RecordCacheSet(string cacheKey, long sizeBytes, TimeSpan latency)
    {
        _metricsCollector.RecordSet(sizeBytes, latency);
    }

    /// <summary>
    /// Records cache invalidation metrics.
    /// </summary>
    private void RecordInvalidation(string cacheKey)
    {
        _metricsCollector.RecordInvalidation();

        if (_keyGenerator.IsValidCacheKey(cacheKey))
        {
            var metadata = _keyGenerator.ExtractMetadata(cacheKey);
            UpdateRouterInvalidationMetrics(metadata.RouterType);
        }
    }

    /// <summary>
    /// Records pattern invalidation metrics.
    /// </summary>
    private void RecordPatternInvalidation(string pattern)
    {
        _metricsCollector.RecordPatternInvalidation();
    }

    /// <summary>
    /// Updates operation-specific metrics.
    /// </summary>
    private void UpdateOperationMetrics(string operationName, bool isHit, TimeSpan latency)
    {
        _operationMetrics.AddOrUpdate(operationName,
            new CacheOperationMetrics
            {
                OperationName = operationName,
                HitCount = isHit ? 1 : 0,
                MissCount = isHit ? 0 : 1,
                AverageLatencyMs = latency.TotalMilliseconds,
                LastAccessTime = DateTime.UtcNow
            },
            (key, existing) => existing with
            {
                HitCount = existing.HitCount + (isHit ? 1 : 0),
                MissCount = existing.MissCount + (isHit ? 0 : 1),
                AverageLatencyMs = (existing.AverageLatencyMs + latency.TotalMilliseconds) / 2.0,
                LastAccessTime = DateTime.UtcNow
            });
    }

    /// <summary>
    /// Updates router-specific metrics.
    /// </summary>
    private void UpdateRouterMetrics(string routerType, bool isHit)
    {
        _routerMetrics.AddOrUpdate(routerType,
            new RouterCacheMetrics
            {
                RouterType = routerType,
                TotalOperations = 1,
                HitCount = isHit ? 1 : 0,
                MissCount = isHit ? 0 : 1
            },
            (key, existing) => existing with
            {
                TotalOperations = existing.TotalOperations + 1,
                HitCount = existing.HitCount + (isHit ? 1 : 0),
                MissCount = existing.MissCount + (isHit ? 0 : 1)
            });
    }

    /// <summary>
    /// Updates router invalidation metrics.
    /// </summary>
    private void UpdateRouterInvalidationMetrics(string routerType)
    {
        _routerMetrics.AddOrUpdate(routerType,
            new RouterCacheMetrics
            {
                RouterType = routerType,
                InvalidationCount = 1
            },
            (key, existing) => existing with
            {
                InvalidationCount = existing.InvalidationCount + 1
            });
    }

    /// <summary>
    /// Performs periodic maintenance operations.
    /// </summary>
    private void PerformMaintenanceAsync(object? state)
    {
        // Execute maintenance on a background task to avoid blocking the timer
        _ = Task.Run(PerformMaintenanceInternalAsync);
    }

    /// <summary>
    /// Internal implementation of maintenance operations.
    /// </summary>
    private Task PerformMaintenanceInternalAsync()
    {
        try
        {
            _logger.LogTrace("Performing cache maintenance");

            // Clean up stale operation metrics (older than 1 hour)
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var staleOperations = _operationMetrics
                .Where(kvp => kvp.Value.LastAccessTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var operation in staleOperations)
            {
                _operationMetrics.TryRemove(operation, out _);
            }

            if (staleOperations.Count > 0)
            {
                _logger.LogDebug("Cleaned up {StaleCount} stale operation metrics", staleOperations.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache maintenance");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Throws ObjectDisposedException if the instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes resources and stops background operations.
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (!_disposed)
            {
                _cleanupTimer?.Dispose();
                _healthCheckSemaphore?.Dispose();
                if (_keyGenerator is IDisposable disposableKeyGenerator)
                {
                    disposableKeyGenerator.Dispose();
                }
                _disposed = true;
            }
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a cached response with metadata for storage and retrieval.
/// </summary>
public record CachedResponse
{
    /// <summary>
    /// Gets the full type name of the cached response.
    /// </summary>
    public required string ResponseType { get; init; }

    /// <summary>
    /// Gets the JSON-serialized response data.
    /// </summary>
    public required string SerializedResponse { get; init; }

    /// <summary>
    /// Gets the size of the cached response in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets the timestamp when this response was cached.
    /// </summary>
    public DateTime CachedAt { get; init; }

    /// <summary>
    /// Gets the cache policy used for this response.
    /// </summary>
    public required CachePolicy Policy { get; init; }
}

/// <summary>
/// Internal metrics collector for response cache operations.
/// </summary>
public class ResponseCacheMetricsCollector
{
    private long _hitCount;
    private long _missCount;
    private long _totalLatencyMs;
    private long _operationCount;
    private long _totalBytesStored;
    private long _invalidationCount;
    private long _evictionCount;

    public void RecordHit(TimeSpan latency)
    {
        Interlocked.Increment(ref _hitCount);
        Interlocked.Add(ref _totalLatencyMs, (long)latency.TotalMilliseconds);
        Interlocked.Increment(ref _operationCount);
    }

    public void RecordMiss(TimeSpan latency)
    {
        Interlocked.Increment(ref _missCount);
        Interlocked.Add(ref _totalLatencyMs, (long)latency.TotalMilliseconds);
        Interlocked.Increment(ref _operationCount);
    }

    public void RecordSet(long sizeBytes, TimeSpan latency)
    {
        Interlocked.Add(ref _totalBytesStored, sizeBytes);
        Interlocked.Add(ref _totalLatencyMs, (long)latency.TotalMilliseconds);
        Interlocked.Increment(ref _operationCount);
    }

    public void RecordInvalidation()
    {
        Interlocked.Increment(ref _invalidationCount);
    }

    public void RecordPatternInvalidation()
    {
        Interlocked.Increment(ref _invalidationCount);
    }

    public void RecordEviction()
    {
        Interlocked.Increment(ref _evictionCount);
    }

    public ResponseCacheMetricsSnapshot GetSnapshot()
    {
        var opCount = Interlocked.Read(ref _operationCount);
        var totalLatency = Interlocked.Read(ref _totalLatencyMs);

        return new ResponseCacheMetricsSnapshot
        {
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            AverageLatencyMs = opCount > 0 ? (double)totalLatency / opCount : 0.0,
            TotalBytesStored = Interlocked.Read(ref _totalBytesStored),
            InvalidationCount = Interlocked.Read(ref _invalidationCount),
            EvictionCount = Interlocked.Read(ref _evictionCount)
        };
    }
}

/// <summary>
/// Snapshot of response cache metrics for reporting.
/// </summary>
public record ResponseCacheMetricsSnapshot
{
    public long HitCount { get; init; }
    public long MissCount { get; init; }
    public double AverageLatencyMs { get; init; }
    public long TotalBytesStored { get; init; }
    public long InvalidationCount { get; init; }
    public long EvictionCount { get; init; }
}