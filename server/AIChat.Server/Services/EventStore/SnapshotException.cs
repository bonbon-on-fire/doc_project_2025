namespace AIChat.Server.Services.EventStore;

/// <summary>
/// Base exception class for all snapshot store related exceptions.
/// Provides structured error information for snapshot operations.
/// </summary>
public class SnapshotStoreException : Exception
{
    /// <summary>
    /// Gets the specific error code for this exception.
    /// </summary>
    public SnapshotErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the snapshot ID associated with this exception, if applicable.
    /// </summary>
    public string? SnapshotId { get; }

    /// <summary>
    /// Gets the stream ID associated with this exception, if applicable.
    /// </summary>
    public string? StreamId { get; }

    /// <summary>
    /// Gets additional context information about the error.
    /// </summary>
    public Dictionary<string, object>? Context { get; }

    /// <summary>
    /// Initializes a new instance of the SnapshotStoreException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="errorCode">The specific error code</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="context">Additional context information</param>
    /// <param name="innerException">The inner exception</param>
    public SnapshotStoreException(
        string message,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError,
        string? snapshotId = null,
        string? streamId = null,
        Dictionary<string, object>? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        SnapshotId = snapshotId;
        StreamId = streamId;
        Context = context;
    }

    public SnapshotStoreException()
    {
    }

    public SnapshotStoreException(string? message) : base(message)
    {
    }

    public SnapshotStoreException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a snapshot not found exception.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID that was not found</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <returns>A snapshot not found exception</returns>
    public static SnapshotStoreException SnapshotNotFound(string snapshotId, string? streamId = null)
    {
        var message = streamId != null
            ? $"Snapshot '{snapshotId}' not found for stream '{streamId}'"
            : $"Snapshot '{snapshotId}' not found";

        return new SnapshotStoreException(
            message,
            SnapshotErrorCode.SnapshotNotFound,
            snapshotId,
            streamId);
    }

    /// <summary>
    /// Creates a stream not found exception.
    /// </summary>
    /// <param name="streamId">The stream ID that was not found</param>
    /// <returns>A stream not found exception</returns>
    public static SnapshotStoreException StreamNotFound(string streamId)
    {
        return new SnapshotStoreException(
            $"Stream '{streamId}' not found",
            SnapshotErrorCode.StreamNotFound,
            streamId: streamId);
    }

    /// <summary>
    /// Creates a validation error exception.
    /// </summary>
    /// <param name="message">The validation error message</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="context">Additional validation context</param>
    /// <returns>A validation error exception</returns>
    public static SnapshotStoreException ValidationError(
        string message,
        string? snapshotId = null,
        string? streamId = null,
        Dictionary<string, object>? context = null)
    {
        return new SnapshotStoreException(
            $"Validation error: {message}",
            SnapshotErrorCode.ValidationError,
            snapshotId,
            streamId,
            context);
    }

    /// <summary>
    /// Creates a duplicate snapshot exception.
    /// </summary>
    /// <param name="snapshotId">The duplicate snapshot ID</param>
    /// <param name="streamId">The stream ID</param>
    /// <param name="version">The version that already has a snapshot</param>
    /// <returns>A duplicate snapshot exception</returns>
    public static SnapshotStoreException DuplicateSnapshot(string snapshotId, string streamId, long version)
    {
        return new SnapshotStoreException(
            $"Snapshot already exists for stream '{streamId}' at version {version}",
            SnapshotErrorCode.DuplicateSnapshot,
            snapshotId,
            streamId,
            new Dictionary<string, object> { ["Version"] = version });
    }

