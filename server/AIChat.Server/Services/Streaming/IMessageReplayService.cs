namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for managing message replay during stream recovery.
/// </summary>
public interface IMessageReplayService
{
    /// <summary>
    /// Adds a message to the replay buffer.
    /// </summary>
    /// <param name="message">The message to buffer</param>
    void AddMessage(ReplayMessage message);

    /// <summary>
    /// Gets messages for replay for a specific stream.
    /// </summary>
    /// <param name="streamId">Stream identifier</param>
    /// <param name="maxAgeSeconds">Maximum age of messages to replay</param>
    /// <returns>Messages ready for replay</returns>
    IEnumerable<ReplayMessage> GetMessagesForReplay(string streamId, int maxAgeSeconds);

    /// <summary>
    /// Cleans up old messages from the replay buffer.
    /// </summary>
    /// <param name="maxAgeSeconds">Maximum age to keep messages</param>
    /// <returns>Number of messages removed</returns>
    int CleanupOldMessages(int maxAgeSeconds);

    /// <summary>
    /// Gets the current size of the replay buffer.
    /// </summary>
    int BufferSize { get; }

    /// <summary>
    /// Clears all messages from the replay buffer.
    /// </summary>
    void Clear();
}

/// <summary>
/// Message to be replayed during stream recovery.
/// </summary>
public sealed class ReplayMessage
{
    public required string StreamId { get; init; }
    public required string Message { get; init; }
    public DateTime Timestamp { get; init; }
    public long SequenceNumber { get; init; }
}