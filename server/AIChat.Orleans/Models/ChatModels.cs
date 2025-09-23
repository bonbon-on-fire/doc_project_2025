using System.ComponentModel.DataAnnotations;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Request for initializing a new chat session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatInitRequest")]
public sealed class ChatInitRequest
{
    /// <summary>
    /// Unique identifier for the chat session.
    /// </summary>
    [Id(0)]
    [Required]
    public required string ChatId { get; set; }

    /// <summary>
    /// Title or name of the chat session.
    /// </summary>
    [Id(1)]
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Title { get; set; }

    /// <summary>
    /// User ID of the chat creator.
    /// </summary>
    [Id(2)]
    [Required]
    public required string CreatedBy { get; set; }

    /// <summary>
    /// Type of chat (e.g., "direct", "group", "ai-assistant").
    /// </summary>
    [Id(3)]
    public string ChatType { get; set; } = "ai-assistant";

    /// <summary>
    /// Optional mode ID for specialized behavior.
    /// </summary>
    [Id(4)]
    public string? ModeId { get; set; }

    /// <summary>
    /// Initial system prompt for AI conversations.
    /// </summary>
    [Id(5)]
    [StringLength(4000)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Maximum number of participants allowed (null for unlimited).
    /// </summary>
    [Id(6)]
    [Range(1, 1000)]
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// Additional metadata for the chat session (JSON).
    /// </summary>
    [Id(7)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Initial participants to add to the chat.
    /// </summary>
    [Id(8)]
    public List<ChatParticipant> InitialParticipants { get; set; } = [];

    /// <summary>
    /// Chat creation timestamp.
    /// </summary>
    [Id(9)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the complete state of a chat session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatState")]
public sealed class ChatState
{
    /// <summary>
    /// Unique identifier for the chat.
    /// </summary>
    [Id(0)]
    public required string ChatId { get; set; }

    /// <summary>
    /// Chat title or name.
    /// </summary>
    [Id(1)]
    public required string Title { get; set; }

    /// <summary>
    /// Current status of the chat.
    /// </summary>
    [Id(2)]
    public ChatStatus Status { get; set; } = ChatStatus.Active;

    /// <summary>
    /// Type of chat.
    /// </summary>
    [Id(3)]
    public string ChatType { get; set; } = "ai-assistant";

    /// <summary>
    /// User ID who created the chat.
    /// </summary>
    [Id(4)]
    public required string CreatedBy { get; set; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [Id(5)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    [Id(6)]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of messages in the chat.
    /// </summary>
    [Id(7)]
    public int MessageCount { get; set; } = 0;

    /// <summary>
    /// Number of active participants.
    /// </summary>
    [Id(8)]
    public int ParticipantCount { get; set; } = 0;

    /// <summary>
    /// Current mode ID if applicable.
    /// </summary>
    [Id(9)]
    public string? ModeId { get; set; }

    /// <summary>
    /// System prompt for AI conversations.
    /// </summary>
    [Id(10)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Additional metadata (JSON).
    /// </summary>
    [Id(11)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Archive timestamp if archived.
    /// </summary>
    [Id(12)]
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// Version number for optimistic concurrency.
    /// </summary>
    [Id(13)]
    public int Version { get; set; } = 1;
}

/// <summary>
/// Result of a message processing operation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.MessageResult")]
public sealed class MessageResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    [Id(0)]
    public bool Success { get; set; }

    /// <summary>
    /// The processed message.
    /// </summary>
    [Id(1)]
    public ChatMessage? Message { get; set; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    [Id(2)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    [Id(3)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Operation timestamp.
    /// </summary>
    [Id(4)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional result metadata.
    /// </summary>
    [Id(5)]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static MessageResult CreateSuccess(ChatMessage message)
        => new() { Success = true, Message = message };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static MessageResult CreateFailure(string errorMessage, string? errorCode = null)
        => new() { Success = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Request for initiating a streaming message.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StreamMessage")]
public sealed class StreamMessage
{
    /// <summary>
    /// Chat ID where the stream will be delivered.
    /// </summary>
    [Id(0)]
    [Required]
    public required string ChatId { get; set; }

    /// <summary>
    /// User ID initiating the stream.
    /// </summary>
    [Id(1)]
    [Required]
    public required string UserId { get; set; }

    /// <summary>
    /// Initial message content or prompt.
    /// </summary>
    [Id(2)]
    [Required]
    public required string Content { get; set; }

    /// <summary>
    /// Role of the message sender.
    /// </summary>
    [Id(3)]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Optional mode ID for the stream.
    /// </summary>
    [Id(4)]
    public string? ModeId { get; set; }

    /// <summary>
    /// Optional system prompt override.
    /// </summary>
    [Id(5)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Stream metadata (JSON).
    /// </summary>
    [Id(6)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Request timestamp.
    /// </summary>
    [Id(7)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Handle for managing an active stream.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StreamHandle")]
public sealed class StreamHandle
{
    /// <summary>
    /// Unique stream identifier.
    /// </summary>
    [Id(0)]
    public required string StreamId { get; set; }

    /// <summary>
    /// Chat ID associated with the stream.
    /// </summary>
    [Id(1)]
    public required string ChatId { get; set; }

    /// <summary>
    /// Orleans stream ID for subscriptions.
    /// </summary>
    [Id(2)]
    public Guid OrleansStreamId { get; set; }

    /// <summary>
    /// Stream creation timestamp.
    /// </summary>
    [Id(3)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current stream status.
    /// </summary>
    [Id(4)]
    public StreamStatus Status { get; set; } = StreamStatus.Active;

    /// <summary>
    /// Estimated completion time if available.
    /// </summary>
    [Id(5)]
    public DateTime? EstimatedCompletionTime { get; set; }
}

/// <summary>
/// Represents a chat participant.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatParticipant")]
public sealed class ChatParticipant
{
    /// <summary>
    /// Unique participant identifier.
    /// </summary>
    [Id(0)]
    [Required]
    public required string ParticipantId { get; set; }

    /// <summary>
    /// Display name of the participant.
    /// </summary>
    [Id(1)]
    [Required]
    public required string DisplayName { get; set; }

    /// <summary>
    /// Participant's role in the chat.
    /// </summary>
    [Id(2)]
    public ParticipantRole Role { get; set; } = ParticipantRole.Member;

    /// <summary>
    /// Current presence status.
    /// </summary>
    [Id(3)]
    public PresenceStatus Status { get; set; } = PresenceStatus.Online;

    /// <summary>
    /// Timestamp when participant joined.
    /// </summary>
    [Id(4)]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    [Id(5)]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Participant type (user, bot, system).
    /// </summary>
    [Id(6)]
    public string ParticipantType { get; set; } = "user";

    /// <summary>
    /// Additional participant metadata (JSON).
    /// </summary>
    [Id(7)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Whether the participant is currently typing.
    /// </summary>
    [Id(8)]
    public bool IsTyping { get; set; } = false;
}

/// <summary>
/// Update information for a chat participant.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ParticipantUpdate")]
public sealed class ParticipantUpdate
{
    /// <summary>
    /// Participant ID to update.
    /// </summary>
    [Id(0)]
    [Required]
    public required string ParticipantId { get; set; }

    /// <summary>
    /// New display name (null to keep current).
    /// </summary>
    [Id(1)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// New role (null to keep current).
    /// </summary>
    [Id(2)]
    public ParticipantRole? Role { get; set; }

    /// <summary>
    /// New metadata (null to keep current).
    /// </summary>
    [Id(3)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Update timestamp.
    /// </summary>
    [Id(4)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status of a message delivery.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.MessageStatus")]
public sealed class MessageStatus
{
    /// <summary>
    /// Message identifier.
    /// </summary>
    [Id(0)]
    public required string MessageId { get; set; }

    /// <summary>
    /// Delivery status.
    /// </summary>
    [Id(1)]
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>
    /// List of participants who have acknowledged the message.
    /// </summary>
    [Id(2)]
    public List<string> AcknowledgedBy { get; set; } = [];

    /// <summary>
    /// List of participants with pending delivery.
    /// </summary>
    [Id(3)]
    public List<string> PendingDelivery { get; set; } = [];

    /// <summary>
    /// Timestamp when message was sent.
    /// </summary>
    [Id(4)]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when all participants acknowledged (if applicable).
    /// </summary>
    [Id(5)]
    public DateTime? FullyDeliveredAt { get; set; }

    /// <summary>
    /// Delivery error if any.
    /// </summary>
    [Id(6)]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Handle for a stream subscription.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StreamSubscriptionHandle")]
public sealed class StreamSubscriptionHandle
{
    /// <summary>
    /// Subscription identifier.
    /// </summary>
    [Id(0)]
    public required string SubscriptionId { get; set; }

    /// <summary>
    /// Orleans stream ID.
    /// </summary>
    [Id(1)]
    public Guid StreamId { get; set; }

    /// <summary>
    /// Stream namespace.
    /// </summary>
    [Id(2)]
    public string Namespace { get; set; } = "chat";

    /// <summary>
    /// Subscription creation timestamp.
    /// </summary>
    [Id(3)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the subscription is active.
    /// </summary>
    [Id(4)]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Status of a chat session.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatStatus")]
public enum ChatStatus
{
    /// <summary>
    /// Chat is active and accepting messages.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Chat is archived (read-only).
    /// </summary>
    Archived = 1,

    /// <summary>
    /// Chat is temporarily suspended.
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Chat is being initialized.
    /// </summary>
    Initializing = 3,

    /// <summary>
    /// Chat has been deleted.
    /// </summary>
    Deleted = 4
}

/// <summary>
/// Role of a participant in a chat.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ParticipantRole")]
public enum ParticipantRole
{
    /// <summary>
    /// Regular member with basic permissions.
    /// </summary>
    Member = 0,

    /// <summary>
    /// Moderator with enhanced permissions.
    /// </summary>
    Moderator = 1,

    /// <summary>
    /// Administrator with full permissions.
    /// </summary>
    Admin = 2,

    /// <summary>
    /// Owner of the chat with all permissions.
    /// </summary>
    Owner = 3,

    /// <summary>
    /// Guest with limited permissions.
    /// </summary>
    Guest = 4
}

/// <summary>
/// Presence status of a participant.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PresenceStatus")]
public enum PresenceStatus
{
    /// <summary>
    /// Participant is online and active.
    /// </summary>
    Online = 0,

    /// <summary>
    /// Participant is away/idle.
    /// </summary>
    Away = 1,

    /// <summary>
    /// Participant is busy/do not disturb.
    /// </summary>
    Busy = 2,

    /// <summary>
    /// Participant is offline.
    /// </summary>
    Offline = 3,

    /// <summary>
    /// Participant appears offline but is online.
    /// </summary>
    Invisible = 4
}

/// <summary>
/// Actions that can be performed in a chat.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatAction")]
public enum ChatAction
{
    /// <summary>
    /// Send a message to the chat.
    /// </summary>
    SendMessage = 0,

    /// <summary>
    /// Edit own messages.
    /// </summary>
    EditOwnMessage = 1,

    /// <summary>
    /// Edit any message.
    /// </summary>
    EditAnyMessage = 2,

    /// <summary>
    /// Delete own messages.
    /// </summary>
    DeleteOwnMessage = 3,

    /// <summary>
    /// Delete any message.
    /// </summary>
    DeleteAnyMessage = 4,

    /// <summary>
    /// Add participants to the chat.
    /// </summary>
    AddParticipant = 5,

    /// <summary>
    /// Remove participants from the chat.
    /// </summary>
    RemoveParticipant = 6,

    /// <summary>
    /// Change participant roles.
    /// </summary>
    ChangeRoles = 7,

    /// <summary>
    /// Archive the chat.
    /// </summary>
    ArchiveChat = 8,

    /// <summary>
    /// Delete the chat.
    /// </summary>
    DeleteChat = 9,

    /// <summary>
    /// Update chat settings.
    /// </summary>
    UpdateSettings = 10
}

/// <summary>
/// Status of message delivery.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DeliveryStatus")]
public enum DeliveryStatus
{
    /// <summary>
    /// Message is pending delivery.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Message has been sent.
    /// </summary>
    Sent = 1,

    /// <summary>
    /// Message has been delivered to all participants.
    /// </summary>
    Delivered = 2,

    /// <summary>
    /// Message has been read by all participants.
    /// </summary>
    Read = 3,

    /// <summary>
    /// Message delivery failed.
    /// </summary>
    Failed = 4
}