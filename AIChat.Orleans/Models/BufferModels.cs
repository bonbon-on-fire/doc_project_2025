using Orleans;
using Orleans.Serialization;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Represents a message stored in the buffer.
/// Contains the original message along with buffer-specific metadata.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.BufferedMessage")]
public sealed class BufferedMessage
{
    /// <summary>
    /// Unique identifier for this buffered message.
    /// </summary>
    [Id(0)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Chat identifier where this message belongs.
    /// </summary>
    [Id(1)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// The original chat message.
    /// </summary>
    [Id(2)]
    public ChatMessage Message { get; set; } = new();

    /// <summary>
    /// Timestamp when the message was buffered.
    /// </summary>
    [Id(3)]
    public DateTime BufferedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the message expires and should be removed from buffer.
    /// </summary>
    [Id(4)]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(1);

    /// <summary>
    /// Priority level of this message.
    /// </summary>
    [Id(5)]
    public BufferPriority Priority { get; set; } = BufferPriority.Normal;

    /// <summary>
    /// Number of delivery attempts for this message.
    /// </summary>
    [Id(6)]
    public int DeliveryAttempts { get; set; } = 0;

    /// <summary>
    /// Last delivery attempt timestamp.
    /// </summary>
    [Id(7)]
    public DateTime? LastDeliveryAttempt { get; set; }

    /// <summary>
    /// Error from last delivery attempt (if any).
    /// </summary>
    [Id(8)]
    public string? LastDeliveryError { get; set; }
}

/// <summary>
/// Represents a chat-specific message buffer.
/// Manages buffered messages for a single chat room.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.ChatMessageBuffer")]
public sealed class ChatMessageBuffer
{
    /// <summary>
    /// Chat identifier for this buffer.
    /// </summary>
    [Id(0)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Queue of buffered messages (ordered by buffer time).
    /// </summary>
    [Id(1)]
    public Queue<BufferedMessage> Messages { get; set; } = new();

    /// <summary>
    /// Maximum number of messages this buffer can hold.
    /// </summary>
    [Id(2)]
    public int MaxSize { get; set; } = 100;

    /// <summary>
    /// TTL in minutes for messages in this buffer.
    /// </summary>
    [Id(3)]
    public int TtlMinutes { get; set; } = 60;

    /// <summary>
    /// Last cleanup operation timestamp.
    /// </summary>
    [Id(4)]
    public DateTime LastCleanupAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of messages that have been buffered in this chat.
    /// </summary>
    [Id(5)]
    public long TotalMessagesBuffered { get; set; } = 0;

    /// <summary>
    /// Total number of messages that have expired and been removed.
    /// </summary>
    [Id(6)]
    public long TotalExpiredMessages { get; set; } = 0;

    /// <summary>
    /// Total number of messages dropped due to buffer overflow.
    /// </summary>
    [Id(7)]
    public long TotalOverflowDrops { get; set; } = 0;

    /// <summary>
    /// Total number of messages successfully delivered.
    /// </summary>
    [Id(8)]
    public long TotalDeliveredMessages { get; set; } = 0;

    /// <summary>
    /// Strategy for handling buffer overflow.
    /// </summary>
    [Id(9)]
    public BufferOverflowStrategy OverflowStrategy { get; set; } = BufferOverflowStrategy.DropOldest;

    /// <summary>
    /// Timestamp when this buffer was created.
    /// </summary>
    [Id(10)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Summary information about a buffer's current state.
/// Used for monitoring and metrics collection.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.BufferSummary")]
public sealed class BufferSummary
{
    /// <summary>
    /// Chat identifier for this buffer summary.
    /// </summary>
    [Id(0)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Current number of messages in buffer.
    /// </summary>
    [Id(1)]
    public int CurrentMessageCount { get; set; } = 0;

    /// <summary>
    /// Maximum buffer capacity.
    /// </summary>
    [Id(2)]
    public int MaxCapacity { get; set; } = 100;

    /// <summary>
    /// Current buffer utilization percentage.
    /// </summary>
    [Id(3)]
    public double UtilizationPercent { get; set; } = 0.0;

    /// <summary>
    /// Oldest message timestamp in buffer.
    /// </summary>
    [Id(4)]
    public DateTime? OldestMessageTime { get; set; }

    /// <summary>
    /// Newest message timestamp in buffer.
    /// </summary>
    [Id(5)]
    public DateTime? NewestMessageTime { get; set; }

    /// <summary>
    /// Number of high-priority messages in buffer.
    /// </summary>
    [Id(6)]
    public int HighPriorityCount { get; set; } = 0;

    /// <summary>
    /// Number of messages approaching expiration (within 5 minutes).
    /// </summary>
    [Id(7)]
    public int ExpiringMessageCount { get; set; } = 0;
}