using AIChat.Orleans.Placement;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Stateless worker grain for accessing Orleans placement metrics.
/// Provides a facade over IPlacementMetricsCollector for WebHost monitoring endpoints.
/// This enables architectural separation between WebHost and Silo internals.
/// </summary>
/// <remarks>
/// This grain acts as a monitoring facade, allowing WebHost controllers to query
/// grain placement effectiveness metrics through grain interfaces rather than
/// directly injecting internal services. Tracks placement strategies, affinity success,
/// and silo load distribution. As a stateless worker, it's lightweight and can be
/// called from any silo in the cluster.
/// </remarks>
[Alias("AIChat.Orleans.Contracts.IPlacementMetricsGrain")]
public interface IPlacementMetricsGrain : IGrainWithIntegerKey
{
    /// <summary>
    /// Gets comprehensive placement metrics including strategy effectiveness and load distribution.
    /// </summary>
    /// <returns>
    /// Placement metrics summary with placement strategy counts, cross-silo communication patterns,
    /// affinity success rates, and silo load distribution
    /// </returns>
    [Alias("GetMetricsSummaryAsync")]
    Task<PlacementMetricsSummary> GetMetricsSummaryAsync();

    /// <summary>
    /// Gets placement strategy information for all active grain types.
    /// </summary>
    /// <returns>Dictionary mapping grain types to their configured placement strategies</returns>
    [Alias("GetPlacementStrategiesAsync")]
    Task<Dictionary<string, string>> GetPlacementStrategiesAsync();

    /// <summary>
    /// Gets current silo load distribution showing grain placement across silos.
    /// </summary>
    /// <returns>Dictionary of silo addresses and their grain counts</returns>
    [Alias("GetSiloLoadDistributionAsync")]
    Task<Dictionary<string, long>> GetSiloLoadDistributionAsync();

    /// <summary>
    /// Resets all collected placement metrics. Use carefully in production environments.
    /// </summary>
    /// <remarks>
    /// This operation clears all placement metric counters and restarts collection.
    /// Should be used primarily for testing, benchmarking, or after configuration changes.
    /// </remarks>
    [Alias("ResetMetricsAsync")]
    Task ResetMetricsAsync();
}
