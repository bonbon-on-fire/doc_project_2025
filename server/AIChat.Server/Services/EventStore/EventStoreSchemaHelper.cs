using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Schema helper for Event Store database tables.
/// Extends the existing database schema with event sourcing tables.
/// </summary>
public static class EventStoreSchemaHelper
{
    /// <summary>
    /// Ensures the event store schema exists in the database.
    /// This should be called after the main schema is created.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task EnsureEventStoreSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        // Check if we need to reset the event store schema
        var needsReset = await NeedsEventStoreSchemaResetAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (needsReset)
            {
                await DropEventStoreSchemaAsync(connection, (SqliteTransaction)transaction, cancellationToken);
            }

            await CreateEventStoreSchemaAsync(connection, (SqliteTransaction)transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Ignore rollback failures
            }
            throw;
        }
    }

    /// <summary>
    /// Checks if the event store schema needs to be reset.
    /// </summary>
    private static async Task<bool> NeedsEventStoreSchemaResetAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the Events table exists with the correct schema
            const string checkTableSql = @"
                SELECT sql FROM sqlite_master
                WHERE type='table' AND name='Events'";

            await using var command = connection.CreateCommand();
            command.CommandText = checkTableSql;
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result == null)
            {
                // Table doesn't exist, no reset needed
                return false;
            }

            var existingSchema = result.ToString();
            var expectedSchema = GetExpectedEventsTableSchema();

            // Compare schemas (simplified comparison)
            return !existingSchema!.Contains("EventId") ||
                   !existingSchema.Contains("StreamId") ||
                   !existingSchema.Contains("EventType") ||
                   !existingSchema.Contains("Version") ||
                   !existingSchema.Contains("EventData");
        }
        catch
        {
            // If we can't determine the schema, assume reset is needed
            return true;
        }
    }

    /// <summary>
    /// Drops the event store schema.
    /// </summary>
    private static async Task DropEventStoreSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string dropSql = @"
            DROP INDEX IF EXISTS idx_events_stream_version;
            DROP INDEX IF EXISTS idx_events_stream_id;
            DROP INDEX IF EXISTS idx_events_event_type;
            DROP INDEX IF EXISTS idx_events_timestamp;
            DROP INDEX IF EXISTS idx_events_correlation_id;
            DROP INDEX IF EXISTS idx_events_causation_id;
            DROP INDEX IF EXISTS idx_snapshots_stream_id;
            DROP TABLE IF EXISTS EventSnapshots;
            DROP TABLE IF EXISTS Events;";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = dropSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the event store schema.
    /// </summary>
    private static async Task CreateEventStoreSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string createSql = @"
-- Events table for immutable event storage
CREATE TABLE IF NOT EXISTS Events (
    EventId TEXT PRIMARY KEY,                    -- Event identifier (GUID)
    StreamId TEXT NOT NULL,                      -- Stream identifier
    EventType TEXT NOT NULL,                     -- Event type for deserialization
    Version INTEGER NOT NULL,                    -- Stream version number
    Timestamp TEXT NOT NULL,                     -- Event timestamp (ISO 8601)
    CorrelationId TEXT,                          -- Correlation tracking
    CausationId TEXT,                            -- Causation tracking
    EventData TEXT NOT NULL,                     -- Serialized event data (JSON)
    Metadata TEXT,                               -- Serialized metadata (JSON)
    CreatedAt TEXT DEFAULT (datetime('now')),    -- Record creation timestamp

    UNIQUE(StreamId, Version)                    -- Ensure version uniqueness per stream
);

-- Indexes for query performance
CREATE INDEX IF NOT EXISTS idx_events_stream_id ON Events(StreamId);
CREATE INDEX IF NOT EXISTS idx_events_stream_version ON Events(StreamId, Version);
CREATE INDEX IF NOT EXISTS idx_events_event_type ON Events(EventType);
CREATE INDEX IF NOT EXISTS idx_events_timestamp ON Events(Timestamp);
CREATE INDEX IF NOT EXISTS idx_events_correlation_id ON Events(CorrelationId) WHERE CorrelationId IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_events_causation_id ON Events(CausationId) WHERE CausationId IS NOT NULL;

