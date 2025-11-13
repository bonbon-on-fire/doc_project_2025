using System.Collections.Concurrent;
using System.Text.Json;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Implementation of metrics recorder for SSE connections.
/// Provides thread-safe metrics collection and aggregation.
/// </summary>
public class SseMetricsRecorder : ISseMetricsRecorder
{
    private readonly ILogger<SseMetricsRecorder> _logger;
    private readonly ConcurrentDictionary<string, ConnectionMetricsData> _connectionMetrics;
    private readonly ConcurrentBag<double> _allLatencies;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private DateTime _startTime;
    private long _totalConnectionAttempts;
    private long _successfulConnections;
    private long _failedConnections;
    private long _totalMessagesReceived;
    private long _totalBytesReceived;
    private long _totalErrors;
    private long _totalReconnectionAttempts;
    private long _successfulReconnections;
    private readonly ConcurrentDictionary<string, long> _errorsByType;
    private readonly ConcurrentDictionary<string, long> _messagesByEventType;

    public SseMetricsRecorder(ILogger<SseMetricsRecorder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionMetrics = new ConcurrentDictionary<string, ConnectionMetricsData>();
        _allLatencies = [];
        _errorsByType = new ConcurrentDictionary<string, long>();
        _messagesByEventType = new ConcurrentDictionary<string, long>();
        _startTime = DateTime.UtcNow;
    }

    public void RecordConnectionAttempt(string connectionId, DateTime timestamp)
    {
        Interlocked.Increment(ref _totalConnectionAttempts);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.LastAttemptTime = timestamp;
        metrics.AttemptCount++;

        _logger.LogTrace("Recorded connection attempt for {ConnectionId}", connectionId);
    }

    public void RecordConnectionSuccess(string connectionId, double latencyMs, DateTime timestamp)
    {
        Interlocked.Increment(ref _successfulConnections);
        _allLatencies.Add(latencyMs);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.IsConnected = true;
        metrics.ConnectedAt = timestamp;
        metrics.ConnectionLatency = latencyMs;

        _logger.LogDebug("Recorded successful connection for {ConnectionId} with latency {Latency}ms",
            connectionId, latencyMs);
    }

    public void RecordConnectionFailure(string connectionId, Exception error, DateTime timestamp)
    {
        Interlocked.Increment(ref _failedConnections);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.LastError = error.Message;
        metrics.LastErrorTime = timestamp;
        metrics.ErrorCount++;

        RecordErrorType(error);

        _logger.LogDebug("Recorded connection failure for {ConnectionId}: {Error}",
            connectionId, error.Message);
    }

    public void RecordDisconnection(string connectionId, string reason, DateTime timestamp)
    {
        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.IsConnected = false;
        metrics.DisconnectedAt = timestamp;
        metrics.DisconnectReason = reason;

        _logger.LogTrace("Recorded disconnection for {ConnectionId}: {Reason}", connectionId, reason);
    }

    public void RecordMessageReceived(string connectionId, SseMessage message, double processingTimeMs)
    {
        Interlocked.Increment(ref _totalMessagesReceived);
        _allLatencies.Add(message.LatencyMs);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.MessagesReceived++;
        metrics.BytesReceived += message.Data.Length;
        metrics.MessageLatencies.Add(message.LatencyMs);

        // Track message types
        _messagesByEventType.AddOrUpdate(message.Event, 1, (_, count) => count + 1);

        _logger.LogTrace("Recorded message for {ConnectionId}: Type={EventType}, Processing={ProcessingTime}ms",
            connectionId, message.Event, processingTimeMs);
    }

    public void RecordChunkReceived(string connectionId, int chunkSizeBytes, double latencyMs)
    {
        Interlocked.Add(ref _totalBytesReceived, chunkSizeBytes);
        _allLatencies.Add(latencyMs);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.ChunksReceived++;
        metrics.BytesReceived += chunkSizeBytes;

        _logger.LogTrace("Recorded chunk for {ConnectionId}: Size={Size}bytes, Latency={Latency}ms",
            connectionId, chunkSizeBytes, latencyMs);
    }

    public void RecordBufferState(string connectionId, int bufferSize, int bufferCapacity, bool overflow)
    {
        var metrics = GetOrCreateConnectionMetrics(connectionId);
        var utilization = bufferCapacity > 0 ? (double)bufferSize / bufferCapacity * 100 : 0;

        metrics.CurrentBufferUtilization = utilization;
        metrics.PeakBufferUtilization = Math.Max(metrics.PeakBufferUtilization, utilization);

        if (overflow)
        {
            metrics.BufferOverflowCount++;
            _logger.LogWarning("Buffer overflow detected for connection {ConnectionId}", connectionId);
        }

        _logger.LogTrace("Recorded buffer state for {ConnectionId}: {Size}/{Capacity} ({Utilization:F1}%)",
            connectionId, bufferSize, bufferCapacity, utilization);
    }

