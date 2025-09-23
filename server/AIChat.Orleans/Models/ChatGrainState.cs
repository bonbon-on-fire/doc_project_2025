using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Persistent state for the ChatGrain.
/// Contains all chat-specific data that needs to survive grain deactivation/reactivation.
/// Uses a hybrid approach with Orleans state for fast access and IStateManager for full persistence.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatGrainState")]
public sealed class ChatGrainState
{
    /// <summary>
    /// Core chat metadata including ID, title, status, creation info.
    /// This is the primary state that defines the chat.
    /// </summary>
    [Id(0)]
    public ChatState ChatMetadata { get; set; } = new()
    {
        ChatId = string.Empty,
        Title = string.Empty,
        CreatedBy = string.Empty
    };

    /// <summary>
    /// Recent messages buffer for fast access (configurable size, typically last 50-100 messages).
    /// Provides immediate access to recent conversation history without external calls.
    /// Older messages are stored via IStateManager for full history access.
    /// </summary>
    [Id(1)]
    public Queue<ChatMessage> RecentMessages { get; set; } = new();

    /// <summary>
    /// Active participants in the chat.
    /// Key: ParticipantId, Value: Participant information and current presence status.
    /// Maintained in Orleans state for fast permission checks and presence updates.
    /// </summary>
    [Id(2)]
    public Dictionary<string, ChatParticipant> Participants { get; set; } = [];

    /// <summary>
    /// Currently active streams in the chat.
    /// Key: StreamId, Value: Stream state including progress and metadata.
    /// Used for real-time streaming operations and timeout management.
    /// </summary>
    [Id(3)]
    public Dictionary<string, StreamState> ActiveStreams { get; set; } = [];

    /// <summary>
    /// Message delivery status tracking for acknowledgment and read receipts.
    /// Key: MessageId, Value: Delivery status including acknowledged participants.
    /// Maintained for recent messages to support delivery confirmations.
    /// </summary>
    [Id(4)]
    public Dictionary<string, MessageStatus> MessageDeliveryStatus { get; set; } = [];

    /// <summary>
    /// Grain performance and usage metrics for monitoring and optimization.
    /// Tracks operations, timing, errors, and resource usage for this chat grain.
    /// </summary>
    [Id(5)]
    public GrainMetrics Metrics { get; set; } = new();

    /// <summary>
    /// State version for optimistic concurrency control.
    /// Incremented on each state modification to detect concurrent updates.
    /// Used to ensure state consistency across multiple operations.
    /// </summary>
    [Id(6)]
    public long StateVersion { get; set; } = 1;

    /// <summary>
    /// Grain activation timestamp for debugging and lifecycle tracking.
    /// Updated each time the grain is activated from persistent storage.
    /// </summary>
    [Id(7)]
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Configuration settings specific to this chat instance.
    /// Includes limits for message buffer size, stream timeouts, and other chat-specific settings.
    /// Allows per-chat customization of behavior and limits.
    /// </summary>
    [Id(8)]
    public ChatConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Timestamp of the last successful state persistence operation.
    /// Used for monitoring persistence health and detecting stale state.
    /// </summary>
    [Id(9)]
    public DateTime LastPersistedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current sequence number for message ordering.
    /// Ensures messages are processed and delivered in the correct order.
    /// Incremented with each new message processed by the grain.
    /// </summary>
    [Id(10)]
    public long MessageSequenceNumber { get; set; } = 0;

    /// <summary>
    /// Pending operations that need to be completed on next activation.
    /// Used for recovery scenarios and ensuring no operations are lost during grain deactivation.
    /// Key: OperationId, Value: Operation context for recovery.
    /// </summary>
    [Id(11)]
    public Dictionary<string, PendingOperation> PendingOperations { get; set; } = [];

