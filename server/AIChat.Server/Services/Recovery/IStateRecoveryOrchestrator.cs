using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Orchestrates state recovery workflow using Event Store and Snapshot Manager.
/// Implements recovery strategies with performance optimization.
/// Follows the Single Responsibility Principle by focusing solely on recovery orchestration.
/// </summary>
public interface IStateRecoveryOrchestrator
{
    /// <summary>
    /// Gets the name of the recovery orchestrator implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Recovers grain state using optimal strategy (snapshot + events).
    /// This is the primary recovery method that coordinates the entire recovery workflow.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request with details about what to recover</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projection is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateRecoveryResult<T>> RecoverStateAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available recovery strategies for a grain based on available data.
    /// Used to determine which recovery approaches are possible for a grain.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available recovery strategies ordered by preference</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when strategy determination fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<IReadOnlyList<RecoveryStrategy>> GetAvailableStrategiesAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers state using a specific recovery strategy.
    /// Allows for explicit control over the recovery approach used.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="strategy">The specific recovery strategy to use</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projection is null</exception>
    /// <exception cref="ArgumentException">Thrown when strategy is not supported</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateRecoveryResult<T>> RecoverWithStrategyAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a dry run of recovery to estimate time and resources without actually recovering.
    /// Useful for cost estimation and planning recovery operations.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="strategy">The recovery strategy to simulate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Simulation results without performing actual recovery</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projection is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when simulation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoverySimulationResult> SimulateRecoveryAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that recovery is possible for a grain before attempting it.
    /// Performs pre-flight checks to ensure recovery prerequisites are met.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="strategy">The recovery strategy to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result indicating whether recovery is possible</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryValidationResult> ValidateRecoveryPossibleAsync(
        string grainId,
        string grainType,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Monitors the progress of an ongoing recovery operation.
    /// Provides real-time status updates for long-running recovery operations.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the recovery operation to monitor</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current progress and status of the recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when correlationId is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when progress monitoring fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryProgressStatus> GetRecoveryProgressAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an ongoing recovery operation.
    /// Provides graceful cancellation with cleanup of partial recovery state.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the recovery operation to cancel</param>
    /// <param name="cancellationToken">Token to cancel the cancellation operation</param>
    /// <returns>Result of the cancellation attempt</returns>
    /// <exception cref="ArgumentNullException">Thrown when correlationId is null</exception>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when cancellation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryCancellationResult> CancelRecoveryAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recovery orchestration metrics and statistics.
    /// Used for monitoring and performance analysis.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Orchestration metrics and statistics</returns>
    /// <exception cref="StateRecoveryOrchestrationException">Thrown when metrics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryOrchestrationMetrics> GetOrchestrationMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a recovery simulation operation.
/// </summary>
public record RecoverySimulationResult
{
    /// <summary>
    /// Whether the simulation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The strategy that was simulated.
    /// </summary>
    public RecoveryStrategy StrategySimulated { get; init; }

    /// <summary>
    /// Estimated time for the actual recovery operation in milliseconds.
    /// </summary>
    public double EstimatedTimeMs { get; init; }

    /// <summary>
    /// Estimated number of events that would be replayed.
    /// </summary>
    public long EstimatedEventsToReplay { get; init; }

    /// <summary>
    /// Whether a snapshot would be used in the recovery.
    /// </summary>
    public bool WouldUseSnapshot { get; init; }

    /// <summary>
    /// The version of the snapshot that would be used, if any.
    /// </summary>
    public long? SnapshotVersionToUse { get; init; }

    /// <summary>
    /// Estimated resource usage for the recovery operation.
    /// </summary>
    public RecoveryResourceEstimate ResourceEstimate { get; init; } = new();

    /// <summary>
    /// Potential issues or warnings identified during simulation.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Any error message if simulation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Confidence level in the simulation results (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; init; } = 0.8;

    /// <summary>
    /// Creates a successful simulation result.
    /// </summary>
    /// <param name="strategy">The strategy that was simulated</param>
    /// <param name="estimatedTimeMs">Estimated time in milliseconds</param>
    /// <param name="eventsToReplay">Estimated events to replay</param>
    /// <param name="wouldUseSnapshot">Whether snapshot would be used</param>
    /// <param name="snapshotVersion">Snapshot version to use, if any</param>
    /// <returns>A successful simulation result</returns>
    public static RecoverySimulationResult CreateSuccess(
        RecoveryStrategy strategy,
        double estimatedTimeMs,
        long eventsToReplay,
        bool wouldUseSnapshot = false,
        long? snapshotVersion = null)
    {
        return new RecoverySimulationResult
        {
            Success = true,
            StrategySimulated = strategy,
            EstimatedTimeMs = estimatedTimeMs,
            EstimatedEventsToReplay = eventsToReplay,
            WouldUseSnapshot = wouldUseSnapshot,
            SnapshotVersionToUse = snapshotVersion,
            ConfidenceLevel = 0.9
        };
    }

