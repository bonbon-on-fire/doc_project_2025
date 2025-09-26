namespace AIChat.Server.Services.EventStore;

/// <summary>
/// High-level interface for snapshot management operations.
/// Orchestrates snapshot creation, restoration, and cleanup operations.
/// Provides the main entry point for Orleans grain snapshot integration.
/// </summary>
public interface ISnapshotManager
{
    /// <summary>
    /// Gets the name of the snapshot manager implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines if a snapshot should be created for a stream based on the specified policy.
    /// Uses configurable triggers like event count, time elapsed, or stream size.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="currentVersion">The current version of the stream</param>
    /// <param name="policy">The snapshot creation policy to evaluate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if a snapshot should be created, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or policy is null</exception>
    /// <exception cref="SnapshotManagerException">Thrown when evaluation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> ShouldCreateSnapshotAsync(
        string streamId,
        long currentVersion,
        SnapshotCreationPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a snapshot of the current state for a stream.
    /// Handles compression, deduplication, and storage optimization automatically.
    /// </summary>
    /// <typeparam name="T">The type of state to snapshot</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="version">The exact version this snapshot represents</param>
    /// <param name="state">The complete state to store</param>
    /// <param name="metadata">Optional metadata for the snapshot</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the snapshot creation operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or state is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when version is negative</exception>
    /// <exception cref="SnapshotManagerException">Thrown when creation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotWriteResult> CreateSnapshotAsync<T>(
        string streamId,
        long version,
        T state,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores state from the latest snapshot and replays events from that point.
    /// Provides optimal performance by avoiding full event stream replay.
    /// </summary>
    /// <typeparam name="T">The type of state to restore</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="targetVersion">Optional target version to restore to (defaults to latest)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The restored state and metadata about the restoration</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId or projection is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when targetVersion is negative</exception>
    /// <exception cref="SnapshotManagerException">Thrown when restoration fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotRestoreResult<T>> RestoreFromSnapshotAsync<T>(
        string streamId,
        IEventProjection<T> projection,
        long? targetVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the integrity of a snapshot by verifying its content hash.
    /// Used for detecting data corruption and ensuring snapshot reliability.
    /// </summary>
    /// <param name="snapshotId">The unique snapshot identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the integrity validation</returns>
    /// <exception cref="ArgumentNullException">Thrown when snapshotId is null</exception>
    /// <exception cref="SnapshotManagerException">Thrown when validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotValidationResult> ValidateSnapshotIntegrityAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup operations based on the specified retention policy.
    /// Removes expired snapshots while preserving those needed for recovery.
    /// </summary>
    /// <param name="retentionPolicy">The retention policy to apply</param>
    /// <param name="dryRun">If true, returns what would be deleted without actually deleting</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the cleanup operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when retentionPolicy is null</exception>
    /// <exception cref="SnapshotManagerException">Thrown when cleanup fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotCleanupResult> CleanupSnapshotsAsync(
        SnapshotRetentionPolicy retentionPolicy,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes snapshot storage by performing compression analysis and deduplication.
    /// Can reclaim significant storage space by optimizing existing snapshots.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the optimization operation</returns>
    /// <exception cref="SnapshotManagerException">Thrown when optimization fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotOptimizationResult> OptimizeStorageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comprehensive statistics about snapshots for a specific stream.
    /// Useful for monitoring and performance analysis.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Detailed statistics about the stream's snapshots</returns>
    /// <exception cref="ArgumentNullException">Thrown when streamId is null</exception>
    /// <exception cref="SnapshotManagerException">Thrown when statistics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotStatistics> GetStreamStatisticsAsync(
        string streamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets global statistics about all snapshots managed by this instance.
    /// Useful for system-wide monitoring and capacity planning.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Global snapshot statistics</returns>
    /// <exception cref="SnapshotManagerException">Thrown when statistics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<SnapshotGlobalStatistics> GetGlobalStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory methods for creating snapshot restoration results.
/// </summary>
public static class SnapshotRestoreResult
{
    /// <summary>
    /// Creates a successful restoration result.
    /// </summary>
    /// <typeparam name="T">The type of state that was restored</typeparam>
    /// <param name="state">The restored state</param>
    /// <param name="finalVersion">The final version</param>
    /// <param name="snapshotVersion">The snapshot version used</param>
    /// <param name="eventsReplayed">Number of events replayed</param>
    /// <param name="restorationTime">Time taken for restoration</param>
    /// <param name="timeSaved">Time saved by using snapshot</param>
    /// <param name="snapshotMetadata">Metadata of the snapshot used</param>
    /// <returns>A successful restoration result</returns>
    public static SnapshotRestoreResult<T> CreateSuccess<T>(
        T state,
        long finalVersion,
        long snapshotVersion,
        int eventsReplayed,
        TimeSpan restorationTime,
        TimeSpan timeSaved,
        SnapshotMetadata? snapshotMetadata = null)
    {
        return new SnapshotRestoreResult<T>
        {
            Success = true,
            State = state,
            FinalVersion = finalVersion,
            SnapshotVersion = snapshotVersion,
            EventsReplayed = eventsReplayed,
            RestorationTime = restorationTime,
            TimeSaved = timeSaved,
            SnapshotMetadata = snapshotMetadata
        };
    }

