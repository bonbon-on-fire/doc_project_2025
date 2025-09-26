using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Services.SignalRBuffering.Models;

/// <summary>
/// Represents comprehensive metrics for SignalR buffer performance.
/// Provides real-time and historical data about buffer operations and health.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.BufferMetrics")]
public record BufferMetrics
{
    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    [Id(0)]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// </summary>
    [Id(1)]
    [Range(0, int.MaxValue)]
    public int CurrentBufferSize { get; init; }

    /// <summary>
    /// Gets the maximum configured buffer capacity.
    /// </summary>
    [Id(2)]
    [Range(1, int.MaxValue)]
    public int MaxBufferCapacity { get; init; }

    /// <summary>
    /// Gets the buffer utilization percentage (0-100).
    /// </summary>
    [Id(3)]
    [Range(0, 100)]
    public double BufferUtilizationPercent { get; init; }

    /// <summary>
    /// Gets the total number of messages enqueued since startup.
    /// </summary>
    [Id(4)]
    [Range(0, long.MaxValue)]
    public long TotalEnqueued { get; init; }

    /// <summary>
    /// Gets the total number of messages delivered since startup.
    /// </summary>
    [Id(5)]
    [Range(0, long.MaxValue)]
    public long TotalDelivered { get; init; }

    /// <summary>
    /// Gets the total number of messages dropped due to overflow.
    /// </summary>
    [Id(6)]
    [Range(0, long.MaxValue)]
    public long TotalDropped { get; init; }

    /// <summary>
    /// Gets the total number of delivery failures.
    /// </summary>
    [Id(7)]
    [Range(0, long.MaxValue)]
    public long TotalFailed { get; init; }

    /// <summary>
    /// Gets the current delivery success rate (0-100).
    /// </summary>
    [Id(8)]
    [Range(0, 100)]
    public double DeliverySuccessRate { get; init; }

    /// <summary>
    /// Gets the average time messages spend in the buffer before delivery.
    /// </summary>
    [Id(9)]
    public TimeSpan AverageQueueTime { get; init; }

    /// <summary>
    /// Gets the average time taken for actual message delivery.
    /// </summary>
    [Id(10)]
    public TimeSpan AverageDeliveryTime { get; init; }

    /// <summary>
    /// Gets the current messages per second throughput.
    /// </summary>
    [Id(11)]
    [Range(0, double.MaxValue)]
    public double MessagesPerSecond { get; init; }

    /// <summary>
    /// Gets the peak messages per second observed.
    /// </summary>
    [Id(12)]
    [Range(0, double.MaxValue)]
    public double PeakMessagesPerSecond { get; init; }

    /// <summary>
    /// Gets the number of overflow events in the last hour.
    /// </summary>
    [Id(13)]
    [Range(0, int.MaxValue)]
    public int OverflowEventsLastHour { get; init; }

    /// <summary>
    /// Gets the current buffer health status.
    /// </summary>
    [Id(14)]
    public BufferHealthStatus HealthStatus { get; init; }

    /// <summary>
    /// Gets the breakdown of delivery statuses.
    /// </summary>
    [Id(15)]
    public Dictionary<DeliveryStatus, long> DeliveryStatusBreakdown { get; init; } = [];

    /// <summary>
    /// Gets the breakdown of buffer operation results.
    /// </summary>
    [Id(16)]
    public Dictionary<BufferOperationResult, long> OperationResultBreakdown { get; init; } = [];

    /// <summary>
    /// Gets additional custom metrics.
    /// </summary>
    [Id(17)]
    public Dictionary<string, object> CustomMetrics { get; init; } = [];
}

/// <summary>
/// Represents the health status of the SignalR buffer.
/// Used for monitoring and alerting purposes.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.BufferHealth")]
public record BufferHealth
{
    /// <summary>
    /// Gets whether the buffer is currently healthy and operational.
    /// </summary>
    [Id(0)]
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the overall health status.
    /// </summary>
    [Id(1)]
    public BufferHealthStatus Status { get; init; }

    /// <summary>
    /// Gets health status details and any issues.
    /// </summary>
    [Id(2)]
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    [Id(3)]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the time since the last successful message delivery.
    /// </summary>
    [Id(4)]
    public TimeSpan? TimeSinceLastDelivery { get; init; }

    /// <summary>
    /// Gets the current error rate (errors per hour).
    /// </summary>
    [Id(5)]
    [Range(0, double.MaxValue)]
    public double ErrorRate { get; init; }

