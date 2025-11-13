using System.Globalization;
using AIChat.Server.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// SQLite-based implementation of the event store.
/// Provides high-performance, ACID-compliant event storage using SQLite as the backend.
/// </summary>
public sealed partial class SqliteEventStore : IEventStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IEventSerializer _serializer;
    private readonly ILogger<SqliteEventStore> _logger;
    private readonly EventStoreMetricsCollector _metrics;

    /// <summary>
    /// Gets the name of this event store implementation.
    /// </summary>
    public string Name => "SQLite Event Store";

    /// <summary>
    /// Initializes a new instance of the SqliteEventStore class.
    /// </summary>
    /// <param name="connectionFactory">Factory for creating SQLite connections</param>
    /// <param name="serializer">Event serializer for JSON conversion</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="metrics">Metrics collector for performance tracking</param>
    public SqliteEventStore(
        ISqliteConnectionFactory connectionFactory,
        IEventSerializer serializer,
        ILogger<SqliteEventStore> logger,
        EventStoreMetricsCollector metrics)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    #region IEventAppender Implementation

    /// <summary>
    /// Appends a single event to the store.
    /// </summary>
    public async Task<EventResult> AppendAsync(IEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        using var activity = _metrics.StartAppendActivity();
        try
        {
            _logger.LogDebug("Appending event {EventId} of type {EventType} to stream {StreamId}",
                eventData.EventId, eventData.EventType, eventData.StreamId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Validate event doesn't already exist
                if (await EventExistsAsync(connection, (SqliteTransaction)transaction, eventData.EventId, cancellationToken))
                {
                    throw new DuplicateEventException(eventData.EventId, eventData.StreamId, eventData.CorrelationId);
                }

                // Validate stream version consistency
                var currentVersion = await GetStreamVersionInternalAsync(connection, (SqliteTransaction)transaction, eventData.StreamId, cancellationToken);
                if (eventData.Version != currentVersion + 1)
                {
                    throw new ConcurrencyException(eventData.StreamId, currentVersion + 1, eventData.Version, eventData.CorrelationId);
                }

                // Serialize event data
                var serializedEventData = _serializer.Serialize(eventData);
                var metadataJson = _serializer.SerializeMetadata(eventData.Metadata);

                // Insert event
                await InsertEventAsync(connection, (SqliteTransaction)transaction, eventData, serializedEventData, metadataJson, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully appended event {EventId} to stream {StreamId} at version {Version}",
                    eventData.EventId, eventData.StreamId, eventData.Version);

                _metrics.RecordAppendSuccess();
                return EventResult.CreateSuccess(1, new Dictionary<string, object>
                {
                    ["streamId"] = eventData.StreamId,
                    ["version"] = eventData.Version
                });
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Failed to rollback transaction for event {EventId}", eventData.EventId);
                }
                throw;
            }
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to append event {EventId} of type {EventType}",
                eventData.EventId, eventData.EventType);
            _metrics.RecordAppendFailure();
            throw new EventStoreException(
                $"Failed to append event {eventData.EventId}",
                EventStoreErrorCode.InternalError,
                eventData.StreamId,
                eventData.EventId,
                eventData.CorrelationId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Appends multiple events atomically to the store.
    /// </summary>
    public async Task<EventResult> AppendAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            throw new ArgumentException("Events collection cannot be empty", nameof(events));
        }

        using var activity = _metrics.StartAppendActivity();
        try
        {
            _logger.LogDebug("Appending {EventCount} events in batch", eventList.Count);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Group events by stream for validation
                var eventsByStream = eventList.GroupBy(e => e.StreamId).ToList();

                // Validate all events and stream versions
                foreach (var streamGroup in eventsByStream)
                {
                    var streamId = streamGroup.Key;
                    var streamEvents = streamGroup.OrderBy(e => e.Version).ToList();

                    // Check for duplicate events
                    foreach (var eventData in streamEvents)
                    {
                        if (await EventExistsAsync(connection, (SqliteTransaction)transaction, eventData.EventId, cancellationToken))
                        {
                            throw new DuplicateEventException(eventData.EventId, streamId, eventData.CorrelationId);
                        }
                    }

                    // Validate version continuity
                    var currentVersion = await GetStreamVersionInternalAsync(connection, (SqliteTransaction)transaction, streamId, cancellationToken);
                    var expectedVersion = currentVersion + 1;

                    foreach (var eventData in streamEvents)
                    {
                        if (eventData.Version != expectedVersion)
                        {
                            throw new ConcurrencyException(streamId, expectedVersion, eventData.Version, eventData.CorrelationId);
                        }
                        expectedVersion++;
                    }
                }

                // Insert all events
                var eventsProcessed = 0;
                foreach (var eventData in eventList)
                {
                    var serializedEventData = _serializer.Serialize(eventData);
                    var metadataJson = _serializer.SerializeMetadata(eventData.Metadata);

                    await InsertEventAsync(connection, (SqliteTransaction)transaction, eventData, serializedEventData, metadataJson, cancellationToken);
                    eventsProcessed++;
                }

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully appended {EventCount} events in batch", eventsProcessed);

                _metrics.RecordAppendSuccess(eventsProcessed);
                return EventResult.CreateSuccess(eventsProcessed, new Dictionary<string, object>
                {
                    ["streamsAffected"] = eventsByStream.Count
                });
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Failed to rollback batch append transaction");
                }
                throw;
            }
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to append {EventCount} events in batch", eventList.Count);
            _metrics.RecordAppendFailure();
            throw new EventStoreException(
                $"Failed to append batch of {eventList.Count} events",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Appends events to a stream with expected version for optimistic concurrency control.
    /// </summary>
    public async Task<EventAppendResult> AppendToStreamAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<IEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(events);

        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            throw new ArgumentException("Events collection cannot be empty", nameof(events));
        }

        using var activity = _metrics.StartAppendActivity();
        try
        {
            _logger.LogDebug("Appending {EventCount} events to stream {StreamId} with expected version {ExpectedVersion}",
                eventList.Count, streamId, expectedVersion);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Verify expected version
                var currentVersion = await GetStreamVersionInternalAsync(connection, (SqliteTransaction)transaction, streamId, cancellationToken);
                if (currentVersion != expectedVersion)
                {
                    throw new ConcurrencyException(streamId, expectedVersion, currentVersion);
                }

                // Assign consecutive versions to events
                var nextVersion = expectedVersion + 1;
                foreach (var eventData in eventList)
                {
                    // Override event version to ensure consistency
                    var versionedEvent = eventData switch
                    {
                        EventBase baseEvent => baseEvent with { Version = nextVersion },
                        _ => eventData // For non-EventBase implementations, assume version is correct
                    };

                    if (versionedEvent.Version != nextVersion)
                    {
                        throw new EventValidationException(
                            $"Event version {versionedEvent.Version} does not match expected version {nextVersion}",
                            versionedEvent.EventId,
                            streamId,
                            versionedEvent.CorrelationId);
                    }

                    // Check for duplicate events
                    if (await EventExistsAsync(connection, (SqliteTransaction)transaction, versionedEvent.EventId, cancellationToken))
                    {
                        throw new DuplicateEventException(versionedEvent.EventId, streamId, versionedEvent.CorrelationId);
                    }

                    // Serialize and insert event
                    var serializedEventData = _serializer.Serialize(versionedEvent);
                    var metadataJson = _serializer.SerializeMetadata(versionedEvent.Metadata);

                    await InsertEventAsync(connection, (SqliteTransaction)transaction, versionedEvent, serializedEventData, metadataJson, cancellationToken);
                    nextVersion++;
                }

                await transaction.CommitAsync(cancellationToken);

                var newStreamVersion = expectedVersion + eventList.Count;
                _logger.LogDebug("Successfully appended {EventCount} events to stream {StreamId}, new version: {NewVersion}",
                    eventList.Count, streamId, newStreamVersion);

                _metrics.RecordAppendSuccess(eventList.Count);
                return EventAppendResult.CreateSuccess(
                    newStreamVersion,
                    expectedVersion,
                    eventList.Count,
                    new Dictionary<string, object>
                    {
                        ["streamId"] = streamId
                    });
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Failed to rollback stream append transaction for stream {StreamId}", streamId);
                }
                throw;
            }
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to append {EventCount} events to stream {StreamId}",
                eventList.Count, streamId);
            _metrics.RecordAppendFailure();
            throw new EventStoreException(
                $"Failed to append events to stream {streamId}",
                EventStoreErrorCode.InternalError,
                streamId,
                correlationId: eventList.FirstOrDefault()?.CorrelationId,
                innerException: ex);
        }
    }

    #endregion IEventAppender Implementation

    #region IEventReader Implementation

    /// <summary>
    /// Retrieves all events for a specific stream.
    /// </summary>
    public async Task<EventQueryResult> GetEventsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting all events for stream {StreamId}", streamId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE StreamId = $streamId
                ORDER BY Version ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$streamId", streamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var eventData = DeserializeEventFromReader(reader);
                events.Add(eventData);
            }

            _logger.LogDebug("Retrieved {EventCount} events for stream {StreamId}", events.Count, streamId);

            _metrics.RecordReadSuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events for stream {StreamId}", streamId);
            _metrics.RecordReadFailure();
            throw new EventStoreException(
                $"Failed to get events for stream {streamId}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Retrieves events for a stream starting from a specific version.
    /// </summary>
    public async Task<EventQueryResult> GetEventsAsync(
        string streamId,
        long fromVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        if (fromVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "fromVersion cannot be negative");
        }

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting events for stream {StreamId} from version {FromVersion}", streamId, fromVersion);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE StreamId = $streamId AND Version >= $fromVersion
                ORDER BY Version ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$streamId", streamId);
            command.Parameters.AddWithValue("$fromVersion", fromVersion);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var eventData = DeserializeEventFromReader(reader);
                events.Add(eventData);
            }

            _logger.LogDebug("Retrieved {EventCount} events for stream {StreamId} from version {FromVersion}",
                events.Count, streamId, fromVersion);

            _metrics.RecordReadSuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events for stream {StreamId} from version {FromVersion}",
                streamId, fromVersion);
            _metrics.RecordReadFailure();
            throw new EventStoreException(
                $"Failed to get events for stream {streamId} from version {fromVersion}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Retrieves events for a stream within a version range.
    /// </summary>
    public async Task<EventQueryResult> GetEventsAsync(
        string streamId,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        if (fromVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "fromVersion cannot be negative");
        }

        if (toVersion < fromVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(toVersion), "toVersion cannot be less than fromVersion");
        }

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting events for stream {StreamId} from version {FromVersion} to {ToVersion}",
                streamId, fromVersion, toVersion);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt
                FROM Events
                WHERE StreamId = $streamId AND Version >= $fromVersion AND Version <= $toVersion
                ORDER BY Version ASC";

            var events = new List<IEvent>();

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$streamId", streamId);
            command.Parameters.AddWithValue("$fromVersion", fromVersion);
            command.Parameters.AddWithValue("$toVersion", toVersion);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var eventData = DeserializeEventFromReader(reader);
                events.Add(eventData);
            }

            _logger.LogDebug("Retrieved {EventCount} events for stream {StreamId} from version {FromVersion} to {ToVersion}",
                events.Count, streamId, fromVersion, toVersion);

            _metrics.RecordReadSuccess();
            return EventQueryResult.Create(events, events.Count, 1, events.Count);
        }
        catch (Exception ex) when (ex is not EventStoreException)
        {
            _logger.LogError(ex, "Failed to get events for stream {StreamId} from version {FromVersion} to {ToVersion}",
                streamId, fromVersion, toVersion);
            _metrics.RecordReadFailure();
            throw new EventStoreException(
                $"Failed to get events for stream {streamId} from version {fromVersion} to {toVersion}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets the current version of a stream.
    /// </summary>
    public async Task<long> GetStreamVersionAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            return await GetStreamVersionInternalAsync(connection, null, streamId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version for stream {StreamId}", streamId);
            throw new EventStoreException(
                $"Failed to get version for stream {streamId}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Checks if a stream exists.
    /// </summary>
    public async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = "SELECT 1 FROM Events WHERE StreamId = $streamId LIMIT 1";

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$streamId", streamId);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of stream {StreamId}", streamId);
            throw new EventStoreException(
                $"Failed to check existence of stream {streamId}",
                EventStoreErrorCode.InternalError,
                streamId,
                innerException: ex);
        }
    }

    #endregion IEventReader Implementation

    #region Helper Methods

    /// <summary>
    /// Inserts an event into the database within a transaction.
    /// </summary>
    private static async Task InsertEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEvent eventData,
        string serializedEventData,
        string? metadataJson,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO Events (EventId, StreamId, EventType, Version, Timestamp, CorrelationId, CausationId, EventData, Metadata, CreatedAt)
            VALUES ($eventId, $streamId, $eventType, $version, $timestamp, $correlationId, $causationId, $eventData, $metadata, $createdAt)";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        command.Parameters.AddWithValue("$eventId", eventData.EventId);
        command.Parameters.AddWithValue("$streamId", eventData.StreamId);
        command.Parameters.AddWithValue("$eventType", eventData.EventType);
        command.Parameters.AddWithValue("$version", eventData.Version);
        command.Parameters.AddWithValue("$timestamp", eventData.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$correlationId", (object?)eventData.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$causationId", (object?)eventData.CausationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$eventData", serializedEventData);
        command.Parameters.AddWithValue("$metadata", (object?)metadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if an event already exists in the database.
    /// </summary>
    private static async Task<bool> EventExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string eventId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT 1 FROM Events WHERE EventId = $eventId LIMIT 1";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$eventId", eventId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    /// <summary>
    /// Gets the current version of a stream within a transaction.
    /// </summary>
    private static async Task<long> GetStreamVersionInternalAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string streamId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT IFNULL(MAX(Version), -1) FROM Events WHERE StreamId = $streamId";

        await using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }
        command.CommandText = sql;
        command.Parameters.AddWithValue("$streamId", streamId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Deserializes an event from a database reader.
    /// </summary>
    private IEvent DeserializeEventFromReader(SqliteDataReader reader)
    {
        var eventId = reader.GetString(0);
        var streamId = reader.GetString(1);
        var eventType = reader.GetString(2);
        var version = reader.GetInt64(3);
        var timestamp = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
        var correlationId = reader.IsDBNull(5) ? null : reader.GetString(5);
        var causationId = reader.IsDBNull(6) ? null : reader.GetString(6);
        var eventData = reader.GetString(7);
        var metadataJson = reader.IsDBNull(8) ? null : reader.GetString(8);

        try
        {
            return _serializer.Deserialize(eventType, eventData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize event {EventId} of type {EventType}", eventId, eventType);
            throw new EventDeserializationException(
                $"Failed to deserialize event {eventId} of type {eventType}",
                eventType,
                eventData,
                eventId,
                correlationId,
                ex);
        }
    }

    #endregion Helper Methods

    #region IEventStore Core Implementation

    /// <summary>
    /// Checks the health of the event store.
    /// </summary>
    public async Task<EventStoreHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Test basic connectivity and schema
            const string sql = "SELECT COUNT(*) FROM Events LIMIT 1";
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteScalarAsync(cancellationToken);

            // Test serialization
            var testEvent = ChatMessageSentEvent.Create(
                "test-stream",
                1,
                new ChatMessageEventData
                {
                    MessageId = "test-message",
                    UserId = "test-user",
                    Content = "Health check test",
                    MessageType = "test",
                    ChatId = "test-chat"
                });

            _serializer.Serialize(testEvent);

            return EventStoreHealthStatus.Healthy(Name, new Dictionary<string, object>
            {
                ["connectionFactory"] = _connectionFactory.GetType().Name,
                ["serializer"] = _serializer.GetType().Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event store health check failed");
            return EventStoreHealthStatus.Unhealthy(
                Name,
                ex.Message,
                isStorageHealthy: false,
                isSerializationHealthy: true,
                new Dictionary<string, object>
                {
                    ["error"] = ex.GetType().Name
                });
        }
    }

    /// <summary>
    /// Gets performance metrics for the event store.
    /// </summary>
    public async Task<EventStoreMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var baseMetrics = _metrics.GetCurrentMetrics();

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Get additional storage metrics
            const string eventCountSql = "SELECT COUNT(*) FROM Events";
            const string streamCountSql = "SELECT COUNT(DISTINCT StreamId) FROM Events";

            await using var eventCountCommand = connection.CreateCommand();
            eventCountCommand.CommandText = eventCountSql;
            var totalEvents = Convert.ToInt64(await eventCountCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            await using var streamCountCommand = connection.CreateCommand();
            streamCountCommand.CommandText = streamCountSql;
            var totalStreams = Convert.ToInt64(await streamCountCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            return baseMetrics with
            {
                TotalEvents = totalEvents,
                TotalStreams = totalStreams,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["implementation"] = Name,
                    ["databaseSize"] = "unknown" // SQLite doesn't provide easy size queries
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get extended metrics, returning base metrics only");
            return baseMetrics;
        }
    }

    /// <summary>
    /// Optimizes the event store for better performance.
    /// </summary>
    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting event store optimization");

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Run SQLite optimization commands
            var optimizationCommands = new[]
            {
                "ANALYZE Events;",
                "PRAGMA optimize;",
                "VACUUM;"
            };

            foreach (var commandText in optimizationCommands)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.CommandTimeout = 300; // 5 minutes for VACUUM
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Event store optimization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event store optimization failed");
            throw new EventStoreException(
                "Event store optimization failed",
                EventStoreErrorCode.InternalError,
                innerException: ex);
        }
    }

    #endregion IEventStore Core Implementation
}
