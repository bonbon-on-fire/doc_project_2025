using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Schema helper for Snapshot Store database tables.
/// Manages the creation and evolution of snapshot storage schema.
/// </summary>
public static class SnapshotSchemaHelper
{
    private const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Ensures the snapshot store schema exists in the database.
    /// This should be called after the main schema is created.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task EnsureSnapshotSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        // Check if we need to reset or upgrade the snapshot schema
        var currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
        var needsReset = await NeedsSnapshotSchemaResetAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (needsReset)
            {
                await DropSnapshotSchemaAsync(connection, (SqliteTransaction)transaction, cancellationToken);
                currentVersion = 0;
            }

            if (currentVersion < CurrentSchemaVersion)
            {
                await CreateOrUpgradeSnapshotSchemaAsync(connection, (SqliteTransaction)transaction, currentVersion, cancellationToken);
                await SetSchemaVersionAsync(connection, (SqliteTransaction)transaction, CurrentSchemaVersion, cancellationToken);
            }

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
    /// Checks if the snapshot schema needs to be reset.
    /// </summary>
    private static async Task<bool> NeedsSnapshotSchemaResetAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the Snapshots table exists with the correct schema
            const string checkTableSql = @"
                SELECT COUNT(*) FROM sqlite_master
                WHERE type='table' AND name='Snapshots'";

            var command = connection.CreateCommand();
            command.CommandText = checkTableSql;
            var tableExists = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;

            if (!tableExists)
            {
                return false; // No reset needed if table doesn't exist
            }

            // Check if the table has the expected columns
            command.CommandText = @"
                SELECT COUNT(*) FROM pragma_table_info('Snapshots')
                WHERE name IN ('Id', 'StreamId', 'Version', 'ContentHash', 'Timestamp', 'CompressedSize', 'UncompressedSize', 'CompressionType', 'StateType', 'Metadata')";
            var columnCount = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

