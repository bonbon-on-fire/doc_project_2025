namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Service for replaying buffered messages with duplicate detection and partial message merging.
/// </summary>
public interface IBufferReplayService
{
    /// <summary>
    /// Replays buffered messages to an HTTP response stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="messages">The messages to replay</param>
    /// <param name="httpResponse">The HTTP response to write to</param>
    /// <param name="options">Replay options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Replay result with statistics</returns>
    Task<ReplayResult> ReplayMessagesAsync(
        string streamId,
        IEnumerable<BufferedStreamMessage> messages,
        HttpResponse httpResponse,
        ReplayOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a message is a duplicate based on sequence tracking.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="sequenceNumber">The message sequence number</param>
    /// <returns>True if duplicate, false otherwise</returns>
    Task<bool> IsDuplicateMessageAsync(string streamId, long sequenceNumber);

    /// <summary>
    /// Records that a message has been delivered.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="sequenceNumber">The message sequence number</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordMessageDeliveryAsync(string streamId, long sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges partial messages for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="partialMessages">The partial messages to merge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Merged complete messages</returns>
    Task<IReadOnlyList<BufferedStreamMessage>> MergePartialMessagesAsync(
        string streamId,
        IEnumerable<PartialMessage> partialMessages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the delivery tracking for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearDeliveryTrackingAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets replay statistics for a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier</param>
    /// <returns>Replay statistics or null if not tracked</returns>
    Task<ReplayStatistics?> GetReplayStatisticsAsync(string streamId);
}

/// <summary>
/// Options for message replay behavior.
/// </summary>
public record ReplayOptions
{
    /// <summary>
    /// Gets whether to skip duplicate detection.
    /// </summary>
    public bool SkipDuplicateDetection { get; init; } = false;

    /// <summary>
    /// Gets the maximum messages to replay (0 for unlimited).
    /// </summary>
    public int MaxMessages { get; init; } = 0;

    /// <summary>
    /// Gets the delay between messages during replay.
    /// </summary>
    public TimeSpan MessageDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Gets whether to include replay metadata in messages.
    /// </summary>
    public bool IncludeReplayMetadata { get; init; } = true;

    /// <summary>
    /// Gets whether to merge partial messages before replay.
    /// </summary>
    public bool MergePartialMessages { get; init; } = true;

    /// <summary>
    /// Gets the batch size for replay (0 for no batching).
    /// </summary>
    public int BatchSize { get; init; } = 10;

    /// <summary>
    /// Gets whether to validate message integrity.
    /// </summary>
    public bool ValidateIntegrity { get; init; } = true;
}

/// <summary>
/// Result of a replay operation.
/// </summary>
public record ReplayResult
{
    /// <summary>
    /// Gets whether the replay was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of messages replayed.
    /// </summary>
    public required int MessagesReplayed { get; init; }

    /// <summary>
    /// Gets the number of duplicates skipped.
    /// </summary>
    public required int DuplicatesSkipped { get; init; }

    /// <summary>
    /// Gets the number of messages that failed to replay.
    /// </summary>
    public required int MessagesFailed { get; init; }

    /// <summary>
    /// Gets the number of partial messages merged.
    /// </summary>
    public required int PartialMessagesMerged { get; init; }

    /// <summary>
    /// Gets the total bytes replayed.
    /// </summary>
    public required long BytesReplayed { get; init; }

    /// <summary>
    /// Gets the duration of the replay operation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets any error message if replay failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the sequence numbers of successfully replayed messages.
    /// </summary>
    public IReadOnlyList<long> ReplayedSequenceNumbers { get; init; } = Array.Empty<long>();

    /// <summary>
    /// Gets the throughput in messages per second.
    /// </summary>
    public double MessageThroughput => Duration.TotalSeconds > 0
        ? MessagesReplayed / Duration.TotalSeconds
        : 0;
}

/// <summary>
/// Represents a partial message that needs merging.
/// </summary>
public record PartialMessage
{
    /// <summary>
    /// Gets the message sequence number.
    /// </summary>
    public required long SequenceNumber { get; init; }

    /// <summary>
    /// Gets the chunk index for this partial message.
    /// </summary>
    public required int ChunkIndex { get; init; }

    /// <summary>
    /// Gets the total number of chunks for the complete message.
    /// </summary>
    public required int TotalChunks { get; init; }

    /// <summary>
    /// Gets the partial data.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Gets the timestamp when this partial was received.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets whether this is the last chunk.
    /// </summary>
    public bool IsLastChunk => ChunkIndex == TotalChunks - 1;
}

/// <summary>
/// Statistics about replay operations for a stream.
/// </summary>
public record ReplayStatistics
{
    /// <summary>
    /// Gets the stream identifier.
    /// </summary>
    public required string StreamId { get; init; }

    /// <summary>
    /// Gets the total number of replay operations.
    /// </summary>
    public required int TotalReplayOperations { get; init; }

    /// <summary>
    /// Gets the total messages replayed.
    /// </summary>
    public required long TotalMessagesReplayed { get; init; }

    /// <summary>
    /// Gets the total duplicates detected.
    /// </summary>
    public required long TotalDuplicatesDetected { get; init; }

    /// <summary>
    /// Gets the total partial messages merged.
    /// </summary>
    public required long TotalPartialMessagesMerged { get; init; }

    /// <summary>
    /// Gets the total bytes replayed.
    /// </summary>
    public required long TotalBytesReplayed { get; init; }

    /// <summary>
    /// Gets the last replay timestamp.
    /// </summary>
    public DateTime? LastReplayTimestamp { get; init; }

    /// <summary>
    /// Gets the average replay duration.
    /// </summary>
    public TimeSpan AverageReplayDuration { get; init; }

    /// <summary>
    /// Gets the highest sequence number delivered.
    /// </summary>
    public long HighestSequenceDelivered { get; init; }

    /// <summary>
    /// Gets the duplicate detection rate.
    /// </summary>
    public double DuplicateRate => TotalMessagesReplayed > 0
        ? (double)TotalDuplicatesDetected / (TotalMessagesReplayed + TotalDuplicatesDetected) * 100
        : 0;
}
