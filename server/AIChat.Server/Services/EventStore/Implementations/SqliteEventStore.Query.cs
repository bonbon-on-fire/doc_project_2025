using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// Query and replay implementation for SqliteEventStore.
/// </summary>
public sealed partial class SqliteEventStore : IEventQuery, IEventReplay
{
    #region IEventQuery Implementation

    /// <summary>
    /// Queries events using flexible criteria.
    /// </summary>
    public async Task<EventQueryResult> QueryEventsAsync(EventQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (isValid, error) = query.Validate();
        if (!isValid)
        {
            throw new EventValidationException(error!, eventId: null, streamId: null, correlationId: query.CorrelationId, innerException: null);
        }

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Executing event query with filters: {Filters}",
                GetQueryDescription(query));

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Build the query SQL and parameters
            var (sql, parameters, countSql, countParameters) = BuildQuerySql(query);

            // Execute count query first
            var totalCount = await ExecuteCountQueryAsync(connection, countSql, countParameters, cancellationToken);

            // Execute main query
            var events = await ExecuteEventQueryAsync(connection, sql, parameters, cancellationToken);

            _logger.LogDebug("Query returned {EventCount} events out of {TotalCount} total matching events",
                events.Count, totalCount);

            _metrics.RecordQuerySuccess();
            return EventQueryResult.Create(
                events,
                totalCount,
                query.Page,
                query.PageSize,
                new Dictionary<string, object>
                {
                    ["queryType"] = "flexible",
                    ["hasFilters"] = HasFilters(query)
                });
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to execute event query");
            _metrics.RecordQueryFailure();
            throw new EventStoreException(
                "Failed to execute event query",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets events by correlation ID across all streams.
    /// </summary>
    public async Task<EventQueryResult> GetEventsByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Getting events by correlation ID: {CorrelationId}", correlationId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE CorrelationId = $correlationId
                ORDER BY Timestamp ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$correlationId", correlationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var @event = DeserializeEventFromReader(reader);
                events.Add(@event);
            }

            _logger.LogDebug("Found {EventCount} events with correlation ID {CorrelationId}",
                events.Count, correlationId);

            _metrics.RecordQuerySuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events by correlation ID {CorrelationId}", correlationId);
            _metrics.RecordQueryFailure();
            throw new EventStoreException(
                $"Failed to get events by correlation ID {correlationId}",
                EventStoreErrorCode.InternalError,
                correlationId: correlationId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets events by event type across all streams.
    /// </summary>
    public async Task<EventQueryResult> GetEventsByTypeAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Getting events by type: {EventType}", eventType);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE EventType = $eventType
                ORDER BY Timestamp ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$eventType", eventType);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var @event = DeserializeEventFromReader(reader);
                events.Add(@event);
            }

            _logger.LogDebug("Found {EventCount} events of type {EventType}", events.Count, eventType);

