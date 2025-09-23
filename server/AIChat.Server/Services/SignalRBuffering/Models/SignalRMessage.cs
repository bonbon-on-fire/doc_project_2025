using System.ComponentModel.DataAnnotations;
using Orleans;

namespace AIChat.Server.Services.SignalRBuffering.Models;

/// <summary>
/// Represents a SignalR message that can be buffered and processed asynchronously.
/// This model encapsulates all information needed to deliver a message to SignalR clients
/// with delivery tracking, retry logic, and metadata support.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.SignalRBuffering.Models.SignalRMessage")]
public record SignalRMessage
{
    /// <summary>
    /// Gets the unique identifier for this message.
    /// Used for delivery tracking and deduplication.
    /// </summary>
    [Id(0)]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets the SignalR group name to broadcast to.
    /// Typically in format "chat_{chatId}" for chat groups.
    /// </summary>
    [Id(1)]
    [Required]
    [StringLength(200)]
    public string GroupName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SignalR method name to invoke on clients.
    /// Examples: "ReceiveMessage", "ReceiveStreamChunk", "ReceiveError"
    /// </summary>
    [Id(2)]
    [Required]
    [StringLength(100)]
    public string MethodName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the payload object to send to clients.
    /// This will be JSON serialized when sent via SignalR.
    /// </summary>
    [Id(3)]
    [Required]
    public object Payload { get; init; } = new();

    /// <summary>
    /// Gets the timestamp when this message was created.
    /// Used for ordering, metrics, and timeout calculations.
    /// </summary>
    [Id(4)]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of delivery retry attempts.
    /// Incremented each time delivery fails and is retried.
    /// </summary>
    [Id(5)]
    [Range(0, int.MaxValue)]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Gets the maximum time to wait for delivery before considering it failed.
    /// Used by delivery confirmation tracking to determine timeouts.
    /// </summary>
    [Id(6)]
    public TimeSpan DeliveryTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the priority level for this message.
    /// Higher priority messages are processed first and are less likely to be dropped.
    /// </summary>
    [Id(7)]
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;

    /// <summary>
    /// Gets additional metadata for this message.
    /// Used for custom filtering, routing, and analytics.
    /// </summary>
    [Id(8)]
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Gets the timestamp of the last delivery attempt.
    /// Used for retry logic and metrics.
    /// </summary>
    [Id(9)]
    public DateTime? LastAttemptTimestamp { get; set; }

    /// <summary>
    /// Gets the error message from the last failed delivery attempt.
    /// Used for troubleshooting and retry decision making.
    /// </summary>
    [Id(10)]
    public string? LastError { get; set; }
}

/// <summary>
/// Represents the priority level for SignalR messages.
/// Used by overflow strategies to determine which messages to drop first.
/// </summary>
[GenerateSerializer]
public enum MessagePriority
{
    /// <summary>
    /// Low priority messages (analytics, non-critical notifications).
    /// First to be dropped during buffer overflow.
    /// </summary>
    [Id(0)]
    Low = 0,

    /// <summary>
    /// Normal priority messages (regular chat messages, updates).
    /// Standard processing priority.
    /// </summary>
    [Id(1)]
    Normal = 1,

    /// <summary>
    /// High priority messages (important notifications, errors).
    /// Processed before normal priority messages.
    /// </summary>
    [Id(2)]
    High = 2,

    /// <summary>
    /// Critical priority messages (system alerts, connection issues).
    /// Last to be dropped, processed immediately.
    /// </summary>
    [Id(3)]
    Critical = 3
}