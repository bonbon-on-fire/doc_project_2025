using AIChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for production monitoring dashboard and API endpoints.
/// Provides comprehensive metrics, alerts, and capacity planning data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MonitoringController : ControllerBase
{
    private readonly ILogger<MonitoringController> _logger;
    private readonly ProductionMonitoringService _monitoringService;

    public MonitoringController(
        ILogger<MonitoringController> logger,
        ProductionMonitoringService monitoringService
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _monitoringService =
            monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
    }

    /// <summary>
    /// Get current system metrics snapshot
    /// </summary>
    [HttpGet("metrics")]
    public ActionResult<Dictionary<string, object>> GetMetrics()
    {
        try
        {
            var metrics = _monitoringService.GetCurrentMetrics();
            var alertStates = _monitoringService.GetAlertStates();

            var response = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow,
                ["metrics"] = metrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        value = kvp.Value.Value,
                        timestamp = kvp.Value.Timestamp,
                        tags = kvp.Value.Tags,
                    }
                ),
                ["alerts"] = alertStates.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        isActive = kvp.Value.IsActive,
                        lastTriggered = kvp.Value.LastTriggered,
                        triggeredCount = kvp.Value.TriggeredCount,
                    }
                ),
                ["summary"] = new
                {
                    totalMetrics = metrics.Count,
                    activeAlerts = alertStates.Count(a => a.Value.IsActive),
                    systemHealthy = !alertStates.Any(a =>
                        a.Value.IsActive && a.Key.Contains("critical")
                    ),
                },
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve current metrics");
            return StatusCode(
                500,
                new { error = "Failed to retrieve metrics", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get historical data for a specific metric
    /// </summary>
    [HttpGet("metrics/{metricName}/history")]
    public ActionResult<object> GetMetricHistory(string metricName, [FromQuery] int hours = 1)
    {
        try
        {
            var timeRange = TimeSpan.FromHours(Math.Min(hours, 24)); // Limit to 24 hours
            var historicalData = _monitoringService.GetHistoricalMetrics(metricName, timeRange);

            var response = new
            {
                metricName,
                timeRange = timeRange.TotalHours,
                dataPoints = historicalData
                    .Select(h => new { value = h.Value, timestamp = h.Timestamp })
                    .OrderBy(d => d.timestamp),
                summary = new
                {
                    count = historicalData.Count,
                    average = historicalData.Count != 0 ? historicalData.Average(h => h.Value) : 0,
                    min = historicalData.Count != 0 ? historicalData.Min(h => h.Value) : 0,
                    max = historicalData.Count != 0 ? historicalData.Max(h => h.Value) : 0,
                    latest = historicalData.LastOrDefault()?.Value ?? 0,
                },
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metric history for {MetricName}", metricName);
            return StatusCode(
                500,
                new { error = "Failed to retrieve metric history", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get system health status
    /// </summary>
    [HttpGet("health")]
    public ActionResult<object> GetSystemHealth()
    {
        try
        {
            var metrics = _monitoringService.GetCurrentMetrics();
            var alertStates = _monitoringService.GetAlertStates();

            // Determine overall system health
            var criticalAlerts = alertStates
                .Where(a =>
                    a.Value.IsActive
                    && (
                        a.Key.Contains("critical", StringComparison.CurrentCultureIgnoreCase)
                        || a.Key.Contains("silo down", StringComparison.CurrentCultureIgnoreCase)
                    )
                )
                .ToList();

            var warningAlerts = alertStates
                .Where(a => a.Value.IsActive && !criticalAlerts.Any(c => c.Key == a.Key))
                .ToList();

            var healthStatus =
                criticalAlerts.Count != 0 ? "Critical"
                : warningAlerts.Count != 0 ? "Warning"
                : "Healthy";

            var response = new
            {
                status = healthStatus,
                timestamp = DateTime.UtcNow,
                criticalAlerts = criticalAlerts.Count,
                warningAlerts = warningAlerts.Count,
                components = new
                {
                    orleans = GetComponentHealth(metrics, "orleans"),
                    backgroundProcessing = GetComponentHealth(metrics, "background"),
                    signalr = GetComponentHealth(metrics, "signalr"),
                    system = GetComponentHealth(metrics, "system"),
                },
                activeAlerts = alertStates
                    .Where(a => a.Value.IsActive)
                    .Select(a => new
                    {
                        name = a.Key,
                        lastTriggered = a.Value.LastTriggered,
                        count = a.Value.TriggeredCount,
                    }),
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve system health");
            return StatusCode(
                500,
                new { error = "Failed to retrieve system health", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get capacity planning metrics and recommendations
    /// </summary>
    [HttpGet("capacity")]
    public ActionResult<object> GetCapacityMetrics()
    {
        try
        {
            var metrics = _monitoringService.GetCurrentMetrics();

            // Get key capacity-related metrics
            var memoryUsage = GetMetricValue(metrics, "system.memory.working_set_mb", 0);
            var cpuUsage = GetMetricValue(metrics, "system.cpu.usage_percent", 0);
            var activeGrains = GetMetricValue(metrics, "orleans.grains.active", 0);
            var queueDepth = GetMetricValue(metrics, "background.queue.depth", 0);

            // Historical trends (last 24 hours)
            var memoryTrend = _monitoringService.GetHistoricalMetrics(
                "system.memory.working_set_mb",
                TimeSpan.FromHours(24)
            );
            var cpuTrend = _monitoringService.GetHistoricalMetrics(
                "system.cpu.usage_percent",
                TimeSpan.FromHours(24)
            );
            var grainsTrend = _monitoringService.GetHistoricalMetrics(
                "orleans.grains.active",
                TimeSpan.FromHours(24)
            );

            // Calculate trends and recommendations
            var recommendations = GenerateCapacityRecommendations(
                memoryUsage,
                cpuUsage,
                activeGrains,
                queueDepth
            );

            var response = new
            {
                timestamp = DateTime.UtcNow,
                current = new
                {
                    memoryUsageMB = memoryUsage,
                    cpuUsagePercent = cpuUsage,
                    activeGrains,
                    backgroundQueueDepth = queueDepth,
                },
                trends = new
                {
                    memory = CalculateTrend(memoryTrend),
                    cpu = CalculateTrend(cpuTrend),
                    grains = CalculateTrend(grainsTrend),
                },
                thresholds = new
                {
                    memoryWarning = 1024, // 1GB
                    memoryCritical = 2048, // 2GB
                    cpuWarning = 80,
                    cpuCritical = 95,
                    queueWarning = 100,
                    queueCritical = 500,
                },
                recommendations,
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve capacity metrics");
            return StatusCode(
                500,
                new { error = "Failed to retrieve capacity metrics", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Get dashboard configuration and metadata
    /// </summary>
    [HttpGet("dashboard/config")]
    public ActionResult<object> GetDashboardConfig()
    {
        try
        {
            var response = new
            {
                title = "Orleans Production Monitoring Dashboard",
                version = "1.0.0",
                refreshInterval = 30, // seconds
                panels = new object[]
                {
                    new
                    {
                        id = "system-health",
                        title = "System Health",
                        type = "status",
                        order = 1,
                    },
                    new
                    {
                        id = "active-grains",
                        title = "Active Grains",
                        type = "gauge",
                        metric = "orleans.grains.active",
                        order = 2,
                    },
                    new
                    {
                        id = "message-latency",
                        title = "Message Latency",
                        type = "histogram",
                        metric = "orleans.message.relay.latency_ms",
                        order = 3,
                    },
                    new
                    {
                        id = "queue-depth",
                        title = "Background Queue",
                        type = "gauge",
                        metric = "background.queue.depth",
                        order = 4,
                    },
                    new
                    {
                        id = "error-rate",
                        title = "Error Rate",
                        type = "counter",
                        metric = "errors.total",
                        order = 5,
                    },
                    new
                    {
                        id = "memory-usage",
                        title = "Memory Usage",
                        type = "gauge",
                        metric = "system.memory.working_set_mb",
                        order = 6,
                    },
                },
                alertRules = new object[]
                {
                    new
                    {
                        name = "High Memory Usage",
                        threshold = "1024MB",
                        severity = "warning",
                    },
                    new
                    {
                        name = "Orleans Silo Down",
                        threshold = "0",
                        severity = "critical",
                    },
                    new
                    {
                        name = "High Error Rate",
                        threshold = "5%",
                        severity = "warning",
                    },
                    new
                    {
                        name = "Queue Overload",
                        threshold = "100",
                        severity = "warning",
                    },
                },
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve dashboard configuration");
            return StatusCode(
                500,
                new { error = "Failed to retrieve dashboard configuration", details = ex.Message }
            );
        }
    }

    /// <summary>
    /// Export metrics in Prometheus format
    /// </summary>
    [HttpGet("export/prometheus")]
    public ActionResult<string> ExportPrometheus()
    {
        try
        {
            var metrics = _monitoringService.GetCurrentMetrics();
            var prometheusFormat = ConvertToPrometheusFormat(metrics);

            return Content(prometheusFormat, "text/plain; version=0.0.4; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export Prometheus metrics");
            return StatusCode(500, "Failed to export metrics");
        }
    }

    #region Private Helper Methods

    private static object GetComponentHealth(
        Dictionary<string, MetricValue> metrics,
        string component
    )
    {
        var componentMetrics = metrics
            .Where(m => m.Key.StartsWith(component, StringComparison.Ordinal))
            .ToList();

        if (componentMetrics.Count == 0)
        {
            return new { status = "Unknown", lastUpdate = (DateTime?)null };
        }

        var latestUpdate = componentMetrics.Max(m => m.Value.Timestamp);
        var isHealthy = component switch
        {
            "orleans" => GetMetricValue(metrics, "orleans.silo.healthy", 0) > 0,
            "system" => GetMetricValue(metrics, "system.cpu.usage_percent", 0) < 90,
            _ => true,
        };

        return new
        {
            status = isHealthy ? "Healthy" : "Unhealthy",
            lastUpdate = latestUpdate,
            metricsCount = componentMetrics.Count,
        };
    }

    private static double GetMetricValue(
        Dictionary<string, MetricValue> metrics,
        string metricName,
        double defaultValue
    )
    {
        return metrics.TryGetValue(metricName, out var metric) ? metric.Value : defaultValue;
    }

    private static object CalculateTrend(List<HistoricalMetric> historicalData)
    {
        if (historicalData.Count < 2)
        {
            return new
            {
                direction = "stable",
                change = 0.0,
                confidence = "low",
            };
        }

        var values = historicalData.Select(h => h.Value).ToList();
        var recent = values.TakeLast(values.Count / 3).Average();
        var older = values.Take(values.Count / 3).Average();

        var change = recent - older;
        var direction =
            Math.Abs(change) < 0.1 ? "stable"
            : change > 0 ? "increasing"
            : "decreasing";

        return new
        {
            direction,
            change = Math.Round(change, 2),
            confidence = values.Count > 10 ? "high" : "medium",
        };
    }

    private static List<string> GenerateCapacityRecommendations(
        double memory,
        double cpu,
        double grains,
        double queue
    )
    {
        var recommendations = new List<string>();

        if (memory > 1536) // 1.5GB
        {
            recommendations.Add(
                "Consider increasing memory allocation or optimizing grain state storage"
            );
        }

        if (cpu > 75)
        {
            recommendations.Add(
                "CPU usage is high - consider horizontal scaling or optimizing processing logic"
            );
        }

        if (queue > 50)
        {
            recommendations.Add(
                "Background queue is building up - consider increasing worker pool size"
            );
        }

        if (grains > 1000)
        {
            recommendations.Add(
                "High grain count detected - monitor for potential memory pressure"
            );
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("System is operating within normal parameters");
        }

        return recommendations;
    }

    private static string ConvertToPrometheusFormat(Dictionary<string, MetricValue> metrics)
    {
        var prometheusMetrics = new List<string>();

        foreach (var metric in metrics)
        {
            var metricName = metric.Key.Replace('.', '_').Replace('-', '_');
            var value = metric.Value.Value;
            var timestamp = ((DateTimeOffset)metric.Value.Timestamp).ToUnixTimeMilliseconds();

            var tags = string.Join(",", metric.Value.Tags.Select(t => $"{t.Key}=\"{t.Value}\""));
            var tagsSection = !string.IsNullOrEmpty(tags) ? $"{{{tags}}}" : "";

            prometheusMetrics.Add($"{metricName}{tagsSection} {value} {timestamp}");
        }

        return string.Join("\n", prometheusMetrics);
    }

    #endregion
}