    /// <summary>
    /// Creates a serialization error exception.
    /// </summary>
    /// <param name="message">The serialization error details</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="innerException">The underlying serialization exception</param>
    /// <returns>A serialization error exception</returns>
    public static SnapshotStoreException SerializationError(
        string message,
        string? snapshotId = null,
        string? streamId = null,
        Exception? innerException = null)
    {
        return new SnapshotStoreException(
            $"Serialization failed: {message}",
            SnapshotErrorCode.SerializationError,
            snapshotId,
            streamId,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a deserialization error exception.
    /// </summary>
    /// <param name="message">The deserialization error details</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="innerException">The underlying deserialization exception</param>
    /// <returns>A deserialization error exception</returns>
    public static SnapshotStoreException DeserializationError(
        string message,
        string? snapshotId = null,
        string? streamId = null,
        Exception? innerException = null)
    {
        return new SnapshotStoreException(
            $"Deserialization failed: {message}",
            SnapshotErrorCode.DeserializationError,
            snapshotId,
            streamId,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a compression error exception.
    /// </summary>
    /// <param name="message">The compression error details</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="innerException">The underlying compression exception</param>
    /// <returns>A compression error exception</returns>
    public static SnapshotStoreException CompressionError(
        string message,
        string? snapshotId = null,
        string? streamId = null,
        Exception? innerException = null)
    {
        return new SnapshotStoreException(
            $"Compression failed: {message}",
            SnapshotErrorCode.CompressionError,
            snapshotId,
            streamId,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a decompression error exception.
    /// </summary>
    /// <param name="message">The decompression error details</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="innerException">The underlying decompression exception</param>
    /// <returns>A decompression error exception</returns>
    public static SnapshotStoreException DecompressionError(
        string message,
        string? snapshotId = null,
        string? streamId = null,
        Exception? innerException = null)
    {
        return new SnapshotStoreException(
            $"Decompression failed: {message}",
            SnapshotErrorCode.DecompressionError,
            snapshotId,
            streamId,
            innerException: innerException);
    }

    /// <summary>
    /// Creates an integrity error exception for content hash mismatches.
    /// </summary>
    /// <param name="expectedHash">The expected content hash</param>
    /// <param name="actualHash">The actual content hash</param>
    /// <param name="snapshotId">The snapshot ID</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <returns>An integrity error exception</returns>
    public static SnapshotStoreException IntegrityError(
        string expectedHash,
        string actualHash,
        string snapshotId,
        string? streamId = null)
    {
        return new SnapshotStoreException(
            $"Content integrity check failed for snapshot '{snapshotId}'. Expected hash: {expectedHash}, Actual hash: {actualHash}",
            SnapshotErrorCode.IntegrityError,
            snapshotId,
            streamId,
            new Dictionary<string, object>
            {
                ["ExpectedHash"] = expectedHash,
                ["ActualHash"] = actualHash
            });
    }

    /// <summary>
    /// Creates a storage unavailable exception.
    /// </summary>
    /// <param name="message">The storage error details</param>
    /// <param name="innerException">The underlying storage exception</param>
    /// <returns>A storage unavailable exception</returns>
    public static SnapshotStoreException StorageUnavailable(string message, Exception? innerException = null)
    {
        return new SnapshotStoreException(
            $"Storage unavailable: {message}",
            SnapshotErrorCode.StorageUnavailable,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a timeout error exception.
    /// </summary>
    /// <param name="operation">The operation that timed out</param>
    /// <param name="timeout">The timeout duration</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <returns>A timeout error exception</returns>
    public static SnapshotStoreException TimeoutError(
        string operation,
        TimeSpan timeout,
        string? snapshotId = null,
        string? streamId = null)
    {
        return new SnapshotStoreException(
            $"Operation '{operation}' timed out after {timeout.TotalMilliseconds}ms",
            SnapshotErrorCode.TimeoutError,
            snapshotId,
            streamId,
            new Dictionary<string, object>
            {
                ["Operation"] = operation,
                ["TimeoutMs"] = timeout.TotalMilliseconds
            });
    }

    /// <summary>
    /// Creates an insufficient storage exception.
    /// </summary>
    /// <param name="requiredSpace">The required storage space in bytes</param>
    /// <param name="availableSpace">The available storage space in bytes</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <returns>An insufficient storage exception</returns>
    public static SnapshotStoreException InsufficientStorage(
        long requiredSpace,
        long availableSpace,
        string? snapshotId = null,
        string? streamId = null)
    {
        return new SnapshotStoreException(
            $"Insufficient storage space. Required: {requiredSpace} bytes, Available: {availableSpace} bytes",
            SnapshotErrorCode.InsufficientStorage,
            snapshotId,
            streamId,
            new Dictionary<string, object>
            {
                ["RequiredSpace"] = requiredSpace,
                ["AvailableSpace"] = availableSpace
            });
    }

    /// <summary>
    /// Creates a version conflict exception.
    /// </summary>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="actualVersion">The actual version</param>
    /// <param name="streamId">The stream ID</param>
    /// <returns>A version conflict exception</returns>
    public static SnapshotStoreException VersionConflict(
        long expectedVersion,
        long actualVersion,
        string streamId)
    {
        return new SnapshotStoreException(
            $"Version conflict for stream '{streamId}'. Expected: {expectedVersion}, Actual: {actualVersion}",
            SnapshotErrorCode.VersionConflict,
            streamId: streamId,
            context: new Dictionary<string, object>
            {
                ["ExpectedVersion"] = expectedVersion,
                ["ActualVersion"] = actualVersion
            });
    }
}

/// <summary>
/// Specialized exception for snapshot manager operations.
/// Provides higher-level error context for orchestration operations.
/// </summary>
public class SnapshotManagerException : SnapshotStoreException
{
    /// <summary>
    /// Gets the operation that failed.
    /// </summary>
    public string Operation { get; init; } = null!;

    /// <summary>
    /// Initializes a new instance of the SnapshotManagerException class.
    /// </summary>
    /// <param name="operation">The operation that failed</param>
    /// <param name="message">The error message</param>
    /// <param name="errorCode">The specific error code</param>
    /// <param name="snapshotId">The snapshot ID if applicable</param>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="context">Additional context information</param>
    /// <param name="innerException">The inner exception</param>
    public SnapshotManagerException(
        string operation,
        string message,
        SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError,
        string? snapshotId = null,
        string? streamId = null,
        Dictionary<string, object>? context = null,
        Exception? innerException = null)
        : base(message, errorCode, snapshotId, streamId, context, innerException)
    {
        Operation = operation;
    }

    public SnapshotManagerException(string message, SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError, string? snapshotId = null, string? streamId = null, Dictionary<string, object>? context = null, Exception? innerException = null) : base(message, errorCode, snapshotId, streamId, context, innerException)
    {
    }

    public SnapshotManagerException()
    {
    }

    public SnapshotManagerException(string? message) : base(message)
    {
    }

    public SnapshotManagerException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a snapshot creation failure exception.
    /// </summary>
    /// <param name="streamId">The stream ID</param>
    /// <param name="version">The version</param>
    /// <param name="innerException">The underlying exception</param>
    /// <returns>A snapshot creation failure exception</returns>
    public static SnapshotManagerException CreateSnapshotFailed(
        string streamId,
        long version,
        Exception innerException)
    {
        return new SnapshotManagerException(
            "CreateSnapshot",
            $"Failed to create snapshot for stream '{streamId}' at version {version}",
            SnapshotErrorCode.InternalError,
            streamId: streamId,
            context: new Dictionary<string, object> { ["Version"] = version },
            innerException: innerException);
    }

    /// <summary>
    /// Creates a snapshot restoration failure exception.
    /// </summary>
    /// <param name="streamId">The stream ID</param>
    /// <param name="snapshotId">The snapshot ID if available</param>
    /// <param name="innerException">The underlying exception</param>
    /// <returns>A snapshot restoration failure exception</returns>
    public static SnapshotManagerException RestoreSnapshotFailed(
        string streamId,
        string? snapshotId = null,
        Exception? innerException = null)
    {
        return new SnapshotManagerException(
            "RestoreSnapshot",
            $"Failed to restore snapshot for stream '{streamId}'",
            SnapshotErrorCode.InternalError,
            snapshotId,
            streamId,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a snapshot validation failure exception.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID</param>
    /// <param name="validationErrors">The validation errors</param>
    /// <returns>A snapshot validation failure exception</returns>
    public static SnapshotManagerException ValidationFailed(
        string snapshotId,
        IEnumerable<string> validationErrors)
    {
        var errors = string.Join(", ", validationErrors);
        return new SnapshotManagerException(
            "ValidateSnapshot",
            $"Snapshot validation failed for '{snapshotId}': {errors}",
            SnapshotErrorCode.ValidationError,
            snapshotId,
            context: new Dictionary<string, object> { ["ValidationErrors"] = validationErrors.ToList() });
    }

    /// <summary>
    /// Creates a cleanup operation failure exception.
    /// </summary>
    /// <param name="retentionPolicy">The retention policy that was being applied</param>
    /// <param name="innerException">The underlying exception</param>
    /// <returns>A cleanup failure exception</returns>
    public static SnapshotManagerException CleanupFailed(
        SnapshotRetentionPolicy retentionPolicy,
        Exception innerException)
    {
        return new SnapshotManagerException(
            "CleanupSnapshots",
            "Failed to cleanup snapshots based on retention policy",
            SnapshotErrorCode.InternalError,
            context: new Dictionary<string, object> { ["RetentionPolicy"] = retentionPolicy },
            innerException: innerException);
    }

    /// <summary>
    /// Creates an optimization operation failure exception.
    /// </summary>
    /// <param name="innerException">The underlying exception</param>
    /// <returns>An optimization failure exception</returns>
    public static SnapshotManagerException OptimizationFailed(Exception innerException)
    {
        return new SnapshotManagerException(
            "OptimizeStorage",
            "Failed to optimize snapshot storage",
            SnapshotErrorCode.InternalError,
            innerException: innerException);
    }

    /// <summary>
    /// Creates a statistics collection failure exception.
    /// </summary>
    /// <param name="streamId">The stream ID if applicable</param>
    /// <param name="innerException">The underlying exception</param>
    /// <returns>A statistics collection failure exception</returns>
    public static SnapshotManagerException StatisticsCollectionFailed(
        string? streamId = null,
        Exception? innerException = null)
    {
        var message = streamId != null
            ? $"Failed to collect statistics for stream '{streamId}'"
            : "Failed to collect global statistics";

        return new SnapshotManagerException(
            "GetStatistics",
            message,
            SnapshotErrorCode.InternalError,
            streamId: streamId,
            innerException: innerException);
    }
}

/// <summary>
/// Exception thrown when a concurrency conflict is detected during snapshot operations.
/// This typically occurs when multiple processes try to create snapshots for the same stream simultaneously.
/// </summary>
public class SnapshotConcurrencyException : SnapshotStoreException
{
    /// <summary>
    /// Gets the conflicting version.
    /// </summary>
    public long ConflictingVersion { get; }

    /// <summary>
    /// Initializes a new instance of the SnapshotConcurrencyException class.
    /// </summary>
    /// <param name="streamId">The stream ID</param>
    /// <param name="expectedVersion">The expected version</param>
    /// <param name="conflictingVersion">The conflicting version</param>
    public SnapshotConcurrencyException(string streamId, long expectedVersion, long conflictingVersion)
        : base(
            $"Concurrency conflict detected for stream '{streamId}'. Expected version {expectedVersion}, but found {conflictingVersion}",
            SnapshotErrorCode.VersionConflict,
            streamId: streamId,
            context: new Dictionary<string, object>
            {
                ["ExpectedVersion"] = expectedVersion,
                ["ConflictingVersion"] = conflictingVersion
            })
    {
        ConflictingVersion = conflictingVersion;
    }

    public SnapshotConcurrencyException(string message, SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError, string? snapshotId = null, string? streamId = null, Dictionary<string, object>? context = null, Exception? innerException = null) : base(message, errorCode, snapshotId, streamId, context, innerException)
    {
    }

    public SnapshotConcurrencyException()
    {
    }

    public SnapshotConcurrencyException(string? message) : base(message)
    {
    }

    public SnapshotConcurrencyException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when snapshot data corruption is detected.
/// This can occur due to storage issues, network problems, or software bugs.
/// </summary>
public class SnapshotCorruptionException : SnapshotStoreException
{
    /// <summary>
    /// Gets the type of corruption detected.
    /// </summary>
    public SnapshotCorruptionType CorruptionType { get; }

    /// <summary>
    /// Initializes a new instance of the SnapshotCorruptionException class.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID</param>
    /// <param name="corruptionType">The type of corruption</param>
    /// <param name="details">Additional details about the corruption</param>
    /// <param name="streamId">The stream ID if applicable</param>
    public SnapshotCorruptionException(
        string snapshotId,
        SnapshotCorruptionType corruptionType,
        string details,
        string? streamId = null)
        : base(
            $"Snapshot corruption detected for '{snapshotId}': {corruptionType} - {details}",
            SnapshotErrorCode.IntegrityError,
            snapshotId,
            streamId,
            new Dictionary<string, object>
            {
                ["CorruptionType"] = corruptionType.ToString(),
                ["Details"] = details
            })
    {
        CorruptionType = corruptionType;
    }

    public SnapshotCorruptionException(string message, SnapshotErrorCode errorCode = SnapshotErrorCode.InternalError, string? snapshotId = null, string? streamId = null, Dictionary<string, object>? context = null, Exception? innerException = null) : base(message, errorCode, snapshotId, streamId, context, innerException)
    {
    }

    public SnapshotCorruptionException()
    {
    }

    public SnapshotCorruptionException(string? message) : base(message)
    {
    }

    public SnapshotCorruptionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents the type of corruption detected in a snapshot.
/// </summary>
public enum SnapshotCorruptionType
{
    /// <summary>
    /// Content hash mismatch detected.
    /// </summary>
    ContentHashMismatch = 0,

    /// <summary>
    /// Compression format is invalid or corrupted.
    /// </summary>
    CompressionCorruption = 1,

    /// <summary>
    /// Serialized data is invalid or corrupted.
    /// </summary>
    SerializationCorruption = 2,

    /// <summary>
    /// Metadata is inconsistent or corrupted.
    /// </summary>
    MetadataCorruption = 3,

    /// <summary>
    /// Storage-level corruption detected.
    /// </summary>
    StorageCorruption = 4
}
