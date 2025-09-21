using System.Collections.Concurrent;
using AIChat.Orleans.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.Orleans.Metrics;

/// <summary>
/// Implementation of SSE-specific metrics collection and monitoring.
/// Thread-safe implementation suitable for high-throughput streaming operations.
/// </summary>
public class SseMetricsCollector : ISseMetricsCollector
{
    private readonly ILogger<SseMetricsCollector> _logger;
    private readonly SseMetricsOptions _options;

    // Thread-safe collections for SSE metrics
    private readonly ConcurrentDictionary<string, SseConnectionMetrics> _activeConnections = new();
    private readonly ConcurrentQueue<SseStreamEvent> _recentStreamEvents = new();
    private readonly ConcurrentQueue<SseChunkMetrics> _recentChunks = new();
    private readonly ConcurrentQueue<SseBufferEvent> _bufferEvents = new();
    private readonly ConcurrentQueue<SseFailureEvent> _failureEvents = new();
    private readonly ConcurrentDictionary<string, StreamSessionMetrics> _streamSessions = new();

    // Configuration values from options
    private int MaxRecentEvents => _options.MaxRecentEvents;
    private int MetricsRetentionHours => _options.MetricsRetentionHours;
    private int HealthCheckIntervalSeconds => _options.HealthCheckIntervalSeconds;

    // Alert thresholds from options
    private double WarningFailureRate => _options.Thresholds.WarningFailureRate;
    private double CriticalFailureRate => _options.Thresholds.CriticalFailureRate;
    private double WarningBufferUtilization => _options.Thresholds.WarningBufferUtilization;
    private double CriticalBufferUtilization => _options.Thresholds.CriticalBufferUtilization;
    private double WarningLatencyMs => _options.Thresholds.WarningLatencyMs;
    private double CriticalLatencyMs => _options.Thresholds.CriticalLatencyMs;
    private int WarningConnectionDropsPerHour => _options.Thresholds.WarningConnectionDropsPerHour;
    private int CriticalConnectionDropsPerHour => _options.Thresholds.CriticalConnectionDropsPerHour;

    private DateTime _lastHealthCheck = DateTime.UtcNow;
    private SseHealthIndicators _cachedHealthIndicators = new();

    public SseMetricsCollector(
        ILogger<SseMetricsCollector> logger,
        IOptions<SseMetricsOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Background cleanup is now handled by SseMetricsCleanupService
    }

