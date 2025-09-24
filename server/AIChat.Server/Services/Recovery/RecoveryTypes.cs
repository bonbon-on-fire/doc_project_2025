using Orleans;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Represents the different types of recovery needs for grain state.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateRecoveryNeed")]
public enum StateRecoveryNeed
{
    /// <summary>
    /// No recovery is needed - state is valid and consistent.
    /// </summary>
    [Id(0)]
    None,

    /// <summary>
    /// State is missing or null and needs to be reconstructed.
    /// </summary>
    [Id(1)]
    MissingState,

    /// <summary>
    /// State exists but is corrupted or invalid.
    /// </summary>
    [Id(2)]
    CorruptedState,

    /// <summary>
    /// State exists but is inconsistent with the event stream.
    /// </summary>
    [Id(3)]
    InconsistentState,

    /// <summary>
    /// Manual recovery has been requested for this grain.
    /// </summary>
    [Id(4)]
    ForceRecovery,

    /// <summary>
    /// Unknown recovery need - used when the need cannot be determined.
    /// </summary>
    [Id(5)]
    Unknown
}

/// <summary>
/// Represents the available recovery strategies for state reconstruction.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryStrategy")]
public enum RecoveryStrategy
{
    /// <summary>
    /// Use the latest snapshot and replay events from that point.
    /// Optimal for performance when snapshots are available.
    /// </summary>
    [Id(0)]
    SnapshotFirst,

    /// <summary>
    /// Replay all events from the beginning of the stream.
    /// Fallback strategy when snapshots are unavailable or corrupted.
    /// </summary>
    [Id(1)]
    FullReplay,

    /// <summary>
    /// Try snapshot-first recovery, fallback to full replay if needed.
    /// Balanced approach for reliability and performance.
    /// </summary>
    [Id(2)]
    HybridRecovery,

    /// <summary>
    /// Initialize with default/empty state as last resort.
    /// Used when event replay is not possible or fails.
    /// </summary>
    [Id(3)]
    EmptyState
}

/// <summary>
/// Represents the type of recovery that was performed.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryType")]
public enum RecoveryType
{
    /// <summary>
    /// No recovery was performed.
    /// </summary>
    [Id(0)]
    None,

    /// <summary>
    /// State was recovered from a snapshot with event replay.
    /// </summary>
    [Id(1)]
    SnapshotWithReplay,

    /// <summary>
    /// State was recovered by replaying all events.
    /// </summary>
    [Id(2)]
    FullEventReplay,

    /// <summary>
    /// State was initialized with default values.
    /// </summary>
    [Id(3)]
    DefaultInitialization,

    /// <summary>
    /// Recovery was attempted but failed, using fallback state.
    /// </summary>
    [Id(4)]
    FallbackState
}

/// <summary>
/// Represents a request for state recovery.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateRecoveryRequest")]
public record StateRecoveryRequest
{
    /// <summary>
    /// The unique identifier of the grain requiring recovery.
    /// </summary>
    [Id(0)]
    public required string GrainId { get; init; }

    /// <summary>
    /// The type of grain being recovered.
    /// </summary>
    [Id(1)]
    public required string GrainType { get; init; }

    /// <summary>
    /// The specific recovery strategy to use.
    /// If null, the orchestrator will choose the optimal strategy.
    /// </summary>
    [Id(2)]
    public RecoveryStrategy? PreferredStrategy { get; init; }

    /// <summary>
    /// The reason why recovery is needed.
    /// </summary>
    [Id(3)]
    public required StateRecoveryNeed RecoveryNeed { get; init; }

    /// <summary>
    /// Optional target version to recover to.
    /// If null, recovers to the latest available version.
    /// </summary>
    [Id(4)]
    public long? TargetVersion { get; init; }

    /// <summary>
    /// Whether to force recovery even if state appears valid.
    /// </summary>
    [Id(5)]
    public bool ForceRecovery { get; init; }