    /// <summary>
    /// Gets whether the buffer is approaching capacity.
    /// </summary>
    [Id(6)]
    public bool IsNearCapacity { get; init; }

    /// <summary>
    /// Gets whether there are any connectivity issues.
    /// </summary>
    [Id(7)]
    public bool HasConnectivityIssues { get; init; }

    /// <summary>
    /// Gets additional health check details.
    /// </summary>
    [Id(8)]
    public Dictionary<string, object> Details { get; init; } = [];

    /// <summary>
    /// Creates a healthy buffer health status.
    /// </summary>
    /// <param name="message">Optional health message</param>
    /// <returns>A healthy BufferHealth instance</returns>
    public static BufferHealth Healthy(string? message = null)
        => new()
        {
            IsHealthy = true,
            Status = BufferHealthStatus.Healthy,
            Message = message ?? "Buffer is operating normally"
        };

    /// <summary>
    /// Creates a degraded buffer health status.
    /// </summary>
    /// <param name="message">Health message describing the issue</param>
    /// <param name="details">Additional details</param>
    /// <returns>A degraded BufferHealth instance</returns>
    public static BufferHealth Degraded(string message, Dictionary<string, object>? details = null)
        => new()
        {
            IsHealthy = true,
            Status = BufferHealthStatus.Degraded,
            Message = message,
            Details = details ?? []
        };

    /// <summary>
    /// Creates an unhealthy buffer health status.
    /// </summary>
    /// <param name="message">Error message describing the issue</param>
    /// <param name="details">Additional error details</param>
    /// <returns>An unhealthy BufferHealth instance</returns>
    public static BufferHealth Unhealthy(string message, Dictionary<string, object>? details = null)
        => new()
        {
            IsHealthy = false,
            Status = BufferHealthStatus.Unhealthy,
            Message = message,
            Details = details ?? []
        };
}

/// <summary>
/// Represents the health status levels for the buffer.
/// Used for monitoring, alerting, and operational decisions.
/// </summary>
[GenerateSerializer]
public enum BufferHealthStatus
{
    /// <summary>
    /// Buffer is operating normally with no issues.
    /// </summary>
    [Id(0)]
    Healthy = 0,

    /// <summary>
    /// Buffer is operational but experiencing some issues that may affect performance.
    /// </summary>
    [Id(1)]
    Degraded = 1,

    /// <summary>
    /// Buffer is not operational or experiencing critical issues.
    /// </summary>
    [Id(2)]
    Unhealthy = 2,

    /// <summary>
    /// Buffer health status is unknown or cannot be determined.
    /// </summary>
    [Id(3)]
    Unknown = 3
}

/// <summary>
/// Represents statistics about message delivery performance.
/// Used for analytics and performance monitoring.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.DeliveryStats")]
public record DeliveryStats
{
    /// <summary>
    /// Gets the time period these statistics cover.
    /// </summary>
    [Id(0)]
    public TimeSpan Period { get; init; }

    /// <summary>
    /// Gets the total number of delivery attempts in this period.
    /// </summary>
    [Id(1)]
    [Range(0, long.MaxValue)]
    public long TotalAttempts { get; init; }

    /// <summary>
    /// Gets the number of successful deliveries in this period.
    /// </summary>
    [Id(2)]
    [Range(0, long.MaxValue)]
    public long SuccessfulDeliveries { get; init; }

    /// <summary>
    /// Gets the number of failed deliveries in this period.
    /// </summary>
    [Id(3)]
    [Range(0, long.MaxValue)]
    public long FailedDeliveries { get; init; }

    /// <summary>
    /// Gets the success rate as a percentage (0-100).
    /// </summary>
    [Id(4)]
    [Range(0, 100)]
    public double SuccessRate { get; init; }

    /// <summary>
    /// Gets the average delivery time in this period.
    /// </summary>
    [Id(5)]
    public TimeSpan AverageDeliveryTime { get; init; }

    /// <summary>
    /// Gets the 95th percentile delivery time.
    /// </summary>
    [Id(6)]
    public TimeSpan P95DeliveryTime { get; init; }

    /// <summary>
    /// Gets the 99th percentile delivery time.
    /// </summary>
    [Id(7)]
    public TimeSpan P99DeliveryTime { get; init; }

    /// <summary>
    /// Gets the breakdown of failures by type.
    /// </summary>
    [Id(8)]
    public Dictionary<DeliveryStatus, long> FailureBreakdown { get; init; } = [];

    /// <summary>
    /// Gets the timestamp when these statistics were calculated.
    /// </summary>
    [Id(9)]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}