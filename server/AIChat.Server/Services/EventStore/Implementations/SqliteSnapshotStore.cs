using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIChat.Server.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// SQLite-based implementation of the snapshot store.
/// Provides high-performance, ACID-compliant snapshot storage with compression and deduplication.
/// Integrates seamlessly with the existing EventStore infrastructure.
/// </summary>
public sealed partial class SqliteSnapshotStore : ISnapshotStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IEventSerializer _serializer;
    private readonly ILogger<SqliteSnapshotStore> _logger;
    private readonly SnapshotMetricsCollector _metrics;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Content deduplication and compression settings
    /// </summary>
    private const string DefaultCompressionType = "gzip";
    private const CompressionLevel DefaultCompressionLevel = CompressionLevel.Optimal;

    /// <summary>
    /// Gets the name of this snapshot store implementation.
    /// </summary>
    public string Name => "SQLite Snapshot Store";

    /// <summary>
    /// Initializes a new instance of the SqliteSnapshotStore class.
    /// </summary>
    /// <param name="connectionFactory">Factory for creating SQLite connections</param>
    /// <param name="serializer">Event serializer for JSON conversion</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="metrics">Metrics collector for performance tracking</param>
    public SqliteSnapshotStore(
        ISqliteConnectionFactory connectionFactory,
        IEventSerializer serializer,
        ILogger<SqliteSnapshotStore> logger,
        SnapshotMetricsCollector metrics)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Health and Metrics

    /// <summary>
    /// Gets whether the snapshot store is currently healthy and available.
    /// </summary>
    public async Task<SnapshotHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            // Test basic connectivity and schema
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Snapshots LIMIT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken);

            // Test compression functionality
            var testData = Encoding.UTF8.GetBytes("Health check test data");
            var compressed = await CompressDataAsync(testData);
            var decompressed = await DecompressDataAsync(compressed);

            var isCompressionHealthy = testData.SequenceEqual(decompressed);

            return SnapshotHealthStatus.Healthy(
                Name,
                new Dictionary<string, object>
                {
                    ["CompressionHealthy"] = isCompressionHealthy,
                    ["SchemaVersion"] = await GetSchemaVersionAsync(connection, cancellationToken)
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for SqliteSnapshotStore");
            return SnapshotHealthStatus.Unhealthy(
                Name,
                ex.Message,
                false,
                false,
                true,
                new Dictionary<string, object> { ["Exception"] = ex.GetType().Name });
        }
    }

    /// <summary>
    /// Gets performance and usage metrics for the snapshot store.
    /// </summary>
    public async Task<SnapshotMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    COUNT(*) as TotalSnapshots,
                    COUNT(DISTINCT StreamId) as TotalStreams,
                    SUM(CompressedSize) as TotalCompressedSize,
                    SUM(UncompressedSize) as TotalUncompressedSize,
                    COUNT(CASE WHEN sc.ReferenceCount > 1 THEN 1 END) as DeduplicatedSnapshots
                FROM Snapshots s
                LEFT JOIN SnapshotContent sc ON s.ContentHash = sc.ContentHash";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var currentMetrics = _metrics.GetCurrentMetrics();
                return new SnapshotMetrics
                {
                    CreateOperations = currentMetrics.CreateOperations,
                    ReadOperations = currentMetrics.ReadOperations,
                    QueryOperations = currentMetrics.QueryOperations,
                    DeleteOperations = currentMetrics.DeleteOperations,
                    FailedOperations = currentMetrics.FailedOperations,
                    AverageCreateTimeMs = currentMetrics.AverageCreateTimeMs,
                    AverageReadTimeMs = currentMetrics.AverageReadTimeMs,
                    AverageQueryTimeMs = currentMetrics.AverageQueryTimeMs,
                    AverageDeleteTimeMs = currentMetrics.AverageDeleteTimeMs,
                    TotalSnapshots = reader.GetInt64(0), // TotalSnapshots
                    TotalStreams = reader.GetInt64(1), // TotalStreams
                    TotalCompressedSize = reader.GetInt64(2), // TotalCompressedSize
                    TotalUncompressedSize = reader.GetInt64(3), // TotalUncompressedSize
                    DeduplicatedSnapshots = reader.GetInt64(4) // DeduplicatedSnapshots
                };
            }

            return SnapshotMetrics.Empty();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect snapshot store metrics");
            return SnapshotMetrics.Empty();
        }
    }

    /// <summary>
    /// Optimizes the snapshot store for better performance.
    /// </summary>
    public async Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _metrics.StartOptimizeActivity();
        try
        {
            _logger.LogInformation("Starting snapshot store optimization");

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Vacuum the database
                var vacuumCommand = connection.CreateCommand();
                vacuumCommand.Transaction = (SqliteTransaction)transaction;
                vacuumCommand.CommandText = "VACUUM";
                _ = await vacuumCommand.ExecuteNonQueryAsync(cancellationToken);

                // Analyze tables for query optimization
                var analyzeCommand = connection.CreateCommand();
                analyzeCommand.Transaction = (SqliteTransaction)transaction;
                analyzeCommand.CommandText = "ANALYZE";
                _ = await analyzeCommand.ExecuteNonQueryAsync(cancellationToken);

                // Clean up orphaned content records
                await CleanupOrphanedContentAsync(connection, (SqliteTransaction)transaction, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Snapshot store optimization completed successfully");
                _metrics.RecordOptimizeSuccess();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot store optimization failed");
            _metrics.RecordOptimizeFailure();
            throw new SnapshotStoreException("Optimization failed", SnapshotErrorCode.InternalError, innerException: ex);
        }
    }

    #endregion Health and Metrics

    #region ISnapshotReader Implementation

    /// <summary>
    /// Retrieves the latest snapshot for a specific stream.
    /// </summary>
    public async Task<SnapshotResult<T>> GetLatestSnapshotAsync<T>(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting latest snapshot for stream {StreamId}", streamId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                       s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata,
                       sc.CompressedData
                FROM Snapshots s
                INNER JOIN SnapshotContent sc ON s.ContentHash = sc.ContentHash
                WHERE s.StreamId = @streamId
                ORDER BY s.Version DESC
                LIMIT 1";

            _ = command.Parameters.AddWithValue("@streamId", streamId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var metadata = ReadSnapshotMetadata(reader);
                var compressedData = (byte[])reader["CompressedData"];

                var data = await DeserializeSnapshotDataAsync<T>(compressedData, metadata.CompressionType, cancellationToken);

                _logger.LogDebug("Successfully retrieved latest snapshot {SnapshotId} for stream {StreamId} at version {Version}",
                    metadata.Id, streamId, metadata.Version);

                _metrics.RecordReadSuccess();
                return SnapshotResult.CreateSuccess(data, metadata);
            }

            _logger.LogDebug("No snapshots found for stream {StreamId}", streamId);
            _metrics.RecordReadNotFound();
            return SnapshotResult.CreateNotFound<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest snapshot for stream {StreamId}", streamId);
            _metrics.RecordReadFailure();
            throw new SnapshotStoreException(
                $"Failed to get latest snapshot for stream '{streamId}'",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Retrieves a snapshot at or before a specific version.
    /// </summary>
    public async Task<SnapshotResult<T>> GetSnapshotAtVersionAsync<T>(string streamId, long maxVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        if (maxVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVersion), "Version cannot be negative");
        }

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting snapshot for stream {StreamId} at or before version {MaxVersion}", streamId, maxVersion);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                       s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata,
                       sc.CompressedData
                FROM Snapshots s
                INNER JOIN SnapshotContent sc ON s.ContentHash = sc.ContentHash
                WHERE s.StreamId = @streamId AND s.Version <= @maxVersion
                ORDER BY s.Version DESC
                LIMIT 1";

            _ = command.Parameters.AddWithValue("@streamId", streamId);
            _ = command.Parameters.AddWithValue("@maxVersion", maxVersion);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var metadata = ReadSnapshotMetadata(reader);
                var compressedData = (byte[])reader["CompressedData"];

                var data = await DeserializeSnapshotDataAsync<T>(compressedData, metadata.CompressionType, cancellationToken);

                _logger.LogDebug("Successfully retrieved snapshot {SnapshotId} for stream {StreamId} at version {Version}",
                    metadata.Id, streamId, metadata.Version);

                _metrics.RecordReadSuccess();
                return SnapshotResult.CreateSuccess(data, metadata);
            }

            _logger.LogDebug("No snapshots found for stream {StreamId} at or before version {MaxVersion}", streamId, maxVersion);
            _metrics.RecordReadNotFound();
            return SnapshotResult.CreateNotFound<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get snapshot for stream {StreamId} at version {MaxVersion}", streamId, maxVersion);
            _metrics.RecordReadFailure();
            throw new SnapshotStoreException(
                $"Failed to get snapshot for stream '{streamId}' at version {maxVersion}",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Retrieves a specific snapshot by its unique identifier.
    /// </summary>
    public async Task<SnapshotResult<T>> GetSnapshotByIdAsync<T>(string snapshotId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotId);

        using var activity = _metrics.StartReadActivity();
        try
        {
            _logger.LogDebug("Getting snapshot by ID {SnapshotId}", snapshotId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT s.Id, s.StreamId, s.Version, s.ContentHash, s.Timestamp,
                       s.CompressedSize, s.UncompressedSize, s.CompressionType, s.StateType, s.Metadata,
                       sc.CompressedData
                FROM Snapshots s
                INNER JOIN SnapshotContent sc ON s.ContentHash = sc.ContentHash
                WHERE s.Id = @snapshotId";

            _ = command.Parameters.AddWithValue("@snapshotId", snapshotId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var metadata = ReadSnapshotMetadata(reader);
                var compressedData = (byte[])reader["CompressedData"];

                var data = await DeserializeSnapshotDataAsync<T>(compressedData, metadata.CompressionType, cancellationToken);

                _logger.LogDebug("Successfully retrieved snapshot {SnapshotId}", snapshotId);

                _metrics.RecordReadSuccess();
                return SnapshotResult.CreateSuccess(data, metadata);
            }

            _logger.LogDebug("Snapshot {SnapshotId} not found", snapshotId);
            _metrics.RecordReadNotFound();
            return SnapshotResult.CreateNotFound<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get snapshot by ID {SnapshotId}", snapshotId);
            _metrics.RecordReadFailure();
            throw new SnapshotStoreException(
                $"Failed to get snapshot '{snapshotId}'",
                SnapshotErrorCode.InternalError,
                snapshotId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Checks if any snapshots exist for a stream.
    /// </summary>
    public async Task<bool> HasSnapshotsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM Snapshots WHERE StreamId = @streamId LIMIT 1";
            _ = command.Parameters.AddWithValue("@streamId", streamId);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if snapshots exist for stream {StreamId}", streamId);
            throw new SnapshotStoreException(
                $"Failed to check snapshots for stream '{streamId}'",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Gets the version of the latest snapshot for a stream.
    /// </summary>
    public async Task<long> GetLatestSnapshotVersionAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(Version) FROM Snapshots WHERE StreamId = @streamId";
            _ = command.Parameters.AddWithValue("@streamId", streamId);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long version ? version : -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest snapshot version for stream {StreamId}", streamId);
            throw new SnapshotStoreException(
                $"Failed to get latest snapshot version for stream '{streamId}'",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    #endregion ISnapshotReader Implementation

    #region Helper Methods

    /// <summary>
    /// Reads snapshot metadata from a data reader.
    /// </summary>
    private static SnapshotMetadata ReadSnapshotMetadata(SqliteDataReader reader)
    {
        var metadataJson = reader.IsDBNull(9) ? null : reader.GetString(9); // Metadata column
        var additionalMetadata = string.IsNullOrEmpty(metadataJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);

        return new SnapshotMetadata
        {
            Id = reader.GetString(0), // Id
            StreamId = reader.GetString(1), // StreamId
            Version = reader.GetInt64(2), // Version
            ContentHash = reader.GetString(3), // ContentHash
            Timestamp = DateTimeOffset.Parse(reader.GetString(4), null, DateTimeStyles.RoundtripKind), // Timestamp
            CompressedSize = reader.GetInt64(5), // CompressedSize
            UncompressedSize = reader.GetInt64(6), // UncompressedSize
            CompressionType = reader.GetString(7), // CompressionType
            StateType = reader.GetString(8), // StateType
            AdditionalMetadata = additionalMetadata
        };
    }

    /// <summary>
    /// Compresses data using the default compression algorithm.
    /// </summary>
    private static async Task<byte[]> CompressDataAsync(byte[] data)
    {
        await using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, DefaultCompressionLevel))
        {
            await gzip.WriteAsync(data);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses data using the specified compression algorithm.
    /// </summary>
    private static async Task<byte[]> DecompressDataAsync(byte[] compressedData, string compressionType = DefaultCompressionType)
    {
        await using var input = new MemoryStream(compressedData);
        await using var output = new MemoryStream();

        Stream decompressionStream = compressionType.ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            _ => throw new NotSupportedException($"Compression type '{compressionType}' is not supported")
        };

        using (decompressionStream)
        {
            await decompressionStream.CopyToAsync(output);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Deserializes snapshot data from compressed bytes.
    /// </summary>
    private async Task<T> DeserializeSnapshotDataAsync<T>(byte[] compressedData, string compressionType, CancellationToken cancellationToken)
    {
        try
        {
            var decompressed = await DecompressDataAsync(compressedData, compressionType);
            var json = Encoding.UTF8.GetString(decompressed);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
        }
        catch (Exception ex)
        {
            throw new SnapshotStoreException(
                "Failed to deserialize snapshot data",
                SnapshotErrorCode.DeserializationError,
                innerException: ex);
        }
    }

    /// <summary>
    /// Calculates the SHA-256 hash of data for content addressing.
    /// </summary>
    private static string CalculateContentHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Gets the current database schema version.
    /// </summary>
    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is long version ? (int)version : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Cleans up orphaned content records that are no longer referenced by any snapshots.
    /// </summary>
    private async Task CleanupOrphanedContentAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            DELETE FROM SnapshotContent
            WHERE ContentHash NOT IN (SELECT DISTINCT ContentHash FROM Snapshots)";

        var deletedCount = await command.ExecuteNonQueryAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} orphaned content records", deletedCount);
        }
    }

    #endregion Helper Methods
}
