using System.Collections.Concurrent;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Persistent state for the UserGrain.
/// Contains all user-specific data that needs to survive grain deactivation/reactivation.
/// </summary>
[Serializable]
public sealed class UserGrainState
{
    /// <summary>
    /// User identifier for this grain.
    /// </summary>

    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Active SignalR connections for this user.
    /// Key: ConnectionId, Value: Connection information.
    /// </summary>

    public Dictionary<string, ConnectionInfo> Connections { get; set; } = new();

    /// <summary>
    /// Active chat subscriptions for this user.
    /// Key: ChatId, Value: Subscription information.
    /// </summary>

    public Dictionary<string, ChatSubscription> ActiveChats { get; set; } = new();

    /// <summary>
    /// Recent user activity (circular buffer, max 100 items).
    /// Used for monitoring and debugging.
    /// </summary>

    public Queue<ActivityRecord> RecentActivity { get; set; } = new();

    /// <summary>
    /// Timestamp of last user activity.
    /// </summary>

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Performance and usage metrics for this grain.
    /// </summary>

    public GrainMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Currently active operations (Phase 3).
    /// Key: OperationId, Value: Operation context.
    /// </summary>

    public Dictionary<string, OperationContext> ActiveOperations { get; set; } = new();

    /// <summary>
    /// Grain activation timestamp for debugging.
    /// </summary>

    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a SignalR connection.
/// </summary>
[Serializable]
public sealed class ConnectionInfo
{
    /// <summary>
    /// SignalR connection identifier.
    /// </summary>

    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Client identifier (browser tab/instance).
    /// </summary>

    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Connection establishment timestamp.
    /// </summary>

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Chat rooms this connection is subscribed to.
    /// </summary>

    public HashSet<string> SubscribedChatIds { get; set; } = new();

    /// <summary>
    /// Last activity timestamp for this connection.
    /// </summary>

    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Chat subscription information.
/// </summary>
[Serializable]
public sealed class ChatSubscription
{
    /// <summary>
    /// Chat room identifier.
    /// </summary>

    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Subscription timestamp.
    /// </summary>

    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current subscription state.
    /// </summary>

    public SubscriptionState State { get; set; } = SubscriptionState.Active;

    /// <summary>
    /// Number of connections subscribed to this chat.
    /// </summary>

    public int ConnectionCount { get; set; } = 0;
}

/// <summary>
/// User activity record for monitoring.
/// </summary>
[Serializable]
public sealed class ActivityRecord
{
    /// <summary>
    /// Type of activity performed.
    /// </summary>

    public ActivityType Type { get; set; }

    /// <summary>
    /// JSON metadata about the activity.
    /// </summary>

    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Activity timestamp.
    /// </summary>

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Unique identifier for correlation.
    /// </summary>

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Grain performance metrics.
/// </summary>
[Serializable]
public sealed class GrainMetrics
{
    /// <summary>
    /// Total number of activities recorded.
    /// </summary>

    public long TotalActivities { get; set; } = 0;

    /// <summary>
    /// Current number of active connections.
    /// </summary>

    public int ActiveConnections { get; set; } = 0;

    /// <summary>
    /// Total messages relayed through this grain.
    /// </summary>

    public long MessagesRelayed { get; set; } = 0;

    /// <summary>
    /// Total number of grain activations.
    /// </summary>

    public int ActivationCount { get; set; } = 0;

    /// <summary>
    /// Last reset timestamp for metrics.
    /// </summary>

    public DateTime LastReset { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Operation context for background processing (Phase 3).
/// </summary>
[Serializable]
public sealed class OperationContext
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>

    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Chat where operation is occurring.
    /// </summary>

    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Type of operation being performed.
    /// </summary>

    public OperationType Type { get; set; }

    /// <summary>
    /// Operation start timestamp.
    /// </summary>

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Operation completion timestamp.
    /// </summary>

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Current operation status.
    /// </summary>

    public OperationStatus Status { get; set; } = OperationStatus.Queued;

    /// <summary>
    /// Error message if operation failed.
    /// </summary>

    public string? Error { get; set; }
}