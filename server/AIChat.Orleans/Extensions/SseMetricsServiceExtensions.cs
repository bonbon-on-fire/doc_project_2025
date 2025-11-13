using AIChat.Orleans.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Orleans.Extensions;

/// <summary>
/// Extension methods for registering SSE metrics services.
/// </summary>
public static class SseMetricsServiceExtensions
{
    /// <summary>
    /// Adds SSE metrics collection services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSseMetricsCollection(this IServiceCollection services)
    {
        // Register default options
        services.Configure<SseMetricsOptions>(options =>
        {
            // Use default values from the SseMetricsOptions constructor
        });

        // Register SSE metrics collector as singleton for metric aggregation
        services.AddSingleton<ISseMetricsCollector, SseMetricsCollector>();

        // Register the background cleanup service
        services.AddHostedService<SseMetricsCleanupService>();

        return services;
    }

    /// <summary>
    /// Adds SSE metrics collection with custom configuration.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Action to configure SSE metrics options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSseMetricsCollection(
        this IServiceCollection services,
        Action<SseMetricsOptions> configureOptions)
    {
        // Configure options using the provided action
        services.Configure<SseMetricsOptions>(configureOptions);

        // Register SSE metrics collector as singleton for metric aggregation
        services.AddSingleton<ISseMetricsCollector, SseMetricsCollector>();

        // Register the background cleanup service
        services.AddHostedService<SseMetricsCleanupService>();

        return services;
    }
}

/// <summary>
/// Configuration options for SSE metrics collection.
/// </summary>
public class SseMetricsOptions
{
    /// <summary>
    /// Maximum number of events to retain in memory.
    /// Default: 10000
    /// </summary>
    public int MaxRecentEvents { get; set; } = 10000;

    /// <summary>
    /// Hours to retain metrics data.
    /// Default: 24
    /// </summary>
    public int MetricsRetentionHours { get; set; } = 24;

    /// <summary>
    /// Health check cache interval in seconds.
    /// Default: 30
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Enable detailed chunk-level metrics.
    /// Default: true
    /// </summary>
    public bool EnableDetailedChunkMetrics { get; set; } = true;

    /// <summary>
    /// Enable automatic alert generation.
    /// Default: true
    /// </summary>
    public bool EnableAutoAlerts { get; set; } = true;

    /// <summary>
    /// Alert threshold configuration.
    /// </summary>
    public AlertThresholds Thresholds { get; set; } = new();
}

/// <summary>
/// Alert threshold configuration for SSE monitoring.
/// </summary>
public class AlertThresholds
{
    /// <summary>
    /// Warning threshold for stream failure rate (percentage).
    /// Default: 5.0
    /// </summary>
    public double WarningFailureRate { get; set; } = 5.0;

    /// <summary>
    /// Critical threshold for stream failure rate (percentage).
    /// Default: 10.0
    /// </summary>
    public double CriticalFailureRate { get; set; } = 10.0;

    /// <summary>
    /// Warning threshold for buffer utilization (percentage).
    /// Default: 75.0
    /// </summary>
    public double WarningBufferUtilization { get; set; } = 75.0;

    /// <summary>
    /// Critical threshold for buffer utilization (percentage).
    /// Default: 90.0
    /// </summary>
    public double CriticalBufferUtilization { get; set; } = 90.0;

    /// <summary>
    /// Warning threshold for chunk processing latency (milliseconds).
    /// Default: 500.0
    /// </summary>
    public double WarningLatencyMs { get; set; } = 500.0;

    /// <summary>
    /// Critical threshold for chunk processing latency (milliseconds).
    /// Default: 1000.0
    /// </summary>
    public double CriticalLatencyMs { get; set; } = 1000.0;

    /// <summary>
    /// Warning threshold for connection drops per hour.
    /// Default: 10
    /// </summary>
    public int WarningConnectionDropsPerHour { get; set; } = 10;

    /// <summary>
    /// Critical threshold for connection drops per hour.
    /// Default: 50
    /// </summary>
    public int CriticalConnectionDropsPerHour { get; set; } = 50;
}
