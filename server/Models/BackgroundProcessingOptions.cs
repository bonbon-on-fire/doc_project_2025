namespace AIChat.Server.Models;

/// <summary>
/// Configuration options for background processing functionality.
/// </summary>
public class BackgroundProcessingOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "BackgroundProcessing";

    /// <summary>
    /// Maximum time an operation can run before being cancelled.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of operations that can be processed concurrently.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 5;

    /// <summary>
    /// Maximum number of operations that can be queued.
    /// </summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>
    /// Retry policy configuration for failed operations.
    /// </summary>
    public RetryPolicyOptions RetryPolicy { get; set; } = new();

    /// <summary>
    /// Monitoring and metrics configuration.
    /// </summary>
    public MonitoringOptions Monitoring { get; set; } = new();

    /// <summary>
    /// Fallback behavior configuration.
    /// </summary>
    public FallbackOptions Fallback { get; set; } = new();
}

/// <summary>
/// Retry policy configuration for background operations.
/// </summary>
public class RetryPolicyOptions
{
    /// <summary>
    /// Maximum number of retry attempts for failed operations.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay between retry attempts in milliseconds.
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Multiplier for exponential backoff between retries.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// Monitoring and metrics configuration for background processing.
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// How frequently to update metrics and statistics.
    /// </summary>
    public TimeSpan MetricsUpdateInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long to retain operation history for monitoring.
    /// </summary>
    public TimeSpan OperationHistoryRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to enable detailed logging for debugging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}

/// <summary>
/// Fallback behavior configuration when Orleans is unavailable.
/// </summary>
public class FallbackOptions
{
    /// <summary>
    /// Whether to automatically fall back to direct processing when Orleans fails.
    /// </summary>
    public bool EnableAutoFallback { get; set; } = true;

    /// <summary>
    /// Timeout for Orleans operations before falling back to direct processing.
    /// </summary>
    public int FallbackTimeoutMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Maximum consecutive Orleans failures before disabling Orleans temporarily.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;
}