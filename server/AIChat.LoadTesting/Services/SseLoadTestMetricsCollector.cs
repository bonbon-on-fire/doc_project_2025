using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Collects and aggregates SSE-specific metrics during load testing.
/// This is different from the Orleans ISseMetricsCollector - this one is for client-side metrics.
/// </summary>
public interface ISseLoadTestMetricsCollector
{
    /// <summary>
    /// Records connection establishment metrics.
    /// </summary>
    void RecordConnectionEstablished(string connectionId, double latencyMs);

    /// <summary>
    /// Records connection failure.
    /// </summary>
    void RecordConnectionFailed(string connectionId, string reason);

    /// <summary>
    /// Records connection drop/disconnect.
    /// </summary>
    void RecordConnectionDropped(string connectionId, string reason, TimeSpan connectionDuration);

    /// <summary>
    /// Records chunk reception metrics.
    /// </summary>
    void RecordChunkReceived(
        string connectionId,
        string chunkType,
        int sizeBytes,
        double latencyMs
    );

    /// <summary>
    /// Records buffer metrics.
    /// </summary>
    void RecordBufferMetrics(string connectionId, int bufferSize, int bufferCapacity);

    /// <summary>
    /// Records reconnection attempt.
    /// </summary>
    void RecordReconnectionAttempt(string connectionId, bool success, double latencyMs);

    /// <summary>
    /// Gets current metrics snapshot.
    /// </summary>
    SseLoadTestMetrics GetMetricsSnapshot();

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    void Reset();
}

/// <summary>
/// Implementation of SSE load test metrics collector.
/// </summary>
public class SseLoadTestMetricsCollector : ISseLoadTestMetricsCollector
{
    private readonly ILogger<SseLoadTestMetricsCollector> _logger;
    private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
    private readonly ConcurrentBag<double> _connectionLatencies = [];
    private readonly ConcurrentBag<double> _chunkLatencies = [];
    private readonly ConcurrentDictionary<string, ChunkTypeStats> _chunkTypeStats = new();
    private readonly Stopwatch _stopwatch = new();

    private long _totalConnections;
    private long _successfulConnections;
    private long _failedConnections;
    private long _droppedConnections;
    private long _totalChunks;
    private long _totalBytes;
    private long _bufferOverflows;
    private long _reconnectionAttempts;
    private long _successfulReconnections;

    public SseLoadTestMetricsCollector(ILogger<SseLoadTestMetricsCollector> logger)
    {
        _logger = logger;
        _stopwatch.Start();
    }

    public void RecordConnectionEstablished(string connectionId, double latencyMs)
    {
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _successfulConnections);
        _connectionLatencies.Add(latencyMs);

        var metrics = _connectionMetrics.GetOrAdd(
            connectionId,
            _ => new ConnectionMetrics { ConnectionId = connectionId }
        );
        metrics.ConnectedAt = DateTime.UtcNow;
        metrics.ConnectionLatency = latencyMs;