    /// <summary>
    /// Additional metadata for the recovery request.
    /// </summary>
    [Id(6)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Correlation ID for tracking recovery across system components.
    /// </summary>
    [Id(7)]
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the recovery request was created.
    /// </summary>
    [Id(8)]
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a recovery request for missing state.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <returns>A recovery request for missing state</returns>
    public static StateRecoveryRequest ForMissingState(string grainId, string grainType)
    {
        return new StateRecoveryRequest
        {
            GrainId = grainId,
            GrainType = grainType,
            RecoveryNeed = StateRecoveryNeed.MissingState,
            PreferredStrategy = RecoveryStrategy.SnapshotFirst
        };
    }

    /// <summary>
    /// Creates a recovery request for corrupted state.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <returns>A recovery request for corrupted state</returns>
    public static StateRecoveryRequest ForCorruptedState(string grainId, string grainType)
    {
        return new StateRecoveryRequest
        {
            GrainId = grainId,
            GrainType = grainType,
            RecoveryNeed = StateRecoveryNeed.CorruptedState,
            PreferredStrategy = RecoveryStrategy.HybridRecovery
        };
    }

    /// <summary>
    /// Creates a recovery request for manual recovery.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="targetVersion">Optional target version</param>
    /// <returns>A recovery request for manual recovery</returns>
    public static StateRecoveryRequest ForManualRecovery(string grainId, string grainType, long? targetVersion = null)
    {
        return new StateRecoveryRequest
        {
            GrainId = grainId,
            GrainType = grainType,
            RecoveryNeed = StateRecoveryNeed.ForceRecovery,
            TargetVersion = targetVersion,
            ForceRecovery = true,
            PreferredStrategy = RecoveryStrategy.HybridRecovery
        };
    }
}

/// <summary>
/// Represents the result of a state recovery operation.
/// </summary>
/// <typeparam name="T">The type of state that was recovered</typeparam>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateRecoveryResult`1")]
public record StateRecoveryResult<T>
{
    /// <summary>
    /// Whether the recovery operation was successful.
    /// </summary>
    [Id(0)]
    public required bool Success { get; init; }

    /// <summary>
    /// The recovered state, if successful.
    /// </summary>
    [Id(1)]
    public T? RecoveredState { get; init; }

    /// <summary>
    /// Alias for RecoveredState for compatibility.
    /// </summary>
    public T? State => RecoveredState;

    /// <summary>
    /// The type of recovery that was performed.
    /// </summary>
    [Id(2)]
    public RecoveryType RecoveryType { get; init; }

    /// <summary>
    /// The strategy that was used for recovery.
    /// </summary>
    [Id(3)]
    public RecoveryStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Alias for StrategyUsed for compatibility.
    /// </summary>
    public RecoveryStrategy Strategy => StrategyUsed;

    /// <summary>
    /// The final version of the recovered state.
    /// </summary>
    [Id(4)]
    public long FinalVersion { get; init; }

    /// <summary>
    /// The number of events that were replayed during recovery.
    /// </summary>
    [Id(5)]
    public int EventsReplayed { get; init; }

    /// <summary>
    /// The total time taken for the recovery operation.
    /// </summary>
    [Id(6)]
    public TimeSpan RecoveryTime { get; init; }

    /// <summary>
    /// Whether a snapshot was used during recovery.
    /// </summary>
    [Id(7)]
    public bool SnapshotUsed { get; init; }

    /// <summary>
    /// The version of the snapshot that was used, if any.
    /// </summary>
    [Id(8)]
    public long? SnapshotVersion { get; init; }

    /// <summary>
    /// Any error message if the recovery failed.
    /// </summary>
    [Id(9)]
    public string? Error { get; init; }

