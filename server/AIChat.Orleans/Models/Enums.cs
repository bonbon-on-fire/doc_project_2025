using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Types of user activities that can be tracked.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityType")]
public enum ActivityType
{
    /// <summary>
    /// User connected to the system.
    /// </summary>
    [Id(0)]
    Connected,

    /// <summary>
    /// User disconnected from the system.
    /// </summary>
    [Id(1)]
    Disconnected,

    /// <summary>
    /// User sent a message.
    /// </summary>
    [Id(2)]
    MessageSent,

    /// <summary>
    /// Message processing completed.
    /// </summary>
    [Id(3)]
    MessageCompleted,

    /// <summary>
    /// User subscribed to a chat.
    /// </summary>
    [Id(4)]
    ChatSubscribed,

    /// <summary>
    /// User unsubscribed from a chat.
    /// </summary>
    [Id(5)]
    ChatUnsubscribed,

    /// <summary>
    /// Operation was cancelled.
    /// </summary>
    [Id(6)]
    OperationCancelled,

    /// <summary>
    /// Error occurred during processing.
    /// </summary>
    [Id(7)]
    ErrorOccurred,
}

/// <summary>
/// Chat subscription states.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.SubscriptionState")]
public enum SubscriptionState
{
    /// <summary>
    /// Subscription is active and receiving messages.
    /// </summary>
    [Id(0)]
    Active,

    /// <summary>
    /// Subscription is paused (not receiving messages).
    /// </summary>
    [Id(1)]
    Paused,

    /// <summary>
    /// Subscription is inactive (no connections).
    /// </summary>
    [Id(2)]
    Inactive,
}

/// <summary>
/// Types of operations that can be performed.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.OperationType")]
public enum OperationType
{
    /// <summary>
    /// Send a new message.
    /// </summary>
    [Id(0)]
    SendMessage,

    /// <summary>
    /// Regenerate a previous response.
    /// </summary>
    [Id(1)]
    RegenerateResponse,

    /// <summary>
    /// Edit an existing message.
    /// </summary>
    [Id(2)]
    EditMessage,

    /// <summary>
    /// Delete a message.
    /// </summary>
    [Id(3)]
    DeleteMessage,
}

/// <summary>
/// Operation execution states.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.OperationStatus")]
public enum OperationStatus
{
    /// <summary>
    /// Operation is queued but not started.
    /// </summary>
    [Id(0)]
    Queued,

    /// <summary>
    /// Operation is currently in progress.
    /// </summary>
    [Id(1)]
    InProgress,

    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    [Id(2)]
    Completed,

    /// <summary>
    /// Operation failed with an error.
    /// </summary>
    [Id(3)]
    Failed,

    /// <summary>
    /// Operation was cancelled by user.
    /// </summary>
    [Id(4)]
    Cancelled,

    /// <summary>
    /// Operation status is unknown.
    /// </summary>
    [Id(5)]
    Unknown,
}

/// <summary>
/// Priority levels for buffered messages.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.BufferPriority")]
public enum BufferPriority
{
    /// <summary>
    /// Normal priority message.
    /// </summary>
    [Id(0)]
    Normal,

    /// <summary>
    /// High priority message (delivered first).
    /// </summary>
    [Id(1)]
    High,
}

/// <summary>
/// Strategies for handling buffer overflow conditions.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.BufferOverflowStrategy")]
public enum BufferOverflowStrategy
{
    /// <summary>
    /// Drop oldest messages when buffer is full.
    /// </summary>
    [Id(0)]
    DropOldest,

    /// <summary>
    /// Reject new messages when buffer is full.
    /// </summary>
    [Id(1)]
    RejectNew,
}
