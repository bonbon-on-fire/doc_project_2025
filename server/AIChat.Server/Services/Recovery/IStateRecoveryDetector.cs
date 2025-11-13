namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Detects when state reconstruction is needed for Orleans grains.
/// Integrates with grain lifecycle to identify recovery scenarios.
/// Follows the Single Responsibility Principle by focusing solely on detection logic.
/// </summary>
public interface IStateRecoveryDetector
{
    /// <summary>
    /// Gets the name of the recovery detector implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines if grain state needs reconstruction during activation.
    /// This is the primary detection method called during grain activation.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being checked</param>
    /// <param name="currentState">The current state of the grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The recovery need assessment</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryDetectionException">Thrown when detection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateRecoveryNeed> DetectRecoveryNeedAsync<T>(
        string grainId,
        string grainType,
        T currentState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates state integrity for runtime corruption detection.
    /// Used to detect corruption during normal grain operations.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The validation result with details about any issues found</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryDetectionException">Thrown when validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateStateIntegrityAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the state is consistent with the event stream.
    /// Validates that the current state matches what would be expected based on events.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being checked</param>
    /// <param name="state">The state to check</param>
    /// <param name="expectedVersion">The expected version based on events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if state is consistent with event stream, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryDetectionException">Thrown when consistency check fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> IsStateConsistentWithEventStreamAsync<T>(
        string grainId,
        string grainType,
        T state,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the cost of recovery for a grain based on event stream size.
    /// Used to determine optimal recovery strategies.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Recovery cost estimation</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryDetectionException">Thrown when cost estimation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryCostEstimate> EstimateRecoveryCostAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if recovery should be performed based on configuration and policies.
    /// Allows for policy-based decision making about when recovery is appropriate.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="recoveryNeed">The detected recovery need</param>
    /// <param name="costEstimate">The estimated cost of recovery</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if recovery should be performed, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="StateRecoveryDetectionException">Thrown when policy evaluation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> ShouldPerformRecoveryAsync(
        string grainId,
        string grainType,
        StateRecoveryNeed recoveryNeed,
        RecoveryCostEstimate costEstimate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recovery detection metrics and statistics.
    /// Used for monitoring and performance analysis.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Detection metrics and statistics</returns>
    /// <exception cref="StateRecoveryDetectionException">Thrown when metrics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryDetectionMetrics> GetDetectionMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the estimated cost of performing a recovery operation.
/// </summary>
public record RecoveryCostEstimate
{
    /// <summary>
    /// The estimated time to complete recovery in milliseconds.
    /// </summary>
    public double EstimatedTimeMs { get; init; }

    /// <summary>
    /// The estimated number of events that would need to be replayed.
    /// </summary>
    public long EventsToReplay { get; init; }

    /// <summary>
    /// Whether a snapshot is available for this grain.
    /// </summary>
    public bool SnapshotAvailable { get; init; }

    /// <summary>
    /// The version of the latest snapshot, if available.
    /// </summary>
    public long? LatestSnapshotVersion { get; init; }

    /// <summary>
    /// The complexity level of the recovery operation.
    /// </summary>
    public RecoveryComplexity Complexity { get; init; }

    /// <summary>
    /// Estimated resource usage for the recovery operation.
    /// </summary>
    public RecoveryResourceEstimate ResourceEstimate { get; init; } = new();

    /// <summary>
    /// Confidence level in the cost estimate (0.0 to 1.0).
    /// </summary>
    public double ConfidenceLevel { get; init; } = 0.8;

    /// <summary>
    /// Additional metadata about the cost estimation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a low-cost recovery estimate.
    /// </summary>
    /// <param name="estimatedTimeMs">The estimated time in milliseconds</param>
    /// <param name="eventsToReplay">Number of events to replay</param>
    /// <param name="snapshotAvailable">Whether snapshot is available</param>
    /// <returns>A low-cost recovery estimate</returns>
    public static RecoveryCostEstimate LowCost(
        double estimatedTimeMs,
        long eventsToReplay,
        bool snapshotAvailable = true)
    {
        return new RecoveryCostEstimate
        {
            EstimatedTimeMs = estimatedTimeMs,
            EventsToReplay = eventsToReplay,
            SnapshotAvailable = snapshotAvailable,
            Complexity = RecoveryComplexity.Low,
            ConfidenceLevel = 0.9
        };
    }

