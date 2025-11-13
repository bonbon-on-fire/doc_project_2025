using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Orleans.Placement;

/// <summary>
/// Extension methods for configuring grain placement optimization services.
/// </summary>
public static class PlacementServiceExtensions
{
    /// <summary>
    /// Adds placement metrics collection to the service collection.
    /// This enables tracking of placement effectiveness across different grain types.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPlacementMetrics(this IServiceCollection services)
    {
        // Register placement metrics collector for tracking placement effectiveness
        services.AddSingleton<IPlacementMetricsCollector, PlacementMetricsCollector>();

        // Register the grain filter that will actually collect metrics
        services.AddSingleton<PlacementMetricsFilter>();

        return services;
    }
}

// NOTE: Configuration classes removed as they were dead code not connected to the runtime.
// Placement strategy is determined by attributes on grain classes, not configuration.
