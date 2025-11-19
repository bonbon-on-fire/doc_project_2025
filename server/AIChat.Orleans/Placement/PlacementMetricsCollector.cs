using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace AIChat.Orleans.Placement;

/// <summary>
/// Collects metrics about grain placement effectiveness to measure optimization success.
/// Tracks placement decisions, cross-silo communications, and affinity success rates.
/// </summary>
public interface IPlacementMetricsCollector
{
    /// <summary>
    /// Records a grain placement decision.
    /// </summary>
    /// <param name="grainType">Type of grain placed</param>
    /// <param name="grainId">Grain identifier</param>
    /// <param name="selectedSilo">Silo where grain was placed</param>
    /// <param name="availableSilos">Number of available silos</param>
    /// <param name="placementStrategy">Placement strategy used</param>
    void RecordPlacement(string grainType, string grainId, SiloAddress selectedSilo, int availableSilos, string placementStrategy);

    /// <summary>
    /// Records a cross-silo communication event.
    /// </summary>
    /// <param name="sourceGrainType">Source grain type</param>
    /// <param name="targetGrainType">Target grain type</param>
    /// <param name="sourceSilo">Source silo</param>
    /// <param name="targetSilo">Target silo</param>
    void RecordCrossSiloCommunication(string sourceGrainType, string targetGrainType, SiloAddress sourceSilo, SiloAddress targetSilo);

    /// <summary>
    /// Records an affinity success (when related grains are on same silo).
    /// </summary>
    /// <param name="grainType1">First grain type</param>
    /// <param name="grainType2">Second grain type</param>
    /// <param name="silo">Shared silo</param>
    void RecordAffinitySuccess(string grainType1, string grainType2, SiloAddress silo);

    /// <summary>
    /// Gets placement effectiveness metrics.
    /// </summary>
    /// <returns>Placement metrics summary</returns>
    Task<PlacementMetricsSummary> GetMetricsSummaryAsync();

    /// <summary>
    /// Resets all collected metrics.
    /// </summary>
    void ResetMetrics();
}

/// <summary>
/// Implementation of placement metrics collector.
/// </summary>
public class PlacementMetricsCollector : IPlacementMetricsCollector
{
    private readonly ILogger<PlacementMetricsCollector> _logger;

    // Metrics storage
    private readonly ConcurrentDictionary<string, long> _placementCounts = new();
    private readonly ConcurrentDictionary<string, long> _crossSiloCommCounts = new();
    private readonly ConcurrentDictionary<string, long> _affinitySuccessCounts = new();
    private readonly ConcurrentDictionary<SiloAddress, long> _siloLoadCounts = new();

    // Timing and performance metrics
    private long _totalPlacements;
    private DateTime _metricsStartTime = DateTime.UtcNow;
    private readonly object _resetLock = new();

