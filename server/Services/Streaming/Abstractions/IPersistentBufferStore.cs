using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Interface for persistent storage of stream buffers to survive service restarts.
/// </summary>
public interface IPersistentBufferStore
{
    /// <summary>
    /// Persists a buffer to storage.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="messages">The messages to persist</param>
    /// <param name="metadata">Optional metadata to store with the buffer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully persisted, false otherwise</returns>
    Task<bool> PersistBufferAsync(
        string streamId,
        IEnumerable<BufferedStreamMessage> messages,
        BufferMetadata? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a persisted buffer from storage.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The persisted buffer or null if not found</returns>
    Task<PersistedBuffer?> LoadBufferAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a persisted buffer from storage.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteBufferAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all persisted buffer identifiers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of stream identifiers with persisted buffers</returns>
    Task<IReadOnlyList<string>> ListPersistedBuffersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for a persisted buffer without loading messages.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Buffer metadata or null if not found</returns>
    Task<BufferMetadata?> GetBufferMetadataAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired persisted buffers based on retention policy.
    /// </summary>
    /// <param name="retentionPeriod">How long to retain buffers</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of buffers cleaned up</returns>
    Task<int> CleanupExpiredBuffersAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets storage statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Storage statistics</returns>
    Task<PersistenceStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the persistence store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if healthy, false otherwise</returns>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a persisted buffer loaded from storage.
/// </summary>
public record PersistedBuffer
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the persisted messages.
    /// </summary>
    public required IReadOnlyList<BufferedStreamMessage> Messages { get; init; }

    /// <summary>
    /// Gets the buffer metadata.
    /// </summary>
    public required BufferMetadata Metadata { get; init; }

    /// <summary>
    /// Gets whether the buffer data is corrupted.
    /// </summary>
    public bool IsCorrupted { get; init; }

    /// <summary>
    /// Gets any corruption details if corrupted.
    /// </summary>
    public string? CorruptionDetails { get; init; }
}

/// <summary>
/// Metadata about a persisted buffer.
/// </summary>
public record BufferMetadata
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets when the buffer was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets when the buffer was last updated.
    /// </summary>
    public required DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Gets the number of messages in the buffer.
    /// </summary>
    public required int MessageCount { get; init; }

    /// <summary>
    /// Gets the total size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the first message sequence number.
    /// </summary>
    public long? FirstSequenceNumber { get; init; }

    /// <summary>
    /// Gets the last message sequence number.
    /// </summary>
    public long? LastSequenceNumber { get; init; }

    /// <summary>
    /// Gets the persistence format version.
    /// </summary>
    public required string FormatVersion { get; init; }

    /// <summary>
    /// Gets whether the buffer is compressed.
    /// </summary>
    public bool IsCompressed { get; init; }

    /// <summary>
    /// Gets the compression algorithm if compressed.
    /// </summary>
    public string? CompressionAlgorithm { get; init; }

    /// <summary>
    /// Gets whether the buffer is encrypted.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// Gets custom properties.
    /// </summary>
    public Dictionary<string, object>? CustomProperties { get; init; }

    /// <summary>
    /// Checks if the buffer has expired based on retention period.
    /// </summary>
    /// <param name="retentionPeriod">The retention period</param>
    /// <returns>True if expired, false otherwise</returns>
    public bool IsExpired(TimeSpan retentionPeriod) => 
        DateTime.UtcNow - LastUpdatedAt > retentionPeriod;
}

/// <summary>
/// Statistics about the persistence store.
/// </summary>
public record PersistenceStatistics
{
    /// <summary>
    /// Gets the total number of persisted buffers.
    /// </summary>
    public required int TotalBuffers { get; init; }

    /// <summary>
    /// Gets the total size of all buffers in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the oldest buffer timestamp.
    /// </summary>
    public DateTime? OldestBufferTimestamp { get; init; }

    /// <summary>
    /// Gets the newest buffer timestamp.
    /// </summary>
    public DateTime? NewestBufferTimestamp { get; init; }

    /// <summary>
    /// Gets the largest buffer size in bytes.
    /// </summary>
    public long LargestBufferSizeBytes { get; init; }

    /// <summary>
    /// Gets the average buffer size in bytes.
    /// </summary>
    public double AverageBufferSizeBytes => TotalBuffers > 0 
        ? (double)TotalSizeBytes / TotalBuffers 
        : 0;

    /// <summary>
    /// Gets the total number of messages across all buffers.
    /// </summary>
    public required long TotalMessages { get; init; }

    /// <summary>
    /// Gets the number of corrupted buffers.
    /// </summary>
    public int CorruptedBuffers { get; init; }

    /// <summary>
    /// Gets the storage path if file-based.
    /// </summary>
    public string? StoragePath { get; init; }

    /// <summary>
    /// Gets the available storage space in bytes.
    /// </summary>
    public long? AvailableStorageBytes { get; init; }

    /// <summary>
    /// Gets the storage utilization percentage.
    /// </summary>
    public double? StorageUtilizationPercentage => AvailableStorageBytes > 0 
        ? (double)TotalSizeBytes / AvailableStorageBytes.Value * 100 
        : null;
}