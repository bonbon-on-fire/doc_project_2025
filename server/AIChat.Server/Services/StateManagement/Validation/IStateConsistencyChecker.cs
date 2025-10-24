namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Interface for checking state consistency across different storage backends.
/// Ensures data integrity between Orleans grain state and DirectDB storage.
/// </summary>
/// <typeparam name="T">The type of entity being checked for consistency</typeparam>
public interface IStateConsistencyChecker<T> where T : class
{
    /// <summary>
    /// Gets the name of this consistency checker for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Checks consistency between Orleans and DirectDB state for a single entity.
    /// Compares entity data across storage backends and identifies discrepancies.
    /// </summary>
    /// <param name="entityId">The ID of the entity to check for consistency</param>
    /// <param name="cancellationToken">Token to cancel the consistency check operation</param>
    /// <returns>Consistency check result with detailed issue information</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ConsistencyCheckResult> CheckConsistencyAsync(string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks consistency for multiple entities in a batch operation.
    /// More efficient than individual checks for large sets of entities.
    /// </summary>
    /// <param name="entityIds">The collection of entity IDs to check</param>
    /// <param name="cancellationToken">Token to cancel the consistency check operation</param>
    /// <returns>Consistency check result covering all specified entities</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityIds is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ConsistencyCheckResult> CheckBatchConsistencyAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive consistency check across all entities of this type.
    /// Can be filtered using query parameters to check specific subsets.
    /// </summary>
    /// <param name="query">Optional query to filter which entities to check</param>
    /// <param name="cancellationToken">Token to cancel the consistency check operation</param>
    /// <returns>Consistency check result for the global check</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ConsistencyCheckResult> CheckGlobalConsistencyAsync(StateQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reconcile inconsistencies found between storage backends.
    /// Uses configured reconciliation strategy (Orleans wins, DirectDB wins, or merge).
    /// </summary>
    /// <param name="entityId">The ID of the entity to reconcile</param>
    /// <param name="strategy">The reconciliation strategy to use</param>
    /// <param name="cancellationToken">Token to cancel the reconciliation operation</param>
    /// <returns>Result of the reconciliation attempt</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<ReconciliationResult> ReconcileAsync(string entityId, ReconciliationStrategy strategy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics about consistency check operations performed by this checker.
    /// </summary>
    /// <returns>Consistency check metrics for monitoring and analysis</returns>
    ConsistencyCheckMetrics GetMetrics();

    /// <summary>
    /// Gets the current consistency status summary for this entity type.
    /// Provides a high-level overview of consistency across all entities.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Summary of consistency status</returns>
    Task<ConsistencyStatusSummary> GetConsistencyStatusSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a consistency check operation.
/// </summary>
public record ConsistencyCheckResult
{
    /// <summary>
    /// Gets whether all checked entities are consistent across storage backends.
    /// </summary>
    public required bool IsConsistent { get; init; }

    /// <summary>
    /// Gets the list of consistency issues found during the check.
    /// </summary>
    public List<ConsistencyIssue> Issues { get; init; } = [];

    /// <summary>
    /// Gets the timestamp when the consistency check was performed.
    /// </summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the duration of the consistency check operation.
    /// </summary>
    public TimeSpan CheckDuration { get; init; }

    /// <summary>
    /// Gets the number of entities that were checked.
    /// </summary>
    public int EntitiesChecked { get; init; }

    /// <summary>
    /// Gets additional metadata about the consistency check.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets the total number of consistency issues found.
    /// </summary>
    public int TotalIssues => Issues.Count;

    /// <summary>
    /// Gets the number of critical consistency issues.
    /// </summary>
    public int CriticalIssues => Issues.Count(i => i.Severity == ConsistencyIssueSeverity.Critical);

    /// <summary>
    /// Gets the number of error-level consistency issues.
    /// </summary>
    public int ErrorIssues => Issues.Count(i => i.Severity == ConsistencyIssueSeverity.Error);

    /// <summary>
    /// Gets whether there are any critical or error-level issues.
    /// </summary>
    public bool HasSeriousIssues => CriticalIssues > 0 || ErrorIssues > 0;