-- Snapshots table for performance optimization
CREATE TABLE IF NOT EXISTS EventSnapshots (
    Id TEXT PRIMARY KEY,                         -- Snapshot identifier (GUID)
    StreamId TEXT NOT NULL UNIQUE,               -- Stream identifier
    Version INTEGER NOT NULL,                    -- Stream version at snapshot
    SnapshotData TEXT NOT NULL,                  -- Serialized snapshot state
    CreatedAt TEXT DEFAULT (datetime('now'))     -- Snapshot creation timestamp
);

-- Index for snapshots
CREATE INDEX IF NOT EXISTS idx_snapshots_stream_id ON EventSnapshots(StreamId);";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = createSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the expected schema for the Events table for comparison.
    /// </summary>
    private static string GetExpectedEventsTableSchema()
    {
        return @"CREATE TABLE Events (
            EventId TEXT PRIMARY KEY,
            StreamId TEXT NOT NULL,
            EventType TEXT NOT NULL,
            Version INTEGER NOT NULL,
            Timestamp TEXT NOT NULL,
            CorrelationId TEXT,
            CausationId TEXT,
            EventData TEXT NOT NULL,
            Metadata TEXT,
            CreatedAt TEXT DEFAULT (datetime('now')),
            UNIQUE(StreamId, Version)
        )";
    }

    /// <summary>
    /// Validates the event store schema integrity.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if schema is valid, false otherwise</returns>
    public static async Task<bool> ValidateEventStoreSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check that all required tables exist
            foreach (var tableName in new[] { "Events", "EventSnapshots" })
            {
                const string checkTableSql = @"
                    SELECT name FROM sqlite_master
                    WHERE type='table' AND name=$tableName";

                await using var command = connection.CreateCommand();
                command.CommandText = checkTableSql;
                command.Parameters.AddWithValue("$tableName", tableName);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result == null)
                {
                    return false; // Table is missing
                }
            }

            // Check that we can insert and query a test event
            const string testEventId = "test-event-schema-validation";

            // Clean up any existing test data
            await using (var cleanupCommand = connection.CreateCommand())
            {
                cleanupCommand.CommandText = "DELETE FROM Events WHERE EventId = $testEventId";
                cleanupCommand.Parameters.AddWithValue("$testEventId", testEventId);
                await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            // Insert a test event
            const string insertSql = @"
                INSERT INTO Events (EventId, StreamId, EventType, Version, Timestamp, EventData)
                VALUES ($eventId, $streamId, $eventType, $version, $timestamp, $eventData)";

            await using (var insertCommand = connection.CreateCommand())
            {
                insertCommand.CommandText = insertSql;
                insertCommand.Parameters.AddWithValue("$eventId", testEventId);
                insertCommand.Parameters.AddWithValue("$streamId", "test-stream");
                insertCommand.Parameters.AddWithValue("$eventType", "TestEvent");
                insertCommand.Parameters.AddWithValue("$version", 1);
                insertCommand.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
                insertCommand.Parameters.AddWithValue("$eventData", "{}");

                var rowsAffected = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                if (rowsAffected != 1)
                {
                    return false; // Insert failed
                }
            }

            // Query the test event back
            const string selectSql = @"
                SELECT EventId, StreamId, EventType, Version, Timestamp, EventData
                FROM Events WHERE EventId = $testEventId";

            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.CommandText = selectSql;
                selectCommand.Parameters.AddWithValue("$testEventId", testEventId);

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return false; // Query failed
                }

                // Verify the data matches what we inserted
                var eventId = reader.GetString(0);
                var streamId = reader.GetString(1);
                var eventType = reader.GetString(2);
                var version = reader.GetInt64(3);

                if (eventId != testEventId || streamId != "test-stream" ||
                    eventType != "TestEvent" || version != 1)
                {
                    return false; // Data corruption
                }
            }

            // Clean up test data
            await using (var cleanupCommand = connection.CreateCommand())
            {
                cleanupCommand.CommandText = "DELETE FROM Events WHERE EventId = $testEventId";
                cleanupCommand.Parameters.AddWithValue("$testEventId", testEventId);
                await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            return true; // All checks passed
        }
        catch
        {
            return false; // Any exception means schema is invalid
        }
    }

    /// <summary>
    /// Gets statistics about the event store data.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Event store statistics</returns>
    public static async Task<EventStoreStatistics> GetEventStoreStatisticsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string statsSql = @"
                SELECT
                    (SELECT COUNT(*) FROM Events) as TotalEvents,
                    (SELECT COUNT(DISTINCT StreamId) FROM Events) as TotalStreams,
                    (SELECT COUNT(*) FROM EventSnapshots) as TotalSnapshots,
                    (SELECT MIN(Timestamp) FROM Events) as EarliestEvent,
                    (SELECT MAX(Timestamp) FROM Events) as LatestEvent";

            await using var command = connection.CreateCommand();
            command.CommandText = statsSql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var totalEvents = reader.GetInt64(0);
                var totalStreams = reader.GetInt64(1);
                var totalSnapshots = reader.GetInt64(2);
                var earliestEvent = reader.IsDBNull(3) ? (DateTimeOffset?)null :
                    DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
                var latestEvent = reader.IsDBNull(4) ? (DateTimeOffset?)null :
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);

                return new EventStoreStatistics
                {
                    TotalEvents = totalEvents,
                    TotalStreams = totalStreams,
                    TotalSnapshots = totalSnapshots,
                    EarliestEventTimestamp = earliestEvent,
                    LatestEventTimestamp = latestEvent,
                    CollectedAt = DateTimeOffset.UtcNow
                };
            }

            return EventStoreStatistics.Empty();
        }
        catch
        {
            return EventStoreStatistics.Empty();
        }
    }
}