    /// <summary>
    /// Creates a high-cost recovery estimate.
    /// </summary>
    /// <param name="estimatedTimeMs">The estimated time in milliseconds</param>
    /// <param name="eventsToReplay">Number of events to replay</param>
    /// <returns>A high-cost recovery estimate</returns>
    public static RecoveryCostEstimate HighCost(double estimatedTimeMs, long eventsToReplay)
    {
        return new RecoveryCostEstimate
        {
            EstimatedTimeMs = estimatedTimeMs,
            EventsToReplay = eventsToReplay,
            SnapshotAvailable = false,
            Complexity = RecoveryComplexity.High,
            ConfidenceLevel = 0.7
        };
    }
}

/// <summary>
/// Represents the complexity level of a recovery operation.
/// </summary>
public enum RecoveryComplexity
{
    /// <summary>
    /// Low complexity - snapshot available, few events to replay.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Medium complexity - some events to replay, moderate resource usage.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High complexity - many events to replay, significant resource usage.
    /// </summary>
    High = 2,

    /// <summary>
    /// Very high complexity - full replay required, maximum resource usage.
    /// </summary>
    VeryHigh = 3
}

/// <summary>
/// Represents estimated resource usage for recovery operations.
/// </summary>
public record RecoveryResourceEstimate
{
    /// <summary>
    /// Estimated CPU usage percentage (0-100).
    /// </summary>
    public double EstimatedCpuUsage { get; init; } = 10.0;

    /// <summary>
    /// Estimated memory usage in MB.
    /// </summary>
    public double EstimatedMemoryUsageMB { get; init; } = 50.0;

    /// <summary>
    /// Estimated I/O operations required.
    /// </summary>
    public long EstimatedIOOperations { get; init; } = 100;

    /// <summary>
    /// Estimated network bandwidth usage in KB.
    /// </summary>
    public double EstimatedNetworkUsageKB { get; init; } = 100.0;
}

/// <summary>
/// Represents metrics and statistics for recovery detection operations.
/// </summary>
public record RecoveryDetectionMetrics
{
    /// <summary>
    /// Total number of detection operations performed.
    /// </summary>
    public long TotalDetectionOperations { get; init; }

    /// <summary>
    /// Number of grains that required recovery.
    /// </summary>
    public long GrainsRequiringRecovery { get; init; }

    /// <summary>
    /// Number of grains with missing state.
    /// </summary>
    public long GrainsWithMissingState { get; init; }

    /// <summary>
    /// Number of grains with corrupted state.
    /// </summary>
    public long GrainsWithCorruptedState { get; init; }

    /// <summary>
    /// Number of grains with inconsistent state.
    /// </summary>
    public long GrainsWithInconsistentState { get; init; }

    /// <summary>
    /// Average time taken for detection operations in milliseconds.
    /// </summary>
    public double AverageDetectionTimeMs { get; init; }

    /// <summary>
    /// Detection accuracy rate as a percentage.
    /// </summary>
    public double AccuracyRate { get; init; } = 99.0;

    /// <summary>
    /// False positive rate as a percentage.
    /// </summary>
    public double FalsePositiveRate { get; init; } = 1.0;

    /// <summary>
    /// False negative rate as a percentage.
    /// </summary>
    public double FalseNegativeRate { get; init; } = 1.0;

    /// <summary>
    /// Distribution of recovery needs detected.
    /// </summary>
    public Dictionary<StateRecoveryNeed, long> RecoveryNeedDistribution { get; init; } = [];

    /// <summary>
    /// Distribution of recovery complexities estimated.
    /// </summary>
    public Dictionary<RecoveryComplexity, long> ComplexityDistribution { get; init; } = [];

    /// <summary>
    /// Timestamp when metrics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty detection metrics.
    /// </summary>
    /// <returns>Empty detection metrics</returns>
    public static RecoveryDetectionMetrics Empty()
    {
        return new RecoveryDetectionMetrics
        {
            TotalDetectionOperations = 0,
            GrainsRequiringRecovery = 0,
            GrainsWithMissingState = 0,
            GrainsWithCorruptedState = 0,
            GrainsWithInconsistentState = 0,
            AverageDetectionTimeMs = 0.0
        };
    }
}
