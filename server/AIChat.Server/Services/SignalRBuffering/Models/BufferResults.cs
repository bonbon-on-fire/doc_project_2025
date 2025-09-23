using System.ComponentModel.DataAnnotations;
using Orleans;

namespace AIChat.Server.Services.SignalRBuffering.Models;

/// <summary>
/// Represents the result of a buffer enqueue operation.
/// Provides information about whether the operation succeeded and any relevant details.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.BufferResult")]
public record BufferResult
{
    /// <summary>
    /// Gets whether the enqueue operation was successful.
    /// </summary>
    [Id(0)]
    public bool Success { get; init; }

    /// <summary>
    /// Gets the reason for the operation result.
    /// Provides context for both successful and failed operations.
    /// </summary>
    [Id(1)]
    public BufferOperationResult Result { get; init; }

    /// <summary>
    /// Gets any error message associated with a failed operation.
    /// Null for successful operations.
    /// </summary>
    [Id(2)]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the number of messages dropped due to overflow (if any).
    /// Zero for successful operations or non-overflow failures.
    /// </summary>
    [Id(3)]
    [Range(0, int.MaxValue)]
    public int DroppedMessageCount { get; init; }

    /// <summary>
    /// Gets the current buffer size after the operation.
    /// Useful for monitoring buffer utilization.
    /// </summary>
    [Id(4)]
    [Range(0, int.MaxValue)]
    public int CurrentBufferSize { get; init; }

    /// <summary>
    /// Gets additional metadata about the operation.
    /// Used for detailed diagnostics and analytics.
    /// </summary>
    [Id(5)]
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Creates a successful buffer result.
    /// </summary>
    /// <param name="currentBufferSize">The current buffer size after the operation</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful BufferResult</returns>
    public static BufferResult CreateSuccess(int currentBufferSize, Dictionary<string, object>? metadata = null)
        => new()
        {
            Success = true,
            Result = BufferOperationResult.Enqueued,
            CurrentBufferSize = currentBufferSize,
            Metadata = metadata ?? new()
        };

    /// <summary>
    /// Creates a failed buffer result due to overflow.
    /// </summary>
    /// <param name="droppedCount">Number of messages dropped</param>
    /// <param name="currentBufferSize">Current buffer size</param>
    /// <param name="errorMessage">Error description</param>
    /// <returns>A failed BufferResult indicating overflow</returns>
    public static BufferResult Overflow(int droppedCount, int currentBufferSize, string? errorMessage = null)
        => new()
        {
            Success = false,
            Result = BufferOperationResult.Overflow,
            ErrorMessage = errorMessage ?? "Buffer overflow occurred",
            DroppedMessageCount = droppedCount,
            CurrentBufferSize = currentBufferSize
        };

    /// <summary>
    /// Creates a failed buffer result due to rejection.
    /// </summary>
    /// <param name="reason">The rejection reason</param>
    /// <param name="currentBufferSize">Current buffer size</param>
    /// <param name="errorMessage">Error description</param>
    /// <returns>A failed BufferResult indicating rejection</returns>
    public static BufferResult Rejected(BufferOperationResult reason, int currentBufferSize, string? errorMessage = null)
        => new()
        {
            Success = false,
            Result = reason,
            ErrorMessage = errorMessage ?? $"Message rejected: {reason}",
            CurrentBufferSize = currentBufferSize
        };
}

/// <summary>
/// Represents the result of a delivery operation.
/// Provides detailed information about message delivery attempts and outcomes.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.DeliveryResult")]
public record DeliveryResult
{
    /// <summary>
    /// Gets the unique identifier of the message that was delivered.
    /// </summary>
    [Id(0)]
    [Required]
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the delivery was successful.
    /// </summary>
    [Id(1)]
    public bool Success { get; init; }

    /// <summary>
    /// Gets the delivery status.
    /// </summary>
    [Id(2)]
    public DeliveryStatus Status { get; init; }

    /// <summary>
    /// Gets the time taken to complete the delivery.
    /// Includes queuing time and actual delivery time.
    /// </summary>
    [Id(3)]
    public TimeSpan DeliveryTime { get; init; }

