namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Base exception for state management operations.
/// Provides structured error information for debugging and monitoring.
/// </summary>
public class StateManagementException : Exception
{
    /// <summary>
    /// Gets the error code for this exception.
    /// </summary>
    public StateErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the entity type that was being managed when the error occurred.
    /// </summary>
    public string? EntityType { get; }

    /// <summary>
    /// Gets the entity ID that was being operated on when the error occurred.
    /// </summary>
    public string? EntityId { get; }

    /// <summary>
    /// Gets the operation that was being performed when the error occurred.
    /// </summary>
    public string? Operation { get; }

    /// <summary>
    /// Gets additional context information about the error.
    /// </summary>
    public Dictionary<string, object>? Context { get; }

    /// <summary>
    /// Gets the correlation ID for tracing this error across services.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of the StateManagementException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="errorCode">The specific error code</param>
    /// <param name="entityType">The entity type being managed</param>
    /// <param name="entityId">The entity ID being operated on</param>
    /// <param name="operation">The operation being performed</param>
    /// <param name="context">Additional context information</param>
    /// <param name="correlationId">Correlation ID for tracing</param>
    /// <param name="innerException">The inner exception</param>
    public StateManagementException(
        string message,
        StateErrorCode errorCode = StateErrorCode.InternalError,
        string? entityType = null,
        string? entityId = null,
        string? operation = null,
        Dictionary<string, object>? context = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        EntityType = entityType;
        EntityId = entityId;
        Operation = operation;
        Context = context;
        CorrelationId = correlationId ?? Guid.NewGuid().ToString();
    }

    public StateManagementException() : base()
    {
    }

    public StateManagementException(string? message) : base(message)
    {
    }

