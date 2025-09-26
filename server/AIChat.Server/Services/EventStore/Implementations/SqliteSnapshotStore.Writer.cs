using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// Writer operations for SqliteSnapshotStore.
/// Handles snapshot creation, deletion, and metadata updates with compression and deduplication.
/// </summary>
public sealed partial class SqliteSnapshotStore : ISnapshotWriter
{
    #region ISnapshotWriter Implementation

    /// <summary>
    /// Creates a new snapshot for a stream at a specific version.
    /// </summary>
    public async Task<SnapshotWriteResult> CreateSnapshotAsync<T>(
        string streamId,
        long version,
        T state,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(state);
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version cannot be negative");
        }

        using var activity = _metrics.StartCreateActivity();
        try
        {
            _logger.LogDebug("Creating snapshot for stream {StreamId} at version {Version}", streamId, version);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Check if snapshot already exists for this stream/version
                if (await SnapshotExistsAsync(connection, (SqliteTransaction)transaction, streamId, version, cancellationToken))
                {
                    throw SnapshotStoreException.DuplicateSnapshot($"{streamId}-{version}", streamId, version);
                }

                // Serialize and compress the state
                var serializedData = JsonSerializer.Serialize(state, _jsonOptions);
                var uncompressedData = Encoding.UTF8.GetBytes(serializedData);
                var compressedData = await CompressDataAsync(uncompressedData);

                // Calculate content hash for deduplication
                var contentHash = CalculateContentHash(compressedData);
                var snapshotId = GenerateSnapshotId(streamId, version, contentHash);

                // Check if this content already exists (deduplication)
                var wasDeduplicated = await ContentExistsAsync(connection, (SqliteTransaction)transaction, contentHash, cancellationToken);

                // Insert or update content record
                if (!wasDeduplicated)
                {
                    await InsertContentAsync(connection, (SqliteTransaction)transaction, contentHash, compressedData, cancellationToken);
                }
                else
                {
                    await IncrementContentReferenceAsync(connection, (SqliteTransaction)transaction, contentHash, cancellationToken);
                }

                // Insert snapshot metadata
                await InsertSnapshotAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    snapshotId,
                    streamId,
                    version,
                    contentHash,
                    compressedData.Length,
                    uncompressedData.Length,
                    DefaultCompressionType,
                    typeof(T).FullName ?? typeof(T).Name,
                    metadata,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully created snapshot {SnapshotId} for stream {StreamId} at version {Version}. " +
                    "Compressed size: {CompressedSize} bytes, Uncompressed size: {UncompressedSize} bytes, " +
                    "Deduplicated: {Deduplicated}",
                    snapshotId, streamId, version, compressedData.Length, uncompressedData.Length, wasDeduplicated);

