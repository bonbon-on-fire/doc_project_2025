namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Core interface for snapshot operations following Interface Segregation Principle.
/// Provides snapshot storage capabilities for Orleans grain state optimization.
/// Extends event sourcing with point-in-time state snapshots for performance.
/// </summary>
public interface ISnapshotStore : ISnapshotReader, ISnapshotWriter, ISnapshotQuery
{
    /// <summary>
    /// Gets the name of the snapshot store implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the snapshot store is currently healthy and available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance and usage metrics for the snapshot store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current snapshot store metrics</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the snapshot store for better performance.
    /// This may include index optimization, compression optimization, cleanup operations, etc.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the optimization operation</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for reading snapshots from the store (read operations).
/// Separated for read-only access scenarios and interface segregation.
/// </summary>
public interface ISnapshotReader
{
    /// <summary>
    /// Retrieves the latest snapshot for a specific stream.
    /// Returns the most recent snapshot that can be used as a starting point for event replay.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The latest snapshot for the stream, or null if no snapshots exist</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotResult<T>> GetLatestSnapshotAsync<T>(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a snapshot at or before a specific version.
    /// Useful for point-in-time state reconstruction at a specific event version.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="maxVersion">The maximum version to consider (inclusive)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The snapshot at or before the specified version, or null if none exists</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when maxVersion is negative</exception>
    /// <exception cref="SnapshotStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotResult<T>> GetSnapshotAtVersionAsync<T>(string streamId, long maxVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific snapshot by its unique identifier.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="snapshotId">The unique snapshot identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The snapshot with the specified ID, or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when snapshotId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotResult<T>> GetSnapshotByIdAsync<T>(string snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any snapshots exist for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if snapshots exist for the stream, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when check fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> HasSnapshotsAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the version of the latest snapshot for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The version of the latest snapshot, or -1 if no snapshots exist</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when read fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<long> GetLatestSnapshotVersionAsync(string streamId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for writing snapshots to the store (write operations).
/// Separated for write-only access scenarios and interface segregation.
/// </summary>
public interface ISnapshotWriter
{
    /// <summary>
    /// Creates a new snapshot for a stream at a specific version.
    /// The snapshot represents the complete state of the stream at the given version.
    /// </summary>
    /// <typeparam name="T">The type of state to snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="version">The exact version of the stream this snapshot represents</param>
    /// <param name="state">The complete state to store in the snapshot</param>
    /// <param name="metadata">Optional metadata for the snapshot</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the snapshot creation operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or state is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when version is negative</exception>
    /// <exception cref="SnapshotStoreException">Thrown when creation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotWriteResult> CreateSnapshotAsync<T>(
        string streamId,
        long version,
        T state,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific snapshot by its unique identifier.
    /// Used for cleanup operations and manual snapshot management.
    /// </summary>
    /// <param name="snapshotId">The unique snapshot identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the deletion operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when snapshotId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when deletion fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotWriteResult> DeleteSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all snapshots for a stream.
    /// Useful when a stream is deleted or for complete cleanup operations.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the deletion operation including count of deleted snapshots</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when deletion fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotWriteResult> DeleteStreamSnapshotsAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the metadata for an existing snapshot.
    /// Useful for adding tags, updating retention policies, or other metadata changes.
    /// </summary>
    /// <param name="snapshotId">The unique snapshot identifier</param>
    /// <param name="metadata">The new metadata to associate with the snapshot</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the update operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when snapshotId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when update fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotWriteResult> UpdateSnapshotMetadataAsync(
        string snapshotId,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for querying snapshots with flexible criteria.
/// Separated for advanced query scenarios and reporting operations.
/// </summary>
public interface ISnapshotQuery
{
    /// <summary>
    /// Queries snapshots using flexible criteria.
    /// Supports filtering by stream, time range, version range, metadata, etc.
    /// </summary>
    /// <param name="query">The query criteria</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Snapshots matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotQueryResult> QuerySnapshotsAsync(SnapshotQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the complete snapshot history for a stream.
    /// Returns all snapshots for the stream ordered by version.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All snapshots for the stream</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotQueryResult> GetSnapshotHistoryAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets snapshots within a time range across all streams.
    /// Useful for bulk operations and reporting.
    /// </summary>
    /// <param name="fromTimestamp">The start time (inclusive)</param>
    /// <param name="toTimestamp">The end time (inclusive)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>All snapshots within the specified time range</returns>
    /// <exception cref="ArgumentException">Thrown when time range is invalid</exception>
    /// <exception cref="SnapshotStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotQueryResult> GetSnapshotsByTimeRangeAsync(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets snapshots that are eligible for cleanup based on retention policies.
    /// Used by cleanup processes to identify snapshots that should be deleted.
    /// </summary>
    /// <param name="retentionPolicy">The retention policy to apply</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Snapshots that can be safely deleted</returns>
    /// <exception cref="ArgumentNullException">Thrown when retentionPolicy is null</exception>
    /// <exception cref="SnapshotStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotQueryResult> GetSnapshotsForCleanupAsync(
        SnapshotRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets snapshots grouped by content hash for deduplication analysis.
    /// Useful for identifying duplicate snapshots and storage optimization.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Snapshots grouped by their content hash</returns>
    /// <exception cref="SnapshotStoreException">Thrown when query fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<Dictionary<string, List<SnapshotMetadata>>> GetSnapshotsByContentHashAsync(CancellationToken cancellationToken = default);
}
