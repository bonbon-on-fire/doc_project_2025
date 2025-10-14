using System.Globalization;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Placement;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Grains;

/// <summary>
/// Stateless worker grain that provides access to Orleans placement metrics.
/// Acts as a facade over IPlacementMetricsCollector, enabling WebHost controllers
/// to query placement effectiveness metrics through grain interfaces.
/// </summary>
/// <remarks>
/// This grain is marked as a stateless worker, meaning Orleans can create multiple
/// instances across silos for better scalability. It provides read-only access to
/// placement metrics data collected by the IPlacementMetricsCollector service.
///
/// Architecture Pattern: Grain Facade
/// - Wraps internal service (IPlacementMetricsCollector)
/// - Provides clean boundary for WebHost monitoring
/// - Enables independent hosting of Orleans silo and WebHost
/// </remarks>
[StatelessWorker]
[Reentrant]
public sealed class PlacementMetricsGrain : Grain, IPlacementMetricsGrain
{
    private readonly IPlacementMetricsCollector _placementMetrics;
    private readonly ILogger<PlacementMetricsGrain> _logger;

    /// <summary>
    /// Initializes a new instance of the PlacementMetricsGrain.
    /// </summary>
    /// <param name="placementMetrics">Placement metrics collector service</param>
    /// <param name="logger">Logger instance for diagnostics</param>
    public PlacementMetricsGrain(
        IPlacementMetricsCollector placementMetrics,
        ILogger<PlacementMetricsGrain> logger)
    {
        _placementMetrics = placementMetrics ?? throw new ArgumentNullException(nameof(placementMetrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PlacementMetricsSummary> GetMetricsSummaryAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "PlacementMetricsGrain",
            nameof(GetMetricsSummaryAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving placement metrics summary via PlacementMetricsGrain");

            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved placement metrics: {TotalPlacements} total placements, {Strategies} strategies",
                summary.TotalPlacements,
                summary.PlacementStrategyCounts.Count
            );

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "metrics.total_placements", summary.TotalPlacements },
                { "metrics.strategies", summary.PlacementStrategyCounts.Count }
            });

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve placement metrics summary");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> GetPlacementStrategiesAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "PlacementMetricsGrain",
            nameof(GetPlacementStrategiesAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving placement strategies via PlacementMetricsGrain");

            var summary = await _placementMetrics.GetMetricsSummaryAsync();
            var strategies = new Dictionary<string, string>();

            foreach (var kvp in summary.PlacementStrategyCounts)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2)
                {
                    strategies[parts[0]] = parts[1]; // GrainType -> PlacementStrategy
                }
            }

            _logger.LogDebug(
                "Successfully retrieved placement strategies for {Count} grain types",
                strategies.Count
            );

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "strategy_count", strategies.Count }
            });

            return strategies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve placement strategies");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, long>> GetSiloLoadDistributionAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "PlacementMetricsGrain",
            nameof(GetSiloLoadDistributionAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving silo load distribution via PlacementMetricsGrain");

            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved silo load distribution for {Count} silos",
                summary.SiloLoadDistribution.Count
            );

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "silo_count", summary.SiloLoadDistribution.Count },
                { "total_load", summary.SiloLoadDistribution.Values.Sum() }
            });

            return summary.SiloLoadDistribution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve silo load distribution");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public Task ResetMetricsAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "PlacementMetricsGrain",
            nameof(ResetMetricsAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogInformation("Resetting placement metrics via PlacementMetricsGrain");

            _placementMetrics.ResetMetrics();

            _logger.LogInformation("Placement metrics reset completed successfully");

            OrleansActivitySource.SetSuccess(activity);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset placement metrics");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }
}
