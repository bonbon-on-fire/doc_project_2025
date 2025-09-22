namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Core interface for validating state entities before persistence operations.
/// Provides validation hooks for all CRUD operations with support for batch validation.
/// </summary>
/// <typeparam name="T">The type of entity being validated</typeparam>
public interface IStateValidator<T> where T : class
{
    /// <summary>
    /// Gets the name of this validator for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validates an entity for creation.
    /// Performs both base validation (data annotations, null checks) and custom business logic validation.
    /// </summary>
    /// <param name="entity">The entity to validate for creation</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result indicating success or failure with detailed error information</returns>
    /// <exception cref="ArgumentNullException">Thrown when entity is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateCreateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an entity for update.
    /// Can compare against existing entity state to validate changes and transitions.
    /// </summary>
    /// <param name="entityId">The ID of the entity being updated</param>
    /// <param name="entity">The updated entity to validate</param>
    /// <param name="existingEntity">The current entity state for comparison (optional)</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result indicating success or failure with detailed error information</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityId or entity is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateUpdateAsync(string entityId, T entity, T? existingEntity = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an entity for deletion.
    /// Can check for referential integrity constraints and business rules that prevent deletion.
    /// </summary>
    /// <param name="entityId">The ID of the entity being deleted</param>
    /// <param name="existingEntity">The current entity state (optional)</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result indicating whether deletion is allowed</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateDeleteAsync(string entityId, T? existingEntity = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a patch operation with partial updates.
    /// Only validates the properties being updated, not the entire entity.
    /// </summary>
    /// <param name="entityId">The ID of the entity being patched</param>
    /// <param name="updates">Dictionary of property names and new values</param>
    /// <param name="existingEntity">The current entity state (optional)</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result for the patch operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when entityId or updates is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidatePatchAsync(string entityId, Dictionary<string, object> updates, T? existingEntity = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates multiple operations as a batch.
    /// Can perform cross-entity validation and ensure batch consistency.
    /// </summary>
    /// <param name="operations">The collection of operations to validate as a batch</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result for the entire batch</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateBatchAsync(IEnumerable<StateOperation<T>> operations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an entity without specifying the operation type.
    /// Performs general validation that applies regardless of operation context.
    /// </summary>
    /// <param name="entity">The entity to validate</param>
    /// <param name="cancellationToken">Token to cancel the validation operation</param>
    /// <returns>Validation result for general entity validation</returns>
    /// <exception cref="ArgumentNullException">Thrown when entity is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets validation rules that apply to the specified operation type.
    /// Useful for introspection and documentation purposes.
    /// </summary>
    /// <param name="operation">The operation type to get rules for</param>
    /// <returns>Collection of validation rule descriptions</returns>
    IEnumerable<ValidationRuleInfo> GetValidationRules(ValidationOperation operation);

    /// <summary>
    /// Gets metrics about validation operations performed by this validator.
    /// </summary>
    /// <returns>Validation metrics for monitoring and performance analysis</returns>
    StateValidationMetrics GetMetrics();
}

/// <summary>
/// Information about a validation rule for documentation and introspection.
/// </summary>
public record ValidationRuleInfo
{
    /// <summary>
    /// Gets the unique identifier for this validation rule.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Gets the human-readable name of the validation rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of what this rule validates.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the property or field name this rule applies to.
    /// </summary>
    public string? PropertyName { get; init; }

