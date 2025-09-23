namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Core interface for event sourcing operations following Interface Segregation Principle.
/// Provides append-only event storage with query and replay capabilities.
/// </summary>
public interface IEventStore : IEventAppender, IEventReader, IEventQuery, IEventReplay
{
    /// <summary>
    /// Gets the name of the event store implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the event store is currently healthy and available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventStoreHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance and usage metrics for the event store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current event store metrics</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventStoreMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the event store for better performance.
    /// This may include index optimization, cleanup operations, etc.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the optimization operation</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for appending events to the store (write operations).
/// Separated for write-only access scenarios.
/// </summary>
public interface IEventAppender
{
    /// <summary>
    /// Appends a single event to the store.
    /// The event must have a unique EventId and proper stream version.
    /// </summary>
    /// <param name="eventData">The event to append</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the append operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when event is null</exception>
    /// <exception cref="EventStoreException">Thrown when append fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventResult> AppendAsync(IEvent eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple events atomically to the store.
    /// All events succeed or all fail as a single transaction.
    /// </summary>
    /// <param name="events">The events to append</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the batch append operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when events is null</exception>
    /// <exception cref="ArgumentException">Thrown when events collection is empty</exception>
    /// <exception cref="EventStoreException">Thrown when append fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventResult> AppendAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends events to a stream with expected version for optimistic concurrency control.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="expectedVersion">The expected current version of the stream</param>
    /// <param name="events">The events to append</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the append operation with new stream version</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or events is null</exception>
    /// <exception cref="ConcurrencyException">Thrown when expected version doesn't match actual version</exception>
    /// <exception cref="EventStoreException">Thrown when append fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventAppendResult> AppendToStreamAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<IEvent> events,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for reading events from the store (read operations).
/// Separated for read-only access scenarios.
/// </summary>
public interface IEventReader
{
    /// <summary>
    /// Retrieves all events for a specific stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All events in the stream ordered by version</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="EventStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events for a stream starting from a specific version.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="fromVersion">The starting version (inclusive)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Events from the specified version onwards</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when fromVersion is negative</exception>
    /// <exception cref="EventStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsAsync(
        string streamId,
        long fromVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events for a stream within a version range.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="fromVersion">The starting version (inclusive)</param>
    /// <param name="toVersion">The ending version (inclusive)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Events within the specified version range</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when version range is invalid</exception>
    /// <exception cref="EventStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsAsync(
        string streamId,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current version of a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The current version of the stream, or -1 if stream doesn't exist</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="EventStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<long> GetStreamVersionAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a stream exists.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the stream exists, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="EventStoreException">Thrown when check fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for querying events with flexible criteria.
/// Separated for advanced query scenarios.
/// </summary>
public interface IEventQuery
{
    /// <summary>
    /// Queries events using flexible criteria.
    /// Supports filtering by event type, time range, correlation ID, etc.
    /// </summary>
    /// <param name="query">The query criteria</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Events matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="EventStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> QueryEventsAsync(EventQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events by correlation ID across all streams.
    /// Useful for tracing related events in distributed scenarios.
    /// </summary>
    /// <param name="correlationId">The correlation identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All events with the specified correlation ID</returns>
    /// <exception cref="ArgumentNullException">Thrown when correlationId is null</exception>
    /// <exception cref="EventStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events by event type across all streams.
    /// Useful for analyzing specific types of events.
    /// </summary>
    /// <param name="eventType">The event type</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All events of the specified type</returns>
    /// <exception cref="ArgumentNullException">Thrown when eventType is null</exception>
    /// <exception cref="EventStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsByTypeAsync(
        string eventType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events within a time range across all streams.
    /// </summary>
    /// <param name="fromTimestamp">The start time (inclusive)</param>
    /// <param name="toTimestamp">The end time (inclusive)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All events within the specified time range</returns>
    /// <exception cref="ArgumentException">Thrown when time range is invalid</exception>
    /// <exception cref="EventStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventQueryResult> GetEventsByTimeRangeAsync(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for replaying events to rebuild state.
/// Separated for replay and projection scenarios.
/// </summary>
public interface IEventReplay
{
    /// <summary>
    /// Replays events from a stream to rebuild state using a projection.
    /// </summary>
    /// <typeparam name="T">The type of state to rebuild</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="projection">The projection logic for rebuilding state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The rebuilt state from replaying events</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or projection is null</exception>
    /// <exception cref="EventStoreException">Thrown when replay fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventReplayResult<T>> ReplayAsync<T>(
        string streamId,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays events from a stream up to a specific version.
    /// Useful for point-in-time state reconstruction.
    /// </summary>
    /// <typeparam name="T">The type of state to rebuild</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="toVersion">The maximum version to replay (inclusive)</param>
    /// <param name="projection">The projection logic for rebuilding state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The rebuilt state from replaying events</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or projection is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when toVersion is negative</exception>
    /// <exception cref="EventStoreException">Thrown when replay fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventReplayResult<T>> ReplayAsync<T>(
        string streamId,
        long toVersion,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays events from multiple streams to rebuild aggregate state.
    /// </summary>
    /// <typeparam name="T">The type of state to rebuild</typeparam>
    /// <param name="streamIds">The stream identifiers</param>
    /// <param name="projection">The projection logic for rebuilding state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The rebuilt state from replaying events</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamIds or projection is null</exception>
    /// <exception cref="EventStoreException">Thrown when replay fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<EventReplayResult<T>> ReplayAsync<T>(
        IEnumerable<string> streamIds,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);
}