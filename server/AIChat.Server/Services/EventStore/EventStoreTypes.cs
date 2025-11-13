namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Represents the result of an event store operation.
/// </summary>
public record EventResult
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the error code for categorization.
    /// </summary>
    public EventStoreErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets additional metadata about the operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets the number of events processed.
    /// </summary>
    public int EventsProcessed { get; init; }

    /// <summary>
    /// Gets the timestamp when the operation completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="eventsProcessed">Number of events processed</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful event result</returns>
    public static EventResult CreateSuccess(int eventsProcessed = 1, Dictionary<string, object>? metadata = null)
    {
        return new EventResult
        {
            Success = true,
            EventsProcessed = eventsProcessed,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed event result</returns>
    public static EventResult CreateFailure(
        string error,
        EventStoreErrorCode errorCode = EventStoreErrorCode.InternalError,
        Dictionary<string, object>? metadata = null)
    {
        return new EventResult
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            EventsProcessed = 0,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of an event append operation with version information.
/// </summary>
public record EventAppendResult : EventResult
{
    /// <summary>
    /// Gets the new version of the stream after appending events.
    /// </summary>
    public long NewStreamVersion { get; init; }

    /// <summary>
    /// Gets the previous version of the stream before appending events.
    /// </summary>
    public long PreviousStreamVersion { get; init; }

    /// <summary>
    /// Creates a successful append result.
    /// </summary>
    /// <param name="newVersion">The new stream version</param>
    /// <param name="previousVersion">The previous stream version</param>
    /// <param name="eventsProcessed">Number of events appended</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful append result</returns>
    public static EventAppendResult CreateSuccess(
        long newVersion,
        long previousVersion,
        int eventsProcessed = 1,
        Dictionary<string, object>? metadata = null)
    {
        return new EventAppendResult
        {
            Success = true,
            NewStreamVersion = newVersion,
            PreviousStreamVersion = previousVersion,
            EventsProcessed = eventsProcessed,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed append result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="currentVersion">The current stream version</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed append result</returns>
    public static EventAppendResult CreateFailure(
        string error,
        EventStoreErrorCode errorCode = EventStoreErrorCode.InternalError,
        long currentVersion = -1,
        Dictionary<string, object>? metadata = null)
    {
        return new EventAppendResult
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            NewStreamVersion = currentVersion,
            PreviousStreamVersion = currentVersion,
            EventsProcessed = 0,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of an event query operation.
/// </summary>
public record EventQueryResult
{
    /// <summary>
    /// Gets the events returned by the query.
    /// </summary>
    public required IReadOnlyList<IEvent> Events { get; init; }

    /// <summary>
    /// Gets the total number of events matching the query criteria.
    /// May be larger than Events.Count due to pagination.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Gets the current page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets the page size used for the query.
    /// </summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// Gets whether there are more pages available.
    /// </summary>
    public bool HasNextPage => Page * PageSize < TotalCount;

    /// <summary>
    /// Gets whether this is the first page.
    /// </summary>
    public bool IsFirstPage => Page <= 1;

    /// <summary>
    /// Gets whether this is the last page.
    /// </summary>
    public bool IsLastPage => !HasNextPage;

    /// <summary>
    /// Gets additional query metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets the timestamp when the query was executed.
    /// </summary>
    public DateTimeOffset QueryExecutedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an empty query result.
    /// </summary>
    /// <returns>An empty event query result</returns>
    public static EventQueryResult Empty()
    {
        return new EventQueryResult
        {
            Events = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 100
        };
    }

    /// <summary>
    /// Creates a query result with events.
    /// </summary>
    /// <param name="events">The events to include</param>
    /// <param name="totalCount">The total count of matching events</param>
    /// <param name="page">The current page</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>An event query result</returns>
    public static EventQueryResult Create(
        IReadOnlyList<IEvent> events,
        int totalCount,
        int page = 1,
        int pageSize = 100,
        Dictionary<string, object>? metadata = null)
    {
        return new EventQueryResult
        {
            Events = events,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of an event replay operation.
/// </summary>
/// <typeparam name="T">The type of state that was rebuilt</typeparam>
public record EventReplayResult<T>
{
    /// <summary>
    /// Gets the rebuilt state from replaying events.
    /// </summary>
    public required T State { get; init; }

    /// <summary>
    /// Gets the final version after replaying events.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets the number of events that were processed during replay.
    /// </summary>
    public required int EventsProcessed { get; init; }

    /// <summary>
    /// Gets the timestamp when the replay was performed.
    /// </summary>
    public DateTimeOffset ReplayedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets additional replay metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether the replay was successful.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Gets any error that occurred during replay.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Factory class for creating EventReplayResult instances.
/// </summary>
public static class EventReplayResultFactory
{
    /// <summary>
    /// Creates a successful replay result.
    /// </summary>
    /// <typeparam name="T">Type of the state</typeparam>
    /// <param name="state">The rebuilt state</param>
    /// <param name="version">The final version</param>
    /// <param name="eventsProcessed">Number of events processed</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful replay result</returns>
    public static EventReplayResult<T> CreateSuccess<T>(
        T state,
        long version,
        int eventsProcessed,
        Dictionary<string, object>? metadata = null)
    {
        return new EventReplayResult<T>
        {
            State = state,
            Version = version,
            EventsProcessed = eventsProcessed,
            Success = true,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed replay result.
    /// </summary>
    /// <typeparam name="T">Type of the state</typeparam>
    /// <param name="error">The error message</param>
    /// <param name="partialState">Partial state if available</param>
    /// <param name="version">The version reached before failure</param>
    /// <param name="eventsProcessed">Number of events processed before failure</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed replay result</returns>
    public static EventReplayResult<T> CreateFailure<T>(
        string error,
        T? partialState = default,
        long version = -1,
        int eventsProcessed = 0,
        Dictionary<string, object>? metadata = null)
    {
        return new EventReplayResult<T>
        {
            State = partialState!,
            Version = version,
            EventsProcessed = eventsProcessed,
            Success = false,
            Error = error,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents a flexible query for events.
/// </summary>
public record EventQuery
{
    /// <summary>
    /// Gets or initializes the stream identifier to filter by.
    /// If null, queries across all streams.
    /// </summary>
    public string? StreamId { get; init; }

    /// <summary>
    /// Gets or initializes the event type to filter by.
    /// If null, includes all event types.
    /// </summary>
    public string? EventType { get; init; }

    /// <summary>
    /// Gets or initializes the starting timestamp (inclusive).
    /// If null, no lower bound is applied.
    /// </summary>
    public DateTimeOffset? FromTimestamp { get; init; }

    /// <summary>
    /// Gets or initializes the ending timestamp (inclusive).
    /// If null, no upper bound is applied.
    /// </summary>
    public DateTimeOffset? ToTimestamp { get; init; }

    /// <summary>
    /// Gets or initializes the starting version (inclusive).
    /// Only applies when StreamId is specified.
    /// </summary>
    public long? FromVersion { get; init; }

    /// <summary>
    /// Gets or initializes the ending version (inclusive).
    /// Only applies when StreamId is specified.
    /// </summary>
    public long? ToVersion { get; init; }

    /// <summary>
    /// Gets or initializes the correlation identifier to filter by.
    /// If null, includes all correlation IDs.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets or initializes the causation identifier to filter by.
    /// If null, includes all causation IDs.
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Gets or initializes the page size for pagination.
    /// </summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// Gets or initializes the page number for pagination.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Gets or initializes metadata filters.
    /// Events must match all specified metadata key-value pairs.
    /// </summary>
    public Dictionary<string, object>? MetadataFilters { get; init; }

    /// <summary>
    /// Gets or initializes the sort order for results.
    /// </summary>
    public EventSortOrder SortOrder { get; init; } = EventSortOrder.TimestampAscending;

    /// <summary>
    /// Validates the query parameters.
    /// </summary>
    /// <returns>Validation result</returns>
    public (bool IsValid, string? Error) Validate()
    {
        if (PageSize <= 0)
        {
            return (false, "PageSize must be greater than 0");
        }

        if (Page <= 0)
        {
            return (false, "Page must be greater than 0");
        }

        if (FromTimestamp.HasValue && ToTimestamp.HasValue && FromTimestamp > ToTimestamp)
        {
            return (false, "FromTimestamp cannot be greater than ToTimestamp");
        }

        if (FromVersion.HasValue && ToVersion.HasValue && FromVersion > ToVersion)
        {
            return (false, "FromVersion cannot be greater than ToVersion");
        }

        if ((FromVersion.HasValue || ToVersion.HasValue) && string.IsNullOrEmpty(StreamId))
        {
            return (false, "Version filters require a specific StreamId");
        }

        return (true, null);
    }

    /// <summary>
    /// Creates a query for all events in a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>An event query for the stream</returns>
    public static EventQuery ForStream(string streamId, int pageSize = 100, int page = 1)
    {
        return new EventQuery
        {
            StreamId = streamId,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for events by type.
    /// </summary>
    /// <param name="eventType">The event type</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>An event query for the event type</returns>
    public static EventQuery ForEventType(string eventType, int pageSize = 100, int page = 1)
    {
        return new EventQuery
        {
            EventType = eventType,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for events by correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation identifier</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>An event query for the correlation ID</returns>
    public static EventQuery ForCorrelationId(string correlationId, int pageSize = 100, int page = 1)
    {
        return new EventQuery
        {
            CorrelationId = correlationId,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for events within a time range.
    /// </summary>
    /// <param name="fromTimestamp">The start time</param>
    /// <param name="toTimestamp">The end time</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>An event query for the time range</returns>
    public static EventQuery ForTimeRange(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        int pageSize = 100,
        int page = 1)
    {
        return new EventQuery
        {
            FromTimestamp = fromTimestamp,
            ToTimestamp = toTimestamp,
            PageSize = pageSize,
            Page = page
        };
    }
}

/// <summary>
/// Represents sort order options for event queries.
/// </summary>
public enum EventSortOrder
{
    /// <summary>
    /// Sort by timestamp ascending (oldest first).
    /// </summary>
    TimestampAscending = 0,

    /// <summary>
    /// Sort by timestamp descending (newest first).
    /// </summary>
    TimestampDescending = 1,

    /// <summary>
    /// Sort by version ascending (lowest version first).
    /// Only valid for single-stream queries.
    /// </summary>
    VersionAscending = 2,

    /// <summary>
    /// Sort by version descending (highest version first).
    /// Only valid for single-stream queries.
    /// </summary>
    VersionDescending = 3
}

/// <summary>
/// Represents error codes for event store operations.
/// </summary>
public enum EventStoreErrorCode
{
    /// <summary>
    /// No error occurred.
    /// </summary>
    None = 0,

    /// <summary>
    /// The specified stream was not found.
    /// </summary>
    StreamNotFound = 1,

    /// <summary>
    /// The specified event was not found.
    /// </summary>
    EventNotFound = 2,

    /// <summary>
    /// A concurrency conflict occurred (version mismatch).
    /// </summary>
    ConcurrencyConflict = 3,

    /// <summary>
    /// Event data validation failed.
    /// </summary>
    ValidationError = 4,

    /// <summary>
    /// An event with the same ID already exists.
    /// </summary>
    DuplicateEvent = 5,

    /// <summary>
    /// Event serialization failed.
    /// </summary>
    SerializationError = 6,

    /// <summary>
    /// Event deserialization failed.
    /// </summary>
    DeserializationError = 7,

    /// <summary>
    /// Storage backend is unavailable.
    /// </summary>
    StorageUnavailable = 8,

    /// <summary>
    /// The operation timed out.
    /// </summary>
    TimeoutError = 9,

    /// <summary>
    /// An internal error occurred.
    /// </summary>
    InternalError = 10,

    /// <summary>
    /// Network connectivity error.
    /// </summary>
    NetworkError = 11
}

/// <summary>
/// Represents the health status of an event store.
/// </summary>
public record EventStoreHealthStatus
{
    /// <summary>
    /// Gets whether the event store is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether the underlying storage is healthy.
    /// </summary>
    public required bool IsStorageHealthy { get; init; }

    /// <summary>
    /// Gets whether the serialization system is healthy.
    /// </summary>
    public required bool IsSerializationHealthy { get; init; }

    /// <summary>
    /// Gets the current event store implementation name.
    /// </summary>
    public required string Implementation { get; init; }

    /// <summary>
    /// Gets any health check message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets additional health check details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a healthy status.
    /// </summary>
    /// <param name="implementation">The implementation name</param>
    /// <param name="details">Optional additional details</param>
    /// <returns>A healthy event store status</returns>
    public static EventStoreHealthStatus Healthy(string implementation, Dictionary<string, object>? details = null)
    {
        return new EventStoreHealthStatus
        {
            IsHealthy = true,
            IsStorageHealthy = true,
            IsSerializationHealthy = true,
            Implementation = implementation,
            Details = details
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="implementation">The implementation name</param>
    /// <param name="message">The error message</param>
    /// <param name="isStorageHealthy">Whether storage is healthy</param>
    /// <param name="isSerializationHealthy">Whether serialization is healthy</param>
    /// <param name="details">Optional additional details</param>
    /// <returns>An unhealthy event store status</returns>
    public static EventStoreHealthStatus Unhealthy(
        string implementation,
        string message,
        bool isStorageHealthy = false,
        bool isSerializationHealthy = true,
        Dictionary<string, object>? details = null)
    {
        return new EventStoreHealthStatus
        {
            IsHealthy = false,
            IsStorageHealthy = isStorageHealthy,
            IsSerializationHealthy = isSerializationHealthy,
            Implementation = implementation,
            Message = message,
            Details = details
        };
    }
}

/// <summary>
/// Represents performance and usage metrics for an event store.
/// </summary>
public record EventStoreMetrics
{
    /// <summary>
    /// Gets the total number of append operations.
    /// </summary>
    public long AppendOperations { get; init; }

    /// <summary>
    /// Gets the total number of read operations.
    /// </summary>
    public long ReadOperations { get; init; }

    /// <summary>
    /// Gets the total number of query operations.
    /// </summary>
    public long QueryOperations { get; init; }

    /// <summary>
    /// Gets the total number of replay operations.
    /// </summary>
    public long ReplayOperations { get; init; }

    /// <summary>
    /// Gets the total number of failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Gets the average execution time for append operations in milliseconds.
    /// </summary>
    public double AverageAppendTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for read operations in milliseconds.
    /// </summary>
    public double AverageReadTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for query operations in milliseconds.
    /// </summary>
    public double AverageQueryTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for replay operations in milliseconds.
    /// </summary>
    public double AverageReplayTimeMs { get; init; }

    /// <summary>
    /// Gets the total number of events stored.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Gets the total number of streams.
    /// </summary>
    public long TotalStreams { get; init; }

    /// <summary>
    /// Gets the timestamp when metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets additional implementation-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Gets the total number of operations.
    /// </summary>
    public long TotalOperations => AppendOperations + ReadOperations + QueryOperations + ReplayOperations;

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalOperations > 0
        ? (double)(TotalOperations - FailedOperations) / TotalOperations * 100
        : 100;

    /// <summary>
    /// Creates empty metrics.
    /// </summary>
    /// <returns>Empty event store metrics</returns>
    public static EventStoreMetrics Empty()
    {
        return new EventStoreMetrics
        {
            AppendOperations = 0,
            ReadOperations = 0,
            QueryOperations = 0,
            ReplayOperations = 0,
            FailedOperations = 0,
            AverageAppendTimeMs = 0,
            AverageReadTimeMs = 0,
            AverageQueryTimeMs = 0,
            AverageReplayTimeMs = 0,
            TotalEvents = 0,
            TotalStreams = 0
        };
    }
}
