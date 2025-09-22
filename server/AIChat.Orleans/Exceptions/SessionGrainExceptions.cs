namespace AIChat.Orleans.Contracts;

/// <summary>
/// Severity level for session exceptions.
/// </summary>
public enum ExceptionSeverity
{
    /// <summary>
    /// Low severity - informational, can be ignored.
    /// </summary>
    Low,

    /// <summary>
    /// Medium severity - should be logged, may require action.
    /// </summary>
    Medium,

    /// <summary>
    /// High severity - requires immediate attention.
    /// </summary>
    High,

    /// <summary>
    /// Critical severity - system failure, requires immediate intervention.
    /// </summary>
    Critical
}

/// <summary>
/// Base exception for all session grain-related exceptions.
/// </summary>
public abstract class SessionGrainException : Exception
{
    /// <summary>
    /// Correlation ID for tracking the exception across services.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Session ID associated with the exception, if available.
    /// </summary>
    public string? SessionId { get; }

    /// <summary>
    /// Timestamp when the exception occurred.
    /// </summary>
    public DateTime OccurredAt { get; }

    /// <summary>
    /// Additional context data for debugging.
    /// </summary>
    public Dictionary<string, object> Context { get; } = [];

    /// <summary>
    /// Severity level of the exception.
    /// </summary>
    public ExceptionSeverity Severity { get; protected set; } = ExceptionSeverity.Medium;

    /// <summary>
    /// Indicates whether the operation can be retried.
    /// </summary>
    public bool CanRetry { get; protected set; }

    /// <summary>
    /// Suggested time to wait before retrying (if CanRetry is true).
    /// </summary>
    public TimeSpan? RetryAfter { get; protected set; }

    /// <summary>
    /// Maximum number of retry attempts recommended.
    /// </summary>
    public int? MaxRetryAttempts { get; protected set; }

    /// <summary>
    /// Indicates if this is a recoverable error.
    /// </summary>
    public bool IsRecoverable { get; protected set; } = true;