        _logger.LogTrace(
            "Connection {ConnectionId} established in {Latency}ms",
            connectionId,
            latencyMs
        );
    }

    public void RecordConnectionFailed(string connectionId, string reason)
    {
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _failedConnections);

        var metrics = _connectionMetrics.GetOrAdd(
            connectionId,
            _ => new ConnectionMetrics { ConnectionId = connectionId }
        );
        metrics.FailureReason = reason;
        metrics.FailedAt = DateTime.UtcNow;

        _logger.LogWarning(
            "Connection {ConnectionId} failed: {Reason}",
            connectionId,
            reason
        );
    }

    public void RecordConnectionDropped(
        string connectionId,
        string reason,
        TimeSpan connectionDuration
    )
    {
        Interlocked.Increment(ref _droppedConnections);

        if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
        {
            metrics.DisconnectedAt = DateTime.UtcNow;
            metrics.ConnectionDuration = connectionDuration;
            metrics.DisconnectReason = reason;
        }

        _logger.LogInformation(
            "Connection {ConnectionId} dropped after {Duration}s: {Reason}",
            connectionId,
            connectionDuration.TotalSeconds,
            reason
        );
    }

    public void RecordChunkReceived(
        string connectionId,
        string chunkType,
        int sizeBytes,
        double latencyMs
    )
    {
        Interlocked.Increment(ref _totalChunks);
        Interlocked.Add(ref _totalBytes, sizeBytes);
        _chunkLatencies.Add(latencyMs);

        if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
        {
            Interlocked.Increment(ref metrics.ChunksReceived);
            Interlocked.Add(ref metrics.BytesReceived, sizeBytes);
            metrics.ChunkLatencies.Add(latencyMs);
        }

        var typeStats = _chunkTypeStats.GetOrAdd(
            chunkType,
            _ => new ChunkTypeStats { Type = chunkType }
        );
        Interlocked.Increment(ref typeStats.Count);
        Interlocked.Add(ref typeStats.TotalBytes, sizeBytes);
        typeStats.Latencies.Add(latencyMs);

        _logger.LogTrace(
            "Chunk received on {ConnectionId}: Type={Type}, Size={Size}, Latency={Latency}ms",
            connectionId,
            chunkType,
            sizeBytes,
            latencyMs
        );
    }

    public void RecordBufferMetrics(string connectionId, int bufferSize, int bufferCapacity)
    {
        if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
        {
            metrics.CurrentBufferSize = bufferSize;
            metrics.BufferCapacity = bufferCapacity;

            if (bufferSize >= bufferCapacity)
            {
                Interlocked.Increment(ref _bufferOverflows);
                Interlocked.Increment(ref metrics.BufferOverflows);

                _logger.LogWarning(
                    "Buffer overflow on connection {ConnectionId}: {Size}/{Capacity}",
                    connectionId,
                    bufferSize,
                    bufferCapacity
                );
            }
        }
    }

    public void RecordReconnectionAttempt(string connectionId, bool success, double latencyMs)
    {
        Interlocked.Increment(ref _reconnectionAttempts);

        if (success)
        {
            Interlocked.Increment(ref _successfulReconnections);
        }

        if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
        {
            Interlocked.Increment(ref metrics.ReconnectionAttempts);
            if (success)
            {
                Interlocked.Increment(ref metrics.SuccessfulReconnections);
            }
            metrics.ReconnectionLatencies.Add(latencyMs);
        }

        _logger.LogInformation(
            "Reconnection attempt for {ConnectionId}: Success={Success}, Latency={Latency}ms",
            connectionId,
            success,
            latencyMs
        );
    }

    public SseLoadTestMetrics GetMetricsSnapshot()
    {
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

        return new SseLoadTestMetrics
        {
            Timestamp = DateTime.UtcNow,
            ElapsedTime = _stopwatch.Elapsed,
            ConnectionMetrics = new SseConnectionAggregateMetrics
            {
                TotalAttempted = _totalConnections,
                Successful = _successfulConnections,
                Failed = _failedConnections,
                Dropped = _droppedConnections,
                CurrentlyActive = _connectionMetrics.Count(m => m.Value.ConnectedAt.HasValue && !m.Value.DisconnectedAt.HasValue),
                ConnectionsPerSecond = _totalConnections / elapsedSeconds,
                AverageConnectionLatency = CalculateAverage(_connectionLatencies),
                P95ConnectionLatency = CalculatePercentile(_connectionLatencies, 0.95),
                P99ConnectionLatency = CalculatePercentile(_connectionLatencies, 0.99),
            },
            ChunkMetrics = new SseChunkAggregateMetrics
            {
                TotalChunks = _totalChunks,
                TotalBytes = _totalBytes,
                ChunksPerSecond = _totalChunks / elapsedSeconds,
                BytesPerSecond = _totalBytes / elapsedSeconds,
                AverageLatency = CalculateAverage(_chunkLatencies),
                P95Latency = CalculatePercentile(_chunkLatencies, 0.95),
                P99Latency = CalculatePercentile(_chunkLatencies, 0.99),
                MaxLatency = !_chunkLatencies.IsEmpty ? _chunkLatencies.Max() : 0,
                ChunkTypeBreakdown = [.._chunkTypeStats
                    .Select(kvp => new ChunkTypeMetrics
                    {
                        Type = kvp.Key,
                        Count = kvp.Value.Count,
                        TotalBytes = kvp.Value.TotalBytes,
                        AverageSize = kvp.Value.Count > 0 ? kvp.Value.TotalBytes / (double)kvp.Value.Count : 0,
                        AverageLatency = CalculateAverage(kvp.Value.Latencies),
                    })]
            },
            BufferMetrics = new SseBufferMetrics
            {
                TotalOverflows = _bufferOverflows,
                AverageUtilization = CalculateAverageBufferUtilization(),
                MaxUtilization = CalculateMaxBufferUtilization(),
            },
            ReconnectionMetrics = new SseReconnectionMetrics
            {
                TotalAttempts = _reconnectionAttempts,
                Successful = _successfulReconnections,
                Failed = _reconnectionAttempts - _successfulReconnections,
                SuccessRate = _reconnectionAttempts > 0
                    ? _successfulReconnections / (double)_reconnectionAttempts
                    : 1.0,
                AverageReconnectLatency = CalculateAverageReconnectLatency(),
            },
            PerConnectionMetrics = [.._connectionMetrics.Values
                .Select(m => new SsePerConnectionMetric
                {
                    ConnectionId = m.ConnectionId,
                    IsActive = m.ConnectedAt.HasValue && !m.DisconnectedAt.HasValue,
                    Duration = m.ConnectionDuration,
                    ChunksReceived = m.ChunksReceived,
                    BytesReceived = m.BytesReceived,
                    AverageChunkLatency = CalculateAverage(m.ChunkLatencies),
                    BufferOverflows = m.BufferOverflows,
                    ReconnectionAttempts = m.ReconnectionAttempts,
                })]
        };
    }

    public void Reset()
    {
        _connectionMetrics.Clear();
        _connectionLatencies.Clear();
        _chunkLatencies.Clear();
        _chunkTypeStats.Clear();

        _totalConnections = 0;
        _successfulConnections = 0;
        _failedConnections = 0;
        _droppedConnections = 0;
        _totalChunks = 0;
        _totalBytes = 0;
        _bufferOverflows = 0;
        _reconnectionAttempts = 0;
        _successfulReconnections = 0;

        _stopwatch.Restart();
        _logger.LogInformation("SSE metrics collector reset");
    }

    private static double CalculateAverage(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count > 0 ? list.Average() : 0;
    }

    private static double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        var sortedValues = values.OrderBy(v => v).ToList();
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return sortedValues[index];
    }

    private double CalculateAverageBufferUtilization()
    {
        var utilizations = _connectionMetrics.Values
            .Where(m => m.BufferCapacity > 0)
            .Select(m => m.CurrentBufferSize / (double)m.BufferCapacity * 100);

        return utilizations.Any() ? utilizations.Average() : 0;
    }

    private double CalculateMaxBufferUtilization()
    {
        var utilizations = _connectionMetrics.Values
            .Where(m => m.BufferCapacity > 0)
            .Select(m => m.CurrentBufferSize / (double)m.BufferCapacity * 100);

        return utilizations.Any() ? utilizations.Max() : 0;
    }

    private double CalculateAverageReconnectLatency()
    {
        var allLatencies = _connectionMetrics.Values
            .SelectMany(m => m.ReconnectionLatencies)
            .ToList();

        return allLatencies.Count > 0 ? allLatencies.Average() : 0;
    }

    private sealed class ConnectionMetrics
    {
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime? ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime? FailedAt { get; set; }
        public TimeSpan ConnectionDuration { get; set; }
        public double ConnectionLatency { get; set; }
        public string? FailureReason { get; set; }
        public string? DisconnectReason { get; set; }
        public long ChunksReceived;
        public long BytesReceived;
        public ConcurrentBag<double> ChunkLatencies { get; } = [];
        public int CurrentBufferSize { get; set; }
        public int BufferCapacity { get; set; }
        public long BufferOverflows;
        public long ReconnectionAttempts;
        public long SuccessfulReconnections;
        public ConcurrentBag<double> ReconnectionLatencies { get; } = [];
    }

    private sealed class ChunkTypeStats
    {
        public string Type { get; set; } = string.Empty;
        public long Count;
        public long TotalBytes;
        public ConcurrentBag<double> Latencies { get; } = [];
    }
}