    public void RecordReconnection(string connectionId, int attemptNumber, bool success, double latencyMs)
    {
        Interlocked.Increment(ref _totalReconnectionAttempts);
        if (success)
        {
            Interlocked.Increment(ref _successfulReconnections);
        }

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.ReconnectAttempts = attemptNumber;

        _logger.LogDebug("Recorded reconnection attempt #{Attempt} for {ConnectionId}: Success={Success}, Latency={Latency}ms",
            attemptNumber, connectionId, success, latencyMs);
    }

    public void RecordError(string connectionId, Exception error, string context)
    {
        Interlocked.Increment(ref _totalErrors);

        var metrics = GetOrCreateConnectionMetrics(connectionId);
        metrics.ErrorCount++;
        metrics.LastError = $"{context}: {error.Message}";
        metrics.LastErrorTime = DateTime.UtcNow;

        RecordErrorType(error);

        _logger.LogWarning(error, "Error recorded for {ConnectionId} in context {Context}",
            connectionId, context);
    }

    public SseConnectionMetrics? GetConnectionMetrics(string connectionId)
    {
        if (!_connectionMetrics.TryGetValue(connectionId, out var data))
        {
            return null;
        }

        return ConvertToConnectionMetrics(connectionId, data);
    }

    public AggregatedSseMetrics GetAggregatedMetrics()
    {
        var now = DateTime.UtcNow;
        var duration = now - _startTime;

        var activeConnections = _connectionMetrics.Count(kvp => kvp.Value.IsConnected);
        var allLatenciesList = _allLatencies.ToList();
        allLatenciesList.Sort();

        return new AggregatedSseMetrics
        {
            TotalConnectionAttempts = _totalConnectionAttempts,
            SuccessfulConnections = _successfulConnections,
            FailedConnections = _failedConnections,
            ActiveConnections = activeConnections,
            TotalMessagesReceived = _totalMessagesReceived,
            TotalBytesReceived = _totalBytesReceived,
            TotalErrors = _totalErrors,
            TotalReconnectionAttempts = _totalReconnectionAttempts,
            SuccessfulReconnections = _successfulReconnections,
            AverageConnectionLatencyMs = allLatenciesList.Count > 0 ? allLatenciesList.Average() : 0,
            AverageMessageLatencyMs = CalculateAverageMessageLatency(),
            LatencyPercentiles = CalculateLatencyPercentiles(allLatenciesList),
            MessagesPerSecond = duration.TotalSeconds > 0 ? _totalMessagesReceived / duration.TotalSeconds : 0,
            BytesPerSecond = duration.TotalSeconds > 0 ? _totalBytesReceived / duration.TotalSeconds : 0,
            StartTime = _startTime,
            LastUpdateTime = now,
            ErrorsByType = new Dictionary<string, long>(_errorsByType),
            MessagesByEventType = new Dictionary<string, long>(_messagesByEventType)
        };
    }

