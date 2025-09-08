using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Metrics;

/// <summary>
/// In-memory implementation of Orleans metrics collector.
/// Collects, aggregates, and provides access to Orleans performance metrics.
/// Thread-safe implementation suitable for high-throughput grain operations.
/// </summary>
public class OrleansMetricsCollector : IOrleansMetricsCollector
{
    private readonly ILogger<OrleansMetricsCollector> _logger;

    // Thread-safe collections for metrics storage
    private readonly ConcurrentDictionary<string, GrainInstanceMetrics> _grainMetrics = new();
    private readonly ConcurrentQueue<GrainActivationEvent> _recentActivations = new();
    private readonly ConcurrentQueue<GrainDeactivationEvent> _recentDeactivations = new();
    private readonly ConcurrentQueue<OperationMetrics> _recentOperations = new();

    // Configuration
    private const int MAX_RECENT_EVENTS = 1000;
    private const int METRICS_RETENTION_HOURS = 24;

    /// <summary>
    /// Initializes a new instance of the OrleansMetricsCollector.
    /// </summary>
    /// <param name="logger">Logger instance for metrics collection operations</param>
    public OrleansMetricsCollector(ILogger<OrleansMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Start background cleanup task
        _ = Task.Run(PeriodicCleanupAsync);
    }

