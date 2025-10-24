using Orleans;

namespace AIChat.Orleans.Metrics;

/// <summary>
/// Interface for collecting and aggregating Orleans-specific metrics.
/// Provides comprehensive monitoring capabilities for grain lifecycle, operations, and performance.
/// </summary>
public interface IOrleansMetricsCollector
{
    /// <summary>
    /// Records grain activation event with associated metadata.
    /// </summary>
    /// <param name="grainType">Type of grain being activated</param>
    /// <param name="grainId">Unique identifier of the grain</param>
    /// <param name="activationTime">Time taken for activation in milliseconds</param>
    Task RecordGrainActivationAsync(string grainType, string grainId, double activationTime);

    /// <summary>
    /// Records grain deactivation event.
    /// </summary>
    /// <param name="grainType">Type of grain being deactivated</param>
    /// <param name="grainId">Unique identifier of the grain</param>
    /// <param name="lifetimeMinutes">How long the grain was active in minutes</param>
    Task RecordGrainDeactivationAsync(string grainType, string grainId, double lifetimeMinutes);

    /// <summary>
    /// Records grain operation metrics.
    /// </summary>
    /// <param name="grainType">Type of grain performing the operation</param>
    /// <param name="operationType">Type of operation (e.g., ProcessMessage, RegisterConnection)</param>
    /// <param name="duration">Operation duration in milliseconds</param>
    /// <param name="success">Whether the operation succeeded</param>
    Task RecordGrainOperationAsync(
        string grainType,
        string operationType,
        double duration,
        bool success
    );

    /// <summary>
    /// Records grain state metrics.
    /// </summary>
    /// <param name="grainType">Type of grain</param>
    /// <param name="grainId">Unique identifier of the grain</param>
    /// <param name="stateSize">Size of grain state in bytes</param>
    /// <param name="connectionCount">Number of active connections (for UserGrain)</param>
    /// <param name="operationCount">Number of active operations</param>
    Task RecordGrainStateMetricsAsync(
        string grainType,
        string grainId,
        long stateSize,
        int connectionCount,
        int operationCount
    );

    /// <summary>
    /// Gets current metrics summary for dashboard display.
    /// </summary>
    /// <returns>Aggregated metrics data</returns>
    Task<OrleansMetricsSummary> GetMetricsSummaryAsync();

    /// <summary>
    /// Gets detailed metrics for a specific grain type.
    /// </summary>
    /// <param name="grainType">Type of grain to get metrics for</param>
    /// <returns>Detailed grain metrics</returns>
    Task<GrainTypeMetrics> GetGrainTypeMetricsAsync(string grainType);

    /// <summary>
    /// Resets all collected metrics. Use carefully in production.
    /// </summary>
    Task ResetMetricsAsync();
}

/// <summary>
/// Summary of all Orleans metrics for dashboard overview.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.OrleansMetricsSummary")]
public class OrleansMetricsSummary
{
    /// <summary>
    /// Total number of active grains across all types.
    /// </summary>
    [Id(0)]
    public int TotalActiveGrains { get; set; }

    /// <summary>
    /// Total grain activations in the last hour.
    /// </summary>
    [Id(1)]
    public int GrainActivationsLastHour { get; set; }

    /// <summary>
    /// Total grain deactivations in the last hour.
    /// </summary>
    [Id(2)]
    public int GrainDeactivationsLastHour { get; set; }

    /// <summary>
    /// Average operation duration across all grains in milliseconds.
    /// </summary>
    [Id(3)]
    public double AverageOperationDuration { get; set; }

    /// <summary>
    /// Operation success rate as a percentage (0-100).
    /// </summary>
    [Id(4)]
    public double OperationSuccessRate { get; set; }

    /// <summary>
    /// Total memory usage by grain states in MB.
    /// </summary>
    [Id(5)]
    public double TotalMemoryUsageMB { get; set; }

    /// <summary>
    /// Metrics by grain type.
    /// </summary>
    [Id(6)]
    public Dictionary<string, GrainTypeMetrics> GrainTypeMetrics { get; set; } = [];

    /// <summary>
    /// Timestamp when these metrics were collected.
    /// </summary>
    [Id(7)]
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed metrics for a specific grain type.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.GrainTypeMetrics")]
public class GrainTypeMetrics
{
    /// <summary>
    /// Type name of the grain.
    /// </summary>
    [Id(0)]
    public string GrainType { get; set; } = string.Empty;

    /// <summary>
    /// Number of active instances of this grain type.
    /// </summary>
    [Id(1)]
    public int ActiveInstances { get; set; }

    /// <summary>
    /// Average activation time for this grain type in milliseconds.
    /// </summary>
    [Id(2)]
    public double AverageActivationTime { get; set; }

    /// <summary>
    /// Average lifetime of deactivated grains in minutes.
    /// </summary>
    [Id(3)]
    public double AverageLifetime { get; set; }

    /// <summary>
    /// Average operation duration for this grain type in milliseconds.
    /// </summary>
    [Id(4)]
    public double AverageOperationDuration { get; set; }

    /// <summary>
    /// Success rate for operations on this grain type (0-100).
    /// </summary>
    [Id(5)]
    public double OperationSuccessRate { get; set; }

    /// <summary>
    /// Total memory usage by this grain type in MB.
    /// </summary>
    [Id(6)]
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// Operation counts by operation type.
    /// </summary>
    [Id(7)]
    public Dictionary<string, long> OperationCounts { get; set; } = [];

    /// <summary>
    /// Recent performance samples for trending.
    /// </summary>
    [Id(8)]
    public List<MetricSample> RecentSamples { get; set; } = [];
}

/// <summary>
/// Individual metric sample for time series data.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Metrics.MetricSample")]
public class MetricSample
{
    /// <summary>
    /// Timestamp when this sample was recorded.
    /// </summary>
    [Id(0)]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Value of the metric at this timestamp.
    /// </summary>
    [Id(1)]
    public double Value { get; set; }

    /// <summary>
    /// Type of metric (e.g., "OperationDuration", "MemoryUsage").
    /// </summary>
    [Id(2)]
    public string MetricType { get; set; } = string.Empty;
}