            // We expect 10 columns
            return columnCount != 10;
        }
        catch
        {
            // If we can't check the schema, assume we need a reset
            return true;
        }
    }

    /// <summary>
    /// Gets the current schema version.
    /// </summary>
    private static async Task<int> GetCurrentSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            const string getVersionSql = @"
                SELECT Value FROM SnapshotSchemaInfo
                WHERE Key = 'SchemaVersion'
                LIMIT 1";

            var command = connection.CreateCommand();
            command.CommandText = getVersionSql;
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result is string versionStr && int.TryParse(versionStr, out var version))
            {
                return version;
            }

            return 0; // No version found, assume version 0
        }
        catch
        {
            return 0; // Error reading version, assume version 0
        }
    }

    /// <summary>
    /// Sets the schema version.
    /// </summary>
    private static async Task SetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        const string setVersionSql = @"
            INSERT OR REPLACE INTO SnapshotSchemaInfo (Key, Value)
            VALUES ('SchemaVersion', @version)";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = setVersionSql;
        _ = command.Parameters.AddWithValue("@version", version.ToString(CultureInfo.InvariantCulture));

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Drops the snapshot store schema.
    /// </summary>
    private static async Task DropSnapshotSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var dropStatements = new[]
        {
            "DROP INDEX IF EXISTS idx_snapshots_stream_version",
            "DROP INDEX IF EXISTS idx_snapshots_timestamp",
            "DROP INDEX IF EXISTS idx_snapshots_hash",
            "DROP INDEX IF EXISTS idx_snapshots_content_hash",
            "DROP INDEX IF EXISTS idx_content_reference_count",
            "DROP TABLE IF EXISTS Snapshots",
            "DROP TABLE IF EXISTS SnapshotContent",
            "DROP TABLE IF EXISTS SnapshotSchemaInfo"
        };

        foreach (var sql in dropStatements)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates or upgrades the snapshot store schema.
    /// </summary>
    private static async Task CreateOrUpgradeSnapshotSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        if (currentVersion == 0)
        {
            await CreateSnapshotSchemaV1Async(connection, transaction, cancellationToken);
        }

        // Future version upgrades would go here
        // if (currentVersion < 2)
        // {
        //     await UpgradeToV2Async(connection, transaction, cancellationToken);
        // }
    }

    /// <summary>
    /// Creates the initial snapshot schema (version 1).
    /// </summary>
    private static async Task CreateSnapshotSchemaV1Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Create schema info table
        const string createSchemaInfoSql = @"
            CREATE TABLE IF NOT EXISTS SnapshotSchemaInfo (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            )";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = createSchemaInfoSql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken);

        // Create snapshot content table (for content-addressable storage)
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS SnapshotContent (
                ContentHash TEXT PRIMARY KEY,
                CompressedData BLOB NOT NULL,
                ReferenceCount INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            )";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);

        // Create snapshots metadata table
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Snapshots (
                Id TEXT PRIMARY KEY,
                StreamId TEXT NOT NULL,
                Version INTEGER NOT NULL,
                ContentHash TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                CompressedSize INTEGER NOT NULL,
                UncompressedSize INTEGER NOT NULL,
                CompressionType TEXT NOT NULL,
                StateType TEXT NOT NULL,
                Metadata TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (ContentHash) REFERENCES SnapshotContent(ContentHash),
                UNIQUE(StreamId, Version)
            )";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);

        // Create indexes for optimal query performance
        var indexCommands = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_snapshots_stream_version ON Snapshots(StreamId, Version DESC)",
            "CREATE INDEX IF NOT EXISTS idx_snapshots_timestamp ON Snapshots(Timestamp DESC)",
            "CREATE INDEX IF NOT EXISTS idx_snapshots_content_hash ON Snapshots(ContentHash)",
            "CREATE INDEX IF NOT EXISTS idx_snapshots_state_type ON Snapshots(StateType)",
            "CREATE INDEX IF NOT EXISTS idx_snapshots_compressed_size ON Snapshots(CompressedSize)",
            "CREATE INDEX IF NOT EXISTS idx_content_reference_count ON SnapshotContent(ReferenceCount)"
        };

        foreach (var indexSql in indexCommands)
        {
            command.CommandText = indexSql;
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Insert initial schema metadata
        var metadataInserts = new[]
        {
            ("SchemaVersion", "1"),
            ("CreatedBy", "SnapshotSchemaHelper"),
            ("CreatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ("Description", "Initial snapshot store schema with content deduplication")
        };

        foreach (var (key, value) in metadataInserts)
        {
            command.CommandText = @"
                INSERT OR REPLACE INTO SnapshotSchemaInfo (Key, Value)
                VALUES (@key, @value)";
            command.Parameters.Clear();
            _ = command.Parameters.AddWithValue("@key", key);
            _ = command.Parameters.AddWithValue("@value", value);
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Validates that the snapshot schema is correctly installed.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the schema is valid, false otherwise</returns>
    public static async Task<bool> ValidateSnapshotSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check that all required tables exist
            foreach (var tableName in new[] { "Snapshots", "SnapshotContent", "SnapshotSchemaInfo" })
            {
                const string checkTableSql = @"
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type='table' AND name=@tableName";

                var command = connection.CreateCommand();
                command.CommandText = checkTableSql;
                _ = command.Parameters.AddWithValue("@tableName", tableName);

                var exists = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0;
                if (!exists)
                {
                    return false;
                }
            }

            // Check that the schema version is current
            var version = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
            if (version != CurrentSchemaVersion)
            {
                return false;
            }

            // Test basic operations
            const string testQuery = "SELECT COUNT(*) FROM Snapshots LIMIT 1";
            var testCommand = connection.CreateCommand();
            testCommand.CommandText = testQuery;
            _ = await testCommand.ExecuteScalarAsync(cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets schema statistics for monitoring and diagnostics.
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Schema statistics</returns>
    public static async Task<SnapshotSchemaStatistics> GetSchemaStatisticsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string statsSql = @"
                SELECT
                    (SELECT COUNT(*) FROM Snapshots) as SnapshotCount,
                    (SELECT COUNT(*) FROM SnapshotContent) as ContentCount,
                    (SELECT COUNT(DISTINCT StreamId) FROM Snapshots) as StreamCount,
                    (SELECT COALESCE(SUM(CompressedSize), 0) FROM Snapshots) as TotalCompressedSize,
                    (SELECT COALESCE(SUM(UncompressedSize), 0) FROM Snapshots) as TotalUncompressedSize,
                    (SELECT COUNT(*) FROM SnapshotContent WHERE ReferenceCount > 1) as DeduplicatedContent";

            var command = connection.CreateCommand();
            command.CommandText = statsSql;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new SnapshotSchemaStatistics
                {
                    SnapshotCount = reader.GetInt64(0), // SnapshotCount
                    ContentCount = reader.GetInt64(1), // ContentCount
                    StreamCount = reader.GetInt64(2), // StreamCount
                    TotalCompressedSize = reader.GetInt64(3), // TotalCompressedSize
                    TotalUncompressedSize = reader.GetInt64(4), // TotalUncompressedSize
                    DeduplicatedContentCount = reader.GetInt64(5), // DeduplicatedContent
                    SchemaVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken),
                    CollectedAt = DateTime.UtcNow
                };
            }

            return new SnapshotSchemaStatistics
            {
                SchemaVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken),
                CollectedAt = DateTime.UtcNow
            };
        }
        catch
        {
            return new SnapshotSchemaStatistics
            {
                CollectedAt = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// Represents statistics about the snapshot schema.
/// </summary>
public record SnapshotSchemaStatistics
{
    /// <summary>
    /// Gets the total number of snapshots in the database.
    /// </summary>
    public long SnapshotCount { get; init; }

    /// <summary>
    /// Gets the total number of unique content records.
    /// </summary>
    public long ContentCount { get; init; }

    /// <summary>
    /// Gets the total number of streams with snapshots.
    /// </summary>
    public long StreamCount { get; init; }

    /// <summary>
    /// Gets the total compressed size of all snapshots.
    /// </summary>
    public long TotalCompressedSize { get; init; }

    /// <summary>
    /// Gets the total uncompressed size of all snapshots.
    /// </summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Gets the number of content records that are deduplicated (referenced by multiple snapshots).
    /// </summary>
    public long DeduplicatedContentCount { get; init; }

    /// <summary>
    /// Gets the current schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets when these statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; }

    /// <summary>
    /// Gets the overall compression ratio.
    /// </summary>
    public double CompressionRatio => TotalUncompressedSize > 0
        ? (double)TotalCompressedSize / TotalUncompressedSize
        : 1.0;

    /// <summary>
    /// Gets the space saved through compression.
    /// </summary>
    public long SpaceSavedByCompression => TotalUncompressedSize - TotalCompressedSize;

    /// <summary>
    /// Gets the deduplication effectiveness (percentage of content that is deduplicated).
    /// </summary>
    public double DeduplicationEffectiveness => ContentCount > 0
        ? (double)DeduplicatedContentCount / ContentCount * 100
        : 0.0;
}
