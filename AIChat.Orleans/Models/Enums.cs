namespace AIChat.Orleans.Contracts;

/// <summary>
/// Types of user activities that can be tracked.
/// </summary>
public enum ActivityType
{
    /// <summary>
    /// User connected to the system.
    /// </summary>
    Connected,

    /// <summary>
    /// User disconnected from the system.
    /// </summary>
    Disconnected,

    /// <summary>
    /// User sent a message.
    /// </summary>
    MessageSent,

    /// <summary>
    /// Message processing completed.
    /// </summary>
    MessageCompleted,

    /// <summary>
    /// User subscribed to a chat.
    /// </summary>
    ChatSubscribed,

    /// <summary>
    /// User unsubscribed from a chat.
    /// </summary>
    ChatUnsubscribed,

    /// <summary>
    /// Operation was cancelled.
    /// </summary>
    OperationCancelled,

    /// <summary>
    /// Error occurred during processing.
    /// </summary>
    ErrorOccurred
}

/// <summary>
/// Chat subscription states.
/// </summary>
public enum SubscriptionState
{
    /// <summary>
    /// Subscription is active and receiving messages.
    /// </summary>
    Active,

    /// <summary>
    /// Subscription is paused (not receiving messages).
    /// </summary>
    Paused,

    /// <summary>
    /// Subscription is inactive (no connections).
    /// </summary>
    Inactive
}

/// <summary>
/// Types of operations that can be performed.
/// </summary>
public enum OperationType
{
    /// <summary>
    /// Send a new message.
    /// </summary>
    SendMessage,

    /// <summary>
    /// Regenerate a previous response.
    /// </summary>
    RegenerateResponse,

    /// <summary>
    /// Edit an existing message.
    /// </summary>
    EditMessage,

    /// <summary>
    /// Delete a message.
    /// </summary>
    DeleteMessage
}

/// <summary>
/// Operation execution states.
/// </summary>
public enum OperationStatus
{
    /// <summary>
    /// Operation is queued but not started.
    /// </summary>
    Queued,

    /// <summary>
    /// Operation is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Operation failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Operation was cancelled by user.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Operation status is unknown.
    /// </summary>
    Unknown
}

/// <summary>
/// Priority levels for buffered messages.
/// </summary>
public enum BufferPriority
{
    /// <summary>
    /// Normal priority message.
    /// </summary>
    Normal,

    /// <summary>
    /// High priority message (delivered first).
    /// </summary>
    High
}

/// <summary>
/// Strategies for handling buffer overflow conditions.
/// </summary>
public enum BufferOverflowStrategy
{
    /// <summary>
    /// Drop oldest messages when buffer is full.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Reject new messages when buffer is full.
    /// </summary>
    RejectNew
}