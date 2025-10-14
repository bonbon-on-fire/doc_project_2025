using System.Globalization;
using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Tracing;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Grains;

/// <summary>
/// Stateless worker grain that provides access to Orleans cluster metrics.
/// Acts as a facade over IOrleansMetricsCollector, enabling WebHost controllers
/// to query metrics through grain interfaces rather than direct service injection.
/// </summary>
/// <remarks>
/// This grain is marked as a stateless worker, meaning Orleans can create multiple
/// instances across silos for better scalability. It provides read-only access to
/// metrics data collected by the IOrleansMetricsCollector service.
///
/// Architecture Pattern: Grain Facade
/// - Wraps internal service (IOrleansMetricsCollector)
/// - Provides clean boundary for WebHost monitoring
/// - Enables independent hosting of Orleans silo and WebHost
/// </remarks>
[StatelessWorker]
[Reentrant]
public sealed class ClusterMetricsGrain : Grain, IClusterMetricsGrain
{
    private readonly IOrleansMetricsCollector _metricsCollector;
    private readonly ILogger<ClusterMetricsGrain> _logger;

    /// <summary>
    /// Initializes a new instance of the ClusterMetricsGrain.
    /// </summary>
    /// <param name="metricsCollector">Orleans metrics collector service</param>
    /// <param name="logger">Logger instance for diagnostics</param>
    public ClusterMetricsGrain(
        IOrleansMetricsCollector metricsCollector,
        ILogger<ClusterMetricsGrain> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrleansMetricsSummary> GetMetricsSummaryAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "ClusterMetricsGrain",
            nameof(GetMetricsSummaryAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogDebug("Retrieving Orleans metrics summary via ClusterMetricsGrain");

            var summary = await _metricsCollector.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved metrics: {TotalGrains} active grains, {ActivationsLastHour} activations in last hour",
                summary.TotalActiveGrains,
                summary.GrainActivationsLastHour
            );

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "metrics.total_grains", summary.TotalActiveGrains },
                { "metrics.grain_types", summary.GrainTypeMetrics.Count }
            });

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Orleans metrics summary");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GrainTypeMetrics> GetGrainTypeMetricsAsync(string grainType)
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "ClusterMetricsGrain",
            nameof(GetGrainTypeMetricsAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            if (string.IsNullOrWhiteSpace(grainType))
            {
                throw new ArgumentException("Grain type cannot be null or empty", nameof(grainType));
            }

            _logger.LogDebug("Retrieving metrics for grain type: {GrainType}", grainType);

            var metrics = await _metricsCollector.GetGrainTypeMetricsAsync(grainType);

            _logger.LogDebug(
                "Successfully retrieved metrics for {GrainType}: {ActiveInstances} active instances",
                grainType,
                metrics.ActiveInstances
            );

            OrleansActivitySource.SetSuccess(activity, new Dictionary<string, object>
            {
                { "grain_type", grainType },
                { "active_instances", metrics.ActiveInstances }
            });

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metrics for grain type: {GrainType}", grainType);
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ResetMetricsAsync()
    {
        using var activity = OrleansActivitySource.StartGrainActivity(
            "ClusterMetricsGrain",
            nameof(ResetMetricsAsync),
            this.GetPrimaryKeyLong().ToString(CultureInfo.InvariantCulture)
        );

        try
        {
            _logger.LogInformation("Resetting Orleans metrics via ClusterMetricsGrain");

            await _metricsCollector.ResetMetricsAsync();

            _logger.LogInformation("Orleans metrics reset completed successfully");

            OrleansActivitySource.SetSuccess(activity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset Orleans metrics");
            OrleansActivitySource.SetError(activity, ex);
            throw;
        }
    }
}
