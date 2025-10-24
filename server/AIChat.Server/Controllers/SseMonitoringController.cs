using AIChat.Orleans.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for SSE (Server-Sent Events) specific monitoring and dashboard endpoints.
/// Provides comprehensive metrics, health indicators, and alerts for Orleans SSE streaming.
/// </summary>
[ApiController]
[Route("api/monitoring/sse")]
public class SseMonitoringController : ControllerBase
{
    private readonly ILogger<SseMonitoringController> _logger;
    private readonly ISseMetricsCollector _sseMetricsCollector;

    public SseMonitoringController(
        ILogger<SseMonitoringController> logger,
        ISseMetricsCollector sseMetricsCollector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sseMetricsCollector = sseMetricsCollector ?? throw new ArgumentNullException(nameof(sseMetricsCollector));
    }

    /// <summary>
    /// Get SSE-specific metrics summary for Orleans dashboard.
    /// </summary>
    /// <returns>Current SSE streaming metrics</returns>
    [HttpGet("metrics")]
    public async Task<ActionResult<object>> GetSseMetrics()
    {
        try
        {
            var metrics = await _sseMetricsCollector.GetSseMetricsSummaryAsync();

            var response = new
            {
                timestamp = DateTime.UtcNow,
                connections = new
                {
                    active = metrics.ActiveConnections,
                    establishedLastHour = metrics.ConnectionsEstablishedLastHour,
                    closedLastHour = metrics.ConnectionsClosedLastHour
                },
                streaming = new
                {
                    averageDuration = metrics.AverageStreamDuration,
                    chunksProcessedLastHour = metrics.ChunksProcessedLastHour,
                    bytesTransmittedLastHour = metrics.BytesTransmittedLastHour,
                    averageChunkProcessingTime = metrics.AverageChunkProcessingTime,
                    successRate = metrics.StreamSuccessRate
                },
                buffers = new
                {
                    averageUtilization = metrics.AverageBufferUtilization,
                    overflowsLastHour = metrics.BufferOverflowsLastHour
                },
                failures = new
                {
                    streamsFailedLastHour = metrics.StreamFailuresLastHour,
                    failureRate = 100.0 - metrics.StreamSuccessRate
                },
                chunkTypes = metrics.ChunkTypeBreakdown.Select(ct => new
                {
                    type = ct.Key,
                    metrics = new
                    {
                        count = ct.Value.Count,
                        averageSize = ct.Value.AverageSize,
                        averageProcessingTime = ct.Value.AverageProcessingTime,
                        successRate = ct.Value.SuccessRate,
                        totalBytes = ct.Value.TotalBytes
                    }
                })
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SSE metrics");
            return StatusCode(
                500,
                new { error = "Failed to retrieve SSE metrics", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get SSE stream health indicators.
    /// </summary>
    /// <returns>Health status and indicators for SSE streaming</returns>
    [HttpGet("health")]
    public async Task<ActionResult<object>> GetSseHealth()
    {
        try
        {
            var health = await _sseMetricsCollector.GetSseHealthIndicatorsAsync();

            var response = new
            {
                timestamp = DateTime.UtcNow,
                overallHealth = health.OverallHealth.ToString(),
                indicators = new
                {
                    connectionStability = FormatHealthIndicator(health.ConnectionStability),
                    streamThroughput = FormatHealthIndicator(health.StreamThroughput),
                    bufferHealth = FormatHealthIndicator(health.BufferHealth),
                    errorRate = FormatHealthIndicator(health.ErrorRate),
                    latency = FormatHealthIndicator(health.Latency),
                    recoveryPerformance = FormatHealthIndicator(health.RecoveryPerformance)
                },
                activeAlerts = health.ActiveAlerts.Select(a => new
                {
                    id = a.AlertId,
                    severity = a.Severity.ToString().ToLowerInvariant(),
                    message = a.Message,
                    triggeredAt = a.TriggeredAt,
                    metric = a.TriggeringMetric,
                    currentValue = a.CurrentValue,
                    threshold = a.ThresholdValue,
                    action = a.RecommendedAction
                }),
                checkedAt = health.CheckedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SSE health indicators");
            return StatusCode(
                500,
                new { error = "Failed to retrieve SSE health indicators", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get historical SSE metrics for trend analysis.
    /// </summary>
    /// <param name="metricType">Type of metric to retrieve</param>
    /// <param name="hours">Number of hours of history (max 24)</param>
    /// <returns>Historical metric data points</returns>
    [HttpGet("metrics/history")]
    public async Task<ActionResult<object>> GetSseMetricsHistory(
        [FromQuery] SseMetricType metricType = SseMetricType.ActiveConnections,
        [FromQuery] int hours = 1)
    {
        try
        {
            var timeRange = TimeSpan.FromHours(Math.Min(hours, 24)); // Limit to 24 hours
            var historicalData = await _sseMetricsCollector.GetSseHistoricalMetricsAsync(metricType, timeRange);

            var response = new
            {
                metricType = metricType.ToString(),
                timeRange = new
                {
                    hours = timeRange.TotalHours,
                    start = DateTime.UtcNow.Subtract(timeRange),
                    end = DateTime.UtcNow
                },
                dataPoints = historicalData.Select(d => new
                {
                    timestamp = d.Timestamp,
                    value = d.Value,
                    tags = d.Tags
                }).OrderBy(d => d.timestamp),
                summary = historicalData.Count != 0 ? new
                {
                    count = historicalData.Count,
                    average = Math.Round(historicalData.Average(h => h.Value), 2),
                    min = historicalData.Min(h => h.Value),
                    max = historicalData.Max(h => h.Value),
                    latest = historicalData.LastOrDefault()?.Value ?? 0,
                    trend = CalculateTrend(historicalData)
                } : null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SSE metrics history for {MetricType}", metricType);
            return StatusCode(
                500,
                new { error = "Failed to retrieve SSE metrics history", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get SSE-specific alerting rules configuration.
    /// </summary>
    /// <returns>Configured alert rules for SSE monitoring</returns>
    [HttpGet("alerts/rules")]
    public ActionResult<object> GetSseAlertRules()
    {
        try
        {
            var rules = new
            {
                timestamp = DateTime.UtcNow,
                rules = new object[]
                {
                    new
                    {
                        id = "sse-connection-drops",
                        name = "SSE Connection Drops",
                        metric = "ConnectionDrops",
                        condition = "greater_than",
                        warningThreshold = 10,
                        criticalThreshold = 50,
                        evaluationWindow = "1 hour",
                        enabled = true,
                        description = "Monitors unexpected SSE connection terminations"
                    },
                    new
                    {
                        id = "sse-stream-failure-rate",
                        name = "SSE Stream Failure Rate",
                        metric = "FailureRate",
                        condition = "greater_than",
                        warningThreshold = 5.0,
                        criticalThreshold = 10.0,
                        evaluationWindow = "1 hour",
                        enabled = true,
                        description = "Tracks percentage of failed SSE streams"
                    },
                    new
                    {
                        id = "sse-buffer-utilization",
                        name = "SSE Buffer Utilization",
                        metric = "BufferUtilization",
                        condition = "greater_than",
                        warningThreshold = 75.0,
                        criticalThreshold = 90.0,
                        evaluationWindow = "5 minutes",
                        enabled = true,
                        description = "Monitors SSE buffer capacity usage"
                    },
                    new
                    {
                        id = "sse-chunk-latency",
                        name = "SSE Chunk Processing Latency",
                        metric = "ChunkLatency",
                        condition = "greater_than",
                        warningThreshold = 500.0,
                        criticalThreshold = 1000.0,
                        evaluationWindow = "5 minutes",
                        enabled = true,
                        description = "Tracks SSE chunk processing time in milliseconds"
                    },
                    new
                    {
                        id = "sse-buffer-overflows",
                        name = "SSE Buffer Overflows",
                        metric = "BufferOverflows",
                        condition = "greater_than",
                        warningThreshold = 1,
                        criticalThreshold = 5,
                        evaluationWindow = "1 hour",
                        enabled = true,
                        description = "Detects SSE buffer overflow events"
                    },
                    new
                    {
                        id = "sse-no-active-connections",
                        name = "No Active SSE Connections",
                        metric = "ActiveConnections",
                        condition = "equals",
                        warningThreshold = 0,
                        criticalThreshold = 0,
                        evaluationWindow = "10 minutes",
                        enabled = false,
                        description = "Alerts when no SSE connections are active (disabled by default)"
                    }
                }
            };

            return Ok(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SSE alert rules");
            return StatusCode(
                500,
                new { error = "Failed to retrieve SSE alert rules", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get current active SSE alerts.
    /// </summary>
    /// <returns>List of currently active SSE alerts</returns>
    [HttpGet("alerts/active")]
    public async Task<ActionResult<object>> GetActiveSseAlerts()
    {
        try
        {
            var health = await _sseMetricsCollector.GetSseHealthIndicatorsAsync();

            var response = new
            {
                timestamp = DateTime.UtcNow,
                totalActive = health.ActiveAlerts.Count,
                bySeverity = new
                {
                    critical = health.ActiveAlerts.Count(a => a.Severity == AlertSeverity.Critical),
                    warning = health.ActiveAlerts.Count(a => a.Severity == AlertSeverity.Warning),
                    info = health.ActiveAlerts.Count(a => a.Severity == AlertSeverity.Info)
                },
                alerts = health.ActiveAlerts
                    .OrderByDescending(a => a.Severity)
                    .ThenByDescending(a => a.TriggeredAt)
                    .Select(a => new
                    {
                        id = a.AlertId,
                        severity = a.Severity.ToString().ToLowerInvariant(),
                        message = a.Message,
                        triggeredAt = a.TriggeredAt,
                        duration = FormatDuration(DateTime.UtcNow - a.TriggeredAt),
                        metric = a.TriggeringMetric,
                        currentValue = a.CurrentValue,
                        threshold = a.ThresholdValue,
                        action = a.RecommendedAction
                    })
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active SSE alerts");
            return StatusCode(
                500,
                new { error = "Failed to retrieve active SSE alerts", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get SSE dashboard configuration for UI.
    /// </summary>
    /// <returns>Dashboard panels and widget configuration</returns>
    [HttpGet("dashboard/config")]
    public ActionResult<object> GetSseDashboardConfig()
    {
        try
        {
            var config = new
            {
                title = "SSE Streaming Monitor",
                refreshInterval = 30, // seconds
                version = "1.0.0",
                panels = new object[]
                {
                    new
                    {
                        id = "sse-connections",
                        title = "Active SSE Connections",
                        type = "gauge",
                        metric = "ActiveConnections",
                        order = 1,
                        size = "small",
                        thresholds = new { warning = 100, critical = 500 }
                    },
                    new
                    {
                        id = "sse-throughput",
                        title = "Stream Throughput",
                        type = "line-chart",
                        metric = "ChunkThroughput",
                        order = 2,
                        size = "medium",
                        timeWindow = "1h"
                    },
                    new
                    {
                        id = "sse-latency",
                        title = "Chunk Processing Latency",
                        type = "histogram",
                        metric = "ChunkLatency",
                        order = 3,
                        size = "medium",
                        unit = "ms"
                    },
                    new
                    {
                        id = "sse-buffer-usage",
                        title = "Buffer Utilization",
                        type = "progress",
                        metric = "BufferUtilization",
                        order = 4,
                        size = "small",
                        unit = "%"
                    },
                    new
                    {
                        id = "sse-success-rate",
                        title = "Stream Success Rate",
                        type = "percentage",
                        metric = "StreamSuccessRate",
                        order = 5,
                        size = "small",
                        unit = "%"
                    },
                    new
                    {
                        id = "sse-chunk-types",
                        title = "Chunk Type Distribution",
                        type = "pie-chart",
                        metric = "ChunkTypeDistribution",
                        order = 6,
                        size = "medium"
                    },
                    new
                    {
                        id = "sse-health-matrix",
                        title = "Health Indicators",
                        type = "health-matrix",
                        order = 7,
                        size = "large"
                    },
                    new
                    {
                        id = "sse-alerts",
                        title = "Active Alerts",
                        type = "alert-list",
                        order = 8,
                        size = "medium",
                        maxItems = 5
                    }
                },
                metrics = new object[]
                {
                    new { name = "ActiveConnections", displayName = "Active Connections", unit = "count" },
                    new { name = "ChunkThroughput", displayName = "Chunks/min", unit = "chunks/min" },
                    new { name = "ChunkLatency", displayName = "Processing Latency", unit = "ms" },
                    new { name = "BufferUtilization", displayName = "Buffer Usage", unit = "%" },
                    new { name = "FailureRate", displayName = "Failure Rate", unit = "%" },
                    new { name = "ByteThroughput", displayName = "Data Rate", unit = "KB/s" }
                }
            };

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve SSE dashboard configuration");
            return StatusCode(
                500,
                new { error = "Failed to retrieve SSE dashboard configuration", details = ex.Message }
            );
        }
    }

    #region Private Helper Methods

    private static object FormatHealthIndicator(HealthIndicator indicator)
    {
        return new
        {
            status = indicator.Status.ToString().ToLowerInvariant(),
            value = Math.Round(indicator.CurrentValue, 2),
            warningThreshold = indicator.WarningThreshold,
            criticalThreshold = indicator.CriticalThreshold,
            description = indicator.Description,
            trend = indicator.Trend
        };
    }

    private static string CalculateTrend(List<SseMetricDataPoint> dataPoints)
    {
        if (dataPoints.Count < 2)
        {
            return "stable";
        }

        var values = dataPoints.ConvertAll(d => d.Value);
        var recentAvg = values.TakeLast(values.Count / 3).Average();
        var olderAvg = values.Take(values.Count / 3).Average();

        var change = recentAvg - olderAvg;
        var percentChange = olderAvg != 0 ? Math.Abs(change / olderAvg) * 100 : 0;

        if (percentChange < 5)
        {
            return "stable";
        }

        return change > 0 ? "increasing" : "decreasing";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{(int)duration.TotalSeconds}s";
    }

    #endregion
}