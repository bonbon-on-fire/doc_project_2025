using System.Diagnostics.CodeAnalysis;

namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Result of a state validation operation.
/// Aligned with CommandValidationResult pattern for consistency across the codebase.
/// </summary>
public record StateValidationResult
{
    /// <summary>
    /// Gets whether the validation was successful.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation errors if any.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets the list of validation warnings if any.
    /// </summary>
    public List<ValidationWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Gets additional metadata about the validation operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets the timestamp when validation was performed.
    /// </summary>
    public DateTime ValidatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets whether the validation failed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Errors))]
    public bool IsFailure => !IsValid;

    /// <summary>
    /// Gets whether there are any warnings.
    /// </summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// Gets the total number of issues (errors + warnings).
    /// </summary>
    public int TotalIssues => Errors.Count + Warnings.Count;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="warnings">Optional warnings to include</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful state validation result</returns>
    public static StateValidationResult Success(List<ValidationWarning>? warnings = null, Dictionary<string, object>? metadata = null)
    {
        return new StateValidationResult
        {
            IsValid = true,
            Warnings = warnings ?? [],
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    /// <param name="errors">The validation errors</param>
    /// <param name="warnings">Optional warnings to include</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state validation result</returns>
    public static StateValidationResult Failed(IEnumerable<ValidationError> errors, List<ValidationWarning>? warnings = null, Dictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new StateValidationResult
        {
            IsValid = false,
            Errors = [.. errors],
            Warnings = warnings ?? [],
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    /// <param name="error">The validation error</param>
    /// <param name="warnings">Optional warnings to include</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state validation result</returns>
    public static StateValidationResult Failed(ValidationError error, List<ValidationWarning>? warnings = null, Dictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Failed([error], warnings, metadata);
    }

    /// <summary>
    /// Creates a failed validation result with error details.
    /// </summary>
    /// <param name="propertyName">The property that failed validation</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="errorCode">The validation error code</param>
    /// <param name="attemptedValue">The value that failed validation</param>
    /// <param name="entityId">Optional entity ID context</param>
    /// <returns>A failed state validation result</returns>
    public static StateValidationResult Failed(
        string propertyName,
        string errorMessage,
        ValidationErrorCode errorCode,
        object? attemptedValue = null,
        string? entityId = null)
    {
        var error = new ValidationError
        {
            PropertyName = propertyName,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            AttemptedValue = attemptedValue,
            EntityId = entityId
        };

        return Failed(error);
    }

    /// <summary>
    /// Combines multiple validation results into a single result.
    /// The result is valid only if all input results are valid.
    /// </summary>
    /// <param name="results">The validation results to combine</param>
    /// <returns>A combined validation result</returns>
    public static StateValidationResult Combine(params StateValidationResult[] results)
    {
        return Combine((IEnumerable<StateValidationResult>)results);
    }

    /// <summary>
    /// Combines multiple validation results into a single result.
    /// The result is valid only if all input results are valid.
    /// </summary>
    /// <param name="results">The validation results to combine</param>
    /// <returns>A combined validation result</returns>
    public static StateValidationResult Combine(IEnumerable<StateValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var resultList = results.ToList();
        if (resultList.Count == 0)
        {
            return Success();
        }

        if (resultList.Count == 1)
        {
            return resultList[0];
        }

        var allErrors = resultList.SelectMany(r => r.Errors).ToList();
        var allWarnings = resultList.SelectMany(r => r.Warnings).ToList();
        var combinedMetadata = new Dictionary<string, object>();

        foreach (var result in resultList)
        {
            if (result.Metadata != null)
            {
                foreach (var kvp in result.Metadata)
                {
                    combinedMetadata[$"result_{resultList.IndexOf(result)}_{kvp.Key}"] = kvp.Value;
                }
            }
        }

        var isValid = allErrors.Count == 0;
        return new StateValidationResult
        {
            IsValid = isValid,
            Errors = allErrors,
            Warnings = allWarnings,
            Metadata = combinedMetadata.Count > 0 ? combinedMetadata : null
        };
    }

    /// <summary>
    /// Gets a summary string of all errors and warnings.
    /// </summary>
    /// <returns>A formatted summary of validation issues</returns>
    public string GetSummary()
    {
        if (IsValid && !HasWarnings)
        {
            return "Validation successful";
        }

        var summary = new List<string>();

        if (Errors.Count > 0)
        {
            summary.Add($"Errors: {string.Join(", ", Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"))}");
        }

        if (Warnings.Count > 0)
        {
            summary.Add($"Warnings: {string.Join(", ", Warnings.Select(w => $"{w.PropertyName}: {w.Message}"))}");
        }

        return string.Join("; ", summary);
    }
}

/// <summary>
/// Represents a specific validation error.
/// </summary>
public record ValidationError
{
    /// <summary>
    /// Gets the name of the property that failed validation.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the error message describing the validation failure.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Gets the specific error code for categorization.
    /// </summary>
    public required ValidationErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the value that failed validation.
    /// </summary>
    public object? AttemptedValue { get; init; }

    /// <summary>
    /// Gets the entity ID context if applicable.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Gets additional metadata about the error.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new validation error.
    /// </summary>
    /// <param name="propertyName">The property that failed validation</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="errorCode">The validation error code</param>
    /// <param name="attemptedValue">The value that failed validation</param>
    /// <param name="entityId">Optional entity ID context</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A new validation error</returns>
    public static ValidationError Create(
        string propertyName,
        string errorMessage,
        ValidationErrorCode errorCode,
        object? attemptedValue = null,
        string? entityId = null,
        Dictionary<string, object>? metadata = null)
    {
        return new ValidationError
        {
            PropertyName = propertyName,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            AttemptedValue = attemptedValue,
            EntityId = entityId,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents a validation warning (non-blocking).
/// </summary>
public record ValidationWarning
{
    /// <summary>
    /// Gets the name of the property that generated the warning.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the value that generated the warning.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Gets additional metadata about the warning.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a new validation warning.
    /// </summary>
    /// <param name="propertyName">The property that generated the warning</param>
    /// <param name="message">The warning message</param>
    /// <param name="value">The value that generated the warning</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A new validation warning</returns>
    public static ValidationWarning Create(
        string propertyName,
        string message,
        object? value = null,
        Dictionary<string, object>? metadata = null)
    {
        return new ValidationWarning
        {
            PropertyName = propertyName,
            Message = message,
            Value = value,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Categorization of validation errors for consistent handling and reporting.
/// </summary>
public enum ValidationErrorCode
{
    /// <summary>
    /// A required field or property is missing or null.
    /// </summary>
    Required = 1,

    /// <summary>
    /// The value format is invalid (e.g., invalid email, phone number).
    /// </summary>
    InvalidFormat = 2,

    /// <summary>
    /// The value is outside the allowed range.
    /// </summary>
    OutOfRange = 3,

    /// <summary>
    /// The value is too long.
    /// </summary>
    TooLong = 4,

    /// <summary>
    /// The value is too short.
    /// </summary>
    TooShort = 5,

    /// <summary>
    /// The value violates a uniqueness constraint.
    /// </summary>
    DuplicateValue = 6,

    /// <summary>
    /// The value violates referential integrity (foreign key constraint).
    /// </summary>
    ReferentialIntegrity = 7,

    /// <summary>
    /// The value violates a business rule.
    /// </summary>
    BusinessRuleViolation = 8,

    /// <summary>
    /// The state transition is not allowed.
    /// </summary>
    StateTransitionInvalid = 9,

    /// <summary>
    /// The value violates a cross-entity constraint.
    /// </summary>
    CrossEntityConstraint = 10,

    /// <summary>
    /// A custom validation error not covered by other codes.
    /// </summary>
    Custom = 99
}

/// <summary>
/// Represents the type of validation operation being performed.
/// </summary>
public enum ValidationOperation
{
    /// <summary>
    /// Validating entity creation.
    /// </summary>
    Create = 0,

    /// <summary>
    /// Validating entity update.
    /// </summary>
    Update = 1,

    /// <summary>
    /// Validating entity deletion.
    /// </summary>
    Delete = 2,

    /// <summary>
    /// Validating partial entity update (patch).
    /// </summary>
    Patch = 3
}