    /// <summary>
    /// Additional metadata about the recovery operation.
    /// </summary>
    [Id(10)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// The correlation ID from the original recovery request.
    /// </summary>
    [Id(11)]
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the recovery was completed.
    /// </summary>
    [Id(12)]
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful recovery result.
    /// </summary>
    /// <param name="recoveredState">The recovered state</param>
    /// <param name="recoveryType">The type of recovery performed</param>
    /// <param name="strategyUsed">The strategy that was used</param>
    /// <param name="finalVersion">The final state version</param>
    /// <param name="eventsReplayed">Number of events replayed</param>
    /// <param name="recoveryTime">Time taken for recovery</param>
    /// <param name="snapshotUsed">Whether a snapshot was used</param>
    /// <param name="snapshotVersion">The snapshot version used, if any</param>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A successful recovery result</returns>
    public static StateRecoveryResult<T> CreateSuccess(
        T recoveredState,
        RecoveryType recoveryType,
        RecoveryStrategy strategyUsed,
        long finalVersion,
        int eventsReplayed,
        TimeSpan recoveryTime,
        bool snapshotUsed = false,
        long? snapshotVersion = null,
        string correlationId = "",
        Dictionary<string, object>? metadata = null)
    {
        return new StateRecoveryResult<T>
        {
            Success = true,
            RecoveredState = recoveredState,
            RecoveryType = recoveryType,
            StrategyUsed = strategyUsed,
            FinalVersion = finalVersion,
            EventsReplayed = eventsReplayed,
            RecoveryTime = recoveryTime,
            SnapshotUsed = snapshotUsed,
            SnapshotVersion = snapshotVersion,
            CorrelationId = correlationId,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed recovery result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="strategyUsed">The strategy that was attempted</param>
    /// <param name="recoveryTime">Time taken before failure</param>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A failed recovery result</returns>
    public static StateRecoveryResult<T> CreateFailure(
        string error,
        RecoveryStrategy strategyUsed = RecoveryStrategy.HybridRecovery,
        TimeSpan recoveryTime = default,
        string correlationId = "",
        Dictionary<string, object>? metadata = null)
    {
        return new StateRecoveryResult<T>
        {
            Success = false,
            Error = error,
            StrategyUsed = strategyUsed,
            RecoveryTime = recoveryTime,
            CorrelationId = correlationId,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of an automatic recovery operation.
/// </summary>
/// <typeparam name="T">The type of state that was recovered</typeparam>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.AutomaticRecoveryResult`1")]
public record AutomaticRecoveryResult<T>
{
    /// <summary>
    /// Whether recovery was performed.
    /// </summary>
    [Id(0)]
    public bool RecoveryPerformed { get; init; }

    /// <summary>
    /// The recovered state, if recovery was performed.
    /// </summary>
    [Id(1)]
    public T? RecoveredState { get; init; }

    /// <summary>
    /// The type of recovery that was performed.
    /// </summary>
    [Id(2)]
    public RecoveryType RecoveryType { get; init; }

    /// <summary>
    /// Whether the recovery was successful.
    /// </summary>
    [Id(3)]
    public bool Success { get; init; }

    /// <summary>
    /// Any error message if recovery failed.
    /// </summary>
    [Id(4)]
    public string? Error { get; init; }

    /// <summary>
    /// The total time taken for the recovery operation.
    /// </summary>
    [Id(5)]
    public TimeSpan RecoveryTime { get; init; }

    /// <summary>
    /// Additional metadata about the recovery.
    /// </summary>
    [Id(6)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a result where no recovery was needed.
    /// </summary>
    /// <param name="originalState">The original state that was valid</param>
    /// <returns>A result indicating no recovery was needed</returns>
    public static AutomaticRecoveryResult<T> NoRecoveryNeeded(T originalState)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = false,
            RecoveredState = originalState,
            Success = true,
            RecoveryType = RecoveryType.None
        };
    }

    /// <summary>
    /// Creates a result where no recovery was needed (alternate factory method).
    /// </summary>
    /// <param name="currentState">The current state that was valid</param>
    /// <param name="elapsed">The time elapsed during evaluation</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    /// <returns>A result indicating no recovery was needed</returns>
    public static AutomaticRecoveryResult<T> CreateNoRecoveryNeeded(T currentState, TimeSpan elapsed, string correlationId)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = false,
            RecoveredState = currentState,
            Success = true,
            RecoveryType = RecoveryType.None,
            RecoveryTime = elapsed,
            Metadata = new Dictionary<string, object> { ["CorrelationId"] = correlationId }
        };
    }

    /// <summary>
    /// Creates a result where recovery was skipped.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="recoveryNeed">The recovery need that was identified</param>
    /// <param name="costEstimate">The estimated cost of recovery</param>
    /// <param name="reason">The reason recovery was skipped</param>
    /// <param name="elapsed">The time elapsed during evaluation</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    /// <returns>A result indicating recovery was skipped</returns>
    public static AutomaticRecoveryResult<T> CreateRecoverySkipped(
        T currentState,
        StateRecoveryNeed recoveryNeed,
        object costEstimate,
        string reason,
        TimeSpan elapsed,
        string correlationId)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = false,
            RecoveredState = currentState,
            Success = true,
            RecoveryType = RecoveryType.None,
            RecoveryTime = elapsed,
            Metadata = new Dictionary<string, object>
            {
                ["SkipReason"] = reason,
                ["RecoveryNeed"] = recoveryNeed.ToString(),
                ["CostEstimate"] = costEstimate,
                ["CorrelationId"] = correlationId
            }
        };
    }

    /// <summary>
    /// Creates a result for failed recovery (alternate factory method).
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="recoveryNeed">The recovery need that was identified</param>
    /// <param name="error">The error message</param>
    /// <param name="elapsed">Time taken before failure</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    /// <returns>A failed recovery result</returns>
    public static AutomaticRecoveryResult<T> CreateRecoveryFailed(
        T currentState,
        StateRecoveryNeed recoveryNeed,
        string error,
        TimeSpan elapsed,
        string correlationId)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = true,
            RecoveredState = currentState,
            Success = false,
            Error = error,
            RecoveryTime = elapsed,
            RecoveryType = RecoveryType.FallbackState,
            Metadata = new Dictionary<string, object>
            {
                ["RecoveryNeed"] = recoveryNeed.ToString(),
                ["CorrelationId"] = correlationId
            }
        };
    }