    /// <summary>
    /// Creates a failed simulation result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="strategy">The strategy that was attempted</param>
    /// <returns>A failed simulation result</returns>
    public static RecoverySimulationResult CreateFailure(string error, RecoveryStrategy strategy)
    {
        return new RecoverySimulationResult
        {
            Success = false,
            Error = error,
            StrategySimulated = strategy,
            ConfidenceLevel = 0.0
        };
    }
}

/// <summary>
/// Represents the result of validating whether recovery is possible.
/// </summary>
public record RecoveryValidationResult
{
    /// <summary>
    /// Whether recovery is possible with the specified strategy.
    /// </summary>
    public required bool IsRecoveryPossible { get; init; }

    /// <summary>
    /// Alias for IsRecoveryPossible for compatibility.
    /// </summary>
    public bool IsPossible => IsRecoveryPossible;

    /// <summary>
    /// The strategy that was validated.
    /// </summary>
    public RecoveryStrategy ValidatedStrategy { get; init; }

    /// <summary>
    /// Issues that would prevent recovery, if any.
    /// </summary>
    public IReadOnlyList<string> BlockingIssues { get; init; } = [];

    /// <summary>
    /// Warnings about potential recovery issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Prerequisites that must be met for recovery to succeed.
    /// </summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];

    /// <summary>
    /// Alternative strategies that might be possible if the requested one isn't.
    /// </summary>
    public IReadOnlyList<RecoveryStrategy> AlternativeStrategies { get; init; } = [];

    /// <summary>
    /// Creates a validation result indicating recovery is possible.
    /// </summary>
    /// <param name="strategy">The validated strategy</param>
    /// <param name="warnings">Any warnings about the recovery</param>
    /// <returns>A validation result indicating recovery is possible</returns>
    public static RecoveryValidationResult Possible(
        RecoveryStrategy strategy,
        IReadOnlyList<string>? warnings = null)
    {
        return new RecoveryValidationResult
        {
            IsRecoveryPossible = true,
            ValidatedStrategy = strategy,
            Warnings = warnings ?? []
        };
    }

    /// <summary>
    /// Creates a validation result indicating recovery is not possible.
    /// </summary>
    /// <param name="strategy">The strategy that was attempted</param>
    /// <param name="blockingIssues">Issues preventing recovery</param>
    /// <param name="alternatives">Alternative strategies that might work</param>
    /// <returns>A validation result indicating recovery is not possible</returns>
    public static RecoveryValidationResult NotPossible(
        RecoveryStrategy strategy,
        IReadOnlyList<string> blockingIssues,
        IReadOnlyList<RecoveryStrategy>? alternatives = null)
    {
        return new RecoveryValidationResult
        {
            IsRecoveryPossible = false,
            ValidatedStrategy = strategy,
            BlockingIssues = blockingIssues,
            AlternativeStrategies = alternatives ?? []
        };
    }
}

/// <summary>
/// Represents the current progress and status of a recovery operation.
/// </summary>
public record RecoveryProgressStatus
{
    /// <summary>
    /// The correlation ID of the recovery operation.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// The current status of the recovery operation.
    /// </summary>
    public RecoveryOperationStatus Status { get; init; }

    /// <summary>
    /// Progress percentage (0.0 to 100.0).
    /// </summary>
    public double ProgressPercentage { get; init; }

    /// <summary>
    /// Current step being performed.
    /// </summary>
    public string? CurrentStep { get; init; }

    /// <summary>
    /// Estimated time remaining in milliseconds.
    /// </summary>
    public double? EstimatedTimeRemainingMs { get; init; }

    /// <summary>
    /// Number of events processed so far.
    /// </summary>
    public long EventsProcessed { get; init; }

    /// <summary>
    /// Total number of events to process.
    /// </summary>
    public long TotalEventsToProcess { get; init; }

    /// <summary>
    /// Time elapsed since recovery started.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Any error that occurred during recovery.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Intermediate results or state information.
    /// </summary>
    public Dictionary<string, object>? IntermediateResults { get; init; }