    public StateManagementException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a StateManagementException for entity not found scenarios.
    /// </summary>
    /// <param name="entityType">The type of entity</param>
    /// <param name="entityId">The ID of the entity</param>
    /// <param name="operation">The operation being performed</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with NotFound error code</returns>
    public static StateManagementException NotFound(
        string entityType,
        string entityId,
        string operation,
        string? correlationId = null)
    {
        return new StateManagementException(
            $"{entityType} with ID '{entityId}' was not found",
            StateErrorCode.NotFound,
            entityType,
            entityId,
            operation,
            correlationId: correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for validation errors.
    /// </summary>
    /// <param name="entityType">The type of entity</param>
    /// <param name="validationErrors">The validation error messages</param>
    /// <param name="operation">The operation being performed</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with ValidationError error code</returns>
    public static StateManagementException ValidationError(
        string entityType,
        IEnumerable<string> validationErrors,
        string operation,
        string? entityId = null,
        string? correlationId = null)
    {
        var message = $"Validation failed for {entityType}: {string.Join("; ", validationErrors)}";
        var context = new Dictionary<string, object>
        {
            ["ValidationErrors"] = validationErrors.ToList()
        };

        return new StateManagementException(
            message,
            StateErrorCode.ValidationError,
            entityType,
            entityId,
            operation,
            context,
            correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for duplicate key errors.
    /// </summary>
    /// <param name="entityType">The type of entity</param>
    /// <param name="entityId">The ID of the entity</param>
    /// <param name="operation">The operation being performed</param>
    /// <param name="conflictingField">The field that has the duplicate value</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with DuplicateKey error code</returns>
    public static StateManagementException DuplicateKey(
        string entityType,
        string entityId,
        string operation,
        string? conflictingField = null,
        string? correlationId = null)
    {
        var message = conflictingField != null
            ? $"{entityType} with {conflictingField} '{entityId}' already exists"
            : $"{entityType} with ID '{entityId}' already exists";

        var context = conflictingField != null
            ? new Dictionary<string, object> { ["ConflictingField"] = conflictingField }
            : null;

        return new StateManagementException(
            message,
            StateErrorCode.DuplicateKey,
            entityType,
            entityId,
            operation,
            context,
            correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for concurrency conflicts.
    /// </summary>
    /// <param name="entityType">The type of entity</param>
    /// <param name="entityId">The ID of the entity</param>
    /// <param name="operation">The operation being performed</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="actualVersion">The actual version</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with ConcurrencyConflict error code</returns>
    public static StateManagementException ConcurrencyConflict(
        string entityType,
        string entityId,
        string operation,
        string? expectedVersion = null,
        string? actualVersion = null,
        string? correlationId = null)
    {
        var message = expectedVersion != null && actualVersion != null
            ? $"Concurrency conflict for {entityType} '{entityId}': expected version {expectedVersion}, actual version {actualVersion}"
            : $"Concurrency conflict for {entityType} '{entityId}': entity was modified by another operation";

        var context = new Dictionary<string, object>();
        if (expectedVersion != null)
        {
            context["ExpectedVersion"] = expectedVersion;
        }

        if (actualVersion != null)
        {
            context["ActualVersion"] = actualVersion;
        }

        return new StateManagementException(
            message,
            StateErrorCode.ConcurrencyConflict,
            entityType,
            entityId,
            operation,
            context.Count > 0 ? context : null,
            correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for network errors.
    /// </summary>
    /// <param name="operation">The operation being performed</param>
    /// <param name="networkError">The underlying network error</param>
    /// <param name="entityType">Optional entity type</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with NetworkError error code</returns>
    public static StateManagementException NetworkError(
        string operation,
        Exception networkError,
        string? entityType = null,
        string? entityId = null,
        string? correlationId = null)
    {
        var message = $"Network error during {operation}: {networkError.Message}";

        return new StateManagementException(
            message,
            StateErrorCode.NetworkError,
            entityType,
            entityId,
            operation,
            correlationId: correlationId,
            innerException: networkError);
    }

    /// <summary>
    /// Creates a StateManagementException for timeout errors.
    /// </summary>
    /// <param name="operation">The operation being performed</param>
    /// <param name="timeoutMs">The timeout value in milliseconds</param>
    /// <param name="entityType">Optional entity type</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with TimeoutError error code</returns>
    public static StateManagementException TimeoutError(
        string operation,
        int timeoutMs,
        string? entityType = null,
        string? entityId = null,
        string? correlationId = null)
    {
        var message = $"Operation {operation} timed out after {timeoutMs}ms";
        var context = new Dictionary<string, object>
        {
            ["TimeoutMs"] = timeoutMs
        };

        return new StateManagementException(
            message,
            StateErrorCode.TimeoutError,
            entityType,
            entityId,
            operation,
            context,
            correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for access denied errors.
    /// </summary>
    /// <param name="operation">The operation being performed</param>
    /// <param name="entityType">The type of entity</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="reason">Optional reason for access denial</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with AccessDenied error code</returns>
    public static StateManagementException AccessDenied(
        string operation,
        string entityType,
        string? entityId = null,
        string? reason = null,
        string? correlationId = null)
    {
        var message = reason != null
            ? $"Access denied for {operation} on {entityType}: {reason}"
            : $"Access denied for {operation} on {entityType}";

        var context = reason != null
            ? new Dictionary<string, object> { ["Reason"] = reason }
            : null;

        return new StateManagementException(
            message,
            StateErrorCode.AccessDenied,
            entityType,
            entityId,
            operation,
            context,
            correlationId);
    }

    /// <summary>
    /// Creates a StateManagementException for service unavailable errors.
    /// </summary>
    /// <param name="operation">The operation being performed</param>
    /// <param name="serviceName">The name of the unavailable service</param>
    /// <param name="entityType">Optional entity type</param>
    /// <param name="entityId">Optional entity ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <returns>A StateManagementException with ServiceUnavailable error code</returns>
    public static StateManagementException ServiceUnavailable(
        string operation,
        string serviceName,
        string? entityType = null,
        string? entityId = null,
        string? correlationId = null)
    {
        var message = $"Service {serviceName} is unavailable for operation {operation}";
        var context = new Dictionary<string, object>
        {
            ["ServiceName"] = serviceName
        };

        return new StateManagementException(
            message,
            StateErrorCode.ServiceUnavailable,
            entityType,
            entityId,
            operation,
            context,
            correlationId);
    }

    /// <summary>
    /// Gets a string representation of this exception with all context information.
    /// </summary>
    /// <returns>A detailed string representation</returns>
    public override string ToString()
    {
        var details = new List<string> { base.ToString() };

        if (!string.IsNullOrEmpty(CorrelationId))
        {
            details.Add($"CorrelationId: {CorrelationId}");
        }

        if (ErrorCode != StateErrorCode.None)
        {
            details.Add($"ErrorCode: {ErrorCode}");
        }

        if (!string.IsNullOrEmpty(EntityType))
        {
            details.Add($"EntityType: {EntityType}");
        }

        if (!string.IsNullOrEmpty(EntityId))
        {
            details.Add($"EntityId: {EntityId}");
        }

        if (!string.IsNullOrEmpty(Operation))
        {
            details.Add($"Operation: {Operation}");
        }

        if (Context?.Count > 0)
        {
            var contextStr = string.Join(", ", Context.Select(kv => $"{kv.Key}: {kv.Value}"));
            details.Add($"Context: {contextStr}");
        }

        return string.Join(Environment.NewLine, details);
    }
}