    /// <summary>
    /// Gets the validation error code this rule produces.
    /// </summary>
    public ValidationErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets whether this rule is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the severity level of violations.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Gets additional metadata about the rule.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new validation rule info.
    /// </summary>
    /// <param name="ruleId">Unique identifier for the rule</param>
    /// <param name="name">Human-readable name</param>
    /// <param name="description">Description of what the rule validates</param>
    /// <param name="propertyName">Property name the rule applies to</param>
    /// <param name="errorCode">Error code for violations</param>
    /// <param name="severity">Severity level</param>
    /// <param name="metadata">Additional metadata</param>
    /// <returns>A new validation rule info instance</returns>
    public static ValidationRuleInfo Create(
        string ruleId,
        string name,
        string description,
        string? propertyName = null,
        ValidationErrorCode errorCode = ValidationErrorCode.Custom,
        ValidationSeverity severity = ValidationSeverity.Error,
        Dictionary<string, object>? metadata = null)
    {
        return new ValidationRuleInfo
        {
            RuleId = ruleId,
            Name = name,
            Description = description,
            PropertyName = propertyName,
            ErrorCode = errorCode,
            Severity = severity,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Severity level for validation rules.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational - does not prevent operation.
    /// </summary>
    Info,

    /// <summary>
    /// Warning - logged but does not prevent operation.
    /// </summary>
    Warning,

    /// <summary>
    /// Error - prevents operation from proceeding.
    /// </summary>
    Error,

    /// <summary>
    /// Critical - indicates a serious system issue.
    /// </summary>
    Critical
}

/// <summary>
/// Metrics for state validation operations.
/// </summary>
public record StateValidationMetrics
{
    /// <summary>
    /// Gets the total number of validation operations performed.
    /// </summary>
    public long TotalValidations { get; init; }

    /// <summary>
    /// Gets the number of successful validations.
    /// </summary>
    public long SuccessfulValidations { get; init; }

    /// <summary>
    /// Gets the number of failed validations.
    /// </summary>
    public long FailedValidations { get; init; }

    /// <summary>
    /// Gets the average validation time in milliseconds.
    /// </summary>
    public double AverageValidationTimeMs { get; init; }

    /// <summary>
    /// Gets the maximum validation time recorded in milliseconds.
    /// </summary>
    public double MaxValidationTimeMs { get; init; }

    /// <summary>
    /// Gets the count of each error code encountered.
    /// </summary>
    public Dictionary<ValidationErrorCode, long> ErrorCounts { get; init; } = [];

    /// <summary>
    /// Gets the count of validations by operation type.
    /// </summary>
    public Dictionary<string, long> OperationTypeCounts { get; init; } = [];

    /// <summary>
    /// Gets the timestamp of the last validation operation.
    /// </summary>
    public DateTime LastValidationAt { get; init; }

    /// <summary>
    /// Gets additional validator-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalValidations > 0 ? (double)SuccessfulValidations / TotalValidations * 100 : 100;

    /// <summary>
    /// Gets the failure rate as a percentage.
    /// </summary>
    public double FailureRate => TotalValidations > 0 ? (double)FailedValidations / TotalValidations * 100 : 0;

    /// <summary>
    /// Creates empty validation metrics.
    /// </summary>
    /// <returns>Empty state validation metrics</returns>
    public static StateValidationMetrics Empty()
    {
        return new StateValidationMetrics
        {
            TotalValidations = 0,
            SuccessfulValidations = 0,
            FailedValidations = 0,
            AverageValidationTimeMs = 0,
            MaxValidationTimeMs = 0,
            LastValidationAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates validation metrics with the specified values.
    /// </summary>
    /// <param name="total">Total validations</param>
    /// <param name="successful">Successful validations</param>
    /// <param name="failed">Failed validations</param>
    /// <param name="avgTimeMs">Average time in milliseconds</param>
    /// <param name="maxTimeMs">Maximum time in milliseconds</param>
    /// <param name="errorCounts">Error counts by code</param>
    /// <param name="operationCounts">Operation counts by type</param>
    /// <param name="additionalMetrics">Additional metrics</param>
    /// <returns>State validation metrics instance</returns>
    public static StateValidationMetrics Create(
        long total,
        long successful,
        long failed,
        double avgTimeMs = 0,
        double maxTimeMs = 0,
        Dictionary<ValidationErrorCode, long>? errorCounts = null,
        Dictionary<string, long>? operationCounts = null,
        Dictionary<string, object>? additionalMetrics = null)
    {
        return new StateValidationMetrics
        {
            TotalValidations = total,
            SuccessfulValidations = successful,
            FailedValidations = failed,
            AverageValidationTimeMs = avgTimeMs,
            MaxValidationTimeMs = maxTimeMs,
            ErrorCounts = errorCounts ?? [],
            OperationTypeCounts = operationCounts ?? [],
            LastValidationAt = DateTime.UtcNow,
            AdditionalMetrics = additionalMetrics
        };
    }
}