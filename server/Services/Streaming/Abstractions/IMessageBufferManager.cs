namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Manages message buffering for resilient streaming operations.
/// Provides thread-safe buffering with TTL, overflow handling, and priority support.
/// </summary>
public interface IMessageBufferManager
{
    /// <summary>
    /// Buffers a message for a specific stream.
    /// </summary>
    /// <typeparam name="T">The type of message to buffer</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="message">The message to buffer</param>
    /// <param name="priority">The message priority</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the message was buffered successfully</returns>
    Task<bool> BufferMessageAsync<T>(
        string streamId, 
        T message, 
        MessagePriority priority = MessagePriority.Normal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves buffered messages for a stream.
    /// </summary>
    /// <typeparam name="T">The type of messages</typeparam>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of buffered messages in order</returns>
    Task<IReadOnlyList<BufferedMessage<T>>> GetBufferedMessagesAsync<T>(
        string streamId,
        int maxMessages = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all buffered messages for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Number of messages cleared</returns>
    Task<int> ClearBufferAsync(string streamId);

    /// <summary>
    /// Gets the current buffer statistics for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Buffer statistics or null if stream not found</returns>
    Task<BufferStats?> GetBufferStatsAsync(string streamId);

    /// <summary>
    /// Removes expired messages from a stream's buffer.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="ttlMinutes">Time to live in minutes</param>
    /// <returns>Number of expired messages removed</returns>
    Task<int> RemoveExpiredMessagesAsync(string streamId, int ttlMinutes);

    /// <summary>
    /// Creates a buffer for a new stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="options">Buffer configuration options</param>
    /// <returns>True if buffer was created, false if it already exists</returns>
    Task<bool> CreateBufferAsync(string streamId, BufferOptions options);

    /// <summary>
    /// Removes a stream's buffer entirely.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>True if buffer was removed, false if not found</returns>
    Task<bool> RemoveBufferAsync(string streamId);

    /// <summary>
    /// Gets all active stream IDs with buffers.
    /// </summary>
    /// <returns>Collection of stream IDs</returns>
    Task<IReadOnlyList<string>> GetActiveStreamIdsAsync();
}

/// <summary>
/// Represents a buffered message with metadata.
/// </summary>
/// <typeparam name="T">The type of the message data</typeparam>
public class BufferedMessage<T>
{
    /// <summary>
    /// Gets or sets the sequence number.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Gets or sets the message data.
    /// </summary>
    public required T Data { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when buffered.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the message priority.
    /// </summary>
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;

    /// <summary>
    /// Gets or sets the size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets or sets whether this is a partial message.
    /// </summary>
    public bool IsPartial { get; init; }

    /// <summary>
    /// Gets or sets the retry count for this message.
    /// </summary>
    public int RetryCount { get; init; }
}

/// <summary>
/// Message priority levels for buffering.
/// </summary>
public enum MessagePriority
{
    /// <summary>
    /// Low priority message.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Normal priority message.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// High priority message.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical priority message.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Buffer configuration options.
/// </summary>
public class BufferOptions
{
    /// <summary>
    /// Gets or sets the maximum buffer size.
    /// </summary>
    public int MaxSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the high priority buffer size.
    /// </summary>
    public int HighPrioritySize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the overflow strategy.
    /// </summary>
    public OverflowStrategy OverflowStrategy { get; set; } = OverflowStrategy.DropOldest;

    /// <summary>
    /// Gets or sets the TTL in minutes.
    /// </summary>
    public int TTLMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether to enable compression.
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum message size in bytes.
    /// </summary>
    public long MaxMessageSizeBytes { get; set; } = 1_048_576; // 1MB
}

/// <summary>
/// Strategies for handling buffer overflow.
/// </summary>
public enum OverflowStrategy
{
    /// <summary>
    /// Drop the oldest messages.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop the newest messages.
    /// </summary>
    DropNewest,

    /// <summary>
    /// Reject new messages.
    /// </summary>
    RejectNew,

    /// <summary>
    /// Drop low priority messages first.
    /// </summary>
    DropLowPriority
}

/// <summary>
/// Statistics about a message buffer.
/// </summary>
public class BufferStats
{
    /// <summary>
    /// Gets or sets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets or sets the current message count.
    /// </summary>
    public int MessageCount { get; init; }

    /// <summary>
    /// Gets or sets the maximum capacity.
    /// </summary>
    public int MaxCapacity { get; init; }

    /// <summary>
    /// Gets or sets the utilization percentage.
    /// </summary>
    public double UtilizationPercentage { get; init; }

    /// <summary>
    /// Gets or sets the total bytes used.
    /// </summary>
    public long TotalBytesUsed { get; init; }

    /// <summary>
    /// Gets or sets the number of high priority messages.
    /// </summary>
    public int HighPriorityCount { get; init; }

    /// <summary>
    /// Gets or sets the number of dropped messages.
    /// </summary>
    public long DroppedMessageCount { get; init; }

    /// <summary>
    /// Gets or sets the oldest message age.
    /// </summary>
    public TimeSpan? OldestMessageAge { get; init; }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the last activity time.
    /// </summary>
    public DateTime LastActivityAt { get; init; }
}