    /// <summary>
    /// Creates a result for successful recovery (alternate factory method).
    /// </summary>
    /// <param name="recoveredState">The recovered state</param>
    /// <param name="recoveryNeed">The recovery need that was identified</param>
    /// <param name="strategy">The strategy used for recovery</param>
    /// <param name="recoveryType">The type of recovery performed</param>
    /// <param name="finalVersion">The final version of recovered state</param>
    /// <param name="eventsReplayed">Number of events replayed</param>
    /// <param name="snapshotUsed">Whether a snapshot was used</param>
    /// <param name="elapsed">Time taken for recovery</param>
    /// <param name="correlationId">The correlation ID for tracking</param>
    /// <returns>A successful recovery result</returns>
    public static AutomaticRecoveryResult<T> CreateRecoverySucceeded(
        T recoveredState,
        StateRecoveryNeed recoveryNeed,
        RecoveryStrategy strategy,
        RecoveryType recoveryType,
        long finalVersion,
        int eventsReplayed,
        bool snapshotUsed,
        TimeSpan elapsed,
        string correlationId)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = true,
            RecoveredState = recoveredState,
            RecoveryType = recoveryType,
            Success = true,
            RecoveryTime = elapsed,
            Metadata = new Dictionary<string, object>
            {
                ["RecoveryNeed"] = recoveryNeed.ToString(),
                ["Strategy"] = strategy.ToString(),
                ["FinalVersion"] = finalVersion,
                ["EventsReplayed"] = eventsReplayed,
                ["SnapshotUsed"] = snapshotUsed,
                ["CorrelationId"] = correlationId
            }
        };
    }

    /// <summary>
    /// Creates a result for successful recovery.
    /// </summary>
    /// <param name="recoveredState">The recovered state</param>
    /// <param name="recoveryType">The type of recovery performed</param>
    /// <param name="recoveryTime">Time taken for recovery</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A successful recovery result</returns>
    public static AutomaticRecoveryResult<T> RecoverySuccessful(
        T recoveredState,
        RecoveryType recoveryType,
        TimeSpan recoveryTime,
        Dictionary<string, object>? metadata = null)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = true,
            RecoveredState = recoveredState,
            RecoveryType = recoveryType,
            Success = true,
            RecoveryTime = recoveryTime,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a result for failed recovery.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="recoveryTime">Time taken before failure</param>
    /// <param name="fallbackState">Fallback state if available</param>
    /// <returns>A failed recovery result</returns>
    public static AutomaticRecoveryResult<T> RecoveryFailed(
        string error,
        TimeSpan recoveryTime,
        T? fallbackState = default)
    {
        return new AutomaticRecoveryResult<T>
        {
            RecoveryPerformed = true,
            RecoveredState = fallbackState,
            Success = false,
            Error = error,
            RecoveryTime = recoveryTime,
            RecoveryType = RecoveryType.FallbackState
        };
    }
}

