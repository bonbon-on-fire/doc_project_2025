using AIChat.Orleans.Placement;

namespace AIChat.Orleans.Host;

/// <summary>
/// Orleans host-specific extension methods for placement metrics configuration.
/// </summary>
public static class PlacementExtensions
{
    /// <summary>
    /// Configures Orleans silo to use placement metrics collection through grain filters.
    /// This enables tracking of grain placement effectiveness and cross-silo communications.
    /// </summary>
    /// <param name="siloBuilder">Orleans silo builder</param>
    /// <returns>Silo builder for chaining</returns>
    public static global::Orleans.Hosting.ISiloBuilder UseOrleansPlacementMetrics(this global::Orleans.Hosting.ISiloBuilder siloBuilder)
    {
        // Add grain filters to intercept grain calls and collect metrics
        _ = siloBuilder.AddIncomingGrainCallFilter<PlacementMetricsFilter>();
        _ = siloBuilder.AddOutgoingGrainCallFilter<PlacementMetricsFilter>();

        return siloBuilder;
    }
}