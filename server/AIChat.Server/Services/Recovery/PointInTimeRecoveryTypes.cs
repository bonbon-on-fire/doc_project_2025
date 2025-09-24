using Orleans;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Represents a request for point-in-time recovery operation.
/// Extends the existing recovery infrastructure with time-based recovery capabilities.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.PointInTimeRecoveryRequest")]
public record PointInTimeRecoveryRequest
{
    /// <summary>
    /// The unique identifier of the grain requiring recovery.
    /// </summary>
    [Id(0)]
    public required string GrainId { get; set; }

    /// <summary>
    /// The type of grain being recovered.
    /// </summary>
    [Id(1)]
    public required string GrainType { get; set; }

    /// <summary>
    /// The target timestamp to recover to.
    /// If null, uses TargetVersion instead.
    /// </summary>
    [Id(2)]
    public DateTimeOffset? TargetTimestamp { get; set; }

    /// <summary>
    /// The target version to recover to.
    /// If null, uses TargetTimestamp instead.
    /// </summary>
    [Id(3)]
    public long? TargetVersion { get; set; }

    /// <summary>
    /// The reason for the recovery request.
    /// </summary>
    [Id(4)]
    public string? Reason { get; set; }

    /// <summary>
    /// The user or system that initiated the recovery.
    /// </summary>
    [Id(5)]
    public string? InitiatedBy { get; set; }

    /// <summary>
    /// Additional metadata for the recovery request.
    /// </summary>
    [Id(6)]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// The level of validation to perform on recovered state.
    /// </summary>
    [Id(7)]
    public RecoveryValidationLevel ValidationLevel { get; set; } = RecoveryValidationLevel.Standard;

    /// <summary>
    /// Whether to force recovery even if current state appears valid.
    /// </summary>
    [Id(8)]
    public bool ForceRecovery { get; set; }

    /// <summary>
    /// The preferred recovery strategy to use.
    /// </summary>
    [Id(9)]
    public RecoveryStrategy? PreferredStrategy { get; set; }

    /// <summary>
    /// Correlation ID for tracking recovery across system components.
    /// </summary>
    [Id(10)]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the recovery request was created.
    /// </summary>
    [Id(11)]
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether to perform recovery in background with progress notifications.
    /// </summary>
    [Id(12)]
    public bool UseBackgroundRecovery { get; set; } = false;

    /// <summary>
    /// Creates a point-in-time recovery request for a specific timestamp.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="targetTimestamp">The target timestamp to recover to</param>
    /// <param name="initiatedBy">Who initiated the recovery</param>
    /// <param name="reason">Reason for recovery</param>
    /// <returns>A point-in-time recovery request</returns>
    public static PointInTimeRecoveryRequest ForTimestamp(
        string grainId,
        string grainType,
        DateTimeOffset targetTimestamp,
        string? initiatedBy = null,
        string? reason = null)
    {
        return new PointInTimeRecoveryRequest
        {
            GrainId = grainId,
            GrainType = grainType,
            TargetTimestamp = targetTimestamp,
            InitiatedBy = initiatedBy,
            Reason = reason,
            PreferredStrategy = RecoveryStrategy.SnapshotFirst
        };
    }

    /// <summary>
    /// Creates a point-in-time recovery request for a specific version.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="targetVersion">The target version to recover to</param>
    /// <param name="initiatedBy">Who initiated the recovery</param>
    /// <param name="reason">Reason for recovery</param>
    /// <returns>A point-in-time recovery request</returns>
    public static PointInTimeRecoveryRequest ForVersion(
        string grainId,
        string grainType,
        long targetVersion,
        string? initiatedBy = null,
        string? reason = null)
    {
        return new PointInTimeRecoveryRequest
        {
            GrainId = grainId,
            GrainType = grainType,
            TargetVersion = targetVersion,
            InitiatedBy = initiatedBy,
            Reason = reason,
            PreferredStrategy = RecoveryStrategy.SnapshotFirst
        };
    }

    /// <summary>
    /// Converts this point-in-time recovery request to a standard state recovery request.
    /// </summary>
    /// <returns>A state recovery request for use with existing infrastructure</returns>
    public StateRecoveryRequest ToStateRecoveryRequest()
    {
        return new StateRecoveryRequest
        {
            GrainId = GrainId,
            GrainType = GrainType,
            TargetVersion = TargetVersion,
            PreferredStrategy = PreferredStrategy,
            RecoveryNeed = ForceRecovery ? StateRecoveryNeed.ForceRecovery : StateRecoveryNeed.MissingState,
            ForceRecovery = ForceRecovery,
            Metadata = Metadata,
            CorrelationId = CorrelationId
        };
    }
}