/// <summary>
/// Represents the result of state validation during recovery detection.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.StateValidationResult")]
public record StateValidationResult
{
    /// <summary>
    /// Whether the state is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The type of recovery needed if state is invalid.
    /// </summary>
    public StateRecoveryNeed RecoveryNeed { get; init; }

    /// <summary>
    /// Validation issues found, if any.
    /// </summary>
    public IReadOnlyList<string> ValidationIssues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether the state data is structurally intact.
    /// </summary>
    public bool IsStructurallyIntact { get; init; } = true;

    /// <summary>
    /// Whether the state passes business rule validation.
    /// </summary>
    public bool PassesBusinessRules { get; init; } = true;

    /// <summary>
    /// Confidence level in the validation result (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; init; } = 1.0;

    /// <summary>
    /// Additional metadata about the validation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a valid state result.
    /// </summary>
    /// <returns>A validation result indicating valid state</returns>
    public static StateValidationResult Valid()
    {
        return new StateValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates an invalid state result.
    /// </summary>
    /// <param name="recoveryNeed">The type of recovery needed</param>
    /// <param name="issues">Validation issues found</param>
    /// <param name="isStructurallyIntact">Whether data structure is intact</param>
    /// <param name="passesBusinessRules">Whether business rules pass</param>
    /// <returns>A validation result indicating invalid state</returns>
    public static StateValidationResult Invalid(
        StateRecoveryNeed recoveryNeed,
        IReadOnlyList<string>? issues = null,
        bool isStructurallyIntact = false,
        bool passesBusinessRules = false)
    {
        return new StateValidationResult
        {
            IsValid = false,
            RecoveryNeed = recoveryNeed,
            ValidationIssues = issues ?? Array.Empty<string>(),
            IsStructurallyIntact = isStructurallyIntact,
            PassesBusinessRules = passesBusinessRules
        };
    }
}

/// <summary>
/// Represents consistency verification results.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.ConsistencyVerificationResult")]
public record ConsistencyVerificationResult
{
    /// <summary>
    /// Whether the state is consistent.
    /// </summary>
    public required bool IsConsistent { get; init; }

    /// <summary>
    /// Consistency issues found, if any.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether data integrity checks passed.
    /// </summary>
    public bool DataIntegrityValid { get; init; } = true;

    /// <summary>
    /// Whether event stream alignment is correct.
    /// </summary>
    public bool EventStreamAligned { get; init; } = true;

    /// <summary>
    /// Whether cross-grain consistency is maintained.
    /// </summary>
    public bool CrossGrainConsistent { get; init; } = true;

    /// <summary>
    /// Confidence level in the consistency verification (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; init; } = 1.0;

    /// <summary>
    /// Confidence score in the consistency verification (0.0 to 1.0).
    /// </summary>
    public double ConfidenceScore { get; init; } = 1.0;

    /// <summary>
    /// Consistency warnings found, if any.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The data integrity verification result.
    /// </summary>
    public DataIntegrityVerificationResult? DataIntegrityResult { get; init; }

    /// <summary>
    /// The business rules validation result.
    /// </summary>
    public BusinessRuleValidationResult? BusinessRulesResult { get; init; }

    /// <summary>
    /// The event stream alignment result.
    /// </summary>
    public EventStreamAlignmentResult? EventStreamAlignmentResult { get; init; }