/// <summary>
/// Comprehensive SSE load test metrics.
/// </summary>
public class SseLoadTestMetrics
{
    public DateTime Timestamp { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public SseConnectionAggregateMetrics ConnectionMetrics { get; set; } = new();
    public SseChunkAggregateMetrics ChunkMetrics { get; set; } = new();
    public SseBufferMetrics BufferMetrics { get; set; } = new();
    public SseReconnectionMetrics ReconnectionMetrics { get; set; } = new();
    public List<SsePerConnectionMetric> PerConnectionMetrics { get; set; } = [];
}

/// <summary>
/// Aggregate connection metrics.
/// </summary>
public class SseConnectionAggregateMetrics
{
    public long TotalAttempted { get; set; }
    public long Successful { get; set; }
    public long Failed { get; set; }
    public long Dropped { get; set; }
    public int CurrentlyActive { get; set; }
    public double ConnectionsPerSecond { get; set; }
    public double AverageConnectionLatency { get; set; }
    public double P95ConnectionLatency { get; set; }
    public double P99ConnectionLatency { get; set; }
}

/// <summary>
/// Aggregate chunk processing metrics.
/// </summary>
public class SseChunkAggregateMetrics
{
    public long TotalChunks { get; set; }
    public long TotalBytes { get; set; }
    public double ChunksPerSecond { get; set; }
    public double BytesPerSecond { get; set; }
    public double AverageLatency { get; set; }
    public double P95Latency { get; set; }
    public double P99Latency { get; set; }
    public double MaxLatency { get; set; }
    public List<ChunkTypeMetrics> ChunkTypeBreakdown { get; set; } = [];
}

/// <summary>
/// Chunk type specific metrics.
/// </summary>
public class ChunkTypeMetrics
{
    public string Type { get; set; } = string.Empty;
    public long Count { get; set; }
    public long TotalBytes { get; set; }
    public double AverageSize { get; set; }
    public double AverageLatency { get; set; }
}

/// <summary>
/// Buffer utilization metrics.
/// </summary>
public class SseBufferMetrics
{
    public long TotalOverflows { get; set; }
    public double AverageUtilization { get; set; }
    public double MaxUtilization { get; set; }
}

/// <summary>
/// Reconnection metrics.
/// </summary>
public class SseReconnectionMetrics
{
    public long TotalAttempts { get; set; }
    public long Successful { get; set; }
    public long Failed { get; set; }
    public double SuccessRate { get; set; }
    public double AverageReconnectLatency { get; set; }
}

/// <summary>
/// Per-connection metrics.
/// </summary>
public class SsePerConnectionMetric
{
    public string ConnectionId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public TimeSpan Duration { get; set; }
    public long ChunksReceived { get; set; }
    public long BytesReceived { get; set; }
    public double AverageChunkLatency { get; set; }
    public long BufferOverflows { get; set; }
    public long ReconnectionAttempts { get; set; }
}