    /// <summary>
    /// Error code for categorization and handling.
    /// </summary>
    public string? ErrorCode { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the SessionGrainException class.
    /// </summary>
    protected SessionGrainException(string message, string? sessionId = null)
        : base(message)
    {
        CorrelationId = Guid.NewGuid().ToString();
        SessionId = sessionId;
        OccurredAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the SessionGrainException class with an inner exception.
    /// </summary>
    protected SessionGrainException(string message, Exception innerException, string? sessionId = null)
        : base(message, innerException)
    {
        CorrelationId = Guid.NewGuid().ToString();
        SessionId = sessionId;
        OccurredAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds context information to the exception.
    /// </summary>
    public SessionGrainException WithContext(string key, object value)
    {
        Context[key] = value;
        return this;
    }

    /// <summary>
    /// Sets the retry policy for this exception.
    /// </summary>
    public SessionGrainException WithRetryPolicy(bool canRetry, TimeSpan? retryAfter = null, int? maxAttempts = null)
    {
        CanRetry = canRetry;
        RetryAfter = retryAfter;
        MaxRetryAttempts = maxAttempts;
        return this;
    }

    /// <summary>
    /// Sets the severity level for this exception.
    /// </summary>
    public SessionGrainException WithSeverity(ExceptionSeverity severity)
    {
        Severity = severity;
        return this;
    }
}

/// <summary>
/// Exception thrown when a session is not found.
/// </summary>
public sealed class SessionNotFoundException : SessionGrainException
{
    /// <summary>
    /// The session ID that was not found.
    /// </summary>
    public string RequestedSessionId { get; }

    /// <summary>
    /// Initializes a new instance of the SessionNotFoundException class.
    /// </summary>
    public SessionNotFoundException(string sessionId)
        : base($"Session with ID '{sessionId}' was not found.", sessionId)
    {
        RequestedSessionId = sessionId;
        WithContext("RequestedSessionId", sessionId);
        Severity = ExceptionSeverity.Low;
        ErrorCode = "SESSION_NOT_FOUND";
        CanRetry = false;
        IsRecoverable = true;
    }

    /// <summary>
    /// Initializes a new instance of the SessionNotFoundException class with a custom message.
    /// </summary>
    public SessionNotFoundException(string sessionId, string message)
        : base(message, sessionId)
    {
        RequestedSessionId = sessionId;
        WithContext("RequestedSessionId", sessionId);
        Severity = ExceptionSeverity.Low;
        ErrorCode = "SESSION_NOT_FOUND";
        CanRetry = false;
        IsRecoverable = true;
    }
}

/// <summary>
/// Exception thrown when attempting to create a session that already exists.
/// </summary>
public sealed class SessionAlreadyExistsException : SessionGrainException
{
    /// <summary>
    /// The existing session ID.
    /// </summary>
    public string ExistingSessionId { get; }

    /// <summary>
    /// When the existing session was created.
    /// </summary>
    public DateTime? ExistingSessionCreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the SessionAlreadyExistsException class.
    /// </summary>
    public SessionAlreadyExistsException(string sessionId, DateTime? createdAt = null)
        : base($"Session with ID '{sessionId}' already exists.", sessionId)
    {
        ExistingSessionId = sessionId;
        ExistingSessionCreatedAt = createdAt;
        WithContext("ExistingSessionId", sessionId);
        if (createdAt.HasValue)
        {
            WithContext("ExistingSessionCreatedAt", createdAt.Value);
        }
    }
}

/// <summary>
/// Exception thrown when an operation is attempted on a disconnected session.
/// </summary>
public sealed class SessionDisconnectedException : SessionGrainException
{
    /// <summary>
    /// When the session was disconnected.
    /// </summary>
    public DateTime? DisconnectedAt { get; }

    /// <summary>
    /// Reason for disconnection.
    /// </summary>
    public string? DisconnectionReason { get; }

    /// <summary>
    /// Initializes a new instance of the SessionDisconnectedException class.
    /// </summary>
    public SessionDisconnectedException(string sessionId, string? reason = null, DateTime? disconnectedAt = null)
        : base($"Session '{sessionId}' is disconnected and cannot perform the requested operation.", sessionId)
    {
        DisconnectionReason = reason;
        DisconnectedAt = disconnectedAt;
        WithContext("DisconnectionReason", reason ?? "Unknown");
        if (disconnectedAt.HasValue)
        {
            WithContext("DisconnectedAt", disconnectedAt.Value);
        }

        // Set retry policy - disconnected sessions can often be reconnected
        Severity = ExceptionSeverity.Medium;
        ErrorCode = "SESSION_DISCONNECTED";
        CanRetry = true;
        RetryAfter = TimeSpan.FromSeconds(5);
        MaxRetryAttempts = 3;
        IsRecoverable = true;
    }
}

/// <summary>
/// Exception thrown when a protocol-related operation fails.
/// </summary>
public sealed class SessionProtocolException : SessionGrainException
{
    /// <summary>
    /// The protocol type involved in the exception.
    /// </summary>
    public string ProtocolType { get; }

    /// <summary>
    /// The specific protocol operation that failed.
    /// </summary>
    public string? FailedOperation { get; }

    /// <summary>
    /// Protocol-specific error code if applicable.
    /// </summary>
    public new string? ErrorCode { get; private set; }

    /// <summary>
    /// Initializes a new instance of the SessionProtocolException class.
    /// </summary>
    public SessionProtocolException(string protocolType, string message, string? sessionId = null)
        : base(message, sessionId)
    {
        ProtocolType = protocolType;
        WithContext("ProtocolType", protocolType);
    }

    /// <summary>
    /// Initializes a new instance with operation details.
    /// </summary>
    public SessionProtocolException(string protocolType, string operation, string message, string? sessionId = null)
        : base($"Protocol operation '{operation}' failed for {protocolType}: {message}", sessionId)
    {
        ProtocolType = protocolType;
        FailedOperation = operation;
        WithContext("ProtocolType", protocolType);
        WithContext("FailedOperation", operation);
    }

    /// <summary>
    /// Initializes a new instance with an inner exception.
    /// </summary>
    public SessionProtocolException(string protocolType, string message, Exception innerException, string? sessionId = null)
        : base(message, innerException, sessionId)
    {
        ProtocolType = protocolType;
        WithContext("ProtocolType", protocolType);
    }

    /// <summary>
    /// Sets the error code for the exception.
    /// </summary>
    public SessionProtocolException WithErrorCode(string errorCode)
    {
        ErrorCode = errorCode;
        WithContext("ErrorCode", errorCode);
        return this;
    }
}

/// <summary>
/// Exception thrown when session reconnection fails.
/// </summary>
public sealed class SessionReconnectionException : SessionGrainException
{
    /// <summary>
    /// Number of reconnection attempts made.
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// Maximum allowed reconnection attempts.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// The last error encountered during reconnection.
    /// </summary>
    public string? LastError { get; }

    /// <summary>
    /// Initializes a new instance of the SessionReconnectionException class.
    /// </summary>
    public SessionReconnectionException(string sessionId, int attemptCount, int maxAttempts)
        : base($"Session '{sessionId}' failed to reconnect after {attemptCount} attempts (max: {maxAttempts}).", sessionId)
    {
        AttemptCount = attemptCount;
        MaxAttempts = maxAttempts;
        WithContext("AttemptCount", attemptCount);
        WithContext("MaxAttempts", maxAttempts);
    }

    /// <summary>
    /// Initializes a new instance with error details.
    /// </summary>
    public SessionReconnectionException(string sessionId, int attemptCount, string lastError)
        : base($"Session '{sessionId}' reconnection failed after {attemptCount} attempts: {lastError}", sessionId)
    {
        AttemptCount = attemptCount;
        LastError = lastError;
        MaxAttempts = 0;
        WithContext("AttemptCount", attemptCount);
        WithContext("LastError", lastError);
    }

    /// <summary>
    /// Initializes a new instance with an inner exception.
    /// </summary>
    public SessionReconnectionException(string sessionId, string message, Exception innerException)
        : base(message, innerException, sessionId)
    {
        AttemptCount = 0;
        MaxAttempts = 0;
        LastError = innerException.Message;
        WithContext("LastError", LastError);
    }
}

/// <summary>
/// Exception thrown when a session operation times out.
/// </summary>
public sealed class SessionTimeoutException : SessionGrainException
{
    /// <summary>
    /// The timeout duration in seconds.
    /// </summary>
    public int TimeoutSeconds { get; }

    /// <summary>
    /// The operation that timed out.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Initializes a new instance of the SessionTimeoutException class.
    /// </summary>
    public SessionTimeoutException(string sessionId, string operation, int timeoutSeconds)
        : base($"Session '{sessionId}' operation '{operation}' timed out after {timeoutSeconds} seconds.", sessionId)
    {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
        WithContext("Operation", operation);
        WithContext("TimeoutSeconds", timeoutSeconds);
    }

    /// <summary>
    /// Initializes a new instance with a custom message.
    /// </summary>
    public SessionTimeoutException(string message, string sessionId, string operation, int timeoutSeconds)
        : base(message, sessionId)
    {
        Operation = operation;
        TimeoutSeconds = timeoutSeconds;
        WithContext("Operation", operation);
        WithContext("TimeoutSeconds", timeoutSeconds);
    }
}

/// <summary>
/// Exception thrown when session validation fails.
/// </summary>
public sealed class SessionValidationException : SessionGrainException
{
    /// <summary>
    /// The validation errors encountered.
    /// </summary>
    public List<string> ValidationErrors { get; } = [];

    /// <summary>
    /// The field or property that failed validation.
    /// </summary>
    public string? FailedField { get; }

    /// <summary>
    /// Initializes a new instance of the SessionValidationException class.
    /// </summary>
    public SessionValidationException(string message, string? sessionId = null)
        : base(message, sessionId)
    {
    }

    /// <summary>
    /// Initializes a new instance with validation errors.
    /// </summary>
    public SessionValidationException(List<string> errors, string? sessionId = null)
        : base($"Session validation failed with {errors.Count} error(s): {string.Join("; ", errors)}", sessionId)
    {
        ValidationErrors = errors;
        WithContext("ValidationErrors", errors);
    }

    /// <summary>
    /// Initializes a new instance for a specific field.
    /// </summary>
    public SessionValidationException(string field, string error, string? sessionId = null)
        : base($"Validation failed for field '{field}': {error}", sessionId)
    {
        FailedField = field;
        ValidationErrors = [error];
        WithContext("FailedField", field);
        WithContext("ValidationError", error);
    }

    /// <summary>
    /// Adds a validation error to the exception.
    /// </summary>
    public SessionValidationException AddError(string error)
    {
        ValidationErrors.Add(error);
        WithContext($"ValidationError_{ValidationErrors.Count}", error);
        return this;
    }
}

/// <summary>
/// Exception thrown when session limits are exceeded.
/// </summary>
public sealed class SessionLimitExceededException : SessionGrainException
{
    /// <summary>
    /// The type of limit that was exceeded.
    /// </summary>
    public string LimitType { get; }

    /// <summary>
    /// The current value that exceeded the limit.
    /// </summary>
    public long CurrentValue { get; }

    /// <summary>
    /// The maximum allowed limit.
    /// </summary>
    public long MaxLimit { get; }

    /// <summary>
    /// Initializes a new instance of the SessionLimitExceededException class.
    /// </summary>
    public SessionLimitExceededException(string limitType, long currentValue, long maxLimit, string? sessionId = null)
        : base($"Session limit exceeded for '{limitType}': current={currentValue}, max={maxLimit}", sessionId)
    {
        LimitType = limitType;
        CurrentValue = currentValue;
        MaxLimit = maxLimit;
        WithContext("LimitType", limitType);
        WithContext("CurrentValue", currentValue);
        WithContext("MaxLimit", maxLimit);
    }

    /// <summary>
    /// Initializes a new instance with a custom message.
    /// </summary>
    public SessionLimitExceededException(string message, string limitType, long currentValue, long maxLimit, string? sessionId = null)
        : base(message, sessionId)
    {
        LimitType = limitType;
        CurrentValue = currentValue;
        MaxLimit = maxLimit;
        WithContext("LimitType", limitType);
        WithContext("CurrentValue", currentValue);
        WithContext("MaxLimit", maxLimit);
    }
}

/// <summary>
/// Exception thrown when a session is already connected.
/// </summary>
public sealed class SessionAlreadyConnectedException : SessionGrainException
{
    /// <summary>
    /// The existing connection ID.
    /// </summary>
    public string ExistingConnectionId { get; }

    /// <summary>
    /// When the existing connection was established.
    /// </summary>
    public DateTime? ConnectedAt { get; }

    /// <summary>
    /// Initializes a new instance of the SessionAlreadyConnectedException class.
    /// </summary>
    public SessionAlreadyConnectedException(string sessionId, string connectionId, DateTime? connectedAt = null)
        : base($"Session '{sessionId}' is already connected with connection ID '{connectionId}'.", sessionId)
    {
        ExistingConnectionId = connectionId;
        ConnectedAt = connectedAt;
        WithContext("ExistingConnectionId", connectionId);
        if (connectedAt.HasValue)
        {
            WithContext("ConnectedAt", connectedAt.Value);
        }
    }
}