    /// <summary>
    /// The verification time taken.
    /// </summary>
    public TimeSpan VerificationTime { get; init; }

    /// <summary>
    /// The timestamp when verification was performed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a consistent result.
    /// </summary>
    /// <returns>A consistency result indicating state is consistent</returns>
    public static ConsistencyVerificationResult Consistent()
    {
        return new ConsistencyVerificationResult { IsConsistent = true };
    }

    /// <summary>
    /// Creates an inconsistent result.
    /// </summary>
    /// <param name="issues">Consistency issues found</param>
    /// <returns>A consistency result indicating state is inconsistent</returns>
    public static ConsistencyVerificationResult Inconsistent(IReadOnlyList<string> issues)
    {
        return new ConsistencyVerificationResult
        {
            IsConsistent = false,
            Issues = issues,
            DataIntegrityValid = false,
            EventStreamAligned = false,
            CrossGrainConsistent = false
        };
    }
}

/// <summary>
/// Represents business rule validation results.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.BusinessRuleValidationResult")]
public record BusinessRuleValidationResult
{
    /// <summary>
    /// Whether all business rules are satisfied.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Business rule violations found, if any.
    /// </summary>
    public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Severity of the violations (if any).
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.None;

    /// <summary>
    /// Whether the violations are recoverable.
    /// </summary>
    public bool IsRecoverable { get; init; } = true;

    /// <summary>
    /// Business rule warnings found, if any.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a valid business rule result.
    /// </summary>
    /// <returns>A validation result indicating all business rules are satisfied</returns>
    public static BusinessRuleValidationResult Valid()
    {
        return new BusinessRuleValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates an invalid business rule result.
    /// </summary>
    /// <param name="violations">The business rule violations</param>
    /// <param name="severity">The severity of violations</param>
    /// <param name="isRecoverable">Whether the violations are recoverable</param>
    /// <returns>A validation result indicating business rule violations</returns>
    public static BusinessRuleValidationResult Invalid(
        IReadOnlyList<string> violations,
        ValidationSeverity severity = ValidationSeverity.Error,
        bool isRecoverable = true)
    {
        return new BusinessRuleValidationResult
        {
            IsValid = false,
            Violations = violations,
            Severity = severity,
            IsRecoverable = isRecoverable
        };
    }
}

/// <summary>
/// Represents the severity of validation issues.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.ValidationSeverity")]
public enum ValidationSeverity
{
    /// <summary>
    /// No issues found.
    /// </summary>
    [Id(0)]
    None,

    /// <summary>
    /// Informational messages.
    /// </summary>
    [Id(1)]
    Info,

    /// <summary>
    /// Warning-level issues that don't prevent operation.
    /// </summary>
    [Id(2)]
    Warning,

    /// <summary>
    /// Error-level issues that require attention.
    /// </summary>
    [Id(3)]
    Error,

    /// <summary>
    /// Critical issues that require immediate recovery.
    /// </summary>
    [Id(4)]
    Critical
}

/// <summary>
/// Represents recovery metrics and statistics.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryMetrics")]
public record RecoveryMetrics
{
    /// <summary>
    /// Total number of recovery operations attempted.
    /// </summary>
    public long TotalRecoveryAttempts { get; init; }

    /// <summary>
    /// Number of successful recoveries.
    /// </summary>
    public long SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Number of failed recoveries.
    /// </summary>
    public long FailedRecoveries { get; init; }

    /// <summary>
    /// Average recovery time in milliseconds.
    /// </summary>
    public double AverageRecoveryTimeMs { get; init; }

    /// <summary>
    /// Recovery success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalRecoveryAttempts > 0
        ? (double)SuccessfulRecoveries / TotalRecoveryAttempts * 100
        : 0;

    /// <summary>
    /// Distribution of recovery strategies used.
    /// </summary>
    public Dictionary<RecoveryStrategy, long> StrategyUsage { get; init; } = new();

    /// <summary>
    /// Distribution of recovery types performed.
    /// </summary>
    public Dictionary<RecoveryType, long> RecoveryTypes { get; init; } = new();

