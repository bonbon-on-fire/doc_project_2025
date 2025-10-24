namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Interface for handling state validation errors and providing recovery mechanisms.
/// Supports both automatic recovery strategies and manual intervention workflows.
/// </summary>
/// <typeparam name="T">The type of entity for error recovery</typeparam>
public interface IStateErrorRecovery<T> where T : class
{
    /// <summary>
    /// Gets the name of this error recovery handler for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Attempts automatic recovery from validation failures.
    /// Uses built-in recovery strategies like data correction, retry, or fallback values.
    /// </summary>
    /// <param name="operation">The state operation that failed validation</param>
    /// <param name="validationFailure">The validation failure details</param>
    /// <param name="cancellationToken">Token to cancel the recovery operation</param>
    /// <returns>Result of the automatic recovery attempt</returns>
    /// <exception cref="ArgumentNullException">Thrown when operation or validationFailure is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryResult> AttemptAutomaticRecoveryAsync(StateOperation<T> operation, StateValidationResult validationFailure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available recovery options for manual intervention.
    /// Provides human-readable options for resolving validation failures.
    /// </summary>
    /// <param name="operation">The state operation that failed validation</param>
    /// <param name="validationFailure">The validation failure details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of available recovery options</returns>
    /// <exception cref="ArgumentNullException">Thrown when operation or validationFailure is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<IEnumerable<RecoveryOption>> GetRecoveryOptionsAsync(StateOperation<T> operation, StateValidationResult validationFailure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a specific recovery strategy chosen by the user or system.
    /// </summary>
    /// <param name="operation">The state operation that failed validation</param>
    /// <param name="recoveryOption">The recovery option to execute</param>
    /// <param name="cancellationToken">Token to cancel the recovery operation</param>
    /// <returns>Result of the recovery execution</returns>
    /// <exception cref="ArgumentNullException">Thrown when operation or option is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryResult> ExecuteRecoveryAsync(StateOperation<T> operation, RecoveryOption recoveryOption, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to rollback a failed operation to restore previous state.
    /// Only applicable for operations that have already been partially executed.
    /// </summary>
    /// <param name="operation">The state operation to rollback</param>
    /// <param name="cancellationToken">Token to cancel the rollback operation</param>
    /// <returns>Result of the rollback attempt</returns>
    /// <exception cref="ArgumentNullException">Thrown when operation is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryResult> RollbackAsync(StateOperation<T> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether a recovery option is applicable to a specific validation failure.
    /// </summary>
    /// <param name="recoveryOption">The recovery option to validate</param>
    /// <param name="validationFailure">The validation failure details</param>
    /// <param name="cancellationToken">Token to cancel the validation</param>
    /// <returns>True if the recovery option is applicable, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when option or validationFailure is null</exception>
    Task<bool> IsRecoveryOptionApplicableAsync(RecoveryOption recoveryOption, StateValidationResult validationFailure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metrics about error recovery operations performed by this handler.
    /// </summary>
    /// <returns>Error recovery metrics for monitoring and analysis</returns>
    ErrorRecoveryMetrics GetMetrics();

    /// <summary>
    /// Gets the recovery history for a specific entity.
    /// Useful for troubleshooting recurring issues and understanding patterns.
    /// </summary>
    /// <param name="entityId">The entity ID to get recovery history for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of recovery history entries</returns>
    Task<IEnumerable<RecoveryHistoryEntry>> GetRecoveryHistoryAsync(string entityId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an error recovery attempt.
/// </summary>
public record RecoveryResult
{
    /// <summary>
    /// Gets whether the recovery attempt was successful.
    /// </summary>
    public required bool IsSuccessful { get; init; }

    /// <summary>
    /// Gets the recovery strategy that was used.
    /// </summary>
    public required RecoveryStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Gets the number of validation errors that were resolved.
    /// </summary>
    public int ErrorsResolved { get; init; }

    /// <summary>
    /// Gets the number of validation errors that remain unresolved.
    /// </summary>
    public int ErrorsRemaining { get; init; }

    /// <summary>
    /// Gets any error message if recovery failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets additional details about the recovery attempt.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets the duration of the recovery operation.
    /// </summary>
    public TimeSpan RecoveryDuration { get; init; }

    /// <summary>
    /// Gets the corrected entity data if recovery was successful.
    /// </summary>
    public object? CorrectedEntity { get; init; }

    /// <summary>
    /// Gets additional metadata about the recovery.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether the recovery requires further action.
    /// </summary>
    public bool RequiresFurtherAction => ErrorsRemaining > 0 || !IsSuccessful;

    /// <summary>
    /// Creates a successful recovery result.
    /// </summary>
    /// <param name="strategy">The strategy used</param>
    /// <param name="errorsResolved">Number of errors resolved</param>
    /// <param name="duration">Duration of the recovery</param>
    /// <param name="correctedEntity">The corrected entity data</param>
    /// <param name="details">Additional details</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful recovery result</returns>
    public static RecoveryResult Success(
        RecoveryStrategy strategy,
        int errorsResolved,
        TimeSpan duration,
        object? correctedEntity = null,
        string? details = null,
        Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult
        {
            IsSuccessful = true,
            StrategyUsed = strategy,
            ErrorsResolved = errorsResolved,
            ErrorsRemaining = 0,
            RecoveryDuration = duration,
            CorrectedEntity = correctedEntity,
            Details = details,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed recovery result.
    /// </summary>
    /// <param name="strategy">The strategy that was attempted</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="errorsRemaining">Number of unresolved errors</param>
    /// <param name="duration">Duration of the recovery attempt</param>
    /// <param name="details">Additional details</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed recovery result</returns>
    public static RecoveryResult Failed(
        RecoveryStrategy strategy,
        string errorMessage,
        int errorsRemaining,
        TimeSpan duration,
        string? details = null,
        Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult
        {
            IsSuccessful = false,
            StrategyUsed = strategy,
            ErrorsResolved = 0,
            ErrorsRemaining = errorsRemaining,
            ErrorMessage = errorMessage,
            RecoveryDuration = duration,
            Details = details,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a partial recovery result (some errors resolved, some remain).
    /// </summary>
    /// <param name="strategy">The strategy used</param>
    /// <param name="errorsResolved">Number of errors resolved</param>
    /// <param name="errorsRemaining">Number of errors remaining</param>
    /// <param name="duration">Duration of the recovery</param>
    /// <param name="correctedEntity">The partially corrected entity data</param>
    /// <param name="details">Additional details</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A partial recovery result</returns>
    public static RecoveryResult Partial(
        RecoveryStrategy strategy,
        int errorsResolved,
        int errorsRemaining,
        TimeSpan duration,
        object? correctedEntity = null,
        string? details = null,
        Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult
        {
            IsSuccessful = errorsResolved > 0, // Successful if any errors were resolved
            StrategyUsed = strategy,
            ErrorsResolved = errorsResolved,
            ErrorsRemaining = errorsRemaining,
            RecoveryDuration = duration,
            CorrectedEntity = correctedEntity,
            Details = details,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents a recovery option that can be executed to resolve validation failures.
/// </summary>
public record RecoveryOption
{
    /// <summary>
    /// Gets the unique identifier for this recovery option.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the human-readable name of the recovery option.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the detailed description of what this recovery option does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the recovery strategy this option implements.
    /// </summary>
    public required RecoveryStrategy Strategy { get; init; }

    /// <summary>
    /// Gets the parameters needed to execute this recovery option.
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = [];

    /// <summary>
    /// Gets whether this recovery option requires user approval before execution.
    /// </summary>
    public bool RequiresUserApproval { get; init; }

    /// <summary>
    /// Gets the estimated duration in seconds for this recovery option.
    /// </summary>
    public int EstimatedDurationSeconds { get; init; }

    /// <summary>
    /// Gets the confidence level (0-100) that this recovery will succeed.
    /// </summary>
    public int ConfidenceLevel { get; init; } = 50;

    /// <summary>
    /// Gets the risk level associated with this recovery option.
    /// </summary>
    public RecoveryRiskLevel RiskLevel { get; init; } = RecoveryRiskLevel.Medium;

    /// <summary>
    /// Gets the validation error codes this option can address.
    /// </summary>
    public List<ValidationErrorCode> ApplicableErrorCodes { get; init; } = [];

    /// <summary>
    /// Gets additional metadata about the recovery option.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new recovery option.
    /// </summary>
    /// <param name="id">Unique identifier</param>
    /// <param name="name">Human-readable name</param>
    /// <param name="description">Detailed description</param>
    /// <param name="strategy">Recovery strategy</param>
    /// <param name="parameters">Execution parameters</param>
    /// <param name="requiresApproval">Whether user approval is required</param>
    /// <param name="estimatedDuration">Estimated duration in seconds</param>
    /// <param name="confidenceLevel">Confidence level (0-100)</param>
    /// <param name="riskLevel">Risk level</param>
    /// <param name="applicableErrorCodes">Error codes this option can address</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A new recovery option</returns>
    public static RecoveryOption Create(
        string id,
        string name,
        string description,
        RecoveryStrategy strategy,
        Dictionary<string, object>? parameters = null,
        bool requiresApproval = false,
        int estimatedDuration = 30,
        int confidenceLevel = 50,
        RecoveryRiskLevel riskLevel = RecoveryRiskLevel.Medium,
        List<ValidationErrorCode>? applicableErrorCodes = null,
        Dictionary<string, object>? metadata = null)
    {
        return new RecoveryOption
        {
            Id = id,
            Name = name,
            Description = description,
            Strategy = strategy,
            Parameters = parameters ?? [],
            RequiresUserApproval = requiresApproval,
            EstimatedDurationSeconds = estimatedDuration,
            ConfidenceLevel = confidenceLevel,
            RiskLevel = riskLevel,
            ApplicableErrorCodes = applicableErrorCodes ?? [],
            Metadata = metadata
        };
    }
}

/// <summary>
/// Recovery strategies for handling validation failures.
/// </summary>
public enum RecoveryStrategy
{
    /// <summary>
    /// Retry the operation with exponential backoff.
    /// </summary>
    RetryWithDelay = 0,

    /// <summary>
    /// Correct the data based on validation rules.
    /// </summary>
    CorrectData = 1,

    /// <summary>
    /// Use default or fallback values for invalid properties.
    /// </summary>
    UseDefaultValue = 2,

    /// <summary>
    /// Remove invalid properties from the entity.
    /// </summary>
    RemoveInvalidProperties = 3,

    /// <summary>
    /// Transform the data to match validation requirements.
    /// </summary>
    TransformData = 4,

    /// <summary>
    /// Skip validation for this specific operation.
    /// </summary>
    SkipValidation = 5,

    /// <summary>
    /// Rollback the operation to previous state.
    /// </summary>
    Rollback = 6,

    /// <summary>
    /// Requires manual intervention to resolve.
    /// </summary>
    ManualIntervention = 7,

    /// <summary>
    /// Split the operation into smaller, valid operations.
    /// </summary>
    SplitOperation = 8,

    /// <summary>
    /// Merge conflicting data using predefined rules.
    /// </summary>
    MergeData = 9
}

/// <summary>
/// Risk levels for recovery operations.
/// </summary>
public enum RecoveryRiskLevel
{
    /// <summary>
    /// Low risk - unlikely to cause issues.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Medium risk - may cause minor issues.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High risk - may cause significant issues.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical risk - may cause system-wide issues.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Metrics for error recovery operations.
/// </summary>
public record ErrorRecoveryMetrics
{
    /// <summary>
    /// Gets the total number of recovery attempts.
    /// </summary>
    public long TotalRecoveryAttempts { get; init; }

    /// <summary>
    /// Gets the number of successful recoveries.
    /// </summary>
    public long SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Gets the number of failed recoveries.
    /// </summary>
    public long FailedRecoveries { get; init; }

    /// <summary>
    /// Gets the number of partial recoveries.
    /// </summary>
    public long PartialRecoveries { get; init; }

    /// <summary>
    /// Gets the average recovery time in milliseconds.
    /// </summary>
    public double AverageRecoveryTimeMs { get; init; }

    /// <summary>
    /// Gets the maximum recovery time recorded in milliseconds.
    /// </summary>
    public double MaxRecoveryTimeMs { get; init; }

    /// <summary>
    /// Gets the count of each recovery strategy used.
    /// </summary>
    public Dictionary<RecoveryStrategy, long> StrategyUsageCounts { get; init; } = [];

    /// <summary>
    /// Gets the count of recoveries by error code.
    /// </summary>
    public Dictionary<ValidationErrorCode, long> ErrorCodeRecoveryCounts { get; init; } = [];

    /// <summary>
    /// Gets the timestamp of the last recovery attempt.
    /// </summary>
    public DateTime LastRecoveryAt { get; init; }

    /// <summary>
    /// Gets additional recovery-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Gets the recovery success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalRecoveryAttempts > 0 ? (double)SuccessfulRecoveries / TotalRecoveryAttempts * 100 : 100;

    /// <summary>
    /// Gets the recovery failure rate as a percentage.
    /// </summary>
    public double FailureRate => TotalRecoveryAttempts > 0 ? (double)FailedRecoveries / TotalRecoveryAttempts * 100 : 0;

    /// <summary>
    /// Creates empty error recovery metrics.
    /// </summary>
    /// <returns>Empty error recovery metrics</returns>
    public static ErrorRecoveryMetrics Empty()
    {
        return new ErrorRecoveryMetrics
        {
            TotalRecoveryAttempts = 0,
            SuccessfulRecoveries = 0,
            FailedRecoveries = 0,
            PartialRecoveries = 0,
            AverageRecoveryTimeMs = 0,
            MaxRecoveryTimeMs = 0,
            LastRecoveryAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Historical record of a recovery operation.
/// </summary>
public record RecoveryHistoryEntry
{
    /// <summary>
    /// Gets the unique identifier for this recovery attempt.
    /// </summary>
    public required string RecoveryId { get; init; }

    /// <summary>
    /// Gets the entity ID that was being recovered.
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Gets the timestamp when recovery was attempted.
    /// </summary>
    public DateTime AttemptedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the recovery strategy that was used.
    /// </summary>
    public required RecoveryStrategy Strategy { get; init; }

    /// <summary>
    /// Gets whether the recovery was successful.
    /// </summary>
    public required bool WasSuccessful { get; init; }

    /// <summary>
    /// Gets the validation errors that were being addressed.
    /// </summary>
    public List<ValidationError> OriginalErrors { get; init; } = [];

    /// <summary>
    /// Gets the number of errors that were resolved.
    /// </summary>
    public int ErrorsResolved { get; init; }

    /// <summary>
    /// Gets the duration of the recovery operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets any error message if recovery failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets additional details about the recovery attempt.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets metadata about the recovery operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}