    /// <summary>
    /// Increments the state version for optimistic concurrency control.
    /// Should be called whenever the state is modified.
    /// </summary>
    public void IncrementVersion()
    {
        StateVersion++;
        LastPersistedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a message to the recent messages buffer.
    /// Maintains the buffer size limit by removing oldest messages when necessary.
    /// </summary>
    /// <param name="message">The message to add to the buffer</param>
    public void AddRecentMessage(ChatMessage message)
    {
        RecentMessages.Enqueue(message);

        // Maintain buffer size limit (default 100 messages)
        while (RecentMessages.Count > Configuration.RecentMessageBufferSize)
        {
            RecentMessages.Dequeue();
        }
    }

    /// <summary>
    /// Gets the next message sequence number and increments the counter.
    /// Ensures unique, ordered sequence numbers for all messages in the chat.
    /// </summary>
    /// <returns>The next sequence number for a new message</returns>
    public long GetNextSequenceNumber()
    {
        return ++MessageSequenceNumber;
    }

    /// <summary>
    /// Checks if the chat is in a valid state for modifications.
    /// </summary>
    /// <returns>True if the chat can be modified, false if archived or deleted</returns>
    public bool CanModify()
    {
        return ChatMetadata.Status is ChatStatus.Active or ChatStatus.Initializing;
    }
}

/// <summary>
/// Configuration settings specific to a chat instance.
/// Allows customization of chat behavior and limits.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatConfiguration")]
public sealed class ChatConfiguration
{
    /// <summary>
    /// Maximum number of recent messages to keep in the Orleans state buffer.
    /// Older messages are automatically moved to external storage via IStateManager.
    /// </summary>
    [Id(0)]
    public int RecentMessageBufferSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of concurrent streams allowed in the chat.
    /// Prevents resource exhaustion from too many simultaneous streams.
    /// </summary>
    [Id(1)]
    public int MaxConcurrentStreams { get; set; } = 5;

    /// <summary>
    /// Timeout in seconds for inactive streams before automatic cleanup.
    /// Streams that don't receive chunks within this time are cancelled.
    /// </summary>
    [Id(2)]
    public int StreamTimeoutSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Maximum number of participants allowed in the chat.
    /// Null means unlimited participants.
    /// </summary>
    [Id(3)]
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// Whether to enable message delivery tracking for this chat.
    /// Affects performance but provides read receipts and delivery confirmations.
    /// </summary>
    [Id(4)]
    public bool EnableDeliveryTracking { get; set; } = true;

    /// <summary>
    /// How long to keep delivery status information before cleanup.
    /// Older delivery status records are automatically removed.
    /// </summary>
    [Id(5)]
    public TimeSpan DeliveryStatusRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether to enable detailed metrics collection for this chat.
    /// May impact performance but provides better monitoring.
    /// </summary>
    [Id(6)]
    public bool EnableDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Interval for automatic state cleanup operations.
    /// Cleanup removes expired streams, old delivery status, etc.
    /// </summary>
    [Id(7)]
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Represents a pending operation that needs to be completed.
/// Used for recovery scenarios when operations are interrupted by grain deactivation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PendingOperation")]
public sealed class PendingOperation
{
    /// <summary>
    /// Unique identifier for the operation.
    /// </summary>
    [Id(0)]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Type of operation (ProcessMessage, CompleteStream, etc.).
    /// </summary>
    [Id(1)]
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Serialized operation data needed for recovery.
    /// Contains all information necessary to retry or complete the operation.
    /// </summary>
    [Id(2)]
    public string OperationData { get; set; } = string.Empty;

    /// <summary>
    /// When the operation was created.
    /// Used for timeout and cleanup of stale operations.
    /// </summary>
    [Id(3)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of retry attempts for this operation.
    /// Used to prevent infinite retry loops.
    /// </summary>
    [Id(4)]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// When the operation should be retried next.
    /// Supports exponential backoff for failed operations.
    /// </summary>
    [Id(5)]
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Last error message if the operation failed.
    /// Used for diagnostics and recovery decision making.
    /// </summary>
    [Id(6)]
    public string? LastError { get; set; }
}