    /// <summary>
    /// Timestamp when metrics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty metrics for initialization.
    /// </summary>
    /// <returns>Empty recovery metrics</returns>
    public static RecoveryMetrics Empty()
    {
        return new RecoveryMetrics
        {
            TotalRecoveryAttempts = 0,
            SuccessfulRecoveries = 0,
            FailedRecoveries = 0,
            AverageRecoveryTimeMs = 0
        };
    }
}

/// <summary>
/// Represents a business rule violation during state validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.BusinessRuleViolation")]
public record BusinessRuleViolation
{
    /// <summary>
    /// Gets the name of the business rule that was violated.
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Gets a human-readable description of the violation.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the severity level of this violation.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the property path where the violation occurred, if applicable.
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// Gets additional metadata about the violation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether this violation can be automatically corrected.
    /// </summary>
    public bool IsAutoCorrectable { get; init; } = false;

    /// <summary>
    /// Gets the timestamp when this violation was detected.
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Priority levels for integrity check operations.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.IntegrityCheckPriority")]
public enum IntegrityCheckPriority
{
    /// <summary>
    /// Low priority - can be deferred if system is under load.
    /// </summary>
    [Id(0)]
    Low = 0,

    /// <summary>
    /// Normal priority - standard integrity check.
    /// </summary>
    [Id(1)]
    Normal = 1,

    /// <summary>
    /// High priority - should be processed quickly.
    /// </summary>
    [Id(2)]
    High = 2,

    /// <summary>
    /// Critical priority - should be processed immediately.
    /// </summary>
    [Id(3)]
    Critical = 3
}

/// <summary>
/// Represents the result of scheduling an integrity check.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.IntegrityCheckScheduleResult")]
public record IntegrityCheckScheduleResult
{
    /// <summary>
    /// Gets the grain ID for which the check was scheduled.
    /// </summary>
    public required string GrainId { get; init; }

    /// <summary>
    /// Gets the grain type for which the check was scheduled.
    /// </summary>
    public required string GrainType { get; init; }

    /// <summary>
    /// Gets the priority of the scheduled check.
    /// </summary>
    public IntegrityCheckPriority Priority { get; init; }

    /// <summary>
    /// Gets when the check was scheduled.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; init; }

    /// <summary>
    /// Gets whether the scheduling was successful.
    /// </summary>
    public bool IsScheduled { get; init; }

    /// <summary>
    /// Gets the unique identifier for this scheduled check.
    /// </summary>
    public string? ScheduleId { get; init; }

    /// <summary>
    /// Gets any message about the scheduling result.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets any error that occurred during scheduling.
    /// </summary>
    public string? Error { get; init; }
}


/// <summary>
/// Represents health status for automatic recovery service.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.AutomaticRecoveryHealthStatus")]
public record AutomaticRecoveryHealthStatus
{
    /// <summary>
    /// Gets whether the service is healthy overall.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the name of the service.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Gets when the health check was performed.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Gets health status of individual components.
    /// </summary>
    public Dictionary<string, bool>? ComponentStatuses { get; init; }

    /// <summary>
    /// Gets any health status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets additional health details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }
}


/// <summary>
/// Represents configuration for automatic recovery service.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.AutomaticRecoveryConfiguration")]
public record AutomaticRecoveryConfiguration
{
    /// <summary>
    /// Gets the name of the service.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Gets the name of the detector component.
    /// </summary>
    public required string DetectorName { get; init; }

    /// <summary>
    /// Gets the name of the orchestrator component.
    /// </summary>
    public required string OrchestratorName { get; init; }

    /// <summary>
    /// Gets the name of the verifier component.
    /// </summary>
    public required string VerifierName { get; init; }

    /// <summary>
    /// Gets whether the service is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the default recovery strategy.
    /// </summary>
    public RecoveryStrategy DefaultStrategy { get; init; }

    /// <summary>
    /// Gets configuration settings.
    /// </summary>
    public Dictionary<string, object>? Settings { get; init; }

    /// <summary>
    /// Gets when the configuration was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; }
}

