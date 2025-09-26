namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Factory methods for creating snapshot results.
/// </summary>
public static class SnapshotResult
{
    /// <summary>
    /// Creates a successful result with snapshot data.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="data">The snapshot data</param>
    /// <param name="metadata">The snapshot metadata</param>
    /// <returns>A successful snapshot result</returns>
    public static SnapshotResult<T> CreateSuccess<T>(T data, SnapshotMetadata metadata)
    {
        return new SnapshotResult<T>
        {
            Success = true,
            Data = data,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a result indicating no snapshot was found.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <returns>A not found snapshot result</returns>
    public static SnapshotResult<T> CreateNotFound<T>()
    {
        return new SnapshotResult<T>
        {
            Success = false,
            ErrorCode = SnapshotErrorCode.SnapshotNotFound
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <typeparam name="T">The type of state stored in the snapshot</typeparam>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <returns>A failed snapshot result</returns>
    public static SnapshotResult<T> CreateFailure<T>(
        string error,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError)
    {
        return new SnapshotResult<T>
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode
        };
    }
}

/// <summary>
/// Represents the result of a snapshot read operation.
/// </summary>
/// <typeparam name="T">The type of state stored in the snapshot</typeparam>
public record SnapshotResult<T>
{
    /// <summary>
    /// Gets whether the operation was successful and a snapshot was found.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the snapshot data if the operation was successful.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Gets the metadata about the snapshot.
    /// </summary>
    public SnapshotMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the error code for categorization.
    /// </summary>
    public SnapshotErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the timestamp when the operation completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents the result of a snapshot write operation.
/// </summary>
public record SnapshotWriteResult
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the unique identifier of the created/modified snapshot.
    /// </summary>
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Gets the content hash of the snapshot data.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Gets the compressed size of the snapshot in bytes.
    /// </summary>
    public long CompressedSize { get; init; }

    /// <summary>
    /// Gets the uncompressed size of the snapshot in bytes.
    /// </summary>
    public long UncompressedSize { get; init; }

    /// <summary>
    /// Gets the compression ratio achieved (compressed size / uncompressed size).
    /// </summary>
    public double CompressionRatio => UncompressedSize > 0 ? (double)CompressedSize / UncompressedSize : 1.0;

    /// <summary>
    /// Gets whether this snapshot was deduplicated (shared content with existing snapshot).
    /// </summary>
    public bool WasDeduplicated { get; init; }

    /// <summary>
    /// Gets the number of snapshots affected by the operation (useful for bulk deletes).
    /// </summary>
    public int SnapshotsAffected { get; init; } = 1;

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the error code for categorization.
    /// </summary>
    public SnapshotErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets additional metadata about the operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets the timestamp when the operation completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a successful write result.
    /// </summary>
    /// <param name="snapshotId">The snapshot identifier</param>
    /// <param name="contentHash">The content hash</param>
    /// <param name="compressedSize">The compressed size</param>
    /// <param name="uncompressedSize">The uncompressed size</param>
    /// <param name="wasDeduplicated">Whether the snapshot was deduplicated</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful write result</returns>
    public static SnapshotWriteResult CreateSuccess(
        string snapshotId,
        string contentHash,
        long compressedSize,
        long uncompressedSize,
        bool wasDeduplicated = false,
        Dictionary<string, object>? metadata = null)
    {
        return new SnapshotWriteResult
        {
            Success = true,
            SnapshotId = snapshotId,
            ContentHash = contentHash,
            CompressedSize = compressedSize,
            UncompressedSize = uncompressedSize,
            WasDeduplicated = wasDeduplicated,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a successful bulk operation result.
    /// </summary>
    /// <param name="snapshotsAffected">Number of snapshots affected</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful bulk operation result</returns>
    public static SnapshotWriteResult CreateBulkSuccess(
        int snapshotsAffected,
        Dictionary<string, object>? metadata = null)
    {
        return new SnapshotWriteResult
        {
            Success = true,
            SnapshotsAffected = snapshotsAffected,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed write result.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed write result</returns>
    public static SnapshotWriteResult CreateFailure(
        string error,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError,
        Dictionary<string, object>? metadata = null)
    {
        return new SnapshotWriteResult
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            SnapshotsAffected = 0,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of a snapshot query operation.
/// </summary>
public record SnapshotQueryResult
{
    /// <summary>
    /// Gets the snapshot metadata returned by the query.
    /// </summary>
    public required IReadOnlyList<SnapshotMetadata> Snapshots { get; init; }

    /// <summary>
    /// Gets the total number of snapshots matching the query criteria.
    /// May be larger than Snapshots.Count due to pagination.
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
    /// <returns>An empty snapshot query result</returns>
    public static SnapshotQueryResult Empty()
    {
        return new SnapshotQueryResult
        {
            Snapshots = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 100
        };
    }

    /// <summary>
    /// Creates a query result with snapshots.
    /// </summary>
    /// <param name="snapshots">The snapshots to include</param>
    /// <param name="totalCount">The total count of matching snapshots</param>
    /// <param name="page">The current page</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A snapshot query result</returns>
    public static SnapshotQueryResult Create(
        IReadOnlyList<SnapshotMetadata> snapshots,
        int totalCount,
        int page = 1,
        int pageSize = 100,
        Dictionary<string, object>? metadata = null)
    {
        return new SnapshotQueryResult
        {
            Snapshots = snapshots,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents metadata about a snapshot without the actual data.
/// Used for querying and listing operations where the full snapshot data isn't needed.
/// </summary>
public record SnapshotMetadata
{
    /// <summary>
    /// Gets the unique identifier of the snapshot.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the stream identifier this snapshot belongs to.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the exact version of the stream this snapshot represents.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets the content hash of the snapshot data for integrity verification.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// Gets the timestamp when the snapshot was created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the compressed size of the snapshot in bytes.
    /// </summary>
    public required long CompressedSize { get; init; }

    /// <summary>
    /// Gets the uncompressed size of the snapshot in bytes.
    /// </summary>
    public required long UncompressedSize { get; init; }

    /// <summary>
    /// Gets the compression type used for the snapshot.
    /// </summary>
    public required string CompressionType { get; init; }

    /// <summary>
    /// Gets the type name of the stored state for deserialization.
    /// </summary>
    public required string StateType { get; init; }

    /// <summary>
    /// Gets additional metadata associated with the snapshot.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetadata { get; init; }

    /// <summary>
    /// Gets the compression ratio achieved (compressed size / uncompressed size).
    /// </summary>
    public double CompressionRatio => UncompressedSize > 0 ? (double)CompressedSize / UncompressedSize : 1.0;

    /// <summary>
    /// Gets the space saved through compression in bytes.
    /// </summary>
    public long SpaceSaved => UncompressedSize - CompressedSize;

    /// <summary>
    /// Gets the compression percentage (how much smaller the compressed version is).
    /// </summary>
    public double CompressionPercentage => UncompressedSize > 0 ? (1.0 - CompressionRatio) * 100 : 0;
}

/// <summary>
/// Represents a flexible query for snapshots.
/// </summary>
public record SnapshotQuery
{
    /// <summary>
    /// Gets or initializes the stream identifier to filter by.
    /// If null, queries across all streams.
    /// </summary>
    public string? StreamId { get; init; }

    /// <summary>
    /// Gets or initializes the state type to filter by.
    /// If null, includes all state types.
    /// </summary>
    public string? StateType { get; init; }

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
    /// Gets or initializes the content hash to filter by.
    /// Useful for deduplication analysis.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// Gets or initializes the minimum compressed size in bytes.
    /// Useful for finding large snapshots.
    /// </summary>
    public long? MinCompressedSize { get; init; }

    /// <summary>
    /// Gets or initializes the maximum compressed size in bytes.
    /// Useful for finding snapshots within size limits.
    /// </summary>
    public long? MaxCompressedSize { get; init; }

    /// <summary>
    /// Gets or initializes the compression type to filter by.
    /// </summary>
    public string? CompressionType { get; init; }

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
    /// Snapshots must match all specified metadata key-value pairs.
    /// </summary>
    public Dictionary<string, object>? MetadataFilters { get; init; }

    /// <summary>
    /// Gets or initializes the sort order for results.
    /// </summary>
    public SnapshotSortOrder SortOrder { get; init; } = SnapshotSortOrder.TimestampDescending;

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

        if (MinCompressedSize.HasValue && MinCompressedSize < 0)
        {
            return (false, "MinCompressedSize cannot be negative");
        }

        if (MaxCompressedSize.HasValue && MaxCompressedSize < 0)
        {
            return (false, "MaxCompressedSize cannot be negative");
        }

        if (MinCompressedSize.HasValue && MaxCompressedSize.HasValue && MinCompressedSize > MaxCompressedSize)
        {
            return (false, "MinCompressedSize cannot be greater than MaxCompressedSize");
        }

        return (true, null);
    }

    /// <summary>
    /// Creates a query for all snapshots in a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>A snapshot query for the stream</returns>
    public static SnapshotQuery ForStream(string streamId, int pageSize = 100, int page = 1)
    {
        return new SnapshotQuery
        {
            StreamId = streamId,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for snapshots by state type.
    /// </summary>
    /// <param name="stateType">The state type</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>A snapshot query for the state type</returns>
    public static SnapshotQuery ForStateType(string stateType, int pageSize = 100, int page = 1)
    {
        return new SnapshotQuery
        {
            StateType = stateType,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for snapshots within a time range.
    /// </summary>
    /// <param name="fromTimestamp">The start time</param>
    /// <param name="toTimestamp">The end time</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>A snapshot query for the time range</returns>
    public static SnapshotQuery ForTimeRange(
        DateTimeOffset fromTimestamp,
        DateTimeOffset toTimestamp,
        int pageSize = 100,
        int page = 1)
    {
        return new SnapshotQuery
        {
            FromTimestamp = fromTimestamp,
            ToTimestamp = toTimestamp,
            PageSize = pageSize,
            Page = page
        };
    }

    /// <summary>
    /// Creates a query for snapshots larger than a specific size.
    /// </summary>
    /// <param name="minSize">The minimum compressed size</param>
    /// <param name="pageSize">The page size</param>
    /// <param name="page">The page number</param>
    /// <returns>A snapshot query for large snapshots</returns>
    public static SnapshotQuery ForLargeSnapshots(long minSize, int pageSize = 100, int page = 1)
    {
        return new SnapshotQuery
        {
            MinCompressedSize = minSize,
            PageSize = pageSize,
            Page = page,
            SortOrder = SnapshotSortOrder.SizeDescending
        };
    }
}

/// <summary>
/// Represents sort order options for snapshot queries.
/// </summary>
public enum SnapshotSortOrder
{
    /// <summary>
    /// Sort by timestamp ascending (oldest first).
    /// </summary>
    TimestampAscending,

    /// <summary>
    /// Sort by timestamp descending (newest first).
    /// </summary>
    TimestampDescending,

    /// <summary>
    /// Sort by version ascending (lowest version first).
    /// Only valid for single-stream queries.
    /// </summary>
    VersionAscending,

    /// <summary>
    /// Sort by version descending (highest version first).
    /// Only valid for single-stream queries.
    /// </summary>
    VersionDescending,

    /// <summary>
    /// Sort by compressed size ascending (smallest first).
    /// </summary>
    SizeAscending,

    /// <summary>
    /// Sort by compressed size descending (largest first).
    /// </summary>
    SizeDescending,

    /// <summary>
    /// Sort by compression ratio ascending (least compressed first).
    /// </summary>
    CompressionRatioAscending,

    /// <summary>
    /// Sort by compression ratio descending (most compressed first).
    /// </summary>
    CompressionRatioDescending
}

/// <summary>
/// Represents error codes for snapshot store operations.
/// </summary>
public enum SnapshotErrorCode
{
    /// <summary>
    /// No error occurred.
    /// </summary>
    None,

    /// <summary>
    /// The specified snapshot was not found.
    /// </summary>
    SnapshotNotFound,

    /// <summary>
    /// The specified stream was not found.
    /// </summary>
    StreamNotFound,

    /// <summary>
    /// Snapshot data validation failed.
    /// </summary>
    ValidationError,

    /// <summary>
    /// A snapshot with the same ID already exists.
    /// </summary>
    DuplicateSnapshot,

    /// <summary>
    /// Snapshot serialization failed.
    /// </summary>
    SerializationError,

    /// <summary>
    /// Snapshot deserialization failed.
    /// </summary>
    DeserializationError,

    /// <summary>
    /// Snapshot compression failed.
    /// </summary>
    CompressionError,

    /// <summary>
    /// Snapshot decompression failed.
    /// </summary>
    DecompressionError,

    /// <summary>
    /// Content hash verification failed (data corruption detected).
    /// </summary>
    IntegrityError,

    /// <summary>
    /// Storage backend is unavailable.
    /// </summary>
    StorageUnavailable,

    /// <summary>
    /// The operation timed out.
    /// </summary>
    TimeoutError,

    /// <summary>
    /// An internal error occurred.
    /// </summary>
    InternalError,

    /// <summary>
    /// Network connectivity error.
    /// </summary>
    NetworkError,

    /// <summary>
    /// Insufficient storage space.
    /// </summary>
    InsufficientStorage,

    /// <summary>
    /// Version conflict detected.
    /// </summary>
    VersionConflict
}

/// <summary>
/// Represents the health status of a snapshot store.
/// </summary>
public record SnapshotHealthStatus
{
    /// <summary>
    /// Gets whether the snapshot store is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether the underlying storage is healthy.
    /// </summary>
    public required bool IsStorageHealthy { get; init; }

    /// <summary>
    /// Gets whether the compression system is healthy.
    /// </summary>
    public required bool IsCompressionHealthy { get; init; }

    /// <summary>
    /// Gets whether the serialization system is healthy.
    /// </summary>
    public required bool IsSerializationHealthy { get; init; }

    /// <summary>
    /// Gets the current snapshot store implementation name.
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
    /// <returns>A healthy snapshot store status</returns>
    public static SnapshotHealthStatus Healthy(string implementation, Dictionary<string, object>? details = null)
    {
        return new SnapshotHealthStatus
        {
            IsHealthy = true,
            IsStorageHealthy = true,
            IsCompressionHealthy = true,
            IsSerializationHealthy = true,
            Implementation = implementation,
            Message = "Snapshot store is healthy",
            Details = details
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="implementation">The implementation name</param>
    /// <param name="message">The error message</param>
    /// <param name="isStorageHealthy">Whether storage is healthy</param>
    /// <param name="isCompressionHealthy">Whether compression is healthy</param>
    /// <param name="isSerializationHealthy">Whether serialization is healthy</param>
    /// <param name="details">Optional additional details</param>
    /// <returns>An unhealthy snapshot store status</returns>
    public static SnapshotHealthStatus Unhealthy(
        string implementation,
        string message,
        bool isStorageHealthy = false,
        bool isCompressionHealthy = true,
        bool isSerializationHealthy = true,
        Dictionary<string, object>? details = null)
    {
        return new SnapshotHealthStatus
        {
            IsHealthy = false,
            IsStorageHealthy = isStorageHealthy,
            IsCompressionHealthy = isCompressionHealthy,
            IsSerializationHealthy = isSerializationHealthy,
            Implementation = implementation,
            Message = message,
            Details = details
        };
    }
}

/// <summary>
/// Represents performance and usage metrics for a snapshot store.
/// </summary>
public record SnapshotMetrics
{
    /// <summary>
    /// Gets the total number of snapshot creation operations.
    /// </summary>
    public long CreateOperations { get; init; }

    /// <summary>
    /// Gets the total number of snapshot read operations.
    /// </summary>
    public long ReadOperations { get; init; }

    /// <summary>
    /// Gets the total number of snapshot query operations.
    /// </summary>
    public long QueryOperations { get; init; }

    /// <summary>
    /// Gets the total number of snapshot delete operations.
    /// </summary>
    public long DeleteOperations { get; init; }

    /// <summary>
    /// Gets the total number of failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Gets the average execution time for create operations in milliseconds.
    /// </summary>
    public double AverageCreateTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for read operations in milliseconds.
    /// </summary>
    public double AverageReadTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for query operations in milliseconds.
    /// </summary>
    public double AverageQueryTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for delete operations in milliseconds.
    /// </summary>
    public double AverageDeleteTimeMs { get; init; }

    /// <summary>
    /// Gets the total number of snapshots stored.
    /// </summary>
    public long TotalSnapshots { get; init; }

    /// <summary>
    /// Gets the total number of streams with snapshots.
    /// </summary>
    public long TotalStreams { get; init; }

    /// <summary>
    /// Gets the total compressed storage size in bytes.
    /// </summary>
    public long TotalCompressedSize { get; init; }

    /// <summary>
    /// Gets the total uncompressed size in bytes.
    /// </summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>
    /// Gets the overall compression ratio.
    /// </summary>
    public double OverallCompressionRatio => TotalUncompressedSize > 0
        ? (double)TotalCompressedSize / TotalUncompressedSize : 1.0;

    /// <summary>
    /// Gets the total space saved through compression in bytes.
    /// </summary>
    public long TotalSpaceSaved => TotalUncompressedSize - TotalCompressedSize;

    /// <summary>
    /// Gets the number of snapshots that were deduplicated.
    /// </summary>
    public long DeduplicatedSnapshots { get; init; }

    /// <summary>
    /// Gets the space saved through deduplication in bytes.
    /// </summary>
    public long DeduplicationSpaceSaved { get; init; }

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
    public long TotalOperations => CreateOperations + ReadOperations + QueryOperations + DeleteOperations;

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalOperations > 0
        ? (double)(TotalOperations - FailedOperations) / TotalOperations * 100
        : 100;

    /// <summary>
    /// Gets the average snapshot size in bytes (compressed).
    /// </summary>
    public double AverageSnapshotSize => TotalSnapshots > 0
        ? (double)TotalCompressedSize / TotalSnapshots : 0;

    /// <summary>
    /// Creates empty metrics.
    /// </summary>
    /// <returns>Empty snapshot store metrics</returns>
    public static SnapshotMetrics Empty()
    {
        return new SnapshotMetrics
        {
            CreateOperations = 0,
            ReadOperations = 0,
            QueryOperations = 0,
            DeleteOperations = 0,
            FailedOperations = 0,
            AverageCreateTimeMs = 0,
            AverageReadTimeMs = 0,
            AverageQueryTimeMs = 0,
            AverageDeleteTimeMs = 0,
            TotalSnapshots = 0,
            TotalStreams = 0,
            TotalCompressedSize = 0,
            TotalUncompressedSize = 0,
            DeduplicatedSnapshots = 0,
            DeduplicationSpaceSaved = 0
        };
    }
}

/// <summary>
/// Represents retention policies for snapshot cleanup.
/// </summary>
public record SnapshotRetentionPolicy
{
    /// <summary>
    /// Gets the maximum age for snapshots before they're eligible for cleanup.
    /// Null means no age-based retention.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// Gets the maximum number of snapshots to keep per stream.
    /// Null means no count-based retention.
    /// </summary>
    public int? MaxSnapshotsPerStream { get; init; }

    /// <summary>
    /// Gets the maximum total storage size before cleanup is triggered.
    /// Null means no size-based retention.
    /// </summary>
    public long? MaxTotalSize { get; init; }

    /// <summary>
    /// Gets the minimum interval between snapshots to keep (e.g., keep at most one per hour).
    /// Null means no interval-based retention.
    /// </summary>
    public TimeSpan? MinIntervalBetweenSnapshots { get; init; }

    /// <summary>
    /// Gets whether to preserve snapshots that are referenced by active restore operations.
    /// </summary>
    public bool PreserveActiveSnapshots { get; init; } = true;

    /// <summary>
    /// Gets additional metadata filters for retention decisions.
    /// Snapshots matching these filters may be retained even if they exceed other limits.
    /// </summary>
    public Dictionary<string, object>? PreservationFilters { get; init; }

    /// <summary>
    /// Creates a retention policy based on age.
    /// </summary>
    /// <param name="maxAge">Maximum age to retain snapshots</param>
    /// <returns>An age-based retention policy</returns>
    public static SnapshotRetentionPolicy ByAge(TimeSpan maxAge)
    {
        return new SnapshotRetentionPolicy { MaxAge = maxAge };
    }

    /// <summary>
    /// Creates a retention policy based on count per stream.
    /// </summary>
    /// <param name="maxCount">Maximum snapshots to keep per stream</param>
    /// <returns>A count-based retention policy</returns>
    public static SnapshotRetentionPolicy ByCount(int maxCount)
    {
        return new SnapshotRetentionPolicy { MaxSnapshotsPerStream = maxCount };
    }

    /// <summary>
    /// Creates a retention policy based on total storage size.
    /// </summary>
    /// <param name="maxSize">Maximum total storage size</param>
    /// <returns>A size-based retention policy</returns>
    public static SnapshotRetentionPolicy BySize(long maxSize)
    {
        return new SnapshotRetentionPolicy { MaxTotalSize = maxSize };
    }

    /// <summary>
    /// Creates a combined retention policy.
    /// </summary>
    /// <param name="maxAge">Maximum age to retain snapshots</param>
    /// <param name="maxCount">Maximum snapshots to keep per stream</param>
    /// <param name="maxSize">Maximum total storage size</param>
    /// <returns>A combined retention policy</returns>
    public static SnapshotRetentionPolicy Combined(
        TimeSpan? maxAge = null,
        int? maxCount = null,
        long? maxSize = null)
    {
        return new SnapshotRetentionPolicy
        {
            MaxAge = maxAge,
            MaxSnapshotsPerStream = maxCount,
            MaxTotalSize = maxSize
        };
    }
}