    /// <summary>
    /// Gets the number of retry attempts made.
    /// Zero for first-attempt successes.
    /// </summary>
    [Id(4)]
    [Range(0, int.MaxValue)]
    public int RetryAttempts { get; init; }

    /// <summary>
    /// Gets any error message associated with failed delivery.
    /// Null for successful deliveries.
    /// </summary>
    [Id(5)]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the timestamp when delivery was completed (or failed).
    /// </summary>
    [Id(6)]
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a successful delivery result.
    /// </summary>
    /// <param name="messageId">The delivered message ID</param>
    /// <param name="deliveryTime">Time taken for delivery</param>
    /// <param name="retryAttempts">Number of retry attempts</param>
    /// <returns>A successful DeliveryResult</returns>
    public static DeliveryResult CreateSuccess(string messageId, TimeSpan deliveryTime, int retryAttempts = 0)
        => new()
        {
            MessageId = messageId,
            Success = true,
            Status = DeliveryStatus.Delivered,
            DeliveryTime = deliveryTime,
            RetryAttempts = retryAttempts
        };

    /// <summary>
    /// Creates a failed delivery result.
    /// </summary>
    /// <param name="messageId">The failed message ID</param>
    /// <param name="status">The failure status</param>
    /// <param name="errorMessage">Error description</param>
    /// <param name="retryAttempts">Number of retry attempts</param>
    /// <returns>A failed DeliveryResult</returns>
    public static DeliveryResult Failed(string messageId, DeliveryStatus status, string errorMessage, int retryAttempts = 0)
        => new()
        {
            MessageId = messageId,
            Success = false,
            Status = status,
            ErrorMessage = errorMessage,
            RetryAttempts = retryAttempts
        };
}

/// <summary>
/// Represents the possible results of buffer operations.
/// Used to categorize different types of operation outcomes.
/// </summary>
[GenerateSerializer]
public enum BufferOperationResult
{
    /// <summary>
    /// Message was successfully enqueued to the buffer.
    /// </summary>
    [Id(0)]
    Enqueued = 0,

    /// <summary>
    /// Buffer overflow occurred - buffer is at capacity.
    /// </summary>
    [Id(1)]
    Overflow = 1,

    /// <summary>
    /// Message was rejected due to invalid content or format.
    /// </summary>
    [Id(2)]
    InvalidMessage = 2,

    /// <summary>
    /// Message was rejected due to duplicate ID.
    /// </summary>
    [Id(3)]
    Duplicate = 3,

    /// <summary>
    /// Operation was rejected due to buffer being disabled or unhealthy.
    /// </summary>
    [Id(4)]
    BufferUnavailable = 5,

    /// <summary>
    /// Operation timed out before completion.
    /// </summary>
    [Id(5)]
    Timeout = 6,

    /// <summary>
    /// Operation was cancelled by the caller.
    /// </summary>
    [Id(6)]
    Cancelled = 7
}

/// <summary>
/// Represents the status of a message delivery attempt.
/// Used for delivery confirmation tracking and metrics.
/// </summary>
[GenerateSerializer]
public enum DeliveryStatus
{
    /// <summary>
    /// Message is pending delivery (queued in buffer).
    /// </summary>
    [Id(0)]
    Pending = 0,

    /// <summary>
    /// Message delivery is in progress.
    /// </summary>
    [Id(1)]
    InProgress = 1,

    /// <summary>
    /// Message was successfully delivered to clients.
    /// </summary>
    [Id(2)]
    Delivered = 2,

    /// <summary>
    /// Message delivery failed due to network or client issues.
    /// </summary>
    [Id(3)]
    Failed = 3,

    /// <summary>
    /// Message delivery timed out.
    /// </summary>
    [Id(4)]
    Timeout = 4,

    /// <summary>
    /// Message delivery was cancelled.
    /// </summary>
    [Id(5)]
    Cancelled = 5,

    /// <summary>
    /// Message was rejected by the SignalR hub.
    /// </summary>
    [Id(6)]
    Rejected = 6,

    /// <summary>
    /// Maximum retry attempts exceeded.
    /// </summary>
    [Id(7)]
    MaxRetriesExceeded = 7
}