/// <summary>
/// Validation levels for point-in-time recovery operations.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryValidationLevel")]
public enum RecoveryValidationLevel
{
    /// <summary>
    /// Minimal validation - basic structure checks only.
    /// </summary>
    [Id(0)]
    Minimal,

    /// <summary>
    /// Standard validation - structure + business rules.
    /// </summary>
    [Id(1)]
    Standard,

    /// <summary>
    /// Full validation - all checks including cross-grain consistency.
    /// </summary>
    [Id(2)]
    Full,

    /// <summary>
    /// Exhaustive validation - all checks + deep integrity verification.
    /// </summary>
    [Id(3)]
    Exhaustive
}

/// <summary>
/// Represents the result of a point-in-time recovery operation.
/// Extends StateRecoveryResult with time-specific information.
/// </summary>
/// <typeparam name="T">The type of state that was recovered</typeparam>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.PointInTimeRecoveryResult`1")]
public record PointInTimeRecoveryResult<T>
{
    /// <summary>
    /// The unique identifier for this recovery operation.
    /// </summary>
    [Id(0)]
    public required string OperationId { get; set; }

    /// <summary>
    /// The status of the recovery operation.
    /// </summary>
    [Id(1)]
    public required RecoveryStatus Status { get; set; }

    /// <summary>
    /// The recovered state, if successful.
    /// </summary>
    [Id(2)]
    public T? RecoveredState { get; set; }

    /// <summary>
    /// The actual timestamp that was recovered to.
    /// </summary>
    [Id(3)]
    public DateTimeOffset? ActualTimestamp { get; set; }

    /// <summary>
    /// The actual version that was recovered to.
    /// </summary>
    [Id(4)]
    public long? ActualVersion { get; set; }

    /// <summary>
    /// The recovery strategy that was used.
    /// </summary>
    [Id(5)]
    public RecoveryStrategy StrategyUsed { get; set; }

    /// <summary>
    /// The ID of the snapshot that was used, if any.
    /// </summary>
    [Id(6)]
    public string? SnapshotId { get; set; }

    /// <summary>
    /// The number of events that were replayed during recovery.
    /// </summary>
    [Id(7)]
    public int EventsReplayed { get; set; }

    /// <summary>
    /// The total time taken for the recovery operation.
    /// </summary>
    [Id(8)]
    public TimeSpan RecoveryDuration { get; set; }

    /// <summary>
    /// The result of state consistency validation.
    /// </summary>
    [Id(9)]
    public ConsistencyVerificationResult? ValidationResult { get; set; }

    /// <summary>
    /// Warning messages from the recovery operation.
    /// </summary>
    [Id(10)]
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Any error that occurred during recovery.
    /// </summary>
    [Id(11)]
    public Exception? Error { get; set; }

    /// <summary>
    /// Additional metadata about the recovery operation.
    /// </summary>
    [Id(12)]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// The correlation ID from the original recovery request.
    /// </summary>
    [Id(13)]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the recovery was completed.
    /// </summary>
    [Id(14)]
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful point-in-time recovery result.
    /// </summary>
    /// <param name="operationId">The operation identifier</param>
    /// <param name="recoveredState">The recovered state</param>
    /// <param name="actualTimestamp">The actual timestamp recovered to</param>
    /// <param name="actualVersion">The actual version recovered to</param>
    /// <param name="strategyUsed">The strategy that was used</param>
    /// <param name="eventsReplayed">Number of events replayed</param>
    /// <param name="recoveryDuration">Time taken for recovery</param>
    /// <param name="snapshotId">ID of snapshot used, if any</param>
    /// <param name="validationResult">Validation result</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <returns>A successful recovery result</returns>
    public static PointInTimeRecoveryResult<T> CreateSuccess(
        string operationId,
        T recoveredState,
        DateTimeOffset? actualTimestamp,
        long? actualVersion,
        RecoveryStrategy strategyUsed,
        int eventsReplayed,
        TimeSpan recoveryDuration,
        string? snapshotId = null,
        ConsistencyVerificationResult? validationResult = null,
        string correlationId = "")
    {
        return new PointInTimeRecoveryResult<T>
        {
            OperationId = operationId,
            Status = RecoveryStatus.Completed,
            RecoveredState = recoveredState,
            ActualTimestamp = actualTimestamp,
            ActualVersion = actualVersion,
            StrategyUsed = strategyUsed,
            EventsReplayed = eventsReplayed,
            RecoveryDuration = recoveryDuration,
            SnapshotId = snapshotId,
            ValidationResult = validationResult,
            CorrelationId = correlationId
        };
    }