            _metrics.RecordQuerySuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events by type {EventType}", eventType);
            _metrics.RecordQueryFailure();
            throw new EventStoreException(
                $"Failed to get events by type {eventType}",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets events within a time range across all streams.
    /// </summary>
    public async Task<EventQueryResult> GetEventsByTimeRangeAsync(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        CancellationToken cancellationToken = default)
    {
        if (fromTimestamp > toTimestamp)
        {
            throw new ArgumentException("fromTimestamp cannot be greater than toTimestamp");
        }

        using var activity = _metrics.StartQueryActivity();
        try
        {
            _logger.LogDebug("Getting events by time range: {FromTime} to {ToTime}",
                fromTimestamp, toTimestamp);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE Timestamp >= $fromTimestamp AND Timestamp <= $toTimestamp
                ORDER BY Timestamp ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$fromTimestamp", fromTimestamp.ToString("O"));
            command.Parameters.AddWithValue("$toTimestamp", toTimestamp.ToString("O"));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var @event = DeserializeEventFromReader(reader);
                events.Add(@event);
            }

            _logger.LogDebug("Found {EventCount} events in time range {FromTime} to {ToTime}",
                events.Count, fromTimestamp, toTimestamp);

            _metrics.RecordQuerySuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events by time range {FromTime} to {ToTime}",
                fromTimestamp, toTimestamp);
            _metrics.RecordQueryFailure();
            throw new EventStoreException(
                $"Failed to get events by time range {fromTimestamp} to {toTimestamp}",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    #endregion IEventQuery Implementation

    #region IEventReplay Implementation

    /// <summary>
    /// Replays events from a stream to rebuild state using a projection.
    /// </summary>
    public async Task<EventReplayResult<T>> ReplayAsync<T>(
        string streamId,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(projection);

        using var activity = _metrics.StartReplayActivity();
        try
        {
            _logger.LogDebug("Starting replay of stream {StreamId} using projection {ProjectionName}",
                streamId, projection.Name);

            // Get all events for the stream
            var queryResult = await GetEventsAsync(streamId, cancellationToken);
            var events = queryResult.Events;

            // Initialize state and replay events
            var state = projection.CreateInitialState();
            var eventsProcessed = 0;
            var version = -1L;

            foreach (var @event in events)
            {
                if (projection.CanHandle(@event.EventType))
                {
                    try
                    {
                        state = projection.Apply(state, @event);
                        version = @event.Version;
                        eventsProcessed++;

                        _logger.LogTrace("Applied event {EventId} of type {EventType} at version {Version}",
                            @event.EventId, @event.EventType, @event.Version);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to apply event {EventId} during replay", @event.EventId);
                        _metrics.RecordReplayFailure();
                        return EventReplayResultFactory.CreateFailure<T>(
                            $"Failed to apply event {@event.EventId}: {ex.Message}",
                            state,
                            version,
                            eventsProcessed,
                            new Dictionary<string, object>
                            {
                                ["failedEventId"] = @event.EventId,
                                ["failedEventType"] = @event.EventType
                            });
                    }
                }
                else
                {
                    _logger.LogTrace("Skipped event {EventId} of type {EventType} (not handled by projection)",
                        @event.EventId, @event.EventType);
                }
            }

            _logger.LogDebug("Completed replay of stream {StreamId}: processed {EventsProcessed}/{TotalEvents} events to version {Version}",
                streamId, eventsProcessed, events.Count, version);

            _metrics.RecordReplaySuccess();
            return EventReplayResultFactory.CreateSuccess<T>(
                state,
                version,
                eventsProcessed,
                new Dictionary<string, object>
                {
                    ["streamId"] = streamId,
                    ["projectionName"] = projection.Name,
                    ["totalEvents"] = events.Count
                });
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to replay stream {StreamId} with projection {ProjectionName}",
                streamId, projection.Name);
            _metrics.RecordReplayFailure();
            throw new EventStoreException(
                $"Failed to replay stream {streamId}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Replays events from a stream up to a specific version.
    /// </summary>
    public async Task<EventReplayResult<T>> ReplayAsync<T>(
        string streamId,
        long toVersion,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(projection);

        if (toVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toVersion), "toVersion cannot be negative");
        }

        using var activity = _metrics.StartReplayActivity();
        try
        {
            _logger.LogDebug("Starting replay of stream {StreamId} up to version {ToVersion} using projection {ProjectionName}",
                streamId, toVersion, projection.Name);

            // Get events up to the specified version
            var queryResult = await GetEventsAsync(streamId, 0, toVersion, cancellationToken);
            var events = queryResult.Events;

            // Initialize state and replay events
            var state = projection.CreateInitialState();
            var eventsProcessed = 0;
            var version = -1L;

            foreach (var @event in events)
            {
                if (projection.CanHandle(@event.EventType))
                {
                    try
                    {
                        state = projection.Apply(state, @event);
                        version = @event.Version;
                        eventsProcessed++;

                        _logger.LogTrace("Applied event {EventId} of type {EventType} at version {Version}",
                            @event.EventId, @event.EventType, @event.Version);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to apply event {EventId} during point-in-time replay", @event.EventId);
                        _metrics.RecordReplayFailure();
                        return EventReplayResultFactory.CreateFailure<T>(
                            $"Failed to apply event {@event.EventId}: {ex.Message}",
                            state,
                            version,
                            eventsProcessed,
                            new Dictionary<string, object>
                            {
                                ["failedEventId"] = @event.EventId,
                                ["failedEventType"] = @event.EventType,
                                ["targetVersion"] = toVersion
                            });
                    }
                }
            }

            _logger.LogDebug("Completed point-in-time replay of stream {StreamId} up to version {ToVersion}: processed {EventsProcessed}/{TotalEvents} events",
                streamId, toVersion, eventsProcessed, events.Count);

            _metrics.RecordReplaySuccess();
            return EventReplayResultFactory.CreateSuccess<T>(
                state,
                version,
                eventsProcessed,
                new Dictionary<string, object>
                {
                    ["streamId"] = streamId,
                    ["projectionName"] = projection.Name,
                    ["targetVersion"] = toVersion,
                    ["totalEvents"] = events.Count
                });
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to replay stream {StreamId} up to version {ToVersion} with projection {ProjectionName}",
                streamId, toVersion, projection.Name);
            _metrics.RecordReplayFailure();
            throw new EventStoreException(
                $"Failed to replay stream {streamId} up to version {toVersion}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Replays events from multiple streams to rebuild aggregate state.
    /// </summary>
    public async Task<EventReplayResult<T>> ReplayAsync<T>(
        IEnumerable<string> streamIds,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamIds);
        ArgumentNullException.ThrowIfNull(projection);

        var streamIdList = streamIds.ToList();
        if (streamIdList.Count == 0)
        {
            throw new ArgumentException("streamIds cannot be empty", nameof(streamIds));
        }

        using var activity = _metrics.StartReplayActivity();
        try
        {
            _logger.LogDebug("Starting multi-stream replay of {StreamCount} streams using projection {ProjectionName}",
                streamIdList.Count, projection.Name);

            // Collect all events from all streams
            var allEvents = new List<IEvent>();
            foreach (var streamId in streamIdList)
            {
                var queryResult = await GetEventsAsync(streamId, cancellationToken);
                allEvents.AddRange(queryResult.Events);
            }

            // Sort events by timestamp for proper chronological replay
            allEvents.Sort((e1, e2) => e1.Timestamp.CompareTo(e2.Timestamp));

            // Initialize state and replay events
            var state = projection.CreateInitialState();
            var eventsProcessed = 0;
            var lastVersion = -1L;

            foreach (var @event in allEvents)
            {
                if (projection.CanHandle(@event.EventType))
                {
                    try
                    {
                        state = projection.Apply(state, @event);
                        lastVersion = Math.Max(lastVersion, @event.Version);
                        eventsProcessed++;

                        _logger.LogTrace("Applied event {EventId} of type {EventType} from stream {StreamId} at version {Version}",
                            @event.EventId, @event.EventType, @event.StreamId, @event.Version);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to apply event {EventId} during multi-stream replay", @event.EventId);
                        _metrics.RecordReplayFailure();
                        return EventReplayResultFactory.CreateFailure<T>(
                            $"Failed to apply event {@event.EventId}: {ex.Message}",
                            state,
                            lastVersion,
                            eventsProcessed,
                            new Dictionary<string, object>
                            {
                                ["failedEventId"] = @event.EventId,
                                ["failedEventType"] = @event.EventType,
                                ["streamCount"] = streamIdList.Count
                            });
                    }
                }
            }

            _logger.LogDebug("Completed multi-stream replay of {StreamCount} streams: processed {EventsProcessed}/{TotalEvents} events",
                streamIdList.Count, eventsProcessed, allEvents.Count);

            _metrics.RecordReplaySuccess();
            return EventReplayResultFactory.CreateSuccess<T>(
                state,
                lastVersion,
                eventsProcessed,
                new Dictionary<string, object>
                {
                    ["streamCount"] = streamIdList.Count,
                    ["projectionName"] = projection.Name,
                    ["totalEvents"] = allEvents.Count
                });
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to replay {StreamCount} streams with projection {ProjectionName}",
                streamIdList.Count, projection.Name);
            _metrics.RecordReplayFailure();
            throw new EventStoreException(
                $"Failed to replay {streamIdList.Count} streams",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    #endregion IEventReplay Implementation

    #region Query Helper Methods

    /// <summary>
    /// Builds SQL query and parameters for event query.
    /// </summary>
    private static (string sql, SqliteParameter[] parameters, string countSql, SqliteParameter[] countParameters) BuildQuerySql(EventQuery query)
    {
        var whereConditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        // Add filters
        if (!string.IsNullOrEmpty(query.StreamId))
        {
            whereConditions.Add("StreamId = $streamId");
            parameters.Add(new SqliteParameter("$streamId", query.StreamId));
        }

        if (!string.IsNullOrEmpty(query.EventType))
        {
            whereConditions.Add("EventType = $eventType");
            parameters.Add(new SqliteParameter("$eventType", query.EventType));
        }

        if (query.FromTimestamp.HasValue)
        {
            whereConditions.Add("Timestamp >= $fromTimestamp");
            parameters.Add(new SqliteParameter("$fromTimestamp", query.FromTimestamp.Value.ToString("O")));
        }

        if (query.ToTimestamp.HasValue)
        {
            whereConditions.Add("Timestamp <= $toTimestamp");
            parameters.Add(new SqliteParameter("$toTimestamp", query.ToTimestamp.Value.ToString("O")));
        }

        if (query.FromVersion.HasValue)
        {
            whereConditions.Add("Version >= $fromVersion");
            parameters.Add(new SqliteParameter("$fromVersion", query.FromVersion.Value));
        }

        if (query.ToVersion.HasValue)
        {
            whereConditions.Add("Version <= $toVersion");
            parameters.Add(new SqliteParameter("$toVersion", query.ToVersion.Value));
        }

        if (!string.IsNullOrEmpty(query.CorrelationId))
        {
            whereConditions.Add("CorrelationId = $correlationId");
            parameters.Add(new SqliteParameter("$correlationId", query.CorrelationId));
        }

        if (!string.IsNullOrEmpty(query.CausationId))
        {
            whereConditions.Add("CausationId = $causationId");
            parameters.Add(new SqliteParameter("$causationId", query.CausationId));
        }

        // Build WHERE clause
        var whereClause = whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : "";

        // Build ORDER BY clause
        var orderByClause = query.SortOrder switch
        {
            EventSortOrder.TimestampAscending => "ORDER BY Timestamp ASC",
            EventSortOrder.TimestampDescending => "ORDER BY Timestamp DESC",
            EventSortOrder.VersionAscending => "ORDER BY Version ASC",
            EventSortOrder.VersionDescending => "ORDER BY Version DESC",
            _ => "ORDER BY Timestamp ASC"
        };

        // Build pagination
        var offset = Math.Max(0, (query.Page - 1) * query.PageSize);
        var limitClause = $"LIMIT {query.PageSize} OFFSET {offset}";

        // Build main query
        var sql = $@"
            SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
            FROM Events
            {whereClause}
            {orderByClause}
            {limitClause}";

        // Build count query
        var countSql = $@"
            SELECT COUNT(*)
            FROM Events
            {whereClause}";

        // Clone parameters for count query
        var countParameters = parameters.Select(p => new SqliteParameter(p.ParameterName, p.Value)).ToArray();

        return (sql, parameters.ToArray(), countSql, countParameters);
    }

    /// <summary>
    /// Executes a count query and returns the total count.
    /// </summary>
    private static async Task<int> ExecuteCountQueryAsync(
        SqliteConnection connection,
        string countSql,
        SqliteParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = countSql;
        command.Parameters.AddRange(parameters);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Executes an event query and returns the events.
    /// </summary>
    private async Task<IReadOnlyList<IEvent>> ExecuteEventQueryAsync(
        SqliteConnection connection,
        string sql,
        SqliteParameter[] parameters,
        CancellationToken cancellationToken)
    {
        var events = new List<IEvent>();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var @event = DeserializeEventFromReader(reader);
            events.Add(@event);
        }

        return events;
    }

    /// <summary>
    /// Gets a description of the query for logging.
    /// </summary>
    private static string GetQueryDescription(EventQuery query)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(query.StreamId))
        {
            parts.Add($"StreamId={query.StreamId}");
        }

        if (!string.IsNullOrEmpty(query.EventType))
        {
            parts.Add($"EventType={query.EventType}");
        }

        if (query.FromTimestamp.HasValue)
        {
            parts.Add($"FromTime={query.FromTimestamp:s}");
        }

        if (query.ToTimestamp.HasValue)
        {
            parts.Add($"ToTime={query.ToTimestamp:s}");
        }

        if (query.FromVersion.HasValue)
        {
            parts.Add($"FromVersion={query.FromVersion}");
        }

        if (query.ToVersion.HasValue)
        {
            parts.Add($"ToVersion={query.ToVersion}");
        }

        if (!string.IsNullOrEmpty(query.CorrelationId))
        {
            parts.Add($"CorrelationId={query.CorrelationId}");
        }

        parts.Add($"Page={query.Page}");
        parts.Add($"PageSize={query.PageSize}");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Checks if the query has any filters applied.
    /// </summary>
    private static bool HasFilters(EventQuery query)
    {
        return !string.IsNullOrEmpty(query.StreamId) ||
               !string.IsNullOrEmpty(query.EventType) ||
               query.FromTimestamp.HasValue ||
               query.ToTimestamp.HasValue ||
               query.FromVersion.HasValue ||
               query.ToVersion.HasValue ||
               !string.IsNullOrEmpty(query.CorrelationId) ||
               !string.IsNullOrEmpty(query.CausationId) ||
               query.MetadataFilters?.Count > 0;
    }

    #endregion Query Helper Methods
}
