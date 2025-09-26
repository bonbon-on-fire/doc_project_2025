namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Base exception for all event store operations.
/// Provides structured error information for event store failures.
/// </summary>
public class EventStoreException : Exception
{
    /// <summary>
    /// Gets the error code categorizing the type of error.
    /// </summary>
    public EventStoreErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the stream identifier associated with the error, if applicable.
    /// </summary>
    public string? StreamId { get; }

    /// <summary>
    /// Gets the event identifier associated with the error, if applicable.
    /// </summary>
    public string? EventId { get; }

    /// <summary>
    /// Gets additional context information about the error.
    /// </summary>
    public Dictionary<string, object>? Context { get; }

    /// <summary>
    /// Gets the correlation identifier for tracing, if available.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of the EventStoreException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="streamId">Optional stream identifier</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="context">Optional additional context</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventStoreException(
        string message,
        EventStoreErrorCode errorCode = EventStoreErrorCode.InternalError,
        string? streamId = null,
        string? eventId = null,
        string? correlationId = null,
        Dictionary<string, object>? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StreamId = streamId;
        EventId = eventId;
        CorrelationId = correlationId;
        Context = context;
    }

    /// <summary>
    /// Gets a string representation of the exception with all context information.
    /// </summary>
    /// <returns>Formatted exception information</returns>
    public override string ToString()
    {
        var details = new List<string> { $"Message: {Message}", $"ErrorCode: {ErrorCode}" };

        if (!string.IsNullOrEmpty(StreamId))
        {
            details.Add($"StreamId: {StreamId}");
        }

        if (!string.IsNullOrEmpty(EventId))
        {
            details.Add($"EventId: {EventId}");
        }

        if (!string.IsNullOrEmpty(CorrelationId))
        {
            details.Add($"CorrelationId: {CorrelationId}");
        }

        if (Context?.Count > 0)
        {
            var contextStr = string.Join(", ", Context.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            details.Add($"Context: {{{contextStr}}}");
        }

        if (InnerException != null)
        {
            details.Add($"InnerException: {InnerException.Message}");
        }

        return string.Join(Environment.NewLine, details);
    }
}

/// <summary>
/// Exception thrown when a concurrency conflict occurs during event appending.
/// </summary>
public class ConcurrencyException : EventStoreException
{
    /// <summary>
    /// Gets the expected stream version.
    /// </summary>
    public long ExpectedVersion { get; }

    /// <summary>
    /// Gets the actual current stream version.
    /// </summary>
    public long ActualVersion { get; }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="actualVersion">The actual version</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public ConcurrencyException(
        string streamId,
        long expectedVersion,
        long actualVersion,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            $"Concurrency conflict in stream '{streamId}': expected version {expectedVersion}, but actual version is {actualVersion}",
            EventStoreErrorCode.ConcurrencyConflict,
            streamId,
            correlationId: correlationId,
            context: new Dictionary<string, object>
            {
                ["expectedVersion"] = expectedVersion,
                ["actualVersion"] = actualVersion
            },
            innerException: innerException)
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

/// <summary>
/// Exception thrown when event serialization fails.
/// </summary>
public class EventSerializationException : EventStoreException
{
    /// <summary>
    /// Gets the event type that failed to serialize.
    /// </summary>
    public string? EventType { get; }

    /// <summary>
    /// Initializes a new instance of the EventSerializationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="eventType">The event type that failed</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventSerializationException(
        string message,
        string? eventType = null,
        string? eventId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            message,
            EventStoreErrorCode.SerializationError,
            eventId: eventId,
            correlationId: correlationId,
            context: eventType != null ? new Dictionary<string, object> { ["eventType"] = eventType } : null,
            innerException: innerException)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Exception thrown when event deserialization fails.
/// </summary>
public class EventDeserializationException : EventStoreException
{
    /// <summary>
    /// Gets the event type that failed to deserialize.
    /// </summary>
    public string? EventType { get; }

