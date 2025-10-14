using AIChat.Orleans.Metrics;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Stateless worker grain for accessing Orleans cluster metrics.
/// Provides a facade over IOrleansMetricsCollector for WebHost monitoring endpoints.
/// This enables architectural separation between WebHost and Silo internals.
/// </summary>
/// <remarks>
/// This grain acts as a monitoring facade, allowing WebHost controllers to query
/// Orleans metrics through grain interfaces rather than directly injecting internal services.
/// As a stateless worker, it's lightweight and can be called from any silo in the cluster.
/// </remarks>
[Alias("AIChat.Orleans.Contracts.IClusterMetricsGrain")]
public interface IClusterMetricsGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Gets comprehensive Orleans metrics summary including grain lifecycle and performance data.
    /// </summary>
    /// <returns>Aggregated metrics data for all grains in the cluster</returns>
    [Alias("GetMetricsSummaryAsync")]
    Task<OrleansMetricsSummary> GetMetricsSummaryAsync();

    /// <summary>
    /// Gets detailed metrics for a specific grain type.
    /// </summary>
    /// <param name="grainType">Type of grain to get metrics for (e.g., "UserGrain", "ChatGrain")</param>
    /// <returns>Detailed metrics for the specified grain type</returns>
    [Alias("GetGrainTypeMetricsAsync")]
    Task<GrainTypeMetrics> GetGrainTypeMetricsAsync(string grainType);

    /// <summary>
    /// Resets all collected metrics. Use carefully in production environments.
    /// </summary>
    /// <remarks>
    /// This operation clears all metric counters and restarts collection.
    /// Should be used primarily for testing or after maintenance windows.
    /// </remarks>
    [Alias("ResetMetricsAsync")]
    Task ResetMetricsAsync();
}