    /// <summary>
    /// Creates a failed restoration result.
    /// </summary>
    /// <typeparam name="T">The type of state that was restored</typeparam>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="partialState">Partial state if available</param>
    /// <param name="restorationTime">Time taken before failure</param>
    /// <returns>A failed restoration result</returns>
    public static SnapshotRestoreResult<T> CreateFailure<T>(
        string error,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError,
        T? partialState = default,
        TimeSpan restorationTime = default)
    {
        return new SnapshotRestoreResult<T>
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            State = partialState,
            RestorationTime = restorationTime
        };
    }
}

/// <summary>
/// Represents the result of a snapshot restoration operation.
/// </summary>
/// <typeparam name="T">The type of state that was restored</typeparam>
public record SnapshotRestoreResult<T>
{
    /// <summary>
    /// Gets whether the restoration was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the restored state.
    /// </summary>
    public T? State { get; init; }

    /// <summary>
    /// Gets the final version after restoration and event replay.
    /// </summary>
    public long FinalVersion { get; init; }

    /// <summary>
    /// Gets the version of the snapshot that was used as the starting point.
    /// </summary>
    public long SnapshotVersion { get; init; }

    /// <summary>
    /// Gets the number of events that were replayed after the snapshot.
    /// </summary>
    public int EventsReplayed { get; init; }

    /// <summary>
    /// Gets the total time taken for the restoration operation.
    /// </summary>
    public TimeSpan RestorationTime { get; init; }

    /// <summary>
    /// Gets the time saved by using a snapshot instead of full replay.
    /// </summary>
    public TimeSpan TimeSaved { get; init; }

    /// <summary>
    /// Gets the metadata of the snapshot that was used.
    /// </summary>
    public SnapshotMetadata? SnapshotMetadata { get; init; }

    /// <summary>
    /// Gets any error message if the restoration failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the error code if the restoration failed.
    /// </summary>
    public SnapshotErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the timestamp when the restoration completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents the result of a snapshot validation operation.
/// </summary>
public record SnapshotValidationResult
{
    /// <summary>
    /// Gets whether the validation was successful.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the issues found during validation.
    /// </summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    /// <summary>
    /// Gets the snapshot metadata that was validated.
    /// </summary>
    public SnapshotMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets whether the content hash matches the stored data.
    /// </summary>
    public bool ContentHashValid { get; init; }

    /// <summary>
    /// Gets whether the compression is valid and data can be decompressed.
    /// </summary>
    public bool CompressionValid { get; init; }

