using System.Globalization;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Service for broadcasting real-time recovery progress notifications via SignalR.
/// Provides integration with existing SignalR infrastructure for recovery status updates.
/// </summary>
public interface IRecoveryNotificationService
{
    /// <summary>
    /// Notifies a specific connection about recovery operation start.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="request">The recovery request details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the notification operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionId, operationId, or request is null</exception>
    Task NotifyRecoveryStartedAsync(
        string connectionId,
        string operationId,
        PointInTimeRecoveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies a specific connection about recovery operation progress.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="progressPercentage">Progress percentage (0-100)</param>
    /// <param name="currentStatus">Current operation status</param>
    /// <param name="details">Optional progress details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the notification operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionId or operationId is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when progressPercentage is not between 0-100</exception>
    Task NotifyRecoveryProgressAsync(
        string connectionId,
        string operationId,
        int progressPercentage,
        string currentStatus,
        RecoveryProgressDetails? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies a specific connection about recovery operation completion.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="status">Final recovery status</param>
    /// <param name="result">Recovery result details (if successful)</param>
    /// <param name="errorInfo">Error information (if failed)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the notification operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionId or operationId is null</exception>
    Task NotifyRecoveryCompletedAsync(
        string connectionId,
        string operationId,
        RecoveryStatus status,
        RecoveryCompletionResult? result = null,
        RecoveryErrorInfo? errorInfo = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts recovery notification to all connections in a group.
    /// Useful for broadcasting to multiple users monitoring the same grain.
    /// </summary>
    /// <param name="groupName">The SignalR group name</param>
    /// <param name="notification">The recovery notification to broadcast</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the broadcast operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when groupName or notification is null</exception>
    Task BroadcastRecoveryToGroupAsync(
        string groupName,
        RecoveryNotification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a connection to a recovery monitoring group.
    /// This allows the connection to receive broadcasts for specific grains or operations.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="groupName">The group name to join</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the join operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionId or groupName is null</exception>
    Task JoinRecoveryGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a connection from a recovery monitoring group.
    /// </summary>
    /// <param name="connectionId">The SignalR connection ID</param>
    /// <param name="groupName">The group name to leave</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the leave operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when connectionId or groupName is null</exception>
    Task LeaveRecoveryGroupAsync(
        string connectionId,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of the recovery notification service.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    Task<RecoveryNotificationHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics about recovery notifications sent.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Notification metrics</returns>
    Task<RecoveryNotificationMetrics> GetNotificationMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Detailed progress information for recovery operations.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryProgressDetails")]
public record RecoveryProgressDetails
{
    /// <summary>
    /// Current step being performed.
    /// </summary>
    [Id(0)]
    public string? CurrentStep { get; init; }

    /// <summary>
    /// Number of events processed so far.
    /// </summary>
    [Id(1)]
    public int EventsProcessed { get; init; }

    /// <summary>
    /// Total number of events to process.
    /// </summary>
    [Id(2)]
    public int TotalEvents { get; init; }

    /// <summary>
    /// Time elapsed since recovery started.
    /// </summary>
    [Id(3)]
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Estimated time remaining.
    /// </summary>
    [Id(4)]
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Any warnings encountered during progress.
    /// </summary>
    [Id(5)]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Additional progress information.
    /// </summary>
    [Id(6)]
    public Dictionary<string, object>? AdditionalInfo { get; init; }
}

/// <summary>
/// Recovery completion result information for notifications.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryCompletionResult")]
public record RecoveryCompletionResult
{
    /// <summary>
    /// The actual timestamp that was recovered to.
    /// </summary>
    [Id(0)]
    public DateTimeOffset? ActualTimestamp { get; init; }

    /// <summary>
    /// The actual version that was recovered to.
    /// </summary>
    [Id(1)]
    public long? ActualVersion { get; init; }

    /// <summary>
    /// The recovery strategy that was used.
    /// </summary>
    [Id(2)]
    public RecoveryStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Number of events that were replayed.
    /// </summary>
    [Id(3)]
    public int EventsReplayed { get; init; }

    /// <summary>
    /// Total time taken for the recovery.
    /// </summary>
    [Id(4)]
    public TimeSpan RecoveryDuration { get; init; }

    /// <summary>
    /// Whether a snapshot was used.
    /// </summary>
    [Id(5)]
    public bool SnapshotUsed { get; init; }

    /// <summary>
    /// Whether state validation passed.
    /// </summary>
    [Id(6)]
    public bool ValidationPassed { get; init; }

    /// <summary>
    /// Any warnings from the recovery.
    /// </summary>
    [Id(7)]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Summary message about the recovery.
    /// </summary>
    [Id(8)]
    public string? Summary { get; init; }
}

/// <summary>
/// Recovery error information for notifications.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryErrorInfo")]
public record RecoveryErrorInfo
{
    /// <summary>
    /// The error message.
    /// </summary>
    [Id(0)]
    public required string Message { get; init; }

    /// <summary>
    /// The type of error that occurred.
    /// </summary>
    [Id(1)]
    public string? ErrorType { get; init; }

    /// <summary>
    /// Error code for categorization.
    /// </summary>
    [Id(2)]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Whether the operation can be retried.
    /// </summary>
    [Id(3)]
    public bool IsRetriable { get; init; }

    /// <summary>
    /// Suggested actions for resolving the error.
    /// </summary>
    [Id(4)]
    public IReadOnlyList<string> SuggestedActions { get; init; } = [];

    /// <summary>
    /// Additional error details.
    /// </summary>
    [Id(5)]
    public Dictionary<string, object>? Details { get; init; }
}

/// <summary>
/// Base class for recovery notifications.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryNotification")]
public record RecoveryNotification
{
    /// <summary>
    /// The recovery operation ID.
    /// </summary>
    [Id(0)]
    public required string OperationId { get; init; }

    /// <summary>
    /// The type of notification.
    /// </summary>
    [Id(1)]
    public required RecoveryNotificationType Type { get; init; }

    /// <summary>
    /// The grain ID being recovered.
    /// </summary>
    [Id(2)]
    public required string GrainId { get; init; }

    /// <summary>
    /// Timestamp when the notification was created.
    /// </summary>
    [Id(3)]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The correlation ID for tracking.
    /// </summary>
    [Id(4)]
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Additional notification data.
    /// </summary>
    [Id(5)]
    public Dictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Creates a started notification.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="grainId">The grain ID</param>
    /// <param name="request">The recovery request</param>
    /// <returns>A started notification</returns>
    public static RecoveryNotification Started(
        string operationId,
        string grainId,
        PointInTimeRecoveryRequest request)
    {
        return new RecoveryNotification
        {
            OperationId = operationId,
            Type = RecoveryNotificationType.Started,
            GrainId = grainId,
            CorrelationId = request.CorrelationId,
            Data = new Dictionary<string, object>
            {
                ["targetTimestamp"] = request.TargetTimestamp?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Not specified",
                ["targetVersion"] = request.TargetVersion?.ToString(CultureInfo.InvariantCulture) ?? "Not specified",
                ["initiatedBy"] = request.InitiatedBy ?? "Unknown",
                ["reason"] = request.Reason ?? "Not specified"
            }
        };
    }

    /// <summary>
    /// Creates a progress notification.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="grainId">The grain ID</param>
    /// <param name="progressPercentage">Progress percentage</param>
    /// <param name="currentStatus">Current status</param>
    /// <param name="details">Progress details</param>
    /// <returns>A progress notification</returns>
    public static RecoveryNotification Progress(
        string operationId,
        string grainId,
        int progressPercentage,
        string currentStatus,
        RecoveryProgressDetails? details = null)
    {
        var data = new Dictionary<string, object>
        {
            ["progressPercentage"] = progressPercentage,
            ["currentStatus"] = currentStatus
        };

        if (details != null)
        {
            data["currentStep"] = details.CurrentStep ?? "";
            data["eventsProcessed"] = details.EventsProcessed;
            data["totalEvents"] = details.TotalEvents;
            data["elapsedTime"] = details.ElapsedTime;
            data["estimatedTimeRemaining"] = details.EstimatedTimeRemaining?.ToString() ?? "Unknown";
            data["warnings"] = details.Warnings;
        }

        return new RecoveryNotification
        {
            OperationId = operationId,
            Type = RecoveryNotificationType.Progress,
            GrainId = grainId,
            Data = data
        };
    }

    /// <summary>
    /// Creates a completed notification.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="grainId">The grain ID</param>
    /// <param name="status">Final status</param>
    /// <param name="result">Result details</param>
    /// <param name="errorInfo">Error details</param>
    /// <returns>A completed notification</returns>
    public static RecoveryNotification Completed(
        string operationId,
        string grainId,
        RecoveryStatus status,
        RecoveryCompletionResult? result = null,
        RecoveryErrorInfo? errorInfo = null)
    {
        var data = new Dictionary<string, object>
        {
            ["status"] = status.ToString()
        };

        if (result != null)
        {
            data["actualTimestamp"] = result.ActualTimestamp?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Not specified";
            data["actualVersion"] = result.ActualVersion?.ToString(CultureInfo.InvariantCulture) ?? "Not specified";
            data["strategyUsed"] = result.StrategyUsed.ToString();
            data["eventsReplayed"] = result.EventsReplayed;
            data["recoveryDuration"] = result.RecoveryDuration;
            data["snapshotUsed"] = result.SnapshotUsed;
            data["validationPassed"] = result.ValidationPassed;
            data["warnings"] = result.Warnings;
            data["summary"] = result.Summary ?? "";
        }

        if (errorInfo != null)
        {
            data["error"] = new Dictionary<string, object>
            {
                ["message"] = errorInfo.Message,
                ["errorType"] = errorInfo.ErrorType ?? "",
                ["errorCode"] = errorInfo.ErrorCode ?? "",
                ["isRetriable"] = errorInfo.IsRetriable,
                ["suggestedActions"] = errorInfo.SuggestedActions
            };
        }

        return new RecoveryNotification
        {
            OperationId = operationId,
            Type = RecoveryNotificationType.Completed,
            GrainId = grainId,
            Data = data
        };
    }
}

/// <summary>
/// Types of recovery notifications.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryNotificationType")]
public enum RecoveryNotificationType
{
    /// <summary>
    /// Recovery operation started.
    /// </summary>
    [Id(0)]
    Started = 0,

    /// <summary>
    /// Recovery operation progress update.
    /// </summary>
    [Id(1)]
    Progress = 1,

    /// <summary>
    /// Recovery operation completed (success or failure).
    /// </summary>
    [Id(2)]
    Completed = 2,

    /// <summary>
    /// Recovery operation cancelled.
    /// </summary>
    [Id(3)]
    Cancelled = 3
}

/// <summary>
/// Health status for the recovery notification service.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryNotificationHealthStatus")]
public record RecoveryNotificationHealthStatus
{
    /// <summary>
    /// Whether the notification service is healthy.
    /// </summary>
    [Id(0)]
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    [Id(1)]
    public required string ServiceName { get; init; }

    /// <summary>
    /// When the health check was performed.
    /// </summary>
    [Id(2)]
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether SignalR hub is available.
    /// </summary>
    [Id(3)]
    public bool SignalRAvailable { get; init; }

    /// <summary>
    /// Number of active connections.
    /// </summary>
    [Id(4)]
    public int ActiveConnections { get; init; }

    /// <summary>
    /// Number of recovery groups currently active.
    /// </summary>
    [Id(5)]
    public int ActiveGroups { get; init; }

    /// <summary>
    /// Any health issues.
    /// </summary>
    [Id(6)]
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Creates a healthy status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="activeConnections">Number of active connections</param>
    /// <param name="activeGroups">Number of active groups</param>
    /// <returns>A healthy status</returns>
    public static RecoveryNotificationHealthStatus Healthy(
        string serviceName,
        int activeConnections = 0,
        int activeGroups = 0)
    {
        return new RecoveryNotificationHealthStatus
        {
            IsHealthy = true,
            ServiceName = serviceName,
            SignalRAvailable = true,
            ActiveConnections = activeConnections,
            ActiveGroups = activeGroups
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="issues">Health issues</param>
    /// <param name="signalRAvailable">Whether SignalR is available</param>
    /// <returns>An unhealthy status</returns>
    public static RecoveryNotificationHealthStatus Unhealthy(
        string serviceName,
        IReadOnlyList<string> issues,
        bool signalRAvailable = false)
    {
        return new RecoveryNotificationHealthStatus
        {
            IsHealthy = false,
            ServiceName = serviceName,
            SignalRAvailable = signalRAvailable,
            Issues = issues
        };
    }
}

/// <summary>
/// Metrics for recovery notifications.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryNotificationMetrics")]
public record RecoveryNotificationMetrics
{
    /// <summary>
    /// Total notifications sent.
    /// </summary>
    [Id(0)]
    public long TotalNotificationsSent { get; init; }

    /// <summary>
    /// Notifications sent in the last hour.
    /// </summary>
    [Id(1)]
    public long RecentNotificationsSent { get; init; }

    /// <summary>
    /// Average notification delivery time.
    /// </summary>
    [Id(2)]
    public TimeSpan AverageDeliveryTime { get; init; }

    /// <summary>
    /// Number of failed notification deliveries.
    /// </summary>
    [Id(3)]
    public long FailedDeliveries { get; init; }

    /// <summary>
    /// Distribution of notification types.
    /// </summary>
    [Id(4)]
    public Dictionary<RecoveryNotificationType, long> NotificationTypeDistribution { get; init; } = [];

    /// <summary>
    /// Current active connections.
    /// </summary>
    [Id(5)]
    public int CurrentActiveConnections { get; init; }

    /// <summary>
    /// Peak concurrent connections in the last 24 hours.
    /// </summary>
    [Id(6)]
    public int PeakConnections { get; init; }

    /// <summary>
    /// When the metrics were collected.
    /// </summary>
    [Id(7)]
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty metrics.
    /// </summary>
    /// <returns>Empty notification metrics</returns>
    public static RecoveryNotificationMetrics Empty()
    {
        return new RecoveryNotificationMetrics
        {
            TotalNotificationsSent = 0,
            RecentNotificationsSent = 0,
            AverageDeliveryTime = TimeSpan.Zero,
            FailedDeliveries = 0,
            CurrentActiveConnections = 0,
            PeakConnections = 0
        };
    }
}