    /// <summary>
    /// Creates a failed point-in-time recovery result.
    /// </summary>
    /// <param name="operationId">The operation identifier</param>
    /// <param name="error">The error that occurred</param>
    /// <param name="strategyUsed">The strategy that was attempted</param>
    /// <param name="recoveryDuration">Time taken before failure</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <returns>A failed recovery result</returns>
    public static PointInTimeRecoveryResult<T> CreateFailure(
        string operationId,
        Exception error,
        RecoveryStrategy strategyUsed = RecoveryStrategy.HybridRecovery,
        TimeSpan recoveryDuration = default,
        string correlationId = "")
    {
        return new PointInTimeRecoveryResult<T>
        {
            OperationId = operationId,
            Status = RecoveryStatus.Failed,
            Error = error,
            StrategyUsed = strategyUsed,
            RecoveryDuration = recoveryDuration,
            CorrelationId = correlationId
        };
    }
}

/// <summary>
/// Status of a recovery operation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryStatus")]
public enum RecoveryStatus
{
    /// <summary>
    /// Recovery operation is pending start.
    /// </summary>
    [Id(0)]
    Pending,

    /// <summary>
    /// Recovery operation is in progress.
    /// </summary>
    [Id(1)]
    InProgress,

    /// <summary>
    /// Recovery operation completed successfully.
    /// </summary>
    [Id(2)]
    Completed,

    /// <summary>
    /// Recovery operation failed.
    /// </summary>
    [Id(3)]
    Failed,

    /// <summary>
    /// Recovery operation partially completed with warnings.
    /// </summary>
    [Id(4)]
    PartiallyCompleted,

    /// <summary>
    /// Recovery completed but validation failed.
    /// </summary>
    [Id(5)]
    ValidationFailed,

    /// <summary>
    /// Recovery operation was cancelled.
    /// </summary>
    [Id(6)]
    Cancelled
}

/// <summary>
/// Represents a point in time where recovery is possible.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryPoint")]
public record RecoveryPoint
{
    /// <summary>
    /// The timestamp of this recovery point.
    /// </summary>
    [Id(0)]
    public required DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The version number at this recovery point.
    /// </summary>
    [Id(1)]
    public required long Version { get; set; }

    /// <summary>
    /// The type of recovery point.
    /// </summary>
    [Id(2)]
    public required RecoveryPointType Type { get; set; }

    /// <summary>
    /// Human-readable description of this recovery point.
    /// </summary>
    [Id(3)]
    public string? Description { get; set; }

    /// <summary>
    /// The ID of the snapshot at this point, if available.
    /// </summary>
    [Id(4)]
    public string? SnapshotId { get; set; }

    /// <summary>
    /// Estimated time to recover to this point.
    /// </summary>
    [Id(5)]
    public TimeSpan EstimatedRecoveryTime { get; set; }

    /// <summary>
    /// Size of data that would need to be processed for recovery.
    /// </summary>
    [Id(6)]
    public long DataSizeBytes { get; set; }

    /// <summary>
    /// Confidence level that recovery to this point will succeed (0.0 to 1.0).
    /// </summary>
    [Id(7)]
    public double ConfidenceLevel { get; set; } = 1.0;

    /// <summary>
    /// Additional metadata about this recovery point.
    /// </summary>
    [Id(8)]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Creates a recovery point from a snapshot.
    /// </summary>
    /// <param name="timestamp">The snapshot timestamp</param>
    /// <param name="version">The snapshot version</param>
    /// <param name="snapshotId">The snapshot ID</param>
    /// <param name="description">Description of the snapshot</param>
    /// <returns>A recovery point representing the snapshot</returns>
    public static RecoveryPoint FromSnapshot(
        DateTimeOffset timestamp,
        long version,
        string snapshotId,
        string? description = null)
    {
        return new RecoveryPoint
        {
            Timestamp = timestamp,
            Version = version,
            Type = RecoveryPointType.Snapshot,
            SnapshotId = snapshotId,
            Description = description ?? $"Snapshot at {timestamp:yyyy-MM-dd HH:mm:ss}",
            EstimatedRecoveryTime = TimeSpan.FromSeconds(1), // Snapshots are fast
            ConfidenceLevel = 0.95
        };
    }

