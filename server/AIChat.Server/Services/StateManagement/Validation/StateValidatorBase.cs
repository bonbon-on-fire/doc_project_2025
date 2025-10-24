using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;

namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Base implementation for state validators.
/// Provides common validation logic and extensibility points following the OperationCommandBase pattern.
/// Implements data annotation validation, basic constraint checking, and metrics collection.
/// </summary>
/// <typeparam name="T">The type of entity being validated</typeparam>
public abstract class StateValidatorBase<T> : IStateValidator<T> where T : class
{
    private readonly ILogger<StateValidatorBase<T>> _logger;
    private readonly ValidationMetricsCollector _metricsCollector;

    /// <summary>
    /// Gets the name of this validator for logging and metrics.
    /// </summary>
    public virtual string Name => GetType().Name;

    /// <summary>
    /// Initializes a new instance of the state validator base.
    /// </summary>
    /// <param name="logger">Logger for validation operations</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <exception cref="ArgumentNullException">Thrown when logger or serviceProvider is null</exception>
    protected StateValidatorBase(ILogger<StateValidatorBase<T>> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _metricsCollector = new ValidationMetricsCollector();
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidateCreateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Validating entity for creation: {EntityType}", typeof(T).Name);

            var baseResult = await ValidateBaseAsync(entity, ValidationOperation.Create, cancellationToken);
            var customResult = await ValidateCustomAsync(entity, ValidationOperation.Create, cancellationToken);
            var combinedResult = StateValidationResult.Combine(baseResult, customResult);

            RecordValidationMetrics(ValidationOperation.Create, combinedResult, stopwatch.Elapsed);
            return combinedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during create validation for {EntityType}", typeof(T).Name);
            RecordValidationMetrics(ValidationOperation.Create, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidateUpdateAsync(string entityId, T entity, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(entity);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Validating entity for update: {EntityType}, ID: {EntityId}", typeof(T).Name, entityId);

            var baseResult = await ValidateBaseAsync(entity, ValidationOperation.Update, cancellationToken);
            var customResult = await ValidateCustomUpdateAsync(entityId, entity, existingEntity, cancellationToken);
            var combinedResult = StateValidationResult.Combine(baseResult, customResult);

            RecordValidationMetrics(ValidationOperation.Update, combinedResult, stopwatch.Elapsed);
            return combinedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during update validation for {EntityType}, ID: {EntityId}", typeof(T).Name, entityId);
            RecordValidationMetrics(ValidationOperation.Update, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidateDeleteAsync(string entityId, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Validating entity for deletion: {EntityType}, ID: {EntityId}", typeof(T).Name, entityId);

            var customResult = await ValidateCustomDeleteAsync(entityId, existingEntity, cancellationToken);

            RecordValidationMetrics(ValidationOperation.Delete, customResult, stopwatch.Elapsed);
            return customResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during delete validation for {EntityType}, ID: {EntityId}", typeof(T).Name, entityId);
            RecordValidationMetrics(ValidationOperation.Delete, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidatePatchAsync(string entityId, Dictionary<string, object> updates, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(updates);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Validating entity for patch: {EntityType}, ID: {EntityId}, Updates: {UpdateCount}",
                typeof(T).Name, entityId, updates.Count);

            var customResult = await ValidateCustomPatchAsync(entityId, updates, existingEntity, cancellationToken);

            RecordValidationMetrics(ValidationOperation.Patch, customResult, stopwatch.Elapsed);
            return customResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during patch validation for {EntityType}, ID: {EntityId}", typeof(T).Name, entityId);
            RecordValidationMetrics(ValidationOperation.Patch, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidateBatchAsync(IEnumerable<StateOperation<T>> operations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var operationList = operations.ToList();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Validating batch of {OperationCount} operations for {EntityType}",
                operationList.Count, typeof(T).Name);

            if (operationList.Count == 0)
            {
                return StateValidationResult.Success();
            }

            // Validate each operation individually first
            var individualResults = new List<StateValidationResult>();
            foreach (var operation in operationList)
            {
                var result = await ValidateOperationAsync(operation, cancellationToken);
                individualResults.Add(result);
            }

            // Combine individual results
            var combinedResult = StateValidationResult.Combine(individualResults);

            // If individual validations passed, perform batch-specific validation
            if (combinedResult.IsValid)
            {
                var batchResult = await ValidateCustomBatchAsync(operationList, cancellationToken);
                combinedResult = StateValidationResult.Combine(combinedResult, batchResult);
            }

            RecordBatchValidationMetrics(operationList.Count, combinedResult, stopwatch.Elapsed);
            return combinedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch validation for {EntityType}", typeof(T).Name);
            RecordBatchValidationMetrics(operationList.Count, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("BatchOperation", "Internal batch validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public async Task<StateValidationResult> ValidateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Performing general validation for {EntityType}", typeof(T).Name);

            var baseResult = await ValidateBaseAsync(entity, ValidationOperation.Create, cancellationToken);
            var customResult = await ValidateCustomAsync(entity, ValidationOperation.Create, cancellationToken);
            var combinedResult = StateValidationResult.Combine(baseResult, customResult);

            RecordValidationMetrics(ValidationOperation.Create, combinedResult, stopwatch.Elapsed);
            return combinedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during general validation for {EntityType}", typeof(T).Name);
            RecordValidationMetrics(ValidationOperation.Create, null, stopwatch.Elapsed, ex);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public virtual IEnumerable<ValidationRuleInfo> GetValidationRules(ValidationOperation operation)
    {
        var rules = new List<ValidationRuleInfo>();

        // Add data annotation rules
        rules.AddRange(StateValidatorBase<T>.GetDataAnnotationRules());

        // Add custom rules from derived class
        rules.AddRange(GetCustomValidationRules(operation));

        return rules;
    }

    /// <inheritdoc />
    public StateValidationMetrics GetMetrics()
    {
        return _metricsCollector.GetMetrics();
    }

    /// <summary>
    /// Performs base validation including data annotations and basic constraints.
    /// Override this method to customize base validation behavior.
    /// </summary>
    /// <param name="entity">The entity to validate</param>
    /// <param name="operation">The validation operation type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Base validation result</returns>
    protected virtual async Task<StateValidationResult> ValidateBaseAsync(T entity, ValidationOperation operation, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Data annotation validation
        var dataAnnotationErrors = ValidateDataAnnotations(entity);
        errors.AddRange(dataAnnotationErrors);

        // Basic null/empty checks
        var basicErrors = StateValidatorBase<T>.ValidateBasicConstraints(entity, operation);
        errors.AddRange(basicErrors);

        return await Task.FromResult(errors.Count > 0
            ? StateValidationResult.Failed(errors, warnings.Count > 0 ? warnings : null)
            : StateValidationResult.Success(warnings.Count > 0 ? warnings : null));
    }

    /// <summary>
    /// Custom validation hook for derived classes.
    /// Implement entity-specific validation logic here.
    /// </summary>
    /// <param name="entity">The entity to validate</param>
    /// <param name="operation">The validation operation type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Custom validation result</returns>
    protected abstract Task<StateValidationResult> ValidateCustomAsync(T entity, ValidationOperation operation, CancellationToken cancellationToken);

    /// <summary>
    /// Custom update validation hook for derived classes.
    /// Default implementation calls ValidateCustomAsync.
    /// </summary>
    /// <param name="entityId">The entity ID being updated</param>
    /// <param name="entity">The updated entity</param>
    /// <param name="existingEntity">The existing entity for comparison</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Custom update validation result</returns>
    protected virtual Task<StateValidationResult> ValidateCustomUpdateAsync(string entityId, T entity, T? existingEntity, CancellationToken cancellationToken)
    {
        return ValidateCustomAsync(entity, ValidationOperation.Update, cancellationToken);
    }

    /// <summary>
    /// Custom delete validation hook for derived classes.
    /// Default implementation returns success.
    /// </summary>
    /// <param name="entityId">The entity ID being deleted</param>
    /// <param name="existingEntity">The existing entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Custom delete validation result</returns>
    protected virtual Task<StateValidationResult> ValidateCustomDeleteAsync(string entityId, T? existingEntity, CancellationToken cancellationToken)
    {
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <summary>
    /// Custom patch validation hook for derived classes.
    /// Default implementation validates each update individually.
    /// </summary>
    /// <param name="entityId">The entity ID being patched</param>
    /// <param name="updates">The partial updates</param>
    /// <param name="existingEntity">The existing entity</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Custom patch validation result</returns>
    protected virtual Task<StateValidationResult> ValidateCustomPatchAsync(string entityId, Dictionary<string, object> updates, T? existingEntity, CancellationToken cancellationToken)
    {
        // Default implementation: validate each property update
        var errors = new List<ValidationError>();

        foreach (var update in updates)
        {
            var propertyInfo = typeof(T).GetProperty(update.Key);
            if (propertyInfo == null)
            {
                errors.Add(ValidationError.Create(
                    update.Key,
                    $"Property '{update.Key}' does not exist on type {typeof(T).Name}",
                    ValidationErrorCode.Custom,
                    update.Value,
                    entityId));
                continue;
            }

            // Basic type checking
            if (update.Value != null && !propertyInfo.PropertyType.IsAssignableFrom(update.Value.GetType()))
            {
                errors.Add(ValidationError.Create(
                    update.Key,
                    $"Value type '{update.Value.GetType().Name}' is not compatible with property type '{propertyInfo.PropertyType.Name}'",
                    ValidationErrorCode.InvalidFormat,
                    update.Value,
                    entityId));
            }
        }

        return Task.FromResult(errors.Count > 0
            ? StateValidationResult.Failed(errors)
            : StateValidationResult.Success());
    }

    /// <summary>
    /// Custom batch validation hook for derived classes.
    /// Default implementation returns success.
    /// </summary>
    /// <param name="operations">The batch of operations to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Custom batch validation result</returns>
    protected virtual Task<StateValidationResult> ValidateCustomBatchAsync(IList<StateOperation<T>> operations, CancellationToken cancellationToken)
    {
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <summary>
    /// Gets custom validation rules for introspection.
    /// Override in derived classes to provide rule information.
    /// </summary>
    /// <param name="operation">The validation operation type</param>
    /// <returns>Collection of custom validation rules</returns>
    protected virtual IEnumerable<ValidationRuleInfo> GetCustomValidationRules(ValidationOperation operation)
    {
        return [];
    }

    /// <summary>
    /// Gets the service provider for accessing dependencies.
    /// </summary>
    protected IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Gets the logger for this validator.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// Validates an individual state operation.
    /// </summary>
    private async Task<StateValidationResult> ValidateOperationAsync(StateOperation<T> operation, CancellationToken cancellationToken)
    {
        return operation.Type switch
        {
            StateOperationType.Create => operation.Entity != null
                ? await ValidateCreateAsync(operation.Entity, cancellationToken)
                : StateValidationResult.Failed("Entity", "Entity is null for create operation", ValidationErrorCode.Required),

            StateOperationType.Update => operation.EntityId != null && operation.Entity != null
                ? await ValidateUpdateAsync(operation.EntityId, operation.Entity, cancellationToken: cancellationToken)
                : StateValidationResult.Failed("Entity", "EntityId or Entity is null for update operation", ValidationErrorCode.Required),

            StateOperationType.Delete => operation.EntityId != null
                ? await ValidateDeleteAsync(operation.EntityId, cancellationToken: cancellationToken)
                : StateValidationResult.Failed("EntityId", "EntityId is null for delete operation", ValidationErrorCode.Required),

            StateOperationType.Patch => operation.EntityId != null && operation.Updates != null
                ? await ValidatePatchAsync(operation.EntityId, operation.Updates, cancellationToken: cancellationToken)
                : StateValidationResult.Failed("EntityId", "EntityId or Updates is null for patch operation", ValidationErrorCode.Required),

            _ => StateValidationResult.Failed("OperationType", $"Unknown operation type: {operation.Type}", ValidationErrorCode.Custom)
        };
    }

    /// <summary>
    /// Validates data annotations on the entity.
    /// </summary>
    private List<ValidationError> ValidateDataAnnotations(T entity)
    {
        var errors = new List<ValidationError>();
        var validationContext = new ValidationContext(entity, ServiceProvider, null);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(entity, validationContext, validationResults, true))
        {
            foreach (var validationResult in validationResults)
            {
                var propertyName = validationResult.MemberNames.FirstOrDefault() ?? "Unknown";
                var errorCode = StateValidatorBase<T>.GetErrorCodeFromValidationResult(validationResult);

                errors.Add(ValidationError.Create(
                    propertyName,
                    validationResult.ErrorMessage ?? "Validation failed",
                    errorCode));
            }
        }

        return errors;
    }

    /// <summary>
    /// Validates basic constraints like null checks.
    /// </summary>
    private static List<ValidationError> ValidateBasicConstraints(T entity, ValidationOperation operation)
    {
        var errors = new List<ValidationError>();

        if (entity == null)
        {
            errors.Add(ValidationError.Create(
                nameof(entity),
                "Entity cannot be null",
                ValidationErrorCode.Required));
        }

        return errors;
    }

    /// <summary>
    /// Gets data annotation rules for introspection.
    /// </summary>
    private static List<ValidationRuleInfo> GetDataAnnotationRules()
    {
        var rules = new List<ValidationRuleInfo>();
        var properties = typeof(T).GetProperties();

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes<ValidationAttribute>();
            foreach (var attribute in attributes)
            {
                var ruleId = $"{property.Name}_{attribute.GetType().Name}";
                var name = $"{property.Name} {attribute.GetType().Name.Replace("Attribute", "")}";
                var description = attribute.ErrorMessage ?? $"{property.Name} validation rule";
                var errorCode = StateValidatorBase<T>.GetErrorCodeFromAttribute(attribute);

                rules.Add(ValidationRuleInfo.Create(ruleId, name, description, property.Name, errorCode));
            }
        }

        return rules;
    }

    /// <summary>
    /// Maps validation attributes to error codes.
    /// </summary>
    private static ValidationErrorCode GetErrorCodeFromAttribute(ValidationAttribute attribute)
    {
        return attribute switch
        {
            RequiredAttribute => ValidationErrorCode.Required,
            StringLengthAttribute => ValidationErrorCode.TooLong,
            RangeAttribute => ValidationErrorCode.OutOfRange,
            RegularExpressionAttribute => ValidationErrorCode.InvalidFormat,
            _ => ValidationErrorCode.Custom
        };
    }

    /// <summary>
    /// Maps validation results to error codes.
    /// </summary>
    private static ValidationErrorCode GetErrorCodeFromValidationResult(ValidationResult validationResult)
    {
        var errorMessage = validationResult.ErrorMessage ?? "";

        if (errorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationErrorCode.Required;
        }

        if (errorMessage.Contains("length", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationErrorCode.TooLong;
        }

        if (errorMessage.Contains("range", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationErrorCode.OutOfRange;
        }

        if (errorMessage.Contains("format", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationErrorCode.InvalidFormat;
        }

        return ValidationErrorCode.Custom;
    }

    /// <summary>
    /// Records validation metrics for monitoring.
    /// </summary>
    private void RecordValidationMetrics(ValidationOperation operation, StateValidationResult? result, TimeSpan duration, Exception? exception = null)
    {
        _metricsCollector.RecordValidation(operation.ToString(), result?.IsValid ?? false, duration, result?.Errors);

        if (result?.IsValid == true)
        {
            _logger.LogDebug("Validation successful for {EntityType} {Operation} in {DurationMs}ms",
                typeof(T).Name, operation, duration.TotalMilliseconds);
        }
        else
        {
            _logger.LogWarning("Validation failed for {EntityType} {Operation} in {DurationMs}ms: {Errors}",
                typeof(T).Name, operation, duration.TotalMilliseconds,
                result?.GetSummary() ?? exception?.Message ?? "Unknown error");
        }
    }

    /// <summary>
    /// Records batch validation metrics for monitoring.
    /// </summary>
    private void RecordBatchValidationMetrics(int operationCount, StateValidationResult? result, TimeSpan duration, Exception? exception = null)
    {
        _metricsCollector.RecordBatchValidation(operationCount, result?.IsValid ?? false, duration, result?.Errors);

        if (result?.IsValid == true)
        {
            _logger.LogDebug("Batch validation successful for {EntityType} ({OperationCount} operations) in {DurationMs}ms",
                typeof(T).Name, operationCount, duration.TotalMilliseconds);
        }
        else
        {
            _logger.LogWarning("Batch validation failed for {EntityType} ({OperationCount} operations) in {DurationMs}ms: {Errors}",
                typeof(T).Name, operationCount, duration.TotalMilliseconds,
                result?.GetSummary() ?? exception?.Message ?? "Unknown error");
        }
    }
}

/// <summary>
/// Helper class for collecting validation metrics.
/// Thread-safe implementation for concurrent validation operations.
/// </summary>
internal sealed class ValidationMetricsCollector
{
    private readonly object _lock = new();
    private long _totalValidations;
    private long _successfulValidations;
    private long _failedValidations;
    private double _totalDurationMs;
    private double _maxDurationMs;
    private readonly Dictionary<ValidationErrorCode, long> _errorCounts = [];
    private readonly Dictionary<string, long> _operationCounts = [];
    // Note: _lastValidationAt field was removed as it was never read

    public void RecordValidation(string operationType, bool isSuccessful, TimeSpan duration, IList<ValidationError>? errors = null)
    {
        lock (_lock)
        {
            _totalValidations++;
            if (isSuccessful)
            {
                _successfulValidations++;
            }
            else
            {
                _failedValidations++;
            }

            var durationMs = duration.TotalMilliseconds;
            _totalDurationMs += durationMs;
            if (durationMs > _maxDurationMs)
            {
                _maxDurationMs = durationMs;
            }

            _operationCounts.TryGetValue(operationType, out var count);
            _operationCounts[operationType] = count + 1;

            if (errors != null)
            {
                foreach (var error in errors)
                {
                    _errorCounts.TryGetValue(error.ErrorCode, out var errorCount);
                    _errorCounts[error.ErrorCode] = errorCount + 1;
                }
            }

            // Note: lastValidationAt tracking was removed as it was never read
        }
    }

    public void RecordBatchValidation(int operationCount, bool isSuccessful, TimeSpan duration, IList<ValidationError>? errors = null)
    {
        // Record as a single validation operation with batch context
        RecordValidation($"Batch({operationCount})", isSuccessful, duration, errors);
    }

    public StateValidationMetrics GetMetrics()
    {
        lock (_lock)
        {
            var avgDuration = _totalValidations > 0 ? _totalDurationMs / _totalValidations : 0;

            return StateValidationMetrics.Create(
                _totalValidations,
                _successfulValidations,
                _failedValidations,
                avgDuration,
                _maxDurationMs,
                new Dictionary<ValidationErrorCode, long>(_errorCounts),
                new Dictionary<string, long>(_operationCounts));
        }
    }
}