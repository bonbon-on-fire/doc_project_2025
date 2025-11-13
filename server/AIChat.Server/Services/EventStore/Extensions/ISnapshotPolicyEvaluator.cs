namespace AIChat.Server.Services.EventStore.Extensions;

/// <summary>
/// Interface for evaluating snapshot creation and retention policies.
/// Enables extension of policy logic without modifying core snapshot management.
/// Follows Open/Closed Principle for policy evaluation extensions.
/// </summary>
public interface ISnapshotPolicyEvaluator
{
    /// <summary>
    /// Gets the name of this policy evaluator.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the priority of this evaluator (higher values have priority).
    /// Used when multiple evaluators are registered for the same policy type.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets whether this evaluator can handle the specified policy type.
    /// </summary>
    /// <param name="policyType">The policy type to check</param>
    /// <returns>True if this evaluator can handle the policy type</returns>
    bool CanEvaluate(Type policyType);

    /// <summary>
    /// Evaluates whether a snapshot should be created based on the policy.
    /// </summary>
    /// <param name="context">The evaluation context containing stream and policy information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Evaluation result indicating whether to create a snapshot</returns>
    Task<PolicyEvaluationResult> EvaluateCreationPolicyAsync(
        SnapshotPolicyContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates whether snapshots should be cleaned up based on retention policy.
    /// </summary>
    /// <param name="context">The evaluation context containing retention policy information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Evaluation result indicating which snapshots to clean up</returns>
    Task<RetentionEvaluationResult> EvaluateRetentionPolicyAsync(
        RetentionPolicyContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a policy configuration is valid and executable.
    /// </summary>
    /// <param name="policy">The policy to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result indicating policy validity</returns>
    Task<PolicyValidationResult> ValidatePolicyAsync(
        object policy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context information for snapshot policy evaluation.
/// </summary>
public record SnapshotPolicyContext
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the current version of the stream.
    /// </summary>
    public required long CurrentVersion { get; init; }

    /// <summary>
    /// Gets the policy to evaluate.
    /// </summary>
    public required object Policy { get; init; }

    /// <summary>
    /// Gets the latest snapshot version (-1 if no snapshots exist).
    /// </summary>
    public long LatestSnapshotVersion { get; init; } = -1;

    /// <summary>
    /// Gets the timestamp of the latest snapshot.
    /// </summary>
    public DateTimeOffset? LatestSnapshotTimestamp { get; init; }

    /// <summary>
    /// Gets the current stream statistics.
    /// </summary>
    public StreamStatistics? StreamStatistics { get; init; }

    /// <summary>
    /// Gets additional context metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Gets the evaluation timestamp.
    /// </summary>
    public DateTimeOffset EvaluationTimestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Context information for retention policy evaluation.
/// </summary>
public record RetentionPolicyContext
{
    /// <summary>
    /// Gets the retention policy to evaluate.
    /// </summary>
    public required object RetentionPolicy { get; init; }

    /// <summary>
    /// Gets the snapshots to evaluate for cleanup.
    /// </summary>
    public required IReadOnlyList<SnapshotMetadata> Snapshots { get; init; }

    /// <summary>
    /// Gets the current storage statistics.
    /// </summary>
    public SnapshotGlobalStatistics? StorageStatistics { get; init; }

    /// <summary>
    /// Gets additional context metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Gets the evaluation timestamp.
    /// </summary>
    public DateTimeOffset EvaluationTimestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of policy evaluation for snapshot creation.
/// </summary>
public record PolicyEvaluationResult
{
    /// <summary>
    /// Gets whether a snapshot should be created.
    /// </summary>
    public required bool ShouldCreateSnapshot { get; init; }

    /// <summary>
    /// Gets the reason for the decision.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the confidence level of the decision (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Gets the criteria that triggered the decision.
    /// </summary>
    public IReadOnlyList<string> TriggeredCriteria { get; init; } = [];

    /// <summary>
    /// Gets additional evaluation metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Gets the recommended snapshot priority (higher = more important).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Creates a result indicating snapshot should be created.
    /// </summary>
    /// <param name="reason">The reason for creating the snapshot</param>
    /// <param name="triggeredCriteria">The criteria that triggered creation</param>
    /// <param name="confidence">Confidence level (0.0 to 1.0)</param>
    /// <param name="priority">Snapshot priority</param>
    /// <returns>A positive evaluation result</returns>
    public static PolicyEvaluationResult CreatePositive(
        string reason,
        IReadOnlyList<string> triggeredCriteria,
        double confidence = 1.0,
        int priority = 0)
    {
        return new PolicyEvaluationResult
        {
            ShouldCreateSnapshot = true,
            Reason = reason,
            TriggeredCriteria = triggeredCriteria,
            Confidence = confidence,
            Priority = priority
        };
    }

    /// <summary>
    /// Creates a result indicating snapshot should not be created.
    /// </summary>
    /// <param name="reason">The reason for not creating the snapshot</param>
    /// <param name="confidence">Confidence level (0.0 to 1.0)</param>
    /// <returns>A negative evaluation result</returns>
    public static PolicyEvaluationResult CreateNegative(string reason, double confidence = 1.0)
    {
        return new PolicyEvaluationResult
        {
            ShouldCreateSnapshot = false,
            Reason = reason,
            Confidence = confidence
        };
    }
}

/// <summary>
/// Result of retention policy evaluation.
/// </summary>
public record RetentionEvaluationResult
{
    /// <summary>
    /// Gets whether cleanup should proceed.
    /// </summary>
    public required bool ShouldCleanup { get; init; }

    /// <summary>
    /// Gets the snapshots that should be deleted.
    /// </summary>
    public required IReadOnlyList<SnapshotCleanupCandidate> CandidatesForDeletion { get; init; }

    /// <summary>
    /// Gets the reason for the cleanup decision.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the total space that would be reclaimed.
    /// </summary>
    public long EstimatedSpaceReclaimed { get; init; }

    /// <summary>
    /// Gets additional evaluation metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a result indicating cleanup should proceed.
    /// </summary>
    /// <param name="candidates">Snapshots to delete</param>
    /// <param name="reason">Reason for cleanup</param>
    /// <param name="estimatedSpaceReclaimed">Expected space savings</param>
    /// <returns>A positive retention evaluation result</returns>
    public static RetentionEvaluationResult CreatePositive(
        IReadOnlyList<SnapshotCleanupCandidate> candidates,
        string reason,
        long estimatedSpaceReclaimed = 0)
    {
        return new RetentionEvaluationResult
        {
            ShouldCleanup = true,
            CandidatesForDeletion = candidates,
            Reason = reason,
            EstimatedSpaceReclaimed = estimatedSpaceReclaimed
        };
    }

    /// <summary>
    /// Creates a result indicating no cleanup is needed.
    /// </summary>
    /// <param name="reason">Reason for not cleaning up</param>
    /// <returns>A negative retention evaluation result</returns>
    public static RetentionEvaluationResult CreateNegative(string reason)
    {
        return new RetentionEvaluationResult
        {
            ShouldCleanup = false,
            CandidatesForDeletion = [],
            Reason = reason
        };
    }
}

/// <summary>
/// Represents a snapshot candidate for cleanup.
/// </summary>
public record SnapshotCleanupCandidate
{
    /// <summary>
    /// Gets the snapshot metadata.
    /// </summary>
    public required SnapshotMetadata Metadata { get; init; }

    /// <summary>
    /// Gets the reason this snapshot is a cleanup candidate.
    /// </summary>
    public required string CleanupReason { get; init; }

    /// <summary>
    /// Gets the priority for cleanup (higher = clean up sooner).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Gets the estimated space that would be reclaimed.
    /// </summary>
    public long EstimatedSpaceReclaimed { get; init; }
}

/// <summary>
/// Result of policy validation.
/// </summary>
public record PolicyValidationResult
{
    /// <summary>
    /// Gets whether the policy is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation issues found.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Gets validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets additional validation metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a valid policy result.
    /// </summary>
    /// <param name="warnings">Optional warnings</param>
    /// <returns>A valid policy validation result</returns>
    public static PolicyValidationResult CreateValid(IReadOnlyList<string>? warnings = null)
    {
        return new PolicyValidationResult
        {
            IsValid = true,
            Warnings = warnings ?? []
        };
    }

    /// <summary>
    /// Creates an invalid policy result.
    /// </summary>
    /// <param name="issues">Validation issues</param>
    /// <param name="warnings">Optional warnings</param>
    /// <returns>An invalid policy validation result</returns>
    public static PolicyValidationResult CreateInvalid(
        IReadOnlyList<string> issues,
        IReadOnlyList<string>? warnings = null)
    {
        return new PolicyValidationResult
        {
            IsValid = false,
            Issues = issues,
            Warnings = warnings ?? []
        };
    }
}

/// <summary>
/// Stream statistics for policy evaluation.
/// </summary>
public record StreamStatistics
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the current event count.
    /// </summary>
    public long EventCount { get; init; }

    /// <summary>
    /// Gets the estimated stream size in bytes.
    /// </summary>
    public long EstimatedSize { get; init; }

    /// <summary>
    /// Gets the last activity timestamp.
    /// </summary>
    public DateTimeOffset LastActivity { get; init; }

    /// <summary>
    /// Gets the average event size.
    /// </summary>
    public double AverageEventSize => EventCount > 0 ? (double)EstimatedSize / EventCount : 0;
}
