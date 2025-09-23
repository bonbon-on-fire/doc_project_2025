using System.Collections.Concurrent;
using AIChat.Orleans.Metrics;
using Prometheus;

namespace AIChat.Server.Services.Metrics;

public class PrometheusMetricsExporter : IPrometheusMetricsExporter
{
    private readonly IOrleansMetricsCollector _orleansMetricsCollector;
    private readonly ILogger<PrometheusMetricsExporter> _logger;

    private readonly Counter _grainActivations = Prometheus.Metrics.CreateCounter(
        "orleans_grain_activations_total",
        "Total number of grain activations",
        new CounterConfiguration { LabelNames = new[] { "grain_type" } });

    private readonly Counter _grainDeactivations = Prometheus.Metrics.CreateCounter(
        "orleans_grain_deactivations_total",
        "Total number of grain deactivations",
        new CounterConfiguration { LabelNames = new[] { "grain_type" } });

    private readonly Histogram _grainOperationDuration = Prometheus.Metrics.CreateHistogram(
        "orleans_grain_operation_duration_seconds",
        "Duration of grain operations in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "grain_type", "operation", "status" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 15)
        });

    private readonly Gauge _grainStateSize = Prometheus.Metrics.CreateGauge(
        "orleans_grain_state_size_bytes",
        "Size of grain state in bytes",
        new GaugeConfiguration { LabelNames = new[] { "grain_type", "grain_category" } });

    private readonly Gauge _grainActiveInstances = Prometheus.Metrics.CreateGauge(
        "orleans_grain_active_instances",
        "Number of active grain instances",
        new GaugeConfiguration { LabelNames = new[] { "grain_type" } });

    private readonly Gauge _grainConnectionCount = Prometheus.Metrics.CreateGauge(
        "orleans_grain_connection_count",
        "Number of active connections to grains",
        new GaugeConfiguration { LabelNames = new[] { "grain_type" } });

    private readonly ConcurrentDictionary<string, HashSet<string>> _trackedGrainIds = new();
    private const int MaxTrackedGrainsPerType = 100;
    private DateTime _lastHealthCheck = DateTime.UtcNow;
    private long _totalMetricsExported = 0;

    public PrometheusMetricsExporter(
        IOrleansMetricsCollector orleansMetricsCollector,
        ILogger<PrometheusMetricsExporter> logger)
    {
        _orleansMetricsCollector = orleansMetricsCollector;
        _logger = logger;

        // Start background task to periodically update Prometheus metrics
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    await UpdateMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update Prometheus metrics in background task");
                }
            }
        });
    }

    public async Task UpdateMetricsAsync()
    {
        try
        {
            // Get summary metrics from Orleans collector
            var summary = await _orleansMetricsCollector.GetMetricsSummaryAsync();

            // Update Prometheus metrics based on summary
            foreach (var grainMetrics in summary.GrainTypeMetrics)
            {
                var grainType = grainMetrics.Key;
                var metrics = grainMetrics.Value;

                // Update active instances gauge
                _grainActiveInstances.WithLabels(grainType).Set(metrics.ActiveInstances);

                // Update state size with bounded cardinality
                var grainCategory = GetBoundedGrainCategory(grainType);
                _grainStateSize
                    .WithLabels(grainType, grainCategory)
                    .Set(metrics.MemoryUsageMB * 1024 * 1024); // Convert MB to bytes

                // Update operation metrics
                foreach (var operation in metrics.OperationCounts)
                {
                    var successRate = metrics.OperationSuccessRate / 100.0;
                    _grainOperationDuration
                        .WithLabels(grainType, operation.Key, "success")
                        .Observe(metrics.AverageOperationDuration / 1000.0); // Convert ms to seconds

                    if (successRate < 1.0)
                    {
                        _grainOperationDuration
                            .WithLabels(grainType, operation.Key, "failure")
                            .Observe(metrics.AverageOperationDuration / 1000.0 * 1.5); // Assume failures take longer
                    }
                }
            }

            // Update total metrics
            _grainActivations.WithLabels("all").IncTo(summary.GrainActivationsLastHour);
            _grainDeactivations.WithLabels("all").IncTo(summary.GrainDeactivationsLastHour);

            Interlocked.Increment(ref _totalMetricsExported);
            _lastHealthCheck = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Prometheus metrics");
        }
    }

    public Task ResetMetricsAsync()
    {
        _trackedGrainIds.Clear();
        Interlocked.Exchange(ref _totalMetricsExported, 0);
        _lastHealthCheck = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<bool> IsHealthyAsync()
    {
        var timeSinceLastCheck = DateTime.UtcNow - _lastHealthCheck;
        var isHealthy = timeSinceLastCheck.TotalMinutes < 5;
        return Task.FromResult(isHealthy);
    }

    public Task<MetricsHealthInfo> GetHealthInfoAsync()
    {
        var info = new MetricsHealthInfo
        {
            IsHealthy = DateTime.UtcNow - _lastHealthCheck < TimeSpan.FromMinutes(5),
            LastUpdateTime = _lastHealthCheck,
            TotalMetricsExported = _totalMetricsExported,
            TrackedGrainTypes = _trackedGrainIds.Count,
            Status = _totalMetricsExported > 0 ? "Healthy" : "Initializing"
        };

        return Task.FromResult(info);
    }

    private string GetBoundedGrainCategory(string grainType)
    {
        var grainSet = _trackedGrainIds.GetOrAdd(grainType, _ => new HashSet<string>());

        lock (grainSet)
        {
            if (grainSet.Count < MaxTrackedGrainsPerType)
            {
                grainSet.Add(grainType);
                return $"category_{grainType.GetHashCode() % 10}";
            }

            return "overflow";
        }
    }
}

public class MetricsHealthInfo
{
    public bool IsHealthy { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public long TotalMetricsExported { get; set; }
    public int TrackedGrainTypes { get; set; }
    public string Status { get; set; } = "Unknown";
}