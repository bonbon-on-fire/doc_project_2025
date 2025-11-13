using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Placement;

namespace AIChat.Orleans.Placement;

/// <summary>
/// Grain filter that intercepts grain method calls to collect placement effectiveness metrics.
/// This filter observes actual grain interactions to measure placement optimization success.
/// </summary>
public class PlacementMetricsFilter : IIncomingGrainCallFilter, IOutgoingGrainCallFilter
{
    private readonly IPlacementMetricsCollector _metricsCollector;
    private readonly ILogger<PlacementMetricsFilter> _logger;

    // Cache for grain type information to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, string> GrainTypeCache = new();
    private static readonly ConcurrentDictionary<Type, string> PlacementStrategyCache = new();

    // Frequently used method names for fast comparison
    private const string OnActivateAsyncMethod = "OnActivateAsync";

    /// <summary>
    /// Initializes a new instance of the PlacementMetricsFilter.
    /// </summary>
    /// <param name="metricsCollector">Metrics collector service</param>
    /// <param name="logger">Logger instance</param>
    public PlacementMetricsFilter(
        IPlacementMetricsCollector metricsCollector,
        ILogger<PlacementMetricsFilter> logger)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        // Early exit for performance - only process OnActivateAsync for placement tracking
        var methodName = context.ImplementationMethod?.Name;
        if (methodName != OnActivateAsyncMethod)
        {
            await context.Invoke();
            return;
        }

        try
        {
            var siloAddress = context.TargetContext.Address.SiloAddress;
            if (siloAddress == null)
            {
                await context.Invoke();
                return;
            }

            var grainInstanceType = context.TargetContext.GrainInstance?.GetType();
            if (grainInstanceType == null)
            {
                await context.Invoke();
                return;
            }

            // Use cached grain type information for better performance
            var grainType = GetCachedGrainTypeName(grainInstanceType);
            var placementStrategy = GetCachedPlacementStrategy(grainInstanceType);
            var grainId = context.TargetContext.GrainId.ToString();

            _metricsCollector.RecordPlacement(
                grainType,
                grainId,
                siloAddress,
                1, // In filter context we don't have visibility to all silos
                placementStrategy);

            _logger.LogDebug(
                "Recorded grain activation: {GrainType} {GrainId} on {Silo} using {Strategy}",
                grainType, grainId, siloAddress, placementStrategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in placement metrics filter for incoming call");
        }

        // Continue with the grain call
        await context.Invoke();
    }

    /// <inheritdoc />
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        // For performance, we'll simplify cross-grain tracking to reduce overhead
        // Only track affinity success for high-value grain type pairs
        try
        {
            if (context.SourceContext?.GrainInstance?.GetType() != null &&
                context.SourceContext.Address.SiloAddress != null &&
                ShouldTrackAffinityForInterface(context.InterfaceName))
            {
                var sourceGrainInstanceType = context.SourceContext.GrainInstance.GetType();
                var sourceGrainType = GetCachedGrainTypeName(sourceGrainInstanceType);
                var targetGrainType = ExtractGrainTypeFromInterface(context.InterfaceName);
                var sourceSilo = context.SourceContext.Address.SiloAddress;

                // Only record affinity for specific high-value grain type pairs
                if (IsHighValueAffinityPair(sourceGrainType, targetGrainType))
                {
                    _metricsCollector.RecordAffinitySuccess(sourceGrainType, targetGrainType, sourceSilo);

                    _logger.LogDebug(
                        "Recorded potential affinity success: {SourceGrain} -> {TargetGrain} on {Silo}",
                        sourceGrainType, targetGrainType, sourceSilo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in placement metrics filter for outgoing call");
        }

        // Continue with the grain call
        await context.Invoke();
    }

    /// <summary>
    /// Gets cached grain type name to avoid repeated reflection.
    /// </summary>
    private static string GetCachedGrainTypeName(Type grainType)
    {
        return GrainTypeCache.GetOrAdd(grainType, type => type.Name);
    }

    /// <summary>
    /// Gets cached placement strategy to avoid repeated reflection.
    /// </summary>
    private static string GetCachedPlacementStrategy(Type grainType)
    {
        return PlacementStrategyCache.GetOrAdd(grainType, DeterminePlacementStrategy);
    }

    /// <summary>
    /// Determines the placement strategy from grain type attributes.
    /// </summary>
    private static string DeterminePlacementStrategy(Type grainType)
    {
        // Check for placement attributes
        if (grainType.GetCustomAttributes(typeof(HashBasedPlacementAttribute), false).Length > 0)
        {
            return "HashBasedPlacement";
        }

        if (grainType.GetCustomAttributes(typeof(ActivationCountBasedPlacementAttribute), false).Length > 0)
        {
            return "ActivationCountBasedPlacement";
        }

        if (grainType.GetCustomAttributes(typeof(RandomPlacementAttribute), false).Length > 0)
        {
            return "RandomPlacement";
        }

        return "DefaultPlacement";
    }

    /// <summary>
    /// Extracts grain type name from interface name.
    /// </summary>
    private static string ExtractGrainTypeFromInterface(string interfaceName)
    {
        // Convert interface name like "IUserGrain" to "UserGrain"
        if (interfaceName.StartsWith('I') && interfaceName.EndsWith("Grain", StringComparison.Ordinal))
        {
            return interfaceName[1..];
        }
        return interfaceName;
    }

    /// <summary>
    /// Fast check if we should track affinity for a given grain interface to reduce processing overhead.
    /// </summary>
    private static bool ShouldTrackAffinityForInterface(string interfaceName)
    {
        // Only track affinity for grain interfaces, not system interfaces
        return interfaceName.EndsWith("Grain", StringComparison.Ordinal);
    }

    /// <summary>
    /// Efficient check for high-value grain type pairs that benefit from affinity tracking.
    /// </summary>
    private static bool IsHighValueAffinityPair(string sourceGrainType, string targetGrainType)
    {
        // Focus on most important affinity patterns for performance
        // User-Chat and Mode-Chat interactions are high-value for placement optimization
        return (sourceGrainType == "UserGrain" && targetGrainType == "ChatGrain") ||
               (sourceGrainType == "ChatGrain" && targetGrainType == "UserGrain") ||
               (sourceGrainType == "ModeGrain" && targetGrainType == "ChatGrain") ||
               (sourceGrainType == "ChatGrain" && targetGrainType == "ModeGrain");
    }
}

/// <summary>
/// Grain filter configuration attribute for placement metrics.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PlacementMetricsFilterAttribute : Attribute
{
    /// <summary>
    /// Whether to enable metrics collection for this grain.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