    public string ExportMetrics(string format = "json")
    {
        var metrics = GetAggregatedMetrics();

        return format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(metrics, s_jsonOptions),
            "csv" => ExportAsCsv(metrics),
            "prometheus" => ExportAsPrometheus(metrics),
            _ => throw new ArgumentException($"Unsupported export format: {format}", nameof(format))
        };
    }

    public void Reset()
    {
        _connectionMetrics.Clear();
        _allLatencies.Clear();
        _errorsByType.Clear();
        _messagesByEventType.Clear();

        _totalConnectionAttempts = 0;
        _successfulConnections = 0;
        _failedConnections = 0;
        _totalMessagesReceived = 0;
        _totalBytesReceived = 0;
        _totalErrors = 0;
        _totalReconnectionAttempts = 0;
        _successfulReconnections = 0;

        _startTime = DateTime.UtcNow;

        _logger.LogInformation("Metrics recorder reset");
    }

    public async Task FlushAsync()
    {
        // In a real implementation, this might flush to external storage or metrics systems
        _logger.LogDebug("Flushing metrics (currently no-op)");
        await Task.CompletedTask;
    }

    private ConnectionMetricsData GetOrCreateConnectionMetrics(string connectionId)
    {
        return _connectionMetrics.GetOrAdd(connectionId, _ => new ConnectionMetricsData
        {
            ConnectionId = connectionId
        });
    }

    private void RecordErrorType(Exception error)
    {
        var errorType = error.GetType().Name;
        _errorsByType.AddOrUpdate(errorType, 1, (_, count) => count + 1);
    }

    private double CalculateAverageMessageLatency()
    {
        var allMessageLatencies = _connectionMetrics.Values
            .SelectMany(m => m.MessageLatencies)
            .ToList();

        return allMessageLatencies.Count > 0 ? allMessageLatencies.Average() : 0;
    }

    private static LatencyPercentiles? CalculateLatencyPercentiles(List<double> sortedLatencies)
    {
        if (sortedLatencies.Count == 0)
        {
            return null;
        }

        return new LatencyPercentiles
        {
            P50 = GetPercentile(sortedLatencies, 50),
            P75 = GetPercentile(sortedLatencies, 75),
            P90 = GetPercentile(sortedLatencies, 90),
            P95 = GetPercentile(sortedLatencies, 95),
            P99 = GetPercentile(sortedLatencies, 99),
            Max = sortedLatencies[^1],
            Min = sortedLatencies[0]
        };
    }

    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile / 100.0 * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return sortedValues[index];
    }

    private static SseConnectionMetrics ConvertToConnectionMetrics(string connectionId, ConnectionMetricsData data)
    {
        var latencies = data.MessageLatencies.ToList();
        latencies.Sort();

        return new SseConnectionMetrics
        {
            ConnectionId = connectionId,
            IsConnected = data.IsConnected,
            ConnectedAt = data.ConnectedAt ?? DateTime.MinValue,
            DisconnectedAt = data.DisconnectedAt,
            Duration = data.IsConnected
                ? DateTime.UtcNow - (data.ConnectedAt ?? DateTime.UtcNow)
                : (data.DisconnectedAt ?? DateTime.UtcNow) - (data.ConnectedAt ?? DateTime.UtcNow),
            MessagesReceived = data.MessagesReceived,
            BytesReceived = data.BytesReceived,
            ChunksReceived = data.ChunksReceived,
            AverageChunkLatency = latencies.Count > 0 ? latencies.Average() : 0,
            ReconnectAttempts = data.ReconnectAttempts,
            ErrorCount = data.ErrorCount,
            LastError = data.LastError,
            BufferUtilization = data.CurrentBufferUtilization,
            PeakBufferUtilization = data.PeakBufferUtilization,
            LatencyPercentiles = latencies.Count > 0 ? CalculateLatencyPercentiles(latencies) : null
        };
    }

    private static string ExportAsCsv(AggregatedSseMetrics metrics)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Metric,Value");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"TotalConnectionAttempts,{metrics.TotalConnectionAttempts}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"SuccessfulConnections,{metrics.SuccessfulConnections}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"FailedConnections,{metrics.FailedConnections}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"ActiveConnections,{metrics.ActiveConnections}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"TotalMessagesReceived,{metrics.TotalMessagesReceived}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"TotalBytesReceived,{metrics.TotalBytesReceived}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"MessagesPerSecond,{metrics.MessagesPerSecond:F2}");
        csv.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"BytesPerSecond,{metrics.BytesPerSecond:F2}");
        return csv.ToString();
    }

    private static string ExportAsPrometheus(AggregatedSseMetrics metrics)
    {
        var prom = new System.Text.StringBuilder();
        prom.AppendLine("# HELP sse_connections_total Total SSE connection attempts");
        prom.AppendLine("# TYPE sse_connections_total counter");
        prom.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"sse_connections_total {metrics.TotalConnectionAttempts}");

        prom.AppendLine("# HELP sse_connections_active Currently active SSE connections");
        prom.AppendLine("# TYPE sse_connections_active gauge");
        prom.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"sse_connections_active {metrics.ActiveConnections}");

        prom.AppendLine("# HELP sse_messages_total Total SSE messages received");
        prom.AppendLine("# TYPE sse_messages_total counter");
        prom.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"sse_messages_total {metrics.TotalMessagesReceived}");

        return prom.ToString();
    }

    private sealed class ConnectionMetricsData
    {
        public string ConnectionId { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime? LastAttemptTime { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public string? LastError { get; set; }
        public string? DisconnectReason { get; set; }
        public int AttemptCount { get; set; }
        public int MessagesReceived { get; set; }
        public long BytesReceived { get; set; }
        public int ChunksReceived { get; set; }
        public int ReconnectAttempts { get; set; }
        public int ErrorCount { get; set; }
        public double ConnectionLatency { get; set; }
        public double CurrentBufferUtilization { get; set; }
        public double PeakBufferUtilization { get; set; }
        public int BufferOverflowCount { get; set; }
        public List<double> MessageLatencies { get; } = [];
    }
}