    /// <summary>
    /// Creates a consistent result (no issues found).
    /// </summary>
    /// <param name="entitiesChecked">Number of entities that were checked</param>
    /// <param name="checkDuration">Duration of the check operation</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A consistent result</returns>
    public static ConsistencyCheckResult Consistent(int entitiesChecked, TimeSpan checkDuration, Dictionary<string, object>? metadata = null)
    {
        return new ConsistencyCheckResult
        {
            IsConsistent = true,
            EntitiesChecked = entitiesChecked,
            CheckDuration = checkDuration,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates an inconsistent result with issues.
    /// </summary>
    /// <param name="issues">The consistency issues found</param>
    /// <param name="entitiesChecked">Number of entities that were checked</param>
    /// <param name="checkDuration">Duration of the check operation</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>An inconsistent result</returns>
    public static ConsistencyCheckResult Inconsistent(IEnumerable<ConsistencyIssue> issues, int entitiesChecked, TimeSpan checkDuration, Dictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return new ConsistencyCheckResult
        {
            IsConsistent = false,
            Issues = [.. issues],
            EntitiesChecked = entitiesChecked,
            CheckDuration = checkDuration,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents a specific consistency issue between storage backends.
/// </summary>
public record ConsistencyIssue
{
    /// <summary>
    /// Gets the ID of the entity with the consistency issue.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Gets the name of the property that has inconsistent values.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the type of consistency issue.
    /// </summary>
    public required ConsistencyIssueType IssueType { get; init; }

    /// <summary>
    /// Gets the value from Orleans grain state.
    /// </summary>
    public object? OrleansValue { get; init; }

    /// <summary>
    /// Gets the value from DirectDB storage.
    /// </summary>
    public object? DirectDbValue { get; init; }

    /// <summary>
    /// Gets a human-readable description of the issue.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the severity level of this consistency issue.
    /// </summary>
    public ConsistencyIssueSeverity Severity { get; init; } = ConsistencyIssueSeverity.Warning;

    /// <summary>
    /// Gets the timestamp when this issue was detected.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets additional metadata about the issue.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new consistency issue.
    /// </summary>
    /// <param name="entityId">The entity ID with the issue</param>
    /// <param name="propertyName">The property with inconsistent values</param>
    /// <param name="issueType">The type of consistency issue</param>
    /// <param name="orleansValue">Value from Orleans</param>
    /// <param name="directDbValue">Value from DirectDB</param>
    /// <param name="description">Human-readable description</param>
    /// <param name="severity">Severity level</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A new consistency issue</returns>
    public static ConsistencyIssue Create(
        string entityId,
        string propertyName,
        ConsistencyIssueType issueType,
        object? orleansValue = null,
        object? directDbValue = null,
        string? description = null,
        ConsistencyIssueSeverity severity = ConsistencyIssueSeverity.Warning,
        Dictionary<string, object>? metadata = null)
    {
        return new ConsistencyIssue
        {
            EntityId = entityId,
            PropertyName = propertyName,
            IssueType = issueType,
            OrleansValue = orleansValue,
            DirectDbValue = directDbValue,
            Description = description,
            Severity = severity,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Types of consistency issues that can occur between storage backends.
/// </summary>
public enum ConsistencyIssueType
{
    /// <summary>
    /// Property values differ between Orleans and DirectDB.
    /// </summary>
    ValueMismatch = 0,

    /// <summary>
    /// Entity exists in DirectDB but not in Orleans.
    /// </summary>
    MissingInOrleans = 1,

    /// <summary>
    /// Entity exists in Orleans but not in DirectDB.
    /// </summary>
    MissingInDirectDb = 2,

    /// <summary>
    /// Property types don't match between storage backends.
    /// </summary>
    TypeMismatch = 3,

    /// <summary>
    /// Version information indicates data skew.
    /// </summary>
    VersionSkew = 4,

    /// <summary>
    /// Timestamp differences indicate temporal inconsistency.
    /// </summary>
    TimestampDrift = 5,

    /// <summary>
    /// Structural differences in complex objects.
    /// </summary>
    StructuralMismatch = 6
}

/// <summary>
/// Severity levels for consistency issues.
/// </summary>
public enum ConsistencyIssueSeverity
{
    /// <summary>
    /// Informational - minor difference that doesn't affect functionality.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning - noticeable difference that should be monitored.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error - significant difference that may affect functionality.
    /// </summary>
    Error = 2,

    /// <summary>
    /// Critical - major difference that will cause system issues.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Result of a reconciliation operation.
/// </summary>
public record ReconciliationResult
{
    /// <summary>
    /// Gets whether the reconciliation was successful.
    /// </summary>
    public required bool IsSuccessful { get; init; }

    /// <summary>
    /// Gets the reconciliation strategy that was used.
    /// </summary>
    public required ReconciliationStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Gets the number of issues that were resolved.
    /// </summary>
    public int IssuesResolved { get; init; }

    /// <summary>
    /// Gets the number of issues that could not be resolved.
    /// </summary>
    public int IssuesRemaining { get; init; }

    /// <summary>
    /// Gets any error message if reconciliation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the duration of the reconciliation operation.
    /// </summary>
    public TimeSpan ReconciliationDuration { get; init; }

    /// <summary>
    /// Gets additional metadata about the reconciliation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful reconciliation result.
    /// </summary>
    /// <param name="strategy">The strategy used</param>
    /// <param name="issuesResolved">Number of issues resolved</param>
    /// <param name="duration">Duration of the operation</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful reconciliation result</returns>
    public static ReconciliationResult Success(ReconciliationStrategy strategy, int issuesResolved, TimeSpan duration, Dictionary<string, object>? metadata = null)
    {
        return new ReconciliationResult
        {
            IsSuccessful = true,
            StrategyUsed = strategy,
            IssuesResolved = issuesResolved,
            IssuesRemaining = 0,
            ReconciliationDuration = duration,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed reconciliation result.
    /// </summary>
    /// <param name="strategy">The strategy that was attempted</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="issuesRemaining">Number of unresolved issues</param>
    /// <param name="duration">Duration of the operation</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed reconciliation result</returns>
    public static ReconciliationResult Failed(ReconciliationStrategy strategy, string errorMessage, int issuesRemaining, TimeSpan duration, Dictionary<string, object>? metadata = null)
    {
        return new ReconciliationResult
        {
            IsSuccessful = false,
            StrategyUsed = strategy,
            IssuesResolved = 0,
            IssuesRemaining = issuesRemaining,
            ErrorMessage = errorMessage,
            ReconciliationDuration = duration,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Strategies for reconciling consistency issues.
/// </summary>
public enum ReconciliationStrategy
{
    /// <summary>
    /// Orleans grain state is considered authoritative.
    /// </summary>
    OrleansWins = 0,

    /// <summary>
    /// DirectDB state is considered authoritative.
    /// </summary>
    DirectDbWins = 1,

    /// <summary>
    /// Use the most recently modified value.
    /// </summary>
    LastWriteWins = 2,

    /// <summary>
    /// Attempt to merge the values intelligently.
    /// </summary>
    Merge = 3,

    /// <summary>
    /// Requires manual intervention to resolve.
    /// </summary>
    Manual = 4
}

/// <summary>
/// Metrics for consistency check operations.
/// </summary>
public record ConsistencyCheckMetrics
{
    /// <summary>
    /// Gets the total number of consistency checks performed.
    /// </summary>
    public long TotalChecks { get; init; }

    /// <summary>
    /// Gets the number of checks that found no issues.
    /// </summary>
    public long ConsistentChecks { get; init; }

    /// <summary>
    /// Gets the number of checks that found inconsistencies.
    /// </summary>
    public long InconsistentChecks { get; init; }

    /// <summary>
    /// Gets the average consistency check time in milliseconds.
    /// </summary>
    public double AverageCheckTimeMs { get; init; }

    /// <summary>
    /// Gets the maximum check time recorded in milliseconds.
    /// </summary>
    public double MaxCheckTimeMs { get; init; }

    /// <summary>
    /// Gets the count of each issue type encountered.
    /// </summary>
    public Dictionary<ConsistencyIssueType, long> IssueTypeCounts { get; init; } = [];

    /// <summary>
    /// Gets the timestamp of the last consistency check.
    /// </summary>
    public DateTime LastCheckAt { get; init; }

    /// <summary>
    /// Gets additional checker-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Gets the consistency rate as a percentage.
    /// </summary>
    public double ConsistencyRate => TotalChecks > 0 ? (double)ConsistentChecks / TotalChecks * 100 : 100;

    /// <summary>
    /// Creates empty consistency check metrics.
    /// </summary>
    /// <returns>Empty consistency check metrics</returns>
    public static ConsistencyCheckMetrics Empty()
    {
        return new ConsistencyCheckMetrics
        {
            TotalChecks = 0,
            ConsistentChecks = 0,
            InconsistentChecks = 0,
            AverageCheckTimeMs = 0,
            MaxCheckTimeMs = 0,
            LastCheckAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Summary of consistency status across all entities of a type.
/// </summary>
public record ConsistencyStatusSummary
{
    /// <summary>
    /// Gets the total number of entities of this type.
    /// </summary>
    public long TotalEntities { get; init; }

    /// <summary>
    /// Gets the number of entities that are consistent.
    /// </summary>
    public long ConsistentEntities { get; init; }

    /// <summary>
    /// Gets the number of entities with consistency issues.
    /// </summary>
    public long InconsistentEntities { get; init; }

    /// <summary>
    /// Gets the number of entities that have not been checked recently.
    /// </summary>
    public long UncheckedEntities { get; init; }

    /// <summary>
    /// Gets the timestamp of the last global consistency check.
    /// </summary>
    public DateTime LastGlobalCheckAt { get; init; }

    /// <summary>
    /// Gets the overall consistency percentage.
    /// </summary>
    public double ConsistencyPercentage => TotalEntities > 0 ? (double)ConsistentEntities / TotalEntities * 100 : 100;

    /// <summary>
    /// Gets whether the overall consistency is healthy.
    /// </summary>
    public bool IsHealthy => ConsistencyPercentage >= 95.0; // 95% consistency threshold
}