    /// <summary>
    /// Gets whether the serialized data can be deserialized.
    /// </summary>
    public bool SerializationValid { get; init; }

    /// <summary>
    /// Gets the timestamp when validation was performed.
    /// </summary>
    public DateTimeOffset ValidatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a valid result.
    /// </summary>
    /// <param name="metadata">The snapshot metadata</param>
    /// <returns>A valid validation result</returns>
    public static SnapshotValidationResult CreateValid(SnapshotMetadata metadata)
    {
        return new SnapshotValidationResult
        {
            IsValid = true,
            Metadata = metadata,
            ContentHashValid = true,
            CompressionValid = true,
            SerializationValid = true
        };
    }

    /// <summary>
    /// Creates an invalid result.
    /// </summary>
    /// <param name="issues">The validation issues found</param>
    /// <param name="metadata">The snapshot metadata if available</param>
    /// <param name="contentHashValid">Whether content hash is valid</param>
    /// <param name="compressionValid">Whether compression is valid</param>
    /// <param name="serializationValid">Whether serialization is valid</param>
    /// <returns>An invalid validation result</returns>
    public static SnapshotValidationResult CreateInvalid(
        IReadOnlyList<string> issues,
        SnapshotMetadata? metadata = null,
        bool contentHashValid = false,
        bool compressionValid = false,
        bool serializationValid = false)
    {
        return new SnapshotValidationResult
        {
            IsValid = false,
            Issues = issues,
            Metadata = metadata,
            ContentHashValid = contentHashValid,
            CompressionValid = compressionValid,
            SerializationValid = serializationValid
        };
    }
}

/// <summary>
/// Represents the result of a snapshot cleanup operation.
/// </summary>
public record SnapshotCleanupResult
{
    /// <summary>
    /// Gets whether the cleanup operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of snapshots that were deleted.
    /// </summary>
    public int SnapshotsDeleted { get; init; }

    /// <summary>
    /// Gets the total storage space reclaimed in bytes.
    /// </summary>
    public long SpaceReclaimed { get; init; }

    /// <summary>
    /// Gets the details of what was cleaned up.
    /// </summary>
    public IReadOnlyList<SnapshotCleanupDetail> CleanupDetails { get; init; } = [];

    /// <summary>
    /// Gets any error message if the cleanup failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the error code if the cleanup failed.
    /// </summary>
    public SnapshotErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the time taken for the cleanup operation.
    /// </summary>
    public TimeSpan CleanupTime { get; init; }

    /// <summary>
    /// Gets the timestamp when cleanup completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful cleanup result.
    /// </summary>
    /// <param name="snapshotsDeleted">Number of snapshots deleted</param>
    /// <param name="spaceReclaimed">Space reclaimed in bytes</param>
    /// <param name="cleanupDetails">Details of the cleanup</param>
    /// <param name="cleanupTime">Time taken for cleanup</param>
    /// <returns>A successful cleanup result</returns>
    public static SnapshotCleanupResult CreateSuccess(
        int snapshotsDeleted,
        long spaceReclaimed,
        IReadOnlyList<SnapshotCleanupDetail> cleanupDetails,
        TimeSpan cleanupTime)
    {
        return new SnapshotCleanupResult
        {
            Success = true,
            SnapshotsDeleted = snapshotsDeleted,
            SpaceReclaimed = spaceReclaimed,
            CleanupDetails = cleanupDetails,
            CleanupTime = cleanupTime
        };
    }

    /// <summary>
    /// Creates a failed cleanup result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="cleanupTime">Time taken before failure</param>
    /// <returns>A failed cleanup result</returns>
    public static SnapshotCleanupResult CreateFailure(
        string error,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError,
        TimeSpan cleanupTime = default)
    {
        return new SnapshotCleanupResult
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            CleanupTime = cleanupTime
        };
    }
}