    /// <inheritdoc />
    public async Task RecordSseConnectionAsync(string grainId, string connectionId, bool established)
    {
        try
        {
            var timestamp = DateTime.UtcNow;

            if (established)
            {
                var connection = new SseConnectionMetrics
                {
                    GrainId = grainId,
                    ConnectionId = connectionId,
                    EstablishedAt = timestamp,
                    IsActive = true
                };

                _activeConnections[connectionId] = connection;

                _logger.LogDebug(
                    "SSE connection established: {ConnectionId} for grain {GrainId}",
                    connectionId,
                    grainId
                );
            }
            else
            {
                if (_activeConnections.TryRemove(connectionId, out var connection))
                {
                    connection.IsActive = false;
                    connection.ClosedAt = timestamp;
                    connection.Duration = (timestamp - connection.EstablishedAt).TotalMilliseconds;

                    // Record as stream event for historical tracking
                    var streamEvent = new SseStreamEvent
                    {
                        GrainId = grainId,
                        ConnectionId = connectionId,
                        EventType = "ConnectionClosed",
                        Timestamp = timestamp,
                        Duration = connection.Duration
                    };

                    _recentStreamEvents.Enqueue(streamEvent);
                    TrimQueue(_recentStreamEvents);

                    _logger.LogDebug(
                        "SSE connection closed: {ConnectionId} after {Duration}ms",
                        connectionId,
                        connection.Duration
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record SSE connection event for {ConnectionId}",
                connectionId
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordSseStreamChunkAsync(
        string grainId,
        string chunkType,
        int chunkSize,
        double processingTime,
        bool success)
    {
        try
        {
            var chunk = new SseChunkMetrics
            {
                GrainId = grainId,
                ChunkType = chunkType,
                Size = chunkSize,
                ProcessingTime = processingTime,
                Success = success,
                Timestamp = DateTime.UtcNow
            };

            _recentChunks.Enqueue(chunk);
            TrimQueue(_recentChunks);

            // Update session metrics if exists
            var sessionKey = GetActiveSessionKey(grainId);
            if (sessionKey != null && _streamSessions.TryGetValue(sessionKey, out var session))
            {
                session.TotalChunks++;
                session.TotalBytes += chunkSize;
                if (!success)
                {
                    session.FailedChunks++;
                }
            }

            _logger.LogTrace(
                "SSE chunk recorded: {ChunkType} ({Size}B) in {ProcessingTime}ms - Success: {Success}",
                chunkType,
                chunkSize,
                processingTime,
                success
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record SSE stream chunk for grain {GrainId}",
                grainId
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordSseBufferMetricsAsync(
        string grainId,
        int bufferSize,
        int bufferCapacity,
        bool overflowOccurred)
    {
        try
        {
            var bufferEvent = new SseBufferEvent
            {
                GrainId = grainId,
                BufferSize = bufferSize,
                BufferCapacity = bufferCapacity,
                Utilization = bufferCapacity > 0 ? (double)bufferSize / bufferCapacity * 100 : 0,
                OverflowOccurred = overflowOccurred,
                Timestamp = DateTime.UtcNow
            };

            _bufferEvents.Enqueue(bufferEvent);
            TrimQueue(_bufferEvents);

            if (overflowOccurred)
            {
                _logger.LogWarning(
                    "SSE buffer overflow detected for grain {GrainId}: {BufferSize}/{BufferCapacity}",
                    grainId,
                    bufferSize,
                    bufferCapacity
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record SSE buffer metrics for grain {GrainId}",
                grainId
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordSseStreamCompletionAsync(
        string grainId,
        double streamDuration,
        int totalChunks,
        long totalBytes,
        bool completedSuccessfully)
    {
        try
        {
            var sessionKey = $"{grainId}:{DateTime.UtcNow.Ticks}";
            var session = new StreamSessionMetrics
            {
                GrainId = grainId,
                Duration = streamDuration,
                TotalChunks = totalChunks,
                TotalBytes = totalBytes,
                CompletedSuccessfully = completedSuccessfully,
                CompletedAt = DateTime.UtcNow
            };

            _streamSessions[sessionKey] = session;

            // Clean old sessions
            CleanOldSessions();

            _logger.LogInformation(
                "SSE stream completed for grain {GrainId}: {TotalChunks} chunks, {TotalBytes}B in {Duration}ms - Success: {Success}",
                grainId,
                totalChunks,
                totalBytes,
                streamDuration,
                completedSuccessfully
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record SSE stream completion for grain {GrainId}",
                grainId
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordSseStreamFailureAsync(
        string grainId,
        string connectionId,
        string failureReason,
        int chunksLost)
    {
        try
        {
            var failure = new SseFailureEvent
            {
                GrainId = grainId,
                ConnectionId = connectionId,
                FailureReason = failureReason,
                ChunksLost = chunksLost,
                Timestamp = DateTime.UtcNow
            };

            _failureEvents.Enqueue(failure);
            TrimQueue(_failureEvents);

            _logger.LogError(
                "SSE stream failure for grain {GrainId}, connection {ConnectionId}: {Reason} ({ChunksLost} chunks lost)",
                grainId,
                connectionId,
                failureReason,
                chunksLost
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to record SSE stream failure for grain {GrainId}",
                grainId
            );
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SseMetricsSummary> GetSseMetricsSummaryAsync()
    {
        try
        {
            var summary = new SseMetricsSummary();
            var cutoffTime = DateTime.UtcNow.AddHours(-1);

            // Active connections
            summary.ActiveConnections = _activeConnections.Count(c => c.Value.IsActive);

            // Connection metrics from events
            var recentEvents = _recentStreamEvents.Where(e => e.Timestamp > cutoffTime).ToList();
            summary.ConnectionsEstablishedLastHour = _activeConnections
                .Count(c => c.Value.EstablishedAt > cutoffTime);
            summary.ConnectionsClosedLastHour = recentEvents
                .Count(e => e.EventType == "ConnectionClosed");

            // Stream duration
            var completedSessions = _streamSessions.Values
                .Where(s => s.CompletedAt > cutoffTime && s.CompletedSuccessfully)
                .ToList();
            if (completedSessions.Count != 0)
            {
                summary.AverageStreamDuration = completedSessions.Average(s => s.Duration);
            }

            // Chunk metrics
            var recentChunks = _recentChunks.Where(c => c.Timestamp > cutoffTime).ToList();
            if (recentChunks.Count != 0)
            {
                summary.AverageChunkProcessingTime = recentChunks.Average(c => c.ProcessingTime);
                summary.ChunksProcessedLastHour = recentChunks.Count;
                summary.BytesTransmittedLastHour = recentChunks.Sum(c => (long)c.Size);
            }

            // Success rate
            var totalStreams = _streamSessions.Values.Count(s => s.CompletedAt > cutoffTime);
            var successfulStreams = _streamSessions.Values
                .Count(s => s.CompletedAt > cutoffTime && s.CompletedSuccessfully);
            if (totalStreams > 0)
            {
                summary.StreamSuccessRate = (double)successfulStreams / totalStreams * 100;
            }
            else
            {
                summary.StreamSuccessRate = 100; // No streams means no failures
            }

            // Failures
            summary.StreamFailuresLastHour = _failureEvents
                .Count(f => f.Timestamp > cutoffTime);

            // Buffer metrics
            var recentBufferEvents = _bufferEvents.Where(b => b.Timestamp > cutoffTime).ToList();
            summary.BufferOverflowsLastHour = recentBufferEvents.Count(b => b.OverflowOccurred);
            if (recentBufferEvents.Count != 0)
            {
                summary.AverageBufferUtilization = recentBufferEvents.Average(b => b.Utilization);
            }

            // Chunk type breakdown
            var chunkGroups = recentChunks.GroupBy(c => c.ChunkType);
            foreach (var group in chunkGroups)
            {
                var chunks = group.ToList();
                summary.ChunkTypeBreakdown[group.Key] = new ChunkTypeMetrics
                {
                    ChunkType = group.Key,
                    Count = chunks.Count,
                    AverageSize = chunks.Average(c => c.Size),
                    AverageProcessingTime = chunks.Average(c => c.ProcessingTime),
                    SuccessRate = (double)chunks.Count(c => c.Success) / chunks.Count * 100,
                    TotalBytes = chunks.Sum(c => (long)c.Size)
                };
            }

            return Task.FromResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SSE metrics summary");
            return Task.FromResult(new SseMetricsSummary());
        }
    }

    /// <inheritdoc />
    public async Task<SseHealthIndicators> GetSseHealthIndicatorsAsync()
    {
        // Cache health indicators to avoid excessive computation
        if ((DateTime.UtcNow - _lastHealthCheck).TotalSeconds < HealthCheckIntervalSeconds)
        {
            return _cachedHealthIndicators;
        }

        try
        {
            var indicators = new SseHealthIndicators();
            var summary = await GetSseMetricsSummaryAsync();
            var cutoffTime = DateTime.UtcNow.AddHours(-1);

            // Connection stability
            var connectionDrops = _failureEvents
                .Count(f => f.Timestamp > cutoffTime && f.FailureReason.Contains("connection"));
            indicators.ConnectionStability = new HealthIndicator
            {
                CurrentValue = connectionDrops,
                WarningThreshold = WarningConnectionDropsPerHour,
                CriticalThreshold = CriticalConnectionDropsPerHour,
                Status = GetHealthStatus(connectionDrops, WarningConnectionDropsPerHour, CriticalConnectionDropsPerHour),
                Description = $"{connectionDrops} connection drops in the last hour"
            };

            // Stream throughput
            var throughputPerMinute = summary.ChunksProcessedLastHour / 60.0;
            indicators.StreamThroughput = new HealthIndicator
            {
                CurrentValue = throughputPerMinute,
                Status = throughputPerMinute > 0 ? HealthStatus.Healthy : HealthStatus.Unknown,
                Description = $"Processing {throughputPerMinute:F2} chunks per minute"
            };

            // Buffer health
            indicators.BufferHealth = new HealthIndicator
            {
                CurrentValue = summary.AverageBufferUtilization,
                WarningThreshold = WarningBufferUtilization,
                CriticalThreshold = CriticalBufferUtilization,
                Status = GetHealthStatus(summary.AverageBufferUtilization, WarningBufferUtilization, CriticalBufferUtilization),
                Description = $"Buffer utilization at {summary.AverageBufferUtilization:F1}%"
            };

            // Error rate
            var failureRate = 100.0 - summary.StreamSuccessRate;
            indicators.ErrorRate = new HealthIndicator
            {
                CurrentValue = failureRate,
                WarningThreshold = WarningFailureRate,
                CriticalThreshold = CriticalFailureRate,
                Status = GetHealthStatus(failureRate, WarningFailureRate, CriticalFailureRate),
                Description = $"Stream failure rate: {failureRate:F1}%"
            };

            // Latency
            indicators.Latency = new HealthIndicator
            {
                CurrentValue = summary.AverageChunkProcessingTime,
                WarningThreshold = WarningLatencyMs,
                CriticalThreshold = CriticalLatencyMs,
                Status = GetHealthStatus(summary.AverageChunkProcessingTime, WarningLatencyMs, CriticalLatencyMs),
                Description = $"Average chunk processing: {summary.AverageChunkProcessingTime:F0}ms"
            };

            // Recovery performance
            var recoveryAttempts = _failureEvents.Count(f => f.Timestamp > cutoffTime);
            var recoverySuccess = recoveryAttempts > 0 ?
                (double)summary.ConnectionsEstablishedLastHour / (recoveryAttempts + summary.ConnectionsEstablishedLastHour) * 100 : 100;
            indicators.RecoveryPerformance = new HealthIndicator
            {
                CurrentValue = recoverySuccess,
                Status = recoverySuccess > 90 ? HealthStatus.Healthy :
                        recoverySuccess > 75 ? HealthStatus.Degraded : HealthStatus.Critical,
                Description = $"Recovery success rate: {recoverySuccess:F1}%"
            };

            // Overall health determination
            var healthStatuses = new[]
            {
                indicators.ConnectionStability.Status,
                indicators.BufferHealth.Status,
                indicators.ErrorRate.Status,
                indicators.Latency.Status
            };

            indicators.OverallHealth =
                healthStatuses.Any(s => s == HealthStatus.Critical) ? HealthStatus.Critical :
                healthStatuses.Any(s => s == HealthStatus.Degraded) ? HealthStatus.Degraded :
                HealthStatus.Healthy;

            // Generate alerts
            indicators.ActiveAlerts = GenerateAlerts(indicators, summary);

            _cachedHealthIndicators = indicators;
            _lastHealthCheck = DateTime.UtcNow;

            return indicators;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get SSE health indicators");
            return new SseHealthIndicators { OverallHealth = HealthStatus.Unknown };
        }
    }

    /// <inheritdoc />
    public Task<List<SseMetricDataPoint>> GetSseHistoricalMetricsAsync(
        SseMetricType metricType,
        TimeSpan timeRange)
    {
        try
        {
            var dataPoints = new List<SseMetricDataPoint>();
            var startTime = DateTime.UtcNow.Subtract(timeRange);

            // Sample data points based on metric type
            // In production, this would query from a time-series database
            switch (metricType)
            {
                case SseMetricType.ActiveConnections:
                    // Create time buckets and count active connections
                    var bucketSize = timeRange.TotalMinutes > 60 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1);
                    for (var time = startTime; time < DateTime.UtcNow; time = time.Add(bucketSize))
                    {
                        var activeAtTime = _activeConnections.Values
                            .Count(c => c.EstablishedAt <= time && (!c.ClosedAt.HasValue || c.ClosedAt > time));
                        dataPoints.Add(new SseMetricDataPoint
                        {
                            Timestamp = time,
                            Value = activeAtTime,
                            MetricType = metricType
                        });
                    }
                    break;

                case SseMetricType.ChunkThroughput:
                    var chunks = _recentChunks.Where(c => c.Timestamp > startTime)
                        .GroupBy(c => new DateTime(c.Timestamp.Year, c.Timestamp.Month, c.Timestamp.Day,
                                                   c.Timestamp.Hour, c.Timestamp.Minute, 0))
                        .Select(g => new SseMetricDataPoint
                        {
                            Timestamp = g.Key,
                            Value = g.Count(),
                            MetricType = metricType
                        });
                    dataPoints.AddRange(chunks);
                    break;

                case SseMetricType.ChunkLatency:
                    var latencies = _recentChunks.Where(c => c.Timestamp > startTime)
                        .GroupBy(c => new DateTime(c.Timestamp.Year, c.Timestamp.Month, c.Timestamp.Day,
                                                   c.Timestamp.Hour, c.Timestamp.Minute, 0))
                        .Select(g => new SseMetricDataPoint
                        {
                            Timestamp = g.Key,
                            Value = g.Average(c => c.ProcessingTime),
                            MetricType = metricType
                        });
                    dataPoints.AddRange(latencies);
                    break;

                case SseMetricType.BufferUtilization:
                    var bufferMetrics = _bufferEvents.Where(b => b.Timestamp > startTime)
                        .GroupBy(b => new DateTime(b.Timestamp.Year, b.Timestamp.Month, b.Timestamp.Day,
                                                   b.Timestamp.Hour, b.Timestamp.Minute, 0))
                        .Select(g => new SseMetricDataPoint
                        {
                            Timestamp = g.Key,
                            Value = g.Average(b => b.Utilization),
                            MetricType = metricType
                        });
                    dataPoints.AddRange(bufferMetrics);
                    break;

                case SseMetricType.FailureRate:
                    var failures = _failureEvents.Where(f => f.Timestamp > startTime)
                        .GroupBy(f => new DateTime(f.Timestamp.Year, f.Timestamp.Month, f.Timestamp.Day,
                                                   f.Timestamp.Hour, 0, 0))
                        .Select(g => new SseMetricDataPoint
                        {
                            Timestamp = g.Key,
                            Value = g.Count(),
                            MetricType = metricType
                        });
                    dataPoints.AddRange(failures);
                    break;
            }

            return Task.FromResult(dataPoints.OrderBy(d => d.Timestamp).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get historical metrics for {MetricType}", metricType);
            return Task.FromResult(new List<SseMetricDataPoint>());
        }
    }

    #region Private Helper Methods

    private static HealthStatus GetHealthStatus(double currentValue, double warningThreshold, double criticalThreshold)
    {
        if (currentValue >= criticalThreshold)
        {
            return HealthStatus.Critical;
        }
        if (currentValue >= warningThreshold)
        {
            return HealthStatus.Degraded;
        }
        return HealthStatus.Healthy;
    }

    private List<SseAlert> GenerateAlerts(SseHealthIndicators indicators, SseMetricsSummary summary)
    {
        var alerts = new List<SseAlert>();

        // Check connection stability
        if (indicators.ConnectionStability.Status >= HealthStatus.Degraded)
        {
            alerts.Add(new SseAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                Severity = indicators.ConnectionStability.Status == HealthStatus.Critical ?
                          AlertSeverity.Critical : AlertSeverity.Warning,
                Message = "High number of SSE connection drops detected",
                TriggeredAt = DateTime.UtcNow,
                TriggeringMetric = "ConnectionDrops",
                CurrentValue = indicators.ConnectionStability.CurrentValue,
                ThresholdValue = indicators.ConnectionStability.Status == HealthStatus.Critical ?
                                indicators.ConnectionStability.CriticalThreshold :
                                indicators.ConnectionStability.WarningThreshold,
                RecommendedAction = "Check network stability and client reconnection logic"
            });
        }

        // Check buffer health
        if (indicators.BufferHealth.Status >= HealthStatus.Degraded)
        {
            alerts.Add(new SseAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                Severity = indicators.BufferHealth.Status == HealthStatus.Critical ?
                          AlertSeverity.Critical : AlertSeverity.Warning,
                Message = "SSE buffer utilization is high",
                TriggeredAt = DateTime.UtcNow,
                TriggeringMetric = "BufferUtilization",
                CurrentValue = indicators.BufferHealth.CurrentValue,
                ThresholdValue = indicators.BufferHealth.Status == HealthStatus.Critical ?
                                indicators.BufferHealth.CriticalThreshold :
                                indicators.BufferHealth.WarningThreshold,
                RecommendedAction = "Consider increasing buffer capacity or optimizing chunk processing"
            });
        }

        // Check error rate
        if (indicators.ErrorRate.Status >= HealthStatus.Degraded)
        {
            alerts.Add(new SseAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                Severity = indicators.ErrorRate.Status == HealthStatus.Critical ?
                          AlertSeverity.Critical : AlertSeverity.Warning,
                Message = "Elevated SSE stream failure rate",
                TriggeredAt = DateTime.UtcNow,
                TriggeringMetric = "FailureRate",
                CurrentValue = indicators.ErrorRate.CurrentValue,
                ThresholdValue = indicators.ErrorRate.Status == HealthStatus.Critical ?
                                indicators.ErrorRate.CriticalThreshold :
                                indicators.ErrorRate.WarningThreshold,
                RecommendedAction = "Review error logs and investigate stream processing pipeline"
            });
        }

        // Check latency
        if (indicators.Latency.Status >= HealthStatus.Degraded)
        {
            alerts.Add(new SseAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                Severity = indicators.Latency.Status == HealthStatus.Critical ?
                          AlertSeverity.Critical : AlertSeverity.Warning,
                Message = "High SSE chunk processing latency",
                TriggeredAt = DateTime.UtcNow,
                TriggeringMetric = "ChunkLatency",
                CurrentValue = indicators.Latency.CurrentValue,
                ThresholdValue = indicators.Latency.Status == HealthStatus.Critical ?
                                indicators.Latency.CriticalThreshold :
                                indicators.Latency.WarningThreshold,
                RecommendedAction = "Optimize chunk processing logic or scale processing resources"
            });
        }

        // Check for buffer overflows
        if (summary.BufferOverflowsLastHour > 0)
        {
            alerts.Add(new SseAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                Severity = summary.BufferOverflowsLastHour > 5 ? AlertSeverity.Critical : AlertSeverity.Warning,
                Message = $"{summary.BufferOverflowsLastHour} buffer overflow events in the last hour",
                TriggeredAt = DateTime.UtcNow,
                TriggeringMetric = "BufferOverflows",
                CurrentValue = summary.BufferOverflowsLastHour,
                ThresholdValue = 0,
                RecommendedAction = "Increase buffer capacity or implement back-pressure mechanism"
            });
        }

        return alerts;
    }

    private void TrimQueue<T>(ConcurrentQueue<T> queue)
    {
        while (queue.Count > MaxRecentEvents)
        {
            _ = queue.TryDequeue(out _);
        }
    }

    private string? GetActiveSessionKey(string grainId)
    {
        // Find the most recent active session for this grain
        var session = _streamSessions
            .Where(s => s.Value.GrainId == grainId && !s.Value.CompletedSuccessfully)
            .OrderByDescending(s => s.Value.CompletedAt)
            .FirstOrDefault();

        return session.Key;
    }

    private void CleanOldSessions()
    {
        var cutoff = DateTime.UtcNow.AddHours(-MetricsRetentionHours);
        var keysToRemove = _streamSessions
            .Where(s => s.Value.CompletedAt < cutoff)
            .Select(s => s.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _ = _streamSessions.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Performs manual cleanup of old metrics data.
    /// This method is called by the SseMetricsCleanupService.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task PerformManualCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-MetricsRetentionHours);

            // Clean old connections
            var oldConnections = _activeConnections
                .Where(c => !c.Value.IsActive && c.Value.ClosedAt < cutoffTime)
                .Select(c => c.Key)
                .ToList();

            foreach (var key in oldConnections)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                _ = _activeConnections.TryRemove(key, out _);
            }

            // Clean old sessions
            CleanOldSessions();

            // Clean old events from queues
            CleanOldQueuedEvents(cutoffTime);

            _logger.LogDebug("SSE metrics cleanup completed. Removed {Count} old items", oldConnections.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform SSE metrics cleanup");
            throw;
        }

        await Task.CompletedTask;
    }

    private void CleanOldQueuedEvents(DateTime cutoffTime)
    {
        // Clean old stream events
        var tempStreamEvents = new ConcurrentQueue<SseStreamEvent>();
        while (_recentStreamEvents.TryDequeue(out var evt))
        {
            if (evt.Timestamp > cutoffTime)
            {
                tempStreamEvents.Enqueue(evt);
            }
        }
        while (tempStreamEvents.TryDequeue(out var evt))
        {
            _recentStreamEvents.Enqueue(evt);
        }

        // Clean old chunk metrics
        var tempChunks = new ConcurrentQueue<SseChunkMetrics>();
        while (_recentChunks.TryDequeue(out var chunk))
        {
            if (chunk.Timestamp > cutoffTime)
            {
                tempChunks.Enqueue(chunk);
            }
        }
        while (tempChunks.TryDequeue(out var chunk))
        {
            _recentChunks.Enqueue(chunk);
        }

        // Clean old buffer events
        var tempBufferEvents = new ConcurrentQueue<SseBufferEvent>();
        while (_bufferEvents.TryDequeue(out var bufferEvt))
        {
            if (bufferEvt.Timestamp > cutoffTime)
            {
                tempBufferEvents.Enqueue(bufferEvt);
            }
        }
        while (tempBufferEvents.TryDequeue(out var bufferEvt))
        {
            _bufferEvents.Enqueue(bufferEvt);
        }

        // Clean old failure events
        var tempFailures = new ConcurrentQueue<SseFailureEvent>();
        while (_failureEvents.TryDequeue(out var failure))
        {
            if (failure.Timestamp > cutoffTime)
            {
                tempFailures.Enqueue(failure);
            }
        }
        while (tempFailures.TryDequeue(out var failure))
        {
            _failureEvents.Enqueue(failure);
        }
    }

    #endregion
}

#region Internal Models

internal sealed class SseConnectionMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public DateTime EstablishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsActive { get; set; }
    public double Duration { get; set; }
}

internal sealed class SseStreamEvent
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Duration { get; set; }
}

internal sealed class SseChunkMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public int Size { get; set; }
    public double ProcessingTime { get; set; }
    public bool Success { get; set; }
    public DateTime Timestamp { get; set; }
}

internal sealed class SseBufferEvent
{
    public string GrainId { get; set; } = string.Empty;
    public int BufferSize { get; set; }
    public int BufferCapacity { get; set; }
    public double Utilization { get; set; }
    public bool OverflowOccurred { get; set; }
    public DateTime Timestamp { get; set; }
}

internal sealed class SseFailureEvent
{
    public string GrainId { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public int ChunksLost { get; set; }
    public DateTime Timestamp { get; set; }
}

internal sealed class StreamSessionMetrics
{
    public string GrainId { get; set; } = string.Empty;
    public double Duration { get; set; }
    public int TotalChunks { get; set; }
    public int FailedChunks { get; set; }
    public long TotalBytes { get; set; }
    public bool CompletedSuccessfully { get; set; }
    public DateTime CompletedAt { get; set; }
}

#endregion