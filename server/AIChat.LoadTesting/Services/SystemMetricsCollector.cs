using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Collects system metrics during load testing
/// </summary>
public class SystemMetricsCollector : IDisposable
{
    private readonly MonitoringConfiguration _config;
    private readonly LoadTestingConfiguration _loadTestConfig;
    private readonly ILogger<SystemMetricsCollector> _logger;
    private readonly HttpClient _httpClient;
    private readonly Timer _metricsTimer;
    private readonly List<SystemMetrics> _metricsHistory = [];
    private readonly object _metricsLock = new();

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryCounter;
    private Process _currentProcess;
    private bool _disposed;

    public SystemMetricsCollector(
        IOptions<MonitoringConfiguration> config,
        IOptions<LoadTestingConfiguration> loadTestConfig,
        ILogger<SystemMetricsCollector> logger,
        HttpClient httpClient
    )
    {
        _config = config.Value;
        _loadTestConfig = loadTestConfig.Value;
        _logger = logger;
        _httpClient = httpClient;
        _currentProcess = Process.GetCurrentProcess();

        InitializePerformanceCounters();

        // Start metrics collection timer
        _metricsTimer = new Timer(
            CollectMetrics,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_config.MetricsIntervalSeconds)
        );
    }

    /// <summary>
    /// Gets the latest collected metrics
    /// </summary>
    public SystemMetrics? GetLatestMetrics()
    {
        lock (_metricsLock)
        {
            return _metricsHistory.LastOrDefault();
        }
    }

    /// <summary>
    /// Gets metrics history
    /// </summary>
    public List<SystemMetrics> GetMetricsHistory()
    {
        lock (_metricsLock)
        {
            return [.. _metricsHistory];
        }
    }

    /// <summary>
    /// Gets resource usage statistics
    /// </summary>
    public ResourceUsageStatistics GetResourceUsageStatistics()
    {
        lock (_metricsLock)
        {
            if (_metricsHistory.Count == 0)
            {
                return new ResourceUsageStatistics();
            }

            var stats = new ResourceUsageStatistics
            {
                AverageCpuPercent = _metricsHistory.Average(m => m.CpuUsagePercent),
                MaxCpuPercent = _metricsHistory.Max(m => m.CpuUsagePercent),
                AverageMemoryMB = (long)_metricsHistory.Average(m => m.MemoryUsageMB),
                MaxMemoryMB = _metricsHistory.Max(m => m.MemoryUsageMB),
                AverageNetworkMbps = _metricsHistory.Average(m => m.NetworkBandwidthMbps),
                MaxNetworkMbps = _metricsHistory.Max(m => m.NetworkBandwidthMbps),
            };

            // Check if resource limits were exceeded
            stats.ResourceLimitsExceeded = stats.MaxCpuPercent > 80 || stats.MaxMemoryMB > 8192;

            if (stats.MaxCpuPercent > 80)
            {
                stats.ResourceWarnings.Add(
                    $"CPU usage exceeded 80% (peak: {stats.MaxCpuPercent:F1}%)"
                );
            }

            if (stats.MaxMemoryMB > 8192)
            {
                stats.ResourceWarnings.Add(
                    $"Memory usage exceeded 8GB (peak: {stats.MaxMemoryMB}MB)"
                );
            }

            return stats;
        }
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");

                // Prime the CPU counter (first call always returns 0)
                _ = _cpuCounter.NextValue();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to initialize performance counters. System metrics may be limited."
            );
        }
    }

    private async void CollectMetrics(object? state)
    {
        try
        {
            var metrics = await CollectCurrentMetricsAsync();

            lock (_metricsLock)
            {
                _metricsHistory.Add(metrics);

                // Keep only last 1000 metrics (about 2.7 hours at 10-second intervals)
                if (_metricsHistory.Count > 1000)
                {
                    _metricsHistory.RemoveAt(0);
                }
            }

            _logger.LogTrace(
                "Collected system metrics: CPU={CpuPercent:F1}%, Memory={MemoryMB}MB",
                metrics.CpuUsagePercent,
                metrics.MemoryUsageMB
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect system metrics");
        }
    }

    private async Task<SystemMetrics> CollectCurrentMetricsAsync()
    {
        var metrics = new SystemMetrics
        {
            Timestamp = DateTime.UtcNow,
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsageMB = GetMemoryUsage(),
            MemoryAvailableMB = GetAvailableMemory(),
            NetworkBandwidthMbps = 0, // TODO: Implement network monitoring
        };

        // Collect Orleans metrics if enabled
        if (_config.MonitorOrleansGrains)
        {
            metrics.Orleans = await CollectOrleansMetricsAsync();
        }

        return metrics;
    }

    private double GetCpuUsage()
    {
        try
        {
            if (_cpuCounter != null && OperatingSystem.IsWindows())
            {
                return _cpuCounter.NextValue();
            }

            // Fallback for non-Windows systems or when performance counters fail
            return _currentProcess.TotalProcessorTime.TotalMilliseconds
                / Environment.ProcessorCount
                / Environment.TickCount
                * 100.0;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get CPU usage");
            return 0;
        }
    }

    private long GetMemoryUsage()
    {
        try
        {
            return _currentProcess.WorkingSet64 / 1024 / 1024; // Convert to MB
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get memory usage");
            return 0;
        }
    }

    private long GetAvailableMemory()
    {
        try
        {
            if (_memoryCounter != null && OperatingSystem.IsWindows())
            {
                return (long)_memoryCounter.NextValue();
            }

            // Fallback - get total system memory (rough estimate)
            var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            return Math.Max(0, 16384 - totalMemory); // Assume 16GB system
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get available memory");
            return 0;
        }
    }

    private async Task<OrleansMetrics> CollectOrleansMetricsAsync()
    {
        var orleansMetrics = new OrleansMetrics();

        try
        {
            var monitoringUrl =
                $"{_loadTestConfig.ServerBaseUrl.TrimEnd('/')}{_config.MonitoringApiUrl}/metrics";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync(monitoringUrl, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var metricsData = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    jsonContent
                );

                if (metricsData != null)
                {
                    orleansMetrics.SiloHealthy = GetMetricValue(
                        metricsData,
                        "orleans.silo.healthy",
                        false
                    );
                    orleansMetrics.ActiveGrainCount = GetMetricValue(
                        metricsData,
                        "orleans.grains.active",
                        0
                    );
                    orleansMetrics.BackgroundQueueDepth = GetMetricValue(
                        metricsData,
                        "background.queue.depth",
                        0
                    );
                    orleansMetrics.AverageMessageLatencyMs = GetMetricValue<double>(
                        metricsData,
                        "orleans.message.relay.latency_ms",
                        0
                    );
                    orleansMetrics.ErrorCount = GetMetricValue(metricsData, "errors.total", 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to collect Orleans metrics from monitoring API");
        }

        return orleansMetrics;
    }

    private T GetMetricValue<T>(Dictionary<string, object> metricsData, string key, T defaultValue)
    {
        try
        {
            if (metricsData.TryGetValue(key, out var value))
            {
                if (value is JsonElement jsonElement)
                {
                    return jsonElement.ValueKind switch
                    {
                        JsonValueKind.Number when typeof(T) == typeof(int) => (T)
                            (object)jsonElement.GetInt32(),
                        JsonValueKind.Number when typeof(T) == typeof(double) => (T)
                            (object)jsonElement.GetDouble(),
                        JsonValueKind.True or JsonValueKind.False when typeof(T) == typeof(bool) =>
                            (T)(object)jsonElement.GetBoolean(),
                        JsonValueKind.String => defaultValue,
                        JsonValueKind.Array => defaultValue,
                        JsonValueKind.Object => defaultValue,
                        JsonValueKind.Null => defaultValue,
                        JsonValueKind.Undefined => defaultValue,
                        _ => defaultValue,
                    };
                }

                if (value is T directValue)
                {
                    return directValue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to parse metric value for key {Key}", key);
        }

        return defaultValue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _metricsTimer?.Dispose();
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        _currentProcess?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