    /// <summary>
    /// Timestamp of the last progress update.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents the status of a recovery operation.
/// </summary>
public enum RecoveryOperationStatus
{
    /// <summary>
    /// Recovery operation is queued but not yet started.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// Recovery operation is currently running.
    /// </summary>
    Running = 1,

    /// <summary>
    /// Recovery operation completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Recovery operation failed with errors.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Recovery operation was cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Recovery operation is paused.
    /// </summary>
    Paused = 5
}

/// <summary>
/// Represents the result of cancelling a recovery operation.
/// </summary>
public record RecoveryCancellationResult
{
    /// <summary>
    /// Whether the cancellation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The correlation ID of the cancelled recovery operation.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// The status of the operation when cancellation occurred.
    /// </summary>
    public RecoveryOperationStatus StatusWhenCancelled { get; init; }

    /// <summary>
    /// Any cleanup actions that were performed.
    /// </summary>
    public IReadOnlyList<string> CleanupActions { get; init; } = [];

    /// <summary>
    /// Any error that occurred during cancellation.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether partial recovery state was preserved.
    /// </summary>
    public bool PartialStatePreserved { get; init; }

    /// <summary>
    /// Creates a successful cancellation result.
    /// </summary>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="statusWhenCancelled">Status when cancelled</param>
    /// <param name="cleanupActions">Cleanup actions performed</param>
    /// <returns>A successful cancellation result</returns>
    public static RecoveryCancellationResult CreateSuccess(
        string correlationId,
        RecoveryOperationStatus statusWhenCancelled,
        IReadOnlyList<string>? cleanupActions = null)
    {
        return new RecoveryCancellationResult
        {
            Success = true,
            CorrelationId = correlationId,
            StatusWhenCancelled = statusWhenCancelled,
            CleanupActions = cleanupActions ?? []
        };
    }

    /// <summary>
    /// Creates a failed cancellation result.
    /// </summary>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="error">The error message</param>
    /// <returns>A failed cancellation result</returns>
    public static RecoveryCancellationResult CreateFailed(string correlationId, string error)
    {
        return new RecoveryCancellationResult
        {
            Success = false,
            CorrelationId = correlationId,
            Error = error
        };
    }
}

/// <summary>
/// Represents metrics and statistics for recovery orchestration operations.
/// </summary>
public record RecoveryOrchestrationMetrics
{
    /// <summary>
    /// Total number of recovery operations orchestrated.
    /// </summary>
    public long TotalRecoveryOperations { get; init; }

    /// <summary>
    /// Number of successful recovery operations.
    /// </summary>
    public long SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Number of failed recovery operations.
    /// </summary>
    public long FailedRecoveries { get; init; }

    /// <summary>
    /// Number of cancelled recovery operations.
    /// </summary>
    public long CancelledRecoveries { get; init; }

    /// <summary>
    /// Average time taken for recovery operations in milliseconds.
    /// </summary>
    public double AverageRecoveryTimeMs { get; init; }

    /// <summary>
    /// Success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalRecoveryOperations > 0
        ? (double)SuccessfulRecoveries / TotalRecoveryOperations * 100
        : 0;

    /// <summary>
    /// Distribution of recovery strategies used.
    /// </summary>
    public Dictionary<RecoveryStrategy, long> StrategyUsageDistribution { get; init; } = [];

    /// <summary>
    /// Distribution of recovery types performed.
    /// </summary>
    public Dictionary<RecoveryType, long> RecoveryTypeDistribution { get; init; } = [];

    /// <summary>
    /// Average number of events replayed per recovery.
    /// </summary>
    public double AverageEventsReplayed { get; init; }

    /// <summary>
    /// Percentage of recoveries that used snapshots.
    /// </summary>
    public double SnapshotUsageRate { get; init; }

    /// <summary>
    /// Timestamp when metrics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty orchestration metrics.
    /// </summary>
    /// <returns>Empty orchestration metrics</returns>
    public static RecoveryOrchestrationMetrics Empty()
    {
        return new RecoveryOrchestrationMetrics
        {
            TotalRecoveryOperations = 0,
            SuccessfulRecoveries = 0,
            FailedRecoveries = 0,
            CancelledRecoveries = 0,
            AverageRecoveryTimeMs = 0.0,
            AverageEventsReplayed = 0.0,
            SnapshotUsageRate = 0.0
        };
    }
}
