using System.Collections.Concurrent;
using System.Diagnostics;

namespace AIChat.Server.Services.EventStore.Optimization;

/// <summary>
/// Performance optimizer for snapshot operations.
/// Provides caching, batching, and performance monitoring for snapshot store operations.
/// Implements performance best practices for high-throughput scenarios.
/// </summary>
public sealed class SnapshotPerformanceOptimizer : IDisposable
{
    private readonly ISnapshotStore _snapshotStore;
    private readonly ILogger<SnapshotPerformanceOptimizer> _logger;
    private readonly SnapshotOptimizationConfiguration _configuration;

    // Performance caching
    private readonly ConcurrentDictionary<string, CachedSnapshot> _snapshotCache = new();
    private readonly ConcurrentDictionary<string, CachedMetadata> _metadataCache = new();

    // Batch operations
    private readonly ConcurrentQueue<BatchedOperation> _batchQueue = new();
    private readonly Timer _batchProcessor;

    // Performance metrics
    private readonly ConcurrentDictionary<string, PerformanceMetrics> _performanceMetrics = new();

    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the SnapshotPerformanceOptimizer class.
    /// </summary>
    /// <param name="snapshotStore">The underlying snapshot store</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="configuration">Optimization configuration</param>
    public SnapshotPerformanceOptimizer(
        ISnapshotStore snapshotStore,
        ILogger<SnapshotPerformanceOptimizer> logger,
        SnapshotOptimizationConfiguration configuration)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        _batchProcessor = new Timer(ProcessBatchedOperations, null,
            _configuration.BatchProcessingInterval,
            _configuration.BatchProcessingInterval);
    }

    /// <summary>
    /// Gets a snapshot with performance optimization (caching, prefetching).
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Optimized snapshot retrieval result</returns>
    public async Task<SnapshotResult<T>> GetLatestSnapshotOptimizedAsync<T>(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Check cache first
            if (_configuration.EnableCaching && TryGetFromCache<T>(streamId, out var cachedResult))
            {
                RecordCacheHit(streamId, stopwatch.Elapsed);
                return cachedResult;
            }

            // Fetch from store
            var result = await _snapshotStore.GetLatestSnapshotAsync<T>(streamId, cancellationToken);

            // Cache the result if successful
            if (_configuration.EnableCaching && result.Success)
            {
                CacheSnapshot(streamId, result, stopwatch.Elapsed);
            }

            RecordCacheMiss(streamId, stopwatch.Elapsed);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(streamId, "GetLatestSnapshot", stopwatch.Elapsed, ex);
            throw;
        }
    }

    /// <summary>
    /// Creates a snapshot with performance optimization (batching, async processing).
    /// </summary>
    /// <typeparam name="T">The type of state to snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="version">The exact version this snapshot represents</param>
    /// <param name="state">The complete state to store</param>
    /// <param name="metadata">Optional metadata for the snapshot</param>
    /// <param name="priority">Creation priority (higher = process sooner)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Optimized snapshot creation result</returns>
    public async Task<SnapshotWriteResult> CreateSnapshotOptimizedAsync<T>(
        string streamId,
        long version,
        T state,
        Dictionary<string, object>? metadata = null,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(state);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (_configuration.EnableBatching && priority < _configuration.BatchingPriorityThreshold)
            {
                // Add to batch queue for later processing
                var batchedOperation = new BatchedOperation
                {
                    Type = BatchOperationType.Create,
                    StreamId = streamId,
                    Version = version,
                    State = state,
                    Metadata = metadata,
                    Priority = priority,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletionSource = new TaskCompletionSource<SnapshotWriteResult>()
                };

                _batchQueue.Enqueue(batchedOperation);

                _logger.LogDebug("Snapshot creation for {StreamId}:{Version} queued for batch processing",
                    streamId, version);

                return await batchedOperation.CompletionSource.Task;
            }

            // Process immediately for high-priority operations
            var result = await _snapshotStore.CreateSnapshotAsync(streamId, version, state, metadata, cancellationToken);

            // Invalidate cache for this stream
            if (_configuration.EnableCaching)
            {
                InvalidateStreamCache(streamId);
            }

            RecordCreateSuccess(streamId, stopwatch.Elapsed);
            return result;
        }
        catch (Exception ex)
        {
            RecordError(streamId, "CreateSnapshot", stopwatch.Elapsed, ex);
            throw;
        }
    }

    /// <summary>
    /// Preloads snapshots for a set of streams to improve cache hit rates.
    /// </summary>
    /// <param name="streamIds">Stream identifiers to preload</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Preloading results</returns>
    public async Task<SnapshotPreloadResult> PreloadSnapshotsAsync(
        IEnumerable<string> streamIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamIds);

        var stopwatch = Stopwatch.StartNew();
        var streamIdList = streamIds.ToList();
        var successCount = 0;
        var failureCount = 0;
        var errors = new List<string>();

        _logger.LogDebug("Preloading snapshots for {Count} streams", streamIdList.Count);

        var preloadTasks = streamIdList.Select(async streamId =>
        {
            try
            {
                var result = await _snapshotStore.GetLatestSnapshotAsync<object>(streamId, cancellationToken);
                if (result.Success && _configuration.EnableCaching)
                {
                    CacheSnapshot(streamId, result, TimeSpan.Zero);
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failureCount);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                lock (errors)
                {
                    errors.Add($"{streamId}: {ex.Message}");
                }
            }
        });

        await Task.WhenAll(preloadTasks);

        _logger.LogInformation("Preloading completed: {SuccessCount} successful, {FailureCount} failed in {ElapsedTime}ms",
            successCount, failureCount, stopwatch.ElapsedMilliseconds);

        return new SnapshotPreloadResult
        {
            TotalStreams = streamIdList.Count,
            SuccessfulPreloads = successCount,
            FailedPreloads = failureCount,
            PreloadTime = stopwatch.Elapsed,
            Errors = errors.AsReadOnly()
        };
    }

    /// <summary>
    /// Gets performance statistics for the optimizer.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Performance statistics</returns>
    public Task<SnapshotOptimizerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var statistics = new SnapshotOptimizerStatistics
        {
            CacheStatistics = GetCacheStatistics(),
            BatchStatistics = GetBatchStatistics(),
            PerformanceMetrics = _performanceMetrics.Values.ToList().AsReadOnly(),
            ConfigurationSnapshot = _configuration with { } // Clone configuration
        };

        return Task.FromResult(statistics);
    }

    /// <summary>
    /// Optimizes the cache by removing expired entries and compacting memory.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Cache optimization result</returns>
    public Task<CacheOptimizationResult> OptimizeCacheAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var expiredEntries = 0;
        var reclaimedMemory = 0L;

        var cutoffTime = DateTimeOffset.UtcNow - _configuration.CacheExpirationTime;

        // Remove expired snapshots
        foreach (var kvp in _snapshotCache.ToArray())
        {
            if (kvp.Value.CachedAt < cutoffTime)
            {
                if (_snapshotCache.TryRemove(kvp.Key, out var removed))
                {
                    expiredEntries++;
                    reclaimedMemory += removed.EstimatedSize;
                }
            }
        }

        // Remove expired metadata
        foreach (var kvp in _metadataCache.ToArray())
        {
            if (kvp.Value.CachedAt < cutoffTime)
            {
                if (_metadataCache.TryRemove(kvp.Key, out _))
                {
                    expiredEntries++;
                    reclaimedMemory += 1024; // Estimate metadata size
                }
            }
        }

        _logger.LogDebug("Cache optimization completed: {ExpiredEntries} expired entries removed, {ReclaimedMemory} bytes reclaimed",
            expiredEntries, reclaimedMemory);

        return Task.FromResult(new CacheOptimizationResult
        {
            ExpiredEntriesRemoved = expiredEntries,
            MemoryReclaimed = reclaimedMemory,
            OptimizationTime = stopwatch.Elapsed
        });
    }

    #region Private Methods

    private bool TryGetFromCache<T>(string streamId, out SnapshotResult<T> result)
    {
        if (_snapshotCache.TryGetValue(streamId, out var cached))
        {
            var age = DateTimeOffset.UtcNow - cached.CachedAt;
            if (age < _configuration.CacheExpirationTime && cached.Data is T typedData)
            {
                result = SnapshotResult.CreateSuccess(typedData, cached.Metadata);
                return true;
            }

            // Remove expired entry
            _snapshotCache.TryRemove(streamId, out _);
        }

        result = default!;
        return false;
    }

    private void CacheSnapshot<T>(string streamId, SnapshotResult<T> result, TimeSpan retrievalTime)
    {
        if (result.Data == null || result.Metadata == null)
        {
            return;
        }

        var cachedSnapshot = new CachedSnapshot
        {
            Data = result.Data,
            Metadata = result.Metadata,
            CachedAt = DateTimeOffset.UtcNow,
            RetrievalTime = retrievalTime,
            EstimatedSize = EstimateObjectSize(result.Data)
        };

        _snapshotCache.AddOrUpdate(streamId, cachedSnapshot, (_, _) => cachedSnapshot);

        // Also cache metadata separately for quick access
        var cachedMetadata = new CachedMetadata
        {
            Metadata = result.Metadata,
            CachedAt = DateTimeOffset.UtcNow
        };

        _metadataCache.AddOrUpdate(streamId, cachedMetadata, (_, _) => cachedMetadata);
    }

    private void InvalidateStreamCache(string streamId)
    {
        _snapshotCache.TryRemove(streamId, out _);
        _metadataCache.TryRemove(streamId, out _);
    }

    private void ProcessBatchedOperations(object? state)
    {
        if (_disposed || _batchQueue.IsEmpty)
        {
            return;
        }

        var batch = new List<BatchedOperation>();
        while (batch.Count < _configuration.MaxBatchSize && _batchQueue.TryDequeue(out var operation))
        {
            batch.Add(operation);
        }

        if (batch.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing batch of {Count} snapshot operations", batch.Count);

        // Sort by priority and age
        batch.Sort((a, b) =>
        {
            var priorityComparison = b.Priority.CompareTo(a.Priority);
            return priorityComparison != 0 ? priorityComparison : a.CreatedAt.CompareTo(b.CreatedAt);
        });

        // Process batch operations
        _ = Task.Run(async () =>
        {
            foreach (var operation in batch)
            {
                try
                {
                    SnapshotWriteResult result = operation.Type switch
                    {
                        BatchOperationType.Create => await _snapshotStore.CreateSnapshotAsync(
                            operation.StreamId,
                            operation.Version,
                            operation.State!,
                            operation.Metadata),
                        _ => throw new InvalidOperationException($"Unsupported batch operation type: {operation.Type}")
                    };

                    operation.CompletionSource.SetResult(result);

                    // Invalidate cache
                    if (_configuration.EnableCaching)
                    {
                        InvalidateStreamCache(operation.StreamId);
                    }
                }
                catch (Exception ex)
                {
                    operation.CompletionSource.SetException(ex);
                }
            }
        });
    }

    private void RecordCacheHit(string streamId, TimeSpan retrievalTime)
    {
        var metrics = GetOrCreateMetrics(streamId);
        Interlocked.Increment(ref metrics.CacheHits);
        metrics.RecordRetrievalTime(retrievalTime);
    }

    private void RecordCacheMiss(string streamId, TimeSpan retrievalTime)
    {
        var metrics = GetOrCreateMetrics(streamId);
        Interlocked.Increment(ref metrics.CacheMisses);
        metrics.RecordRetrievalTime(retrievalTime);
    }

    private void RecordCreateSuccess(string streamId, TimeSpan creationTime)
    {
        var metrics = GetOrCreateMetrics(streamId);
        Interlocked.Increment(ref metrics.CreateOperations);
        metrics.RecordCreationTime(creationTime);
    }

    private void RecordError(string streamId, string operation, TimeSpan operationTime, Exception exception)
    {
        var metrics = GetOrCreateMetrics(streamId);
        Interlocked.Increment(ref metrics.ErrorCount);

        _logger.LogWarning(exception, "Error in optimized snapshot operation {Operation} for stream {StreamId} after {ElapsedTime}ms",
            operation, streamId, operationTime.TotalMilliseconds);
    }

    private PerformanceMetrics GetOrCreateMetrics(string streamId)
    {
        return _performanceMetrics.GetOrAdd(streamId, _ => new PerformanceMetrics());
    }

    private static long EstimateObjectSize(object obj)
    {
        // Simple estimation - in practice, you might use more sophisticated sizing
        return obj switch
        {
            string str => str.Length * 2,
            byte[] bytes => bytes.Length,
            _ => 1024 // Default estimate
        };
    }

    private CacheStatistics GetCacheStatistics()
    {
        var totalHits = _performanceMetrics.Values.Sum(m => m.CacheHits);
        var totalMisses = _performanceMetrics.Values.Sum(m => m.CacheMisses);
        var totalOperations = totalHits + totalMisses;

        return new CacheStatistics
        {
            CacheHits = totalHits,
            CacheMisses = totalMisses,
            HitRatio = totalOperations > 0 ? (double)totalHits / totalOperations : 0.0,
            CachedEntries = _snapshotCache.Count,
            EstimatedMemoryUsage = _snapshotCache.Values.Sum(c => c.EstimatedSize)
        };
    }

    private BatchStatistics GetBatchStatistics()
    {
        return new BatchStatistics
        {
            QueuedOperations = _batchQueue.Count,
            TotalBatchedOperations = _performanceMetrics.Values.Sum(m => m.BatchedOperations),
            AverageBatchSize = _configuration.MaxBatchSize / 2.0 // Rough estimate
        };
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _batchProcessor?.Dispose();

        // Process remaining batched operations
        ProcessBatchedOperations(null);

        _snapshotCache.Clear();
        _metadataCache.Clear();
        _performanceMetrics.Clear();
    }
}

// Supporting types and configurations would be defined here...