    /// <summary>
    /// Initializes a new instance of the PlacementMetricsCollector.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public PlacementMetricsCollector(ILogger<PlacementMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void RecordPlacement(string grainType, string grainId, SiloAddress selectedSilo, int availableSilos, string placementStrategy)
    {
        try
        {
            var key = $"{grainType}:{placementStrategy}";
            _ = _placementCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            _ = _siloLoadCounts.AddOrUpdate(selectedSilo, 1, (_, count) => count + 1);

            _ = Interlocked.Increment(ref _totalPlacements);

            _logger.LogDebug(
                "Recorded placement: {GrainType} grain {GrainId} -> {Silo} using {Strategy} ({AvailableSilos} silos available)",
                grainType, grainId, selectedSilo, placementStrategy, availableSilos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording placement metric for {GrainType} grain {GrainId}", grainType, grainId);
        }
    }

    /// <inheritdoc />
    public void RecordCrossSiloCommunication(string sourceGrainType, string targetGrainType, SiloAddress sourceSilo, SiloAddress targetSilo)
    {
        try
        {
            var key = $"{sourceGrainType}->{targetGrainType}";
            _ = _crossSiloCommCounts.AddOrUpdate(key, 1, (_, count) => count + 1);

            _logger.LogDebug(
                "Recorded cross-silo communication: {SourceType}@{SourceSilo} -> {TargetType}@{TargetSilo}",
                sourceGrainType, sourceSilo, targetGrainType, targetSilo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error recording cross-silo communication: {SourceType} -> {TargetType}",
                sourceGrainType, targetGrainType);
        }
    }

    /// <inheritdoc />
    public void RecordAffinitySuccess(string grainType1, string grainType2, SiloAddress silo)
    {
        try
        {
            // Normalize the key to avoid duplicate entries for different orderings
            var key = string.CompareOrdinal(grainType1, grainType2) <= 0
                ? $"{grainType1}+{grainType2}"
                : $"{grainType2}+{grainType1}";

            _ = _affinitySuccessCounts.AddOrUpdate(key, 1, (_, count) => count + 1);

            _logger.LogDebug(
                "Recorded affinity success: {GrainType1} and {GrainType2} co-located on {Silo}",
                grainType1, grainType2, silo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error recording affinity success: {GrainType1} + {GrainType2}",
                grainType1, grainType2);
        }
    }

    /// <inheritdoc />
    public Task<PlacementMetricsSummary> GetMetricsSummaryAsync()
    {
        try
        {
            // Capture atomic snapshot of metrics to ensure consistency
            var endTime = DateTime.UtcNow;
            var totalPlacements = Interlocked.Read(ref _totalPlacements);
            var startTime = _metricsStartTime; // This is set once and read-only after that

            // Create efficient snapshots of concurrent dictionaries
            var placementStrategyCounts = new Dictionary<string, long>(_placementCounts.Count);
            foreach (var kvp in _placementCounts)
            {
                placementStrategyCounts[kvp.Key] = kvp.Value;
            }

            var crossSiloCommCounts = new Dictionary<string, long>(_crossSiloCommCounts.Count);
            foreach (var kvp in _crossSiloCommCounts)
            {
                crossSiloCommCounts[kvp.Key] = kvp.Value;
            }

            var affinitySuccessCounts = new Dictionary<string, long>(_affinitySuccessCounts.Count);
            foreach (var kvp in _affinitySuccessCounts)
            {
                affinitySuccessCounts[kvp.Key] = kvp.Value;
            }

            var siloLoadDistribution = new Dictionary<string, long>(_siloLoadCounts.Count);
            foreach (var kvp in _siloLoadCounts)
            {
                siloLoadDistribution[kvp.Key.ToString()] = kvp.Value;
            }

            var summary = new PlacementMetricsSummary
            {
                TotalPlacements = totalPlacements,
                MetricsStartTime = startTime,
                MetricsEndTime = endTime,
                PlacementStrategyCounts = placementStrategyCounts,
                CrossSiloCommunicationCounts = crossSiloCommCounts,
                AffinitySuccessCounts = affinitySuccessCounts,
                SiloLoadDistribution = siloLoadDistribution
            };

            _logger.LogDebug(
                "Generated placement metrics summary: {TotalPlacements} placements, {Strategies} strategies, {CrossSilo} cross-silo communications",
                summary.TotalPlacements, summary.PlacementStrategyCounts.Count,
                summary.CrossSiloCommunicationCounts.Values.Sum());

            return Task.FromResult(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating placement metrics summary");

            // Return safe fallback with atomic read of total placements
            var fallbackTotalPlacements = Interlocked.Read(ref _totalPlacements);
            return Task.FromResult(new PlacementMetricsSummary
            {
                TotalPlacements = fallbackTotalPlacements,
                MetricsStartTime = _metricsStartTime,
                MetricsEndTime = DateTime.UtcNow,
                PlacementStrategyCounts = [],
                CrossSiloCommunicationCounts = [],
                AffinitySuccessCounts = [],
                SiloLoadDistribution = []
            });
        }
    }

    /// <inheritdoc />
    public void ResetMetrics()
    {
        lock (_resetLock)
        {
            try
            {
                // Atomic reset of all metrics to ensure consistent state
                var newStartTime = DateTime.UtcNow;

                _placementCounts.Clear();
                _crossSiloCommCounts.Clear();
                _affinitySuccessCounts.Clear();
                _siloLoadCounts.Clear();

                // Reset total placements atomically
                _ = Interlocked.Exchange(ref _totalPlacements, 0);
                _metricsStartTime = newStartTime;

                _logger.LogInformation("Placement metrics have been reset at {ResetTime}", newStartTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting placement metrics");
            }
        }
    }
}

/// <summary>
/// Summary of placement metrics for analysis and monitoring.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Placement.PlacementMetricsSummary")]
public class PlacementMetricsSummary
{
    /// <summary>
    /// Total number of grain placements recorded.
    /// </summary>
    [Id(0)]
    public long TotalPlacements { get; set; }

    /// <summary>
    /// When metrics collection started.
    /// </summary>
    [Id(1)]
    public DateTime MetricsStartTime { get; set; }

    /// <summary>
    /// When metrics summary was generated.
    /// </summary>
    [Id(2)]
    public DateTime MetricsEndTime { get; set; }

    /// <summary>
    /// Count of placements by strategy type.
    /// Key format: "GrainType:PlacementStrategy"
    /// </summary>
    [Id(3)]
    public Dictionary<string, long> PlacementStrategyCounts { get; set; } = [];

    /// <summary>
    /// Count of cross-silo communications by grain type pairs.
    /// Key format: "SourceGrainType->TargetGrainType"
    /// </summary>
    [Id(4)]
    public Dictionary<string, long> CrossSiloCommunicationCounts { get; set; } = [];

    /// <summary>
    /// Count of successful affinity co-locations by grain type pairs.
    /// Key format: "GrainType1+GrainType2"
    /// </summary>
    [Id(5)]
    public Dictionary<string, long> AffinitySuccessCounts { get; set; } = [];

    /// <summary>
    /// Distribution of grain placements across silos.
    /// </summary>
    [Id(6)]
    public Dictionary<string, long> SiloLoadDistribution { get; set; } = [];

    /// <summary>
    /// Duration of metrics collection period.
    /// </summary>
    public TimeSpan CollectionDuration => MetricsEndTime - MetricsStartTime;
}
