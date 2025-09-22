using Orleans;

namespace AIChat.Orleans.Contracts;

#region Session State Models for UserGrain

/// <summary>
/// User session state for tracking session lifecycle within UserGrain.
/// Maps ConnectionStateTracker data to Orleans persistent state.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.UserSessionState")]
public sealed class UserSessionState
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    [Id(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Stream identifier that maps to ConnectionStateTracker.
    /// </summary>
    [Id(1)]
    public string StreamId { get; set; } = string.Empty;

    /// <summary>
    /// Current session lifecycle state.
    /// </summary>
    [Id(2)]
    public SessionLifecycleState CurrentState { get; set; } = SessionLifecycleState.Initialized;

    /// <summary>
    /// Session creation timestamp.
    /// </summary>
    [Id(3)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp for this session.
    /// </summary>
    [Id(4)]
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Connection establishment timestamp.
    /// </summary>
    [Id(5)]
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Disconnection timestamp.
    /// </summary>
    [Id(6)]
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Total number of reconnection attempts.
    /// </summary>
    [Id(7)]
    public int ReconnectionAttempts { get; set; }

    /// <summary>
    /// Number of successful reconnections.
    /// </summary>
    [Id(8)]
    public int SuccessfulReconnections { get; set; }

    /// <summary>
    /// Total time spent in connected state.
    /// </summary>
    [Id(9)]
    public TimeSpan TotalConnectionTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Total time spent in disconnected state.
    /// </summary>
    [Id(10)]
    public TimeSpan TotalDisconnectionTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Total time spent in reconnection attempts.
    /// </summary>
    [Id(11)]
    public TimeSpan TotalReconnectionTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Longest single connection duration.
    /// </summary>
    [Id(12)]
    public TimeSpan LongestConnectionDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Reason for last disconnection.
    /// </summary>
    [Id(13)]
    public string? LastDisconnectionReason { get; set; }

    /// <summary>
    /// Number of total disconnections.
    /// </summary>
    [Id(14)]
    public int DisconnectionCount { get; set; }

    /// <summary>
    /// Session metadata for extensibility.
    /// </summary>
    [Id(15)]
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Whether the session is archived.
    /// </summary>
    [Id(16)]
    public bool IsArchived { get; set; }

    /// <summary>
    /// Archive timestamp if archived.
    /// </summary>
    [Id(17)]
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// Archive reason if archived.
    /// </summary>
    [Id(18)]
    public string? ArchiveReason { get; set; }
}

/// <summary>
/// Session metrics for monitoring and analytics.
/// Complements UserSessionState with detailed performance data.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.UserSessionMetrics")]
public sealed class UserSessionMetrics
{
    /// <summary>
    /// Session identifier this metrics data belongs to.
    /// </summary>
    [Id(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Individual reconnection times for analysis.
    /// </summary>
    [Id(1)]
    public List<TimeSpan> ReconnectionTimes { get; set; } = [];

    /// <summary>
    /// Recent failure count for health assessment.
    /// </summary>
    [Id(2)]
    public int RecentFailureCount { get; set; }

    /// <summary>
    /// Connection start time for current session.
    /// </summary>
    [Id(3)]
    public DateTime ConnectionStartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Disconnection start time for tracking.
    /// </summary>
    [Id(4)]
    public DateTime? DisconnectionStartTime { get; set; }

    /// <summary>
    /// Uptime percentage for this session.
    /// </summary>
    [Id(5)]
    public double UptimePercentage { get; set; } = 100.0;

    /// <summary>
    /// Last metrics calculation timestamp.
    /// </summary>
    [Id(6)]
    public DateTime LastCalculatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Custom metrics for extensibility.
    /// </summary>
    [Id(7)]
    public Dictionary<string, double> CustomMetrics { get; set; } = [];
}

#endregion