    /// <inheritdoc />
    public async Task RecordGrainActivationAsync(string grainType, string grainId, double activationTime)
    {
        try
        {
            var key = $"{grainType}:{grainId}";
            var activation = new GrainActivationEvent
            {
                GrainType = grainType,
                GrainId = grainId,
                ActivationTime = activationTime,
                Timestamp = DateTime.UtcNow
            };

            _recentActivations.Enqueue(activation);

            // Limit queue size
            while (_recentActivations.Count > MAX_RECENT_EVENTS)
            {
                _ = _recentActivations.TryDequeue(out _);
            }

            // Update grain instance metrics
            _ = _grainMetrics.AddOrUpdate(key,
                _ => new GrainInstanceMetrics
                {
                    GrainType = grainType,
                    GrainId = grainId,
                    ActivatedAt = DateTime.UtcNow,
                    LastActivationTime = activationTime,
                    IsActive = true
                },
                (_, existing) =>
                {
                    existing.LastActivationTime = activationTime;
                    existing.ActivatedAt = DateTime.UtcNow;
                    existing.IsActive = true;
                    existing.ActivationCount++;
                    return existing;
                });

            _logger.LogDebug("Recorded grain activation: {GrainType}:{GrainId} in {ActivationTime}ms",
                grainType, grainId, activationTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record grain activation for {GrainType}:{GrainId}", grainType, grainId);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordGrainDeactivationAsync(string grainType, string grainId, double lifetimeMinutes)
    {
        try
        {
            var key = $"{grainType}:{grainId}";
            var deactivation = new GrainDeactivationEvent
            {
                GrainType = grainType,
                GrainId = grainId,
                LifetimeMinutes = lifetimeMinutes,
                Timestamp = DateTime.UtcNow
            };

            _recentDeactivations.Enqueue(deactivation);

            // Limit queue size
            while (_recentDeactivations.Count > MAX_RECENT_EVENTS)
            {
                _ = _recentDeactivations.TryDequeue(out _);
            }

            // Update grain instance metrics
            if (_grainMetrics.TryGetValue(key, out var metrics))
            {
                metrics.IsActive = false;
                metrics.DeactivatedAt = DateTime.UtcNow;
                metrics.TotalLifetimeMinutes += lifetimeMinutes;
            }

            _logger.LogDebug("Recorded grain deactivation: {GrainType}:{GrainId} after {LifetimeMinutes} minutes",
                grainType, grainId, lifetimeMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record grain deactivation for {GrainType}:{GrainId}", grainType, grainId);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordGrainOperationAsync(string grainType, string operationType, double duration, bool success)
    {
        try
        {
            var operation = new OperationMetrics
            {
                GrainType = grainType,
                OperationType = operationType,
                Duration = duration,
                Success = success,
                Timestamp = DateTime.UtcNow
            };

            _recentOperations.Enqueue(operation);

            // Limit queue size
            while (_recentOperations.Count > MAX_RECENT_EVENTS)
            {
                _ = _recentOperations.TryDequeue(out _);
            }

            _logger.LogTrace("Recorded operation: {GrainType}.{OperationType} - {Duration}ms (Success: {Success})",
                grainType, operationType, duration, success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record grain operation for {GrainType}.{OperationType}", grainType, operationType);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordGrainStateMetricsAsync(string grainType, string grainId, long stateSize, int connectionCount, int operationCount)
    {
        try
        {
            var key = $"{grainType}:{grainId}";

            _ = _grainMetrics.AddOrUpdate(key,
                _ => new GrainInstanceMetrics
                {
                    GrainType = grainType,
                    GrainId = grainId,
                    StateSizeBytes = stateSize,
                    ConnectionCount = connectionCount,
                    ActiveOperationCount = operationCount,
                    LastStateUpdate = DateTime.UtcNow
                },
                (_, existing) =>
                {
                    existing.StateSizeBytes = stateSize;
                    existing.ConnectionCount = connectionCount;
                    existing.ActiveOperationCount = operationCount;
                    existing.LastStateUpdate = DateTime.UtcNow;
                    return existing;
                });

            _logger.LogTrace("Recorded state metrics: {GrainType}:{GrainId} - Size: {StateSize}B, Connections: {Connections}, Operations: {Operations}",
                grainType, grainId, stateSize, connectionCount, operationCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record grain state metrics for {GrainType}:{GrainId}", grainType, grainId);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<OrleansMetricsSummary> GetMetricsSummaryAsync()
    {
        try
        {
            var summary = new OrleansMetricsSummary();
            var cutoffTime = DateTime.UtcNow.AddHours(-1);

            // Active grains
            var activeGrains = _grainMetrics.Values.Where(g => g.IsActive).ToList();
            summary.TotalActiveGrains = activeGrains.Count;

            // Recent activations/deactivations
            summary.GrainActivationsLastHour = _recentActivations.Count(a => a.Timestamp > cutoffTime);
            summary.GrainDeactivationsLastHour = _recentDeactivations.Count(d => d.Timestamp > cutoffTime);

            // Operation metrics
            var recentOps = _recentOperations.Where(o => o.Timestamp > cutoffTime).ToList();
            if (recentOps.Count != 0)
            {
                summary.AverageOperationDuration = recentOps.Average(o => o.Duration);
                summary.OperationSuccessRate = (double)recentOps.Count(o => o.Success) / recentOps.Count * 100;
            }

            // Memory usage
            summary.TotalMemoryUsageMB = activeGrains.Sum(g => g.StateSizeBytes) / (1024.0 * 1024.0);

            // Grain type metrics
            var grainGroups = activeGrains.GroupBy(g => g.GrainType);
            foreach (var group in grainGroups)
            {
                var typeMetrics = await GetGrainTypeMetricsAsync(group.Key);
                summary.GrainTypeMetrics[group.Key] = typeMetrics;
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics summary");
            return new OrleansMetricsSummary();
        }
    }

    /// <inheritdoc />
    public Task<GrainTypeMetrics> GetGrainTypeMetricsAsync(string grainType)
    {
        try
        {
            var metrics = new GrainTypeMetrics { GrainType = grainType };
            var grainInstances = _grainMetrics.Values.Where(g => g.GrainType == grainType).ToList();
            var activeInstances = grainInstances.Where(g => g.IsActive).ToList();

            metrics.ActiveInstances = activeInstances.Count;

            if (grainInstances.Count != 0)
            {
                var activations = _recentActivations.Where(a => a.GrainType == grainType).ToList();
                var deactivations = _recentDeactivations.Where(d => d.GrainType == grainType).ToList();
                var operations = _recentOperations.Where(o => o.GrainType == grainType).ToList();

                if (activations.Count != 0)
                {
                    metrics.AverageActivationTime = activations.Average(a => a.ActivationTime);
                }

                if (deactivations.Count != 0)
                {
                    metrics.AverageLifetime = deactivations.Average(d => d.LifetimeMinutes);
                }

                if (operations.Count != 0)
                {
                    metrics.AverageOperationDuration = operations.Average(o => o.Duration);
                    metrics.OperationSuccessRate = (double)operations.Count(o => o.Success) / operations.Count * 100;

                    // Operation counts by type
                    metrics.OperationCounts = operations.GroupBy(o => o.OperationType)
                        .ToDictionary(g => g.Key, g => (long)g.Count());
                }

                metrics.MemoryUsageMB = activeInstances.Sum(g => g.StateSizeBytes) / (1024.0 * 1024.0);

                // Recent samples for trending (last 10 operations)
                var recentSamples = operations
                    .OrderByDescending(o => o.Timestamp)
                    .Take(10)
                    .Select(o => new MetricSample
                    {
                        Timestamp = o.Timestamp,
                        Value = o.Duration,
                        MetricType = "OperationDuration"
                    })
                    .OrderBy(s => s.Timestamp)
                    .ToList();

                metrics.RecentSamples = recentSamples;
            }

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get grain type metrics for {GrainType}", grainType);
            return Task.FromResult(new GrainTypeMetrics { GrainType = grainType });
        }
    }

    /// <inheritdoc />
    public async Task ResetMetricsAsync()
    {
        try
        {
            _grainMetrics.Clear();

            while (_recentActivations.TryDequeue(out _)) { }
            while (_recentDeactivations.TryDequeue(out _)) { }
            while (_recentOperations.TryDequeue(out _)) { }

            _logger.LogWarning("All Orleans metrics have been reset");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset metrics");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Periodic cleanup of old metrics data.
    /// </summary>
    private async Task PeriodicCleanupAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1));

                var cutoffTime = DateTime.UtcNow.AddHours(-METRICS_RETENTION_HOURS);

                // Remove old inactive grain metrics
                var keysToRemove = _grainMetrics
                    .Where(kvp => !kvp.Value.IsActive && kvp.Value.DeactivatedAt < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _ = _grainMetrics.TryRemove(key, out _);
                }

                _logger.LogDebug("Cleaned up {Count} old grain metrics", keysToRemove.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform metrics cleanup");
            }
        }
    }
}

/// <summary>
/// Internal metrics for tracking individual grain instances.
/// </summary>
internal sealed class GrainInstanceMetrics
{
    public string GrainType { get; set; } = string.Empty;
    public string GrainId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime LastStateUpdate { get; set; }
    public double LastActivationTime { get; set; }
    public int ActivationCount { get; set; } = 1;
    public double TotalLifetimeMinutes { get; set; }
    public long StateSizeBytes { get; set; }
    public int ConnectionCount { get; set; }
    public int ActiveOperationCount { get; set; }
}

/// <summary>
/// Event record for grain activation.
/// </summary>
internal sealed class GrainActivationEvent
{
    public string GrainType { get; set; } = string.Empty;
    public string GrainId { get; set; } = string.Empty;
    public double ActivationTime { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event record for grain deactivation.
/// </summary>
internal sealed class GrainDeactivationEvent
{
    public string GrainType { get; set; } = string.Empty;
    public string GrainId { get; set; } = string.Empty;
    public double LifetimeMinutes { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Metrics record for grain operations.
/// </summary>
internal sealed class OperationMetrics
{
    public string GrainType { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public double Duration { get; set; }
    public bool Success { get; set; }
    public DateTime Timestamp { get; set; }
}
