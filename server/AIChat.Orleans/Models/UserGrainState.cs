using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Persistent state for the UserGrain.
/// Contains all user-specific data that needs to survive grain deactivation/reactivation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.UserGrainState")]
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
    public Dictionary<string, ConnectionInfo> Connections { get; set; } = [];

    /// <summary>
    /// Active chat subscriptions for this user.
    /// Key: ChatId, Value: Subscription information.
    /// </summary>
    [Id(2)]
    public Dictionary<string, ChatSubscription> ActiveChats { get; set; } = [];

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
    public Dictionary<string, OperationContext> ActiveOperations { get; set; } = [];

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
    public Dictionary<string, ChatMessageBuffer> MessageBuffers { get; set; } = [];

    /// <summary>
    /// Active streaming operations (Phase 4).
    /// Key: StreamId, Value: Stream state.
    /// </summary>
    [Id(9)]
    public Dictionary<string, StreamState> ActiveStreams { get; set; } = [];

    /// <summary>
    /// Total number of streams processed.
    /// </summary>
    [Id(10)]
    public long TotalStreamsProcessed { get; set; } = 0;

    /// <summary>
    /// User session states managed by this grain (Phase 2 - ORL-ST-P2-003).
    /// Key: SessionId, Value: Session state data.
    /// Migrated from ConnectionStateTracker to Orleans persistent state.
    /// </summary>
    [Id(11)]
    public Dictionary<string, UserSessionState> Sessions { get; set; } = [];

    /// <summary>
    /// Session metrics data for monitoring and analytics (Phase 2 - ORL-ST-P2-003).
    /// Key: SessionId, Value: Session metrics data.
    /// Complements session state with detailed performance tracking.
    /// </summary>
    [Id(12)]
    public Dictionary<string, UserSessionMetrics> SessionMetrics { get; set; } = [];

    /// <summary>
    /// User preferences stored in Orleans persistent state (Phase 2 - ORL-ST-P2-004).
    /// Migrated from client-side storage to provide cross-session persistence.
    /// Includes message expansion preferences, mode selection, and UI settings.
    /// </summary>
    [Id(13)]
    public UserPreferencesState Preferences { get; set; } = new();

    /// <summary>
    /// User preferences metadata and audit information (Phase 2 - ORL-ST-P2-004).
    /// Tracks preference changes, synchronization status, and version control.
    /// Used for cache invalidation and conflict resolution.
    /// </summary>
    [Id(14)]
    public UserPreferencesMetadata PreferencesMetadata { get; set; } = new();

    /// <summary>
    /// Enhanced activity tracking state with analytics integration and privacy compliance (Phase 2 - ORL-ST-P2-005).
    /// Extends existing activity tracking with production-ready features including PII detection,
    /// anonymization, data retention policies, and seamless integration with telemetry systems.
    /// Maintains backward compatibility while adding comprehensive privacy and analytics capabilities.
    /// </summary>
    [Id(15)]
    public ActivityTrackingState ActivityTracking { get; set; } = new();

    /// <summary>
    /// Privacy and compliance metadata for activity tracking (Phase 2 - ORL-ST-P2-005).
    /// Tracks user consent, retention policies, privacy audit information, and GDPR compliance.
    /// Provides comprehensive privacy protection and regulatory compliance for activity data.
    /// Supports data export, deletion, and consent management for user privacy rights.
    /// </summary>
    [Id(16)]
    public ActivityPrivacyMetadata ActivityPrivacy { get; set; } = new();
}

/// <summary>
/// Information about a SignalR connection.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConnectionInfo")]
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
    public HashSet<string> SubscribedChatIds { get; set; } = [];

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
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatSubscription")]
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
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityRecord")]
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
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.GrainMetrics")]
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

    /// <summary>
    /// Total number of streams processed.
    /// </summary>
    [Id(17)]
    public long TotalStreamsProcessed { get; set; } = 0;
}

/// <summary>
/// Operation context for background processing (Phase 3).
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.OperationContext")]
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

/// <summary>
/// User preferences state for persistent storage in Orleans grains (Phase 2 - ORL-ST-P2-004).
/// Contains all user-specific preferences migrated from client-side storage.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.UserPreferencesState")]
public sealed class UserPreferencesState
{
    /// <summary>
    /// Message expansion preferences by message ID.
    /// Preserves user's expand/collapse choices across sessions.
    /// </summary>
    [Id(0)]
    public Dictionary<string, MessagePreference> MessagePreferences { get; set; } = [];

    /// <summary>
    /// Currently selected mode ID for this user.
    /// Maintains mode selection across browser sessions.
    /// </summary>
    [Id(1)]
    public string? SelectedModeId { get; set; }

    /// <summary>
    /// UI-level preferences (theme, layout, etc.).
    /// Extensible dictionary for future UI preferences without schema changes.
    /// </summary>
    [Id(2)]
    public Dictionary<string, object> UIPreferences { get; set; } = [];

    /// <summary>
    /// Timestamp of last preferences update.
    /// Used for cache invalidation and conflict resolution.
    /// </summary>
    [Id(3)]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// Incremented on each update to detect conflicts.
    /// </summary>
    [Id(4)]
    public long Version { get; set; } = 1;
}

/// <summary>
/// Individual message expansion preference data.
/// Stores user's expand/collapse choice for specific messages.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.MessagePreference")]
public sealed class MessagePreference
{
    /// <summary>
    /// Message identifier this preference applies to.
    /// </summary>
    [Id(0)]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// Whether user prefers this message to be expanded.
    /// </summary>
    [Id(1)]
    public bool IsExpanded { get; set; }

    /// <summary>
    /// When this preference was last modified.
    /// Used for cleanup of old preferences.
    /// </summary>
    [Id(2)]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current render phase of the message.
    /// Maps to client-side MessageState.renderPhase values.
    /// </summary>
    [Id(3)]
    public string RenderPhase { get; set; } = "initial";
}

/// <summary>
/// Metadata and audit information for user preferences (Phase 2 - ORL-ST-P2-004).
/// Tracks preference changes, synchronization status, and version control.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.UserPreferencesMetadata")]
public sealed class UserPreferencesMetadata
{
    /// <summary>
    /// When preferences were first created for this user.
    /// </summary>
    [Id(0)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last successful synchronization with client.
    /// Used to detect stale client data.
    /// </summary>
    [Id(1)]
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of preference updates made.
    /// Used for auditing and analytics.
    /// </summary>
    [Id(2)]
    public int TotalPreferenceUpdates { get; set; } = 0;

    /// <summary>
    /// Source of the last synchronization (client, api, migration, etc.).
    /// Helps track where preference changes originate.
    /// </summary>
    [Id(3)]
    public string LastSyncSource { get; set; } = "system";

    /// <summary>
    /// Additional metadata for extensibility.
    /// Allows storing custom metadata without schema changes.
    /// </summary>
    [Id(4)]
    public Dictionary<string, object> Metadata { get; set; } = [];
}