                _metrics.RecordCreateSuccess();
                return SnapshotWriteResult.CreateSuccess(
                    snapshotId,
                    contentHash,
                    compressedData.Length,
                    uncompressedData.Length,
                    wasDeduplicated,
                    new Dictionary<string, object>
                    {
                        ["streamId"] = streamId,
                        ["version"] = version,
                        ["stateType"] = typeof(T).FullName ?? typeof(T).Name
                    });
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex) when (ex is not SnapshotStoreException)
        {
            _logger.LogError(ex, "Failed to create snapshot for stream {StreamId} at version {Version}", streamId, version);
            _metrics.RecordCreateFailure();
            throw new SnapshotStoreException(
                $"Failed to create snapshot for stream '{streamId}' at version {version}",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                context: new Dictionary<string, object> { ["version"] = version },
                innerException: ex);
        }
    }

    /// <summary>
    /// Deletes a specific snapshot by its unique identifier.
    /// </summary>
    public async Task<SnapshotWriteResult> DeleteSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotId);

        using var activity = _metrics.StartDeleteActivity();
        try
        {
            _logger.LogDebug("Deleting snapshot {SnapshotId}", snapshotId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Get snapshot metadata before deletion
                var metadata = await GetSnapshotMetadataAsync(connection, (SqliteTransaction)transaction, snapshotId, cancellationToken) ?? throw SnapshotStoreException.SnapshotNotFound(snapshotId);

                // Delete the snapshot record
                var deleteSnapshotCommand = connection.CreateCommand();
                deleteSnapshotCommand.Transaction = (SqliteTransaction)transaction;
                deleteSnapshotCommand.CommandText = "DELETE FROM Snapshots WHERE Id = @snapshotId";
                deleteSnapshotCommand.Parameters.AddWithValue("@snapshotId", snapshotId);

                var deletedCount = await deleteSnapshotCommand.ExecuteNonQueryAsync(cancellationToken);
                if (deletedCount == 0)
                {
                    throw SnapshotStoreException.SnapshotNotFound(snapshotId);
                }

                // Decrement content reference count
                await DecrementContentReferenceAsync(connection, (SqliteTransaction)transaction, metadata.ContentHash, cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully deleted snapshot {SnapshotId}", snapshotId);

                _metrics.RecordDeleteSuccess();
                return SnapshotWriteResult.CreateBulkSuccess(
                    1,
                    new Dictionary<string, object>
                    {
                        ["snapshotId"] = snapshotId,
                        ["contentHash"] = metadata.ContentHash,
                        ["size"] = metadata.CompressedSize
                    });
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex) when (ex is not SnapshotStoreException)
        {
            _logger.LogError(ex, "Failed to delete snapshot {SnapshotId}", snapshotId);
            _metrics.RecordDeleteFailure();
            throw new SnapshotStoreException(
                $"Failed to delete snapshot '{snapshotId}'",
                SnapshotErrorCode.InternalError,
                snapshotId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Deletes all snapshots for a stream.
    /// </summary>
    public async Task<SnapshotWriteResult> DeleteStreamSnapshotsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        using var activity = _metrics.StartDeleteActivity();
        try
        {
            _logger.LogDebug("Deleting all snapshots for stream {StreamId}", streamId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Get all content hashes for the stream before deletion
                var getContentHashesCommand = connection.CreateCommand();
                getContentHashesCommand.Transaction = (SqliteTransaction)transaction;
                getContentHashesCommand.CommandText = "SELECT ContentHash FROM Snapshots WHERE StreamId = @streamId";
                getContentHashesCommand.Parameters.AddWithValue("@streamId", streamId);

                var contentHashes = new List<string>();
                await using (var reader = await getContentHashesCommand.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        contentHashes.Add(reader.GetString(0)); // ContentHash
                    }
                }

                // Delete all snapshots for the stream
                var deleteSnapshotsCommand = connection.CreateCommand();
                deleteSnapshotsCommand.Transaction = (SqliteTransaction)transaction;
                deleteSnapshotsCommand.CommandText = "DELETE FROM Snapshots WHERE StreamId = @streamId";
                deleteSnapshotsCommand.Parameters.AddWithValue("@streamId", streamId);

                var deletedCount = await deleteSnapshotsCommand.ExecuteNonQueryAsync(cancellationToken);

                // Decrement content reference counts
                foreach (var contentHash in contentHashes)
                {
                    await DecrementContentReferenceAsync(connection, (SqliteTransaction)transaction, contentHash, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully deleted {Count} snapshots for stream {StreamId}", deletedCount, streamId);

                _metrics.RecordDeleteSuccess();
                return SnapshotWriteResult.CreateBulkSuccess(
                    deletedCount,
                    new Dictionary<string, object>
                    {
                        ["streamId"] = streamId,
                        ["contentHashes"] = contentHashes
                    });
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete snapshots for stream {StreamId}", streamId);
            _metrics.RecordDeleteFailure();
            throw new SnapshotStoreException(
                $"Failed to delete snapshots for stream '{streamId}'",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Updates the metadata for an existing snapshot.
    /// </summary>
    public async Task<SnapshotWriteResult> UpdateSnapshotMetadataAsync(
        string snapshotId,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotId);
        ArgumentNullException.ThrowIfNull(metadata);

        using var activity = _metrics.StartCreateActivity(); // Using create activity for metadata updates
        try
        {
            _logger.LogDebug("Updating metadata for snapshot {SnapshotId}", snapshotId);

            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Check if snapshot exists
                var existsCommand = connection.CreateCommand();
                existsCommand.Transaction = (SqliteTransaction)transaction;
                existsCommand.CommandText = "SELECT 1 FROM Snapshots WHERE Id = @snapshotId";
                existsCommand.Parameters.AddWithValue("@snapshotId", snapshotId);

                var exists = await existsCommand.ExecuteScalarAsync(cancellationToken) ?? throw SnapshotStoreException.SnapshotNotFound(snapshotId);

                // Update metadata
                var metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);

                var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = (SqliteTransaction)transaction;
                updateCommand.CommandText = "UPDATE Snapshots SET Metadata = @metadata WHERE Id = @snapshotId";
                updateCommand.Parameters.AddWithValue("@snapshotId", snapshotId);
                updateCommand.Parameters.AddWithValue("@metadata", metadataJson);

                await updateCommand.ExecuteNonQueryAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Successfully updated metadata for snapshot {SnapshotId}", snapshotId);

                _metrics.RecordCreateSuccess(); // Using create success for metadata updates
                return SnapshotWriteResult.CreateBulkSuccess(
                    1,
                    new Dictionary<string, object>
                    {
                        ["snapshotId"] = snapshotId,
                        ["updatedMetadata"] = metadata
                    });
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex) when (ex is not SnapshotStoreException)
        {
            _logger.LogError(ex, "Failed to update metadata for snapshot {SnapshotId}", snapshotId);
            _metrics.RecordCreateFailure();
            throw new SnapshotStoreException(
                $"Failed to update metadata for snapshot '{snapshotId}'",
                SnapshotErrorCode.InternalError,
                snapshotId,
                innerException: ex);
        }
    }

    #endregion

    #region Writer Helper Methods

    /// <summary>
    /// Checks if a snapshot already exists for the given stream and version.
    /// </summary>
    private static async Task<bool> SnapshotExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string streamId,
        long version,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM Snapshots WHERE StreamId = @streamId AND Version = @version LIMIT 1";
        command.Parameters.AddWithValue("@streamId", streamId);
        command.Parameters.AddWithValue("@version", version);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    /// <summary>
    /// Checks if content with the given hash already exists.
    /// </summary>
    private static async Task<bool> ContentExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM SnapshotContent WHERE ContentHash = @contentHash LIMIT 1";
        command.Parameters.AddWithValue("@contentHash", contentHash);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    /// <summary>
    /// Inserts compressed content data into the content table.
    /// </summary>
    private static async Task InsertContentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        byte[] compressedData,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO SnapshotContent (ContentHash, CompressedData, ReferenceCount)
            VALUES (@contentHash, @compressedData, 1)";

        command.Parameters.AddWithValue("@contentHash", contentHash);
        command.Parameters.AddWithValue("@compressedData", compressedData);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Increments the reference count for existing content.
    /// </summary>
    private static async Task IncrementContentReferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            UPDATE SnapshotContent
            SET ReferenceCount = ReferenceCount + 1
            WHERE ContentHash = @contentHash";

        command.Parameters.AddWithValue("@contentHash", contentHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Decrements the reference count for content and deletes if no longer referenced.
    /// </summary>
    private static async Task DecrementContentReferenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string contentHash,
        CancellationToken cancellationToken)
    {
        // First decrement the reference count
        var decrementCommand = connection.CreateCommand();
        decrementCommand.Transaction = transaction;
        decrementCommand.CommandText = @"
            UPDATE SnapshotContent
            SET ReferenceCount = ReferenceCount - 1
            WHERE ContentHash = @contentHash";

        decrementCommand.Parameters.AddWithValue("@contentHash", contentHash);
        await decrementCommand.ExecuteNonQueryAsync(cancellationToken);

        // Delete if no longer referenced
        var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = @"
            DELETE FROM SnapshotContent
            WHERE ContentHash = @contentHash AND ReferenceCount <= 0";

        deleteCommand.Parameters.AddWithValue("@contentHash", contentHash);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts snapshot metadata into the snapshots table.
    /// </summary>
    private async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string snapshotId,
        string streamId,
        long version,
        string contentHash,
        long compressedSize,
        long uncompressedSize,
        string compressionType,
        string stateType,
        Dictionary<string, object>? metadata,
        CancellationToken cancellationToken)
    {
        var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata, _jsonOptions) : null;

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO Snapshots (
                Id, StreamId, Version, ContentHash, Timestamp,
                CompressedSize, UncompressedSize, CompressionType, StateType, Metadata
            ) VALUES (
                @id, @streamId, @version, @contentHash, @timestamp,
                @compressedSize, @uncompressedSize, @compressionType, @stateType, @metadata
            )";

        command.Parameters.AddWithValue("@id", snapshotId);
        command.Parameters.AddWithValue("@streamId", streamId);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@contentHash", contentHash);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@compressedSize", compressedSize);
        command.Parameters.AddWithValue("@uncompressedSize", uncompressedSize);
        command.Parameters.AddWithValue("@compressionType", compressionType);
        command.Parameters.AddWithValue("@stateType", stateType);
        command.Parameters.AddWithValue("@metadata", (object?)metadataJson ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets snapshot metadata by ID.
    /// </summary>
    private static async Task<SnapshotMetadata?> GetSnapshotMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT Id, StreamId, Version, ContentHash, Timestamp,
                   CompressedSize, UncompressedSize, CompressionType, StateType, Metadata
            FROM Snapshots
            WHERE Id = @snapshotId";

        command.Parameters.AddWithValue("@snapshotId", snapshotId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadSnapshotMetadata(reader);
        }

        return null;
    }

    /// <summary>
    /// Generates a unique snapshot ID based on stream, version, and content.
    /// </summary>
    private static string GenerateSnapshotId(string streamId, long version, string contentHash)
    {
        // Create a deterministic ID that includes stream, version, and content info
        var input = $"{streamId}-{version}-{contentHash[..8]}";
        return $"snapshot-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16]}";
    }

    #endregion
}