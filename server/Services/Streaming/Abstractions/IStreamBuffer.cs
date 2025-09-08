using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Defines the contract for stream message buffering with configurable size, TTL, and overflow strategies.
/// </summary>
public interface IStreamBuffer
{
    /// <summary>
    /// Gets the unique identifier for this buffer.
    /// </summary>
    string BufferId { get; }

    /// <summary>
    /// Gets the maximum number of messages this buffer can hold.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of messages in the buffer.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the buffer configuration.
    /// </summary>
    BufferConfiguration Configuration { get; }

    /// <summary>
    /// Adds a message to the buffer.
    /// </summary>
    /// <param name="message">The message to buffer</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was added, false if rejected due to overflow strategy</returns>
    Task<bool> AddMessageAsync(BufferedStreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all messages from the buffer without removing them.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of buffered messages in chronological order</returns>
    Task<IReadOnlyList<BufferedStreamMessage>> GetMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and removes messages from the buffer.
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages to retrieve (0 for all)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of buffered messages in chronological order</returns>
    Task<IReadOnlyList<BufferedStreamMessage>> DrainMessagesAsync(int maxMessages = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all messages from the buffer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of messages cleared</returns>
    Task<int> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired messages based on TTL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of expired messages removed</returns>
    Task<int> RemoveExpiredMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the buffer.
    /// </summary>
    /// <returns>Buffer statistics</returns>
    Task<StreamBufferStatistics> GetStatisticsAsync();
}

/// <summary>
/// Represents a buffered stream message with metadata.
/// </summary>
public record BufferedStreamMessage
{
    /// <summary>
    /// Gets the unique sequence number for this message.
    /// </summary>
    public required long SequenceNumber { get; init; }

    /// <summary>
    /// Gets the message data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Gets the timestamp when the message was buffered.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the message size in bytes.
    /// </summary>
    public required int SizeBytes { get; init; }

    /// <summary>
    /// Gets optional metadata associated with the message.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether this message has been replayed.
    /// </summary>
    public bool IsReplayed { get; set; }

    /// <summary>
    /// Gets the number of replay attempts for this message.
    /// </summary>
    public int ReplayAttempts { get; set; }

    /// <summary>
    /// Checks if the message has expired based on TTL.
    /// </summary>
    /// <param name="ttl">Time to live duration</param>
    /// <returns>True if expired, false otherwise</returns>
    public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - Timestamp > ttl;
}

/// <summary>
/// Configuration for stream buffer behavior.
/// </summary>
public record BufferConfiguration
{
    /// <summary>
    /// Gets the maximum buffer size.
    /// </summary>
    public int MaxSize { get; init; } = 100;

    /// <summary>
    /// Gets the time-to-live for buffered messages.
    /// </summary>
    public TimeSpan MessageTTL { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the overflow strategy when buffer is full.
    /// </summary>
    public OverflowStrategy OverflowStrategy { get; init; } = OverflowStrategy.DropOldest;

    /// <summary>
    /// Gets whether persistence is enabled.
    /// </summary>
    public bool EnablePersistence { get; init; } = false;

    /// <summary>
    /// Gets the persistence path if enabled.
    /// </summary>
    public string? PersistencePath { get; init; }

    /// <summary>
    /// Gets the maximum message size in bytes (0 for unlimited).
    /// </summary>
    public int MaxMessageSizeBytes { get; init; } = 1024 * 1024; // 1MB default
}


/// <summary>
/// Statistics about buffer usage.
/// </summary>
public record StreamBufferStatistics
{
    /// <summary>
    /// Gets the current message count.
    /// </summary>
    public required int MessageCount { get; init; }

    /// <summary>
    /// Gets the total size in bytes.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the oldest message timestamp.
    /// </summary>
    public DateTime? OldestMessageTimestamp { get; init; }

    /// <summary>
    /// Gets the newest message timestamp.
    /// </summary>
    public DateTime? NewestMessageTimestamp { get; init; }

    /// <summary>
    /// Gets the total messages added.
    /// </summary>
    public required long TotalMessagesAdded { get; init; }

    /// <summary>
    /// Gets the total messages dropped due to overflow.
    /// </summary>
    public required long TotalMessagesDropped { get; init; }

    /// <summary>
    /// Gets the total messages expired.
    /// </summary>
    public required long TotalMessagesExpired { get; init; }

    /// <summary>
    /// Gets the total messages replayed.
    /// </summary>
    public required long TotalMessagesReplayed { get; init; }

    /// <summary>
    /// Gets the average message size in bytes.
    /// </summary>
    public double AverageMessageSizeBytes => MessageCount > 0 ? (double)TotalSizeBytes / MessageCount : 0;
}