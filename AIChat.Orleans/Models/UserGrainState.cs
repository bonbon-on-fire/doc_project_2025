using System.Collections.Concurrent;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Persistent state for the UserGrain.
/// Contains all user-specific data that needs to survive grain deactivation/reactivation.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.UserGrainState")]
public sealed class UserGrainState
{
    /// <summary>
    /// User identifier for this grain.
    /// </summary>
    [Id(0)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Active SignalR connections for this user.
    /// Key: ConnectionId, Value: Connection information.
    /// </summary>
    [Id(1)]
    public Dictionary<string, ConnectionInfo> Connections { get; set; } = new();

    /// <summary>
    /// Active chat subscriptions for this user.
    /// Key: ChatId, Value: Subscription information.
    /// </summary>
    [Id(2)]
    public Dictionary<string, ChatSubscription> ActiveChats { get; set; } = new();

    /// <summary>
    /// Recent user activity (circular buffer, max 100 items).
    /// Used for monitoring and debugging.
    /// </summary>
    [Id(3)]
    public Queue<ActivityRecord> RecentActivity { get; set; } = new();

    /// <summary>
    /// Timestamp of last user activity.
    /// </summary>
    [Id(4)]
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Performance and usage metrics for this grain.
    /// </summary>
    [Id(5)]
    public GrainMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Currently active operations (Phase 3).
    /// Key: OperationId, Value: Operation context.
    /// </summary>
    [Id(6)]
    public Dictionary<string, OperationContext> ActiveOperations { get; set; } = new();

    /// <summary>
    /// Grain activation timestamp for debugging.
    /// </summary>
    [Id(7)]
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message buffers per chat room (Phase 3).
    /// Key: ChatId, Value: Chat message buffer.
    /// </summary>
    [Id(8)]
    public Dictionary<string, ChatMessageBuffer> MessageBuffers { get; set; } = new();

    /// <summary>
    /// Active streaming operations (Phase 4).
    /// Key: StreamId, Value: Stream state.
    /// </summary>
    [Id(9)]
    public Dictionary<string, StreamState> ActiveStreams { get; set; } = new();

    /// <summary>
    /// Total number of streams processed.
    /// </summary>
    [Id(10)]
    public long TotalStreamsProcessed { get; set; } = 0;
}

/// <summary>
/// Information about a SignalR connection.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.ConnectionInfo")]
public sealed class ConnectionInfo
{
    /// <summary>
    /// SignalR connection identifier.
    /// </summary>
    [Id(0)]
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Client identifier (browser tab/instance).
    /// </summary>
    [Id(1)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Connection establishment timestamp.
    /// </summary>
    [Id(2)]
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Chat rooms this connection is subscribed to.
    /// </summary>
    [Id(3)]
    public HashSet<string> SubscribedChatIds { get; set; } = new();

    /// <summary>
    /// Last activity timestamp for this connection.
    /// </summary>
    [Id(4)]
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Chat subscription information.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.ChatSubscription")]
public sealed class ChatSubscription
{
    /// <summary>
    /// Chat room identifier.
    /// </summary>
    [Id(0)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Subscription timestamp.
    /// </summary>
    [Id(1)]
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current subscription state.
    /// </summary>
    [Id(2)]
    public SubscriptionState State { get; set; } = SubscriptionState.Active;

    /// <summary>
    /// Number of connections subscribed to this chat.
    /// </summary>
    [Id(3)]
    public int ConnectionCount { get; set; } = 0;
}

/// <summary>
/// User activity record for monitoring.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.ActivityRecord")]
public sealed class ActivityRecord
{
    /// <summary>
    /// Type of activity performed.
    /// </summary>
    [Id(0)]
    public ActivityType Type { get; set; }

    /// <summary>
    /// JSON metadata about the activity.
    /// </summary>
    [Id(1)]
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Activity timestamp.
    /// </summary>
    [Id(2)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Unique identifier for correlation.
    /// </summary>
    [Id(3)]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Grain performance metrics.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.GrainMetrics")]
public sealed class GrainMetrics
{
    /// <summary>
    /// Total number of activities recorded.
    /// </summary>
    [Id(0)]
    public long TotalActivities { get; set; } = 0;

    /// <summary>
    /// Current number of active connections.
    /// </summary>
    [Id(1)]
    public int ActiveConnections { get; set; } = 0;

    /// <summary>
    /// Total messages relayed through this grain.
    /// </summary>
    [Id(2)]
    public long MessagesRelayed { get; set; } = 0;

    /// <summary>
    /// Total number of grain activations.
    /// </summary>
    [Id(3)]
    public int ActivationCount { get; set; } = 0;

    /// <summary>
    /// Last reset timestamp for metrics.
    /// </summary>
    [Id(4)]
    public DateTime LastReset { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of background operations started.
    /// </summary>
    [Id(5)]
    public long TotalOperationsStarted { get; set; } = 0;

    /// <summary>
    /// Total number of operations completed successfully.
    /// </summary>
    [Id(6)]
    public long TotalOperationsCompleted { get; set; } = 0;

    /// <summary>
    /// Total number of operations that failed.
    /// </summary>
    [Id(7)]
    public long TotalOperationsFailed { get; set; } = 0;

    /// <summary>
    /// Total number of operations that were cancelled.
    /// </summary>
    [Id(8)]
    public long TotalOperationsCancelled { get; set; } = 0;

    /// <summary>
    /// Total operation processing time in milliseconds.
    /// </summary>
    [Id(9)]
    public long TotalOperationDurationMs { get; set; } = 0;

    /// <summary>
    /// Current number of active operations.
    /// </summary>
    [Id(10)]
    public int ActiveOperationsCount { get; set; } = 0;

    /// <summary>
    /// Total number of messages buffered across all chats.
    /// </summary>
    [Id(11)]
    public long TotalMessagesBuffered { get; set; } = 0;

    /// <summary>
    /// Total number of buffered messages that expired and were removed.
    /// </summary>
    [Id(12)]
    public long TotalBufferExpiredMessages { get; set; } = 0;

    /// <summary>
    /// Total number of messages dropped due to buffer overflow.
    /// </summary>
    [Id(13)]
    public long TotalBufferOverflowDrops { get; set; } = 0;

    /// <summary>
    /// Current number of message buffers.
    /// </summary>
    [Id(14)]
    public int CurrentBufferCount { get; set; } = 0;

    /// <summary>
    /// Total number of buffer cleanup operations performed.
    /// </summary>
    [Id(15)]
    public long TotalBufferCleanupRuns { get; set; } = 0;

    /// <summary>
    /// Total number of buffered messages successfully delivered.
    /// </summary>
    [Id(16)]
    public long TotalBufferedMessagesDelivered { get; set; } = 0;
}

/// <summary>
/// Operation context for background processing (Phase 3).
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.OperationContext")]
public sealed class OperationContext
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>
    [Id(0)]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Chat where operation is occurring.
    /// </summary>
    [Id(1)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Type of operation being performed.
    /// </summary>
    [Id(2)]
    public OperationType Type { get; set; }

    /// <summary>
    /// Operation start timestamp.
    /// </summary>
    [Id(3)]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Operation completion timestamp.
    /// </summary>
    [Id(4)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Current operation status.
    /// </summary>
    [Id(5)]
    public OperationStatus Status { get; set; } = OperationStatus.Queued;

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    [Id(6)]
    public string? Error { get; set; }
}