    /// <summary>
    /// Gets the raw event data that failed to deserialize.
    /// </summary>
    public string? EventData { get; }

    /// <summary>
    /// Initializes a new instance of the EventDeserializationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="eventType">The event type that failed</param>
    /// <param name="eventData">The raw event data</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventDeserializationException(
        string message,
        string? eventType = null,
        string? eventData = null,
        string? eventId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            message,
            EventStoreErrorCode.DeserializationError,
            eventId: eventId,
            correlationId: correlationId,
            context: new Dictionary<string, object>
            {
                ["eventType"] = eventType ?? "unknown",
                ["eventDataLength"] = eventData?.Length ?? 0
            },
            innerException: innerException)
    {
        EventType = eventType;
        EventData = eventData;
    }
}

/// <summary>
/// Exception thrown when a stream is not found.
/// </summary>
public class StreamNotFoundException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the StreamNotFoundException class.
    /// </summary>
    /// <param name="streamId">The stream identifier that was not found</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public StreamNotFoundException(
        string streamId,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            $"Stream '{streamId}' was not found",
            EventStoreErrorCode.StreamNotFound,
            streamId,
            correlationId: correlationId,
            innerException: innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an event is not found.
/// </summary>
public class EventNotFoundException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class.
    /// </summary>
    /// <param name="eventId">The event identifier that was not found</param>
    /// <param name="streamId">Optional stream identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventNotFoundException(
        string eventId,
        string? streamId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            $"Event '{eventId}' was not found" + (streamId != null ? $" in stream '{streamId}'" : ""),
            EventStoreErrorCode.EventNotFound,
            streamId,
            eventId,
            correlationId,
            innerException: innerException)
    {
    }
}

/// <summary>
/// Exception thrown when storage is unavailable.
/// </summary>
public class StorageUnavailableException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the StorageUnavailableException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public StorageUnavailableException(
        string message = "Event store storage is currently unavailable",
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            message,
            EventStoreErrorCode.StorageUnavailable,
            correlationId: correlationId,
            innerException: innerException)
    {
    }
}

/// <summary>
/// Exception thrown when event validation fails.
/// </summary>
public class EventValidationException : EventStoreException
{
    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    /// <summary>
    /// Initializes a new instance of the EventValidationException class.
    /// </summary>
    /// <param name="validationErrors">The validation errors</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="streamId">Optional stream identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventValidationException(
        IEnumerable<string> validationErrors,
        string? eventId = null,
        string? streamId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            $"Event validation failed: {string.Join(", ", validationErrors)}",
            EventStoreErrorCode.ValidationError,
            streamId,
            eventId,
            correlationId,
            context: new Dictionary<string, object> { ["validationErrors"] = validationErrors.ToArray() },
            innerException: innerException)
    {
        ValidationErrors = validationErrors.ToList().AsReadOnly();
    }

    /// <summary>
    /// Initializes a new instance of the EventValidationException class with a single error.
    /// </summary>
    /// <param name="validationError">The validation error</param>
    /// <param name="eventId">Optional event identifier</param>
    /// <param name="streamId">Optional stream identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public EventValidationException(
        string validationError,
        string? eventId = null,
        string? streamId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : this(
            [validationError],
            eventId,
            streamId,
            correlationId,
            innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a duplicate event is detected.
/// </summary>
public class DuplicateEventException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the DuplicateEventException class.
    /// </summary>
    /// <param name="eventId">The duplicate event identifier</param>
    /// <param name="streamId">Optional stream identifier</param>
    /// <param name="correlationId">Optional correlation identifier</param>
    /// <param name="innerException">Optional inner exception</param>
    public DuplicateEventException(
        string eventId,
        string? streamId = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(
            $"Event '{eventId}' already exists" + (streamId != null ? $" in stream '{streamId}'" : ""),
            EventStoreErrorCode.DuplicateEvent,
            streamId,
            eventId,
            correlationId,
            innerException: innerException)
    {
    }
}