/// <summary>
/// Represents statistics about the event store.
/// </summary>
public record EventStoreStatistics
{
    /// <summary>
    /// Gets the total number of events in the store.
    /// </summary>
    public long TotalEvents { get; init; }

    /// <summary>
    /// Gets the total number of streams in the store.
    /// </summary>
    public long TotalStreams { get; init; }

    /// <summary>
    /// Gets the total number of snapshots in the store.
    /// </summary>
    public long TotalSnapshots { get; init; }

    /// <summary>
    /// Gets the timestamp of the earliest event.
    /// </summary>
    public DateTimeOffset? EarliestEventTimestamp { get; init; }

    /// <summary>
    /// Gets the timestamp of the latest event.
    /// </summary>
    public DateTimeOffset? LatestEventTimestamp { get; init; }

    /// <summary>
    /// Gets when these statistics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// Gets the age of the oldest event.
    /// </summary>
    public TimeSpan? DataAge => EarliestEventTimestamp.HasValue
        ? CollectedAt - EarliestEventTimestamp.Value
        : null;

    /// <summary>
    /// Gets the average events per stream.
    /// </summary>
    public double AverageEventsPerStream => TotalStreams > 0 ? (double)TotalEvents / TotalStreams : 0;

    /// <summary>
    /// Creates empty statistics.
    /// </summary>
    /// <returns>Empty event store statistics</returns>
    public static EventStoreStatistics Empty()
    {
        return new EventStoreStatistics
        {
            TotalEvents = 0,
            TotalStreams = 0,
            TotalSnapshots = 0,
            EarliestEventTimestamp = null,
            LatestEventTimestamp = null,
            CollectedAt = DateTimeOffset.UtcNow
        };
    }
}
