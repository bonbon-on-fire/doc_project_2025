using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Services.SignalRBuffering.Models;

/// <summary>
/// Configuration settings for the SignalR message buffering system.
/// Provides comprehensive control over buffer behavior, performance, and reliability.
/// </summary>
public class SignalRBufferConfiguration
{
    /// <summary>
    /// Gets or sets whether SignalR buffering is enabled.
    /// When disabled, messages are sent directly without buffering.
    /// </summary>
    [Required]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of messages the buffer can hold.
    /// When capacity is reached, overflow strategy is applied.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxBufferSize { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the interval between buffer processing cycles.
    /// Shorter intervals provide lower latency but higher CPU usage.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
    public TimeSpan ProcessingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the maximum number of messages to process in a single batch.
    /// Larger batches improve throughput but may increase memory usage.
    /// </summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the strategy to use when the buffer overflows.
    /// Available strategies: DropOldest, DropNewest, DropByPriority, Blocking
    /// </summary>
    [Required]
    [StringLength(50)]
    public string OverflowStrategy { get; set; } = "DropOldest";

    /// <summary>
    /// Gets or sets the maximum time to wait for message delivery before considering it failed.
    /// Used for delivery confirmation tracking and timeout detection.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the base interval between retry attempts for failed deliveries.
    /// Actual retry interval may be adjusted using exponential backoff.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for failed deliveries.
    /// After this limit, messages are moved to the dead letter queue.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to use exponential backoff for retry intervals.
    /// When enabled, retry intervals increase exponentially with each attempt.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry interval when using exponential backoff.
    /// Prevents retry intervals from becoming excessively long.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether to enable delivery confirmation tracking.
    /// When enabled, tracks delivery success/failure for metrics and retry logic.
    /// </summary>
    public bool EnableDeliveryConfirmation { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable comprehensive metrics collection.
    /// Metrics include throughput, latency, success rates, and buffer utilization.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval for metrics calculation and reporting.
    /// More frequent updates provide better monitoring but use more resources.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:05:00")]
    public TimeSpan MetricsInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long to retain delivery tracking information.
    /// Older tracking data is automatically cleaned up to prevent memory leaks.
    /// </summary>
    [Range(typeof(TimeSpan), "00:05:00", "24:00:00")]
    public TimeSpan DeliveryTrackingRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the interval for cleaning up expired delivery tracking data.
    /// Automatic cleanup prevents memory growth from accumulated tracking records.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "01:00:00")]
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the buffer utilization percentage that triggers degraded health status.
    /// When buffer usage exceeds this threshold, health status becomes "Degraded".
    /// </summary>
    [Range(50, 95)]
    public int DegradedThresholdPercent { get; set; } = 80;

    /// <summary>
    /// Gets or sets the buffer utilization percentage that triggers unhealthy status.
    /// When buffer usage exceeds this threshold, health status becomes "Unhealthy".
    /// </summary>
    [Range(85, 99)]
    public int UnhealthyThresholdPercent { get; set; } = 95;

    /// <summary>
    /// Gets or sets the maximum acceptable error rate (errors per hour) for healthy status.
    /// Error rates above this threshold trigger degraded or unhealthy status.
    /// </summary>
    [Range(0, 1000)]
    public double MaxErrorRatePerHour { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to enable the dead letter queue for permanently failed messages.
    /// Dead letter messages can be reviewed and potentially reprocessed later.
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum size of the dead letter queue.
    /// When exceeded, oldest dead letter messages are removed.
    /// </summary>
    [Range(0, 100_000)]
    public int MaxDeadLetterQueueSize { get; set; } = 1_000;

    /// <summary>
    /// Gets or sets how long to retain messages in the dead letter queue.
    /// Older messages are automatically removed to prevent unlimited growth.
    /// </summary>
    [Range(typeof(TimeSpan), "01:00:00", "30.00:00:00")]
    public TimeSpan DeadLetterRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets whether to prioritize messages based on their Priority property.
    /// When enabled, higher priority messages are processed before lower priority ones.
    /// </summary>
    public bool EnablePriorityProcessing { get; set; } = true;

    /// <summary>
    /// Gets or sets additional custom configuration properties.
    /// Used for experimental features and environment-specific settings.
    /// </summary>
    public Dictionary<string, object> CustomSettings { get; set; } = [];

    /// <summary>
    /// Validates the configuration and returns any validation errors.
    /// </summary>
    /// <returns>A list of validation error messages, empty if configuration is valid</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (MaxBufferSize <= 0)
        {
            errors.Add("MaxBufferSize must be greater than 0");
        }

        if (BatchSize > MaxBufferSize)
        {
            errors.Add("BatchSize cannot be larger than MaxBufferSize");
        }

        if (ProcessingInterval < TimeSpan.FromMilliseconds(1))
        {
            errors.Add("ProcessingInterval must be at least 1 millisecond");
        }

        if (DegradedThresholdPercent >= UnhealthyThresholdPercent)
        {
            errors.Add("DegradedThresholdPercent must be less than UnhealthyThresholdPercent");
        }

        if (MaxRetryInterval < RetryInterval)
        {
            errors.Add("MaxRetryInterval must be greater than or equal to RetryInterval");
        }

        if (MaxDeadLetterQueueSize > MaxBufferSize)
        {
            errors.Add("MaxDeadLetterQueueSize should not exceed MaxBufferSize");
        }

        var validOverflowStrategies = new[] { "DropOldest", "DropNewest", "DropByPriority", "Blocking" };
        if (!validOverflowStrategies.Contains(OverflowStrategy))
        {
            errors.Add($"OverflowStrategy must be one of: {string.Join(", ", validOverflowStrategies)}");
        }

        return errors;
    }

    /// <summary>
    /// Creates a configuration optimized for high-throughput scenarios.
    /// Suitable for production environments with high message volumes.
    /// </summary>
    /// <returns>A high-throughput SignalRBufferConfiguration</returns>
    public static SignalRBufferConfiguration HighThroughput()
        => new()
        {
            MaxBufferSize = 50_000,
            ProcessingInterval = TimeSpan.FromMilliseconds(50),
            BatchSize = 200,
            OverflowStrategy = "DropOldest",
            EnableMetrics = true,
            EnableDeliveryConfirmation = true,
            DegradedThresholdPercent = 85,
            UnhealthyThresholdPercent = 95
        };

    /// <summary>
    /// Creates a configuration optimized for low-latency scenarios.
    /// Suitable for real-time applications where latency is critical.
    /// </summary>
    /// <returns>A low-latency SignalRBufferConfiguration</returns>
    public static SignalRBufferConfiguration LowLatency()
        => new()
        {
            MaxBufferSize = 5_000,
            ProcessingInterval = TimeSpan.FromMilliseconds(10),
            BatchSize = 10,
            OverflowStrategy = "Blocking",
            EnableMetrics = true,
            EnableDeliveryConfirmation = true,
            DegradedThresholdPercent = 70,
            UnhealthyThresholdPercent = 90
        };

    /// <summary>
    /// Creates a configuration optimized for development and testing.
    /// Includes detailed logging and metrics for debugging purposes.
    /// </summary>
    /// <returns>A development-optimized SignalRBufferConfiguration</returns>
    public static SignalRBufferConfiguration Development()
        => new()
        {
            MaxBufferSize = 1_000,
            ProcessingInterval = TimeSpan.FromMilliseconds(200),
            BatchSize = 5,
            OverflowStrategy = "DropOldest",
            EnableMetrics = true,
            EnableDeliveryConfirmation = true,
            MetricsInterval = TimeSpan.FromSeconds(5),
            DeliveryTrackingRetention = TimeSpan.FromMinutes(30),
            DegradedThresholdPercent = 60,
            UnhealthyThresholdPercent = 80
        };
}