/// <summary>
/// Represents details about a specific snapshot cleanup operation.
/// </summary>
public record SnapshotCleanupDetail
{
    /// <summary>
    /// Gets the ID of the snapshot that was deleted.
    /// </summary>
    public required string SnapshotId { get; init; }

    /// <summary>
    /// Gets the stream ID the snapshot belonged to.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the version of the deleted snapshot.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets the reason why this snapshot was deleted.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Gets the size of the deleted snapshot in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets the age of the snapshot when it was deleted.
    /// </summary>
    public TimeSpan Age { get; init; }
}

/// <summary>
/// Represents the result of a snapshot optimization operation.
/// </summary>
public record SnapshotOptimizationResult
{
    /// <summary>
    /// Gets whether the optimization was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the total space saved through optimization in bytes.
    /// </summary>
    public long SpaceSaved { get; init; }

    /// <summary>
    /// Gets the number of snapshots that were optimized.
    /// </summary>
    public int SnapshotsOptimized { get; init; }

    /// <summary>
    /// Gets the number of duplicate snapshots that were consolidated.
    /// </summary>
    public int DuplicatesConsolidated { get; init; }

    /// <summary>
    /// Gets the improvement in compression ratio achieved.
    /// </summary>
    public double CompressionImprovement { get; init; }

    /// <summary>
    /// Gets any error message if optimization failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the time taken for optimization.
    /// </summary>
    public TimeSpan OptimizationTime { get; init; }

    /// <summary>
    /// Gets the timestamp when optimization completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents statistics about snapshots for a specific stream.
/// </summary>
public record SnapshotStatistics
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the total number of snapshots for this stream.
    /// </summary>
    public int TotalSnapshots { get; init; }

    /// <summary>
    /// Gets the total compressed size of all snapshots for this stream.
    /// </summary>
    public long TotalCompressedSize { get; init; }

    /// <summary>
    /// Gets the total uncompressed size of all snapshots for this stream.
    /// </summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Gets the average compression ratio for this stream's snapshots.
    /// </summary>
    public double AverageCompressionRatio { get; init; }

    /// <summary>
    /// Gets the latest snapshot version for this stream.
    /// </summary>
    public long LatestSnapshotVersion { get; init; }

    /// <summary>
    /// Gets the oldest snapshot timestamp for this stream.
    /// </summary>
    public DateTimeOffset? OldestSnapshotTimestamp { get; init; }

    /// <summary>
    /// Gets the newest snapshot timestamp for this stream.
    /// </summary>
    public DateTimeOffset? NewestSnapshotTimestamp { get; init; }

    /// <summary>
    /// Gets the average size of snapshots for this stream.
    /// </summary>
    public double AverageSnapshotSize => TotalSnapshots > 0 ? (double)TotalCompressedSize / TotalSnapshots : 0;

    /// <summary>
    /// Gets the total space saved through compression for this stream.
    /// </summary>
    public long TotalSpaceSaved => TotalUncompressedSize - TotalCompressedSize;
}

/// <summary>
/// Represents global statistics about all snapshots.
/// </summary>
public record SnapshotGlobalStatistics
{
    /// <summary>
    /// Gets the total number of snapshots across all streams.
    /// </summary>
    public long TotalSnapshots { get; init; }

    /// <summary>
    /// Gets the total number of streams with snapshots.
    /// </summary>
    public long TotalStreams { get; init; }

    /// <summary>
    /// Gets the total compressed storage size across all snapshots.
    /// </summary>
    public long TotalCompressedSize { get; init; }

    /// <summary>
    /// Gets the total uncompressed size across all snapshots.
    /// </summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Gets the overall compression ratio across all snapshots.
    /// </summary>
    public double OverallCompressionRatio => TotalUncompressedSize > 0
        ? (double)TotalCompressedSize / TotalUncompressedSize : 1.0;

