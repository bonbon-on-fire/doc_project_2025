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
public class OrleansMetricsSummary
{
    /// <summary>
    /// Total number of active grains across all types.
    /// </summary>
    public int TotalActiveGrains { get; set; }

    /// <summary>
    /// Total grain activations in the last hour.
    /// </summary>
    public int GrainActivationsLastHour { get; set; }

    /// <summary>
    /// Total grain deactivations in the last hour.
    /// </summary>
    public int GrainDeactivationsLastHour { get; set; }

    /// <summary>
    /// Average operation duration across all grains in milliseconds.
    /// </summary>
    public double AverageOperationDuration { get; set; }

    /// <summary>
    /// Operation success rate as a percentage (0-100).
    /// </summary>
    public double OperationSuccessRate { get; set; }

    /// <summary>
    /// Total memory usage by grain states in MB.
    /// </summary>
    public double TotalMemoryUsageMB { get; set; }

    /// <summary>
    /// Metrics by grain type.
    /// </summary>
    public Dictionary<string, GrainTypeMetrics> GrainTypeMetrics { get; set; } = [];

    /// <summary>
    /// Timestamp when these metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed metrics for a specific grain type.
/// </summary>
public class GrainTypeMetrics
{
    /// <summary>
    /// Type name of the grain.
    /// </summary>
    public string GrainType { get; set; } = string.Empty;

    /// <summary>
    /// Number of active instances of this grain type.
    /// </summary>
    public int ActiveInstances { get; set; }

    /// <summary>
    /// Average activation time for this grain type in milliseconds.
    /// </summary>
    public double AverageActivationTime { get; set; }

    /// <summary>
    /// Average lifetime of deactivated grains in minutes.
    /// </summary>
    public double AverageLifetime { get; set; }

    /// <summary>
    /// Average operation duration for this grain type in milliseconds.
    /// </summary>
    public double AverageOperationDuration { get; set; }

    /// <summary>
    /// Success rate for operations on this grain type (0-100).
    /// </summary>
    public double OperationSuccessRate { get; set; }

    /// <summary>
    /// Total memory usage by this grain type in MB.
    /// </summary>
    public double MemoryUsageMB { get; set; }

    /// <summary>
    /// Operation counts by operation type.
    /// </summary>
    public Dictionary<string, long> OperationCounts { get; set; } = [];

    /// <summary>
    /// Recent performance samples for trending.
    /// </summary>
    public List<MetricSample> RecentSamples { get; set; } = [];
}

/// <summary>
/// Individual metric sample for time series data.
/// </summary>
public class MetricSample
{
    /// <summary>
    /// Timestamp when this sample was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Value of the metric at this timestamp.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Type of metric (e.g., "OperationDuration", "MemoryUsage").
    /// </summary>
    public string MetricType { get; set; } = string.Empty;
}