    /// <summary>
    /// Creates a recovery point from an event.
    /// </summary>
    /// <param name="timestamp">The event timestamp</param>
    /// <param name="version">The event version</param>
    /// <param name="description">Description of the event</param>
    /// <param name="dataSizeBytes">Size of data to replay</param>
    /// <returns>A recovery point representing the event</returns>
    public static RecoveryPoint FromEvent(
        DateTimeOffset timestamp,
        long version,
        string? description = null,
        long dataSizeBytes = 0)
    {
        return new RecoveryPoint
        {
            Timestamp = timestamp,
            Version = version,
            Type = RecoveryPointType.Event,
            Description = description ?? $"Event at {timestamp:yyyy-MM-dd HH:mm:ss}",
            DataSizeBytes = dataSizeBytes,
            EstimatedRecoveryTime = TimeSpan.FromMilliseconds(dataSizeBytes / 1024 + 100), // Rough estimate
            ConfidenceLevel = 0.85
        };
    }
}

/// <summary>
/// Types of recovery points.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryPointType")]
public enum RecoveryPointType
{
    /// <summary>
    /// Recovery point is based on a snapshot.
    /// </summary>
    [Id(0)]
    Snapshot,

    /// <summary>
    /// Recovery point is based on an event.
    /// </summary>
    [Id(1)]
    Event,

    /// <summary>
    /// Recovery point is a system checkpoint.
    /// </summary>
    [Id(2)]
    Checkpoint,

    /// <summary>
    /// Recovery point represents a state transition.
    /// </summary>
    [Id(3)]
    StateTransition,

    /// <summary>
    /// Recovery point is a manual bookmark.
    /// </summary>
    [Id(4)]
    Manual
}

/// <summary>
/// Result of validating recovery possibility.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.PointInTimeRecoveryValidationResult")]
public record PointInTimeRecoveryValidationResult
{
    /// <summary>
    /// Whether recovery to the requested point is possible.
    /// </summary>
    [Id(0)]
    public required bool IsPossible { get; set; }

    /// <summary>
    /// The confidence level that recovery will succeed (0.0 to 1.0).
    /// </summary>
    [Id(1)]
    public double ConfidenceLevel { get; set; } = 1.0;

    /// <summary>
    /// Estimated time to complete the recovery.
    /// </summary>
    [Id(2)]
    public TimeSpan EstimatedDuration { get; set; }

    /// <summary>
    /// The recommended recovery strategy.
    /// </summary>
    [Id(3)]
    public RecoveryStrategy RecommendedStrategy { get; set; }

    /// <summary>
    /// Issues that prevent or complicate recovery.
    /// </summary>
    [Id(4)]
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Warnings about the recovery operation.
    /// </summary>
    [Id(5)]
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Whether the requested timestamp/version has data available.
    /// </summary>
    [Id(6)]
    public bool DataAvailable { get; set; } = true;

    /// <summary>
    /// Whether snapshots are available near the target point.
    /// </summary>
    [Id(7)]
    public bool SnapshotsAvailable { get; set; } = false;

    /// <summary>
    /// The nearest available recovery point if exact target isn't possible.
    /// </summary>
    [Id(8)]
    public RecoveryPoint? NearestRecoveryPoint { get; set; }

    /// <summary>
    /// Additional validation details.
    /// </summary>
    [Id(9)]
    public Dictionary<string, object>? Details { get; set; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="confidenceLevel">Confidence in recovery success</param>
    /// <param name="estimatedDuration">Expected recovery time</param>
    /// <param name="recommendedStrategy">Best strategy to use</param>
    /// <param name="snapshotsAvailable">Whether snapshots are available</param>
    /// <param name="warnings">Any warnings</param>
    /// <returns>A successful validation result</returns>
    public static PointInTimeRecoveryValidationResult Success(
        double confidenceLevel,
        TimeSpan estimatedDuration,
        RecoveryStrategy recommendedStrategy,
        bool snapshotsAvailable = false,
        IReadOnlyList<string>? warnings = null)
    {
        return new PointInTimeRecoveryValidationResult
        {
            IsPossible = true,
            ConfidenceLevel = confidenceLevel,
            EstimatedDuration = estimatedDuration,
            RecommendedStrategy = recommendedStrategy,
            SnapshotsAvailable = snapshotsAvailable,
            Warnings = warnings ?? Array.Empty<string>(),
            DataAvailable = true
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="issues">Issues preventing recovery</param>
    /// <param name="nearestPoint">Nearest available recovery point</param>
    /// <returns>A failed validation result</returns>
    public static PointInTimeRecoveryValidationResult Failure(
        IReadOnlyList<string> issues,
        RecoveryPoint? nearestPoint = null)
    {
        return new PointInTimeRecoveryValidationResult
        {
            IsPossible = false,
            Issues = issues,
            NearestRecoveryPoint = nearestPoint,
            ConfidenceLevel = 0.0,
            DataAvailable = false
        };
    }
}