    /// <summary>
    /// Gets the total space saved through compression.
    /// </summary>
    public long TotalSpaceSaved => TotalUncompressedSize - TotalCompressedSize;

    /// <summary>
    /// Gets the number of deduplicated snapshots.
    /// </summary>
    public long DeduplicatedSnapshots { get; init; }

    /// <summary>
    /// Gets the space saved through deduplication.
    /// </summary>
    public long DeduplicationSpaceSaved { get; init; }

    /// <summary>
    /// Gets the average snapshot size across all snapshots.
    /// </summary>
    public double AverageSnapshotSize => TotalSnapshots > 0 ? (double)TotalCompressedSize / TotalSnapshots : 0;

    /// <summary>
    /// Gets the timestamp when these statistics were collected.
    /// </summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents policies for automated snapshot creation.
/// </summary>
public record SnapshotCreationPolicy
{
    /// <summary>
    /// Gets the minimum number of events before a snapshot should be created.
    /// Null means no event count trigger.
    /// </summary>
    public int? EventCountThreshold { get; init; }

    /// <summary>
    /// Gets the maximum time elapsed since the last snapshot before creating a new one.
    /// Null means no time-based trigger.
    /// </summary>
    public TimeSpan? TimeThreshold { get; init; }

    /// <summary>
    /// Gets the minimum version increase since the last snapshot.
    /// Null means no version-based trigger.
    /// </summary>
    public long? VersionThreshold { get; init; }

    /// <summary>
    /// Gets the estimated stream size threshold in bytes before creating a snapshot.
    /// Null means no size-based trigger.
    /// </summary>
    public long? StreamSizeThreshold { get; init; }

    /// <summary>
    /// Gets whether to create snapshots during specific grain lifecycle events.
    /// </summary>
    public bool CreateOnDeactivation { get; init; }

    /// <summary>
    /// Gets whether to create snapshots before significant state changes.
    /// </summary>
    public bool CreateBeforeSignificantChanges { get; init; }

    /// <summary>
    /// Gets custom conditions for snapshot creation.
    /// These are evaluated in addition to the standard thresholds.
    /// </summary>
    public Dictionary<string, object>? CustomConditions { get; init; }

    /// <summary>
    /// Creates a policy based on event count.
    /// </summary>
    /// <param name="eventCount">Number of events before creating snapshot</param>
    /// <returns>An event count-based policy</returns>
    public static SnapshotCreationPolicy ByEventCount(int eventCount)
    {
        return new SnapshotCreationPolicy { EventCountThreshold = eventCount };
    }

    /// <summary>
    /// Creates a policy based on time elapsed.
    /// </summary>
    /// <param name="timeSpan">Time elapsed before creating snapshot</param>
    /// <returns>A time-based policy</returns>
    public static SnapshotCreationPolicy ByTime(TimeSpan timeSpan)
    {
        return new SnapshotCreationPolicy { TimeThreshold = timeSpan };
    }

    /// <summary>
    /// Creates a policy based on version changes.
    /// </summary>
    /// <param name="versionDelta">Version increase before creating snapshot</param>
    /// <returns>A version-based policy</returns>
    public static SnapshotCreationPolicy ByVersion(long versionDelta)
    {
        return new SnapshotCreationPolicy { VersionThreshold = versionDelta };
    }

    /// <summary>
    /// Creates a combined policy with multiple triggers.
    /// </summary>
    /// <param name="eventCount">Event count threshold</param>
    /// <param name="timeThreshold">Time threshold</param>
    /// <param name="versionThreshold">Version threshold</param>
    /// <returns>A combined policy</returns>
    public static SnapshotCreationPolicy Combined(
        int? eventCount = null,
        TimeSpan? timeThreshold = null,
        long? versionThreshold = null)
    {
        return new SnapshotCreationPolicy
        {
            EventCountThreshold = eventCount,
            TimeThreshold = timeThreshold,
            VersionThreshold = versionThreshold
        };
    }
}