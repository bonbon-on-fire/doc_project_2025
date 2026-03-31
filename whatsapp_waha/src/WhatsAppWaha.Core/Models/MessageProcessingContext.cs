namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents the context for message processing, including deduplication tracking.
/// </summary>
public sealed class MessageProcessingContext
{
    private readonly HashSet<string> _processedMessageIds = new();
    private readonly Queue<string> _messageIdQueue = new();
    private readonly int _maxProcessedIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageProcessingContext"/> class.
    /// </summary>
    /// <param name="maxProcessedIds">Maximum number of processed message IDs to keep in memory.</param>
    public MessageProcessingContext(int maxProcessedIds = 1000)
    {
        if (maxProcessedIds <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxProcessedIds), "Must be greater than 0");

        _maxProcessedIds = maxProcessedIds;
    }

    /// <summary>
    /// Gets the count of currently tracked processed message IDs.
    /// </summary>
    public int ProcessedMessageCount => _processedMessageIds.Count;

    /// <summary>
    /// Gets the maximum number of processed message IDs that can be tracked.
    /// </summary>
    public int MaxProcessedIds => _maxProcessedIds;

    /// <summary>
    /// Gets the timestamp when this context was created.
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the timestamp when this context was last used.
    /// </summary>
    public DateTime LastUsedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Checks if a message ID has been processed before.
    /// </summary>
    /// <param name="messageId">The message ID to check.</param>
    /// <returns>True if the message has been processed; otherwise false.</returns>
    public bool IsMessageProcessed(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        LastUsedAt = DateTime.UtcNow;
        return _processedMessageIds.Contains(messageId);
    }

    /// <summary>
    /// Marks a message ID as processed.
    /// </summary>
    /// <param name="messageId">The message ID to mark as processed.</param>
    /// <returns>True if the message was newly marked as processed; false if it was already processed.</returns>
    public bool MarkMessageAsProcessed(string messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        LastUsedAt = DateTime.UtcNow;

        // If already processed, return false
        if (_processedMessageIds.Contains(messageId))
            return false;

        // Add to processed set
        _processedMessageIds.Add(messageId);
        _messageIdQueue.Enqueue(messageId);

        // Maintain size limit by removing oldest entries
        while (_processedMessageIds.Count > _maxProcessedIds)
        {
            if (_messageIdQueue.TryDequeue(out var oldestId))
            {
                _processedMessageIds.Remove(oldestId);
            }
        }

        return true;
    }

    /// <summary>
    /// Marks multiple message IDs as processed.
    /// </summary>
    /// <param name="messageIds">The message IDs to mark as processed.</param>
    /// <returns>The number of messages that were newly marked as processed.</returns>
    public int MarkMessagesAsProcessed(IEnumerable<string> messageIds)
    {
        if (messageIds == null)
            return 0;

        var newlyProcessedCount = 0;
        foreach (var messageId in messageIds)
        {
            if (MarkMessageAsProcessed(messageId))
            {
                newlyProcessedCount++;
            }
        }

        return newlyProcessedCount;
    }

    /// <summary>
    /// Filters a collection of messages to only include those that haven't been processed.
    /// </summary>
    /// <param name="messages">The messages to filter.</param>
    /// <returns>Only the messages that haven't been processed before.</returns>
    public IEnumerable<NtfyMessage> FilterUnprocessedMessages(IEnumerable<NtfyMessage> messages)
    {
        if (messages == null)
            return Array.Empty<NtfyMessage>();

        LastUsedAt = DateTime.UtcNow;
        return messages.Where(m => !string.IsNullOrWhiteSpace(m.Id) && !IsMessageProcessed(m.Id));
    }

    /// <summary>
    /// Clears all processed message IDs.
    /// </summary>
    public void Clear()
    {
        LastUsedAt = DateTime.UtcNow;
        _processedMessageIds.Clear();
        _messageIdQueue.Clear();
    }

    /// <summary>
    /// Gets statistics about the message processing context.
    /// </summary>
    /// <returns>A dictionary containing context statistics.</returns>
    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["ProcessedMessageCount"] = ProcessedMessageCount,
            ["MaxProcessedIds"] = MaxProcessedIds,
            ["MemoryUtilization"] = (double)ProcessedMessageCount / MaxProcessedIds,
            ["CreatedAt"] = CreatedAt,
            ["LastUsedAt"] = LastUsedAt,
            ["AgeInMinutes"] = (DateTime.UtcNow - CreatedAt).TotalMinutes
        };
    }
}