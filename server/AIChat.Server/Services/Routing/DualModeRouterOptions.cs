namespace AIChat.Server.Services.Routing;

/// <summary>
/// Configuration options for the DualModeRouter service.
/// Provides tunable parameters for circuit breaker behavior, health checks, and metrics collection.
/// </summary>
public class DualModeRouterOptions
{
    /// <summary>
    /// Configuration section name for binding from appsettings.json
    /// </summary>
    public const string SectionName = "DualModeRouter";

    /// <summary>
    /// Gets or sets the circuit breaker timeout duration.
    /// After this timeout, the circuit breaker will attempt to reset and retry Orleans operations.
    /// Default: 1 minute
    /// </summary>
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of execution time measurements to keep for metrics.
    /// Older measurements are discarded to prevent memory growth.
    /// Default: 1000 measurements
    /// </summary>
    public int MetricsWindowSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the timeout for Orleans health checks.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan OrleansHealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the timeout for direct service health checks.
    /// Default: 2 seconds
    /// </summary>
    public TimeSpan DirectServiceHealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets whether to enable detailed performance logging.
    /// Default: false
    /// </summary>
    public bool EnableDetailedLogging { get; set; }
}