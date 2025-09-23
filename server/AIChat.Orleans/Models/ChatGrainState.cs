using AIChat.Orleans.Contracts;
using Orleans;

namespace AIChat.Orleans.Models;

/// <summary>
/// Persistent state for the ChatGrain.
/// Contains all chat-specific data that needs to survive grain deactivation/reactivation.
/// Uses a hybrid approach with Orleans state for fast access and IStateManager for full persistence.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Models.ChatGrainState")]
public sealed class ChatGrainState
{
    /// <summary>
    /// Core chat metadata including ID, title, status, creation info.
    /// This is the primary state that defines the chat.
    /// </summary>
    [Id(0)]
    public ChatState ChatMetadata { get; set; } = new()
    {
        ChatId = string.Empty,
        Title = string.Empty,
        CreatedBy = string.Empty
    };

    /// <summary>
    /// Recent messages buffer for fast access (configurable size, typically last 50-100 messages).
    /// Provides immediate access to recent conversation history without external calls.
    /// Older messages are stored via IStateManager for full history access.
    /// </summary>
    [Id(1)]
    public Queue<ChatMessage> RecentMessages { get; set; } = new();

    /// <summary>
    /// Active participants in the chat.
    /// Key: ParticipantId, Value: Participant information and current presence status.
    /// Maintained in Orleans state for fast permission checks and presence updates.
    /// </summary>
    [Id(2)]
    public Dictionary<string, ChatParticipant> Participants { get; set; } = [];

    /// <summary>
    /// Currently active streams in the chat.
    /// Key: StreamId, Value: Stream state including progress and metadata.
    /// Used for real-time streaming operations and timeout management.
    /// </summary>
    [Id(3)]
    public Dictionary<string, StreamState> ActiveStreams { get; set; } = [];

    /// <summary>
    /// Message delivery status tracking for acknowledgment and read receipts.
    /// Key: MessageId, Value: Delivery status including acknowledged participants.
    /// Maintained for recent messages to support delivery confirmations.
    /// </summary>
    [Id(4)]
    public Dictionary<string, MessageStatus> MessageDeliveryStatus { get; set; } = [];

    /// <summary>
    /// Grain performance and usage metrics for monitoring and optimization.
    /// Tracks operations, timing, errors, and resource usage for this chat grain.
    /// </summary>
    [Id(5)]
    public GrainMetrics Metrics { get; set; } = new();

    /// <summary>
    /// State version for optimistic concurrency control.
    /// Incremented on each state modification to detect concurrent updates.
    /// Used to ensure state consistency across multiple operations.
    /// </summary>
    [Id(6)]
    public long StateVersion { get; set; } = 1;

    /// <summary>
    /// Grain activation timestamp for debugging and lifecycle tracking.
    /// Updated each time the grain is activated from persistent storage.
    /// </summary>
    [Id(7)]
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Configuration settings specific to this chat instance.
    /// Includes limits for message buffer size, stream timeouts, and other chat-specific settings.
    /// Allows per-chat customization of behavior and limits.
    /// </summary>
    [Id(8)]
    public ChatConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Timestamp of the last successful state persistence operation.
    /// Used for monitoring persistence health and detecting stale state.
    /// </summary>
    [Id(9)]
    public DateTime LastPersistedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current sequence number for message ordering.
    /// Ensures messages are processed and delivered in the correct order.
    /// Incremented with each new message processed by the grain.
    /// </summary>
    [Id(10)]
    public long MessageSequenceNumber { get; set; } = 0;

    /// <summary>
    /// Pending operations that need to be completed on next activation.
    /// Used for recovery scenarios and ensuring no operations are lost during grain deactivation.
    /// Key: OperationId, Value: Operation context for recovery.
    /// </summary>
    [Id(11)]
    public Dictionary<string, PendingOperation> PendingOperations { get; set; } = [];

    /// <summary>
    /// Last sequence number that was successfully processed in order.
    /// Used to detect sequence gaps and maintain message ordering integrity.
    /// Messages with sequence numbers higher than this may be queued if lower numbers are missing.
    /// </summary>
    [Id(12)]
    public long LastProcessedSequenceNumber { get; set; } = 0;

    /// <summary>
    /// Queue for messages that arrive out of sequence order.
    /// Messages are held here until the missing sequence numbers are received or timeout.
    /// Ordered by sequence number to enable efficient processing when gaps are filled.
    /// </summary>
    [Id(13)]
    public SortedDictionary<long, ChatMessage> OutOfOrderMessageQueue { get; set; } = [];

    /// <summary>
    /// Tracks sequence numbers that are missing and when they should timeout.
    /// Key: Missing sequence number, Value: Timeout timestamp for recovery.
    /// Used to detect permanently missing messages and skip forward in sequence.
    /// </summary>
    [Id(14)]
    public Dictionary<long, DateTime> SequenceGapTimeouts { get; set; } = [];

    /// <summary>
    /// Configuration settings for message sequencing behavior.
    /// Controls timeouts, queue sizes, and recovery policies for sequence management.
    /// </summary>
    [Id(15)]
    public SequenceProcessingConfiguration SequenceConfig { get; set; } = new();

    /// <summary>
    /// Increments the state version for optimistic concurrency control.
    /// Should be called whenever the state is modified.
    /// </summary>
    public void IncrementVersion()
    {
        StateVersion++;
        LastPersistedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a message to the recent messages buffer.
    /// Maintains the buffer size limit by removing oldest messages when necessary.
    /// </summary>
    /// <param name="message">The message to add to the buffer</param>
    public void AddRecentMessage(ChatMessage message)
    {
        RecentMessages.Enqueue(message);

        // Maintain buffer size limit (default 100 messages)
        while (RecentMessages.Count > Configuration.RecentMessageBufferSize)
        {
            RecentMessages.Dequeue();
        }
    }

    /// <summary>
    /// Gets the next message sequence number and increments the counter.
    /// Ensures unique, ordered sequence numbers for all messages in the chat.
    /// </summary>
    /// <returns>The next sequence number for a new message</returns>
    public long GetNextSequenceNumber()
    {
        return ++MessageSequenceNumber;
    }

    /// <summary>
    /// Checks if the chat is in a valid state for modifications.
    /// </summary>
    /// <returns>True if the chat can be modified, false if archived or deleted</returns>
    public bool CanModify()
    {
        return ChatMetadata.Status is ChatStatus.Active or ChatStatus.Initializing;
    }

    /// <summary>
    /// Determines if a message with the given sequence number can be processed immediately.
    /// Messages can be processed if they are the next expected sequence number.
    /// </summary>
    /// <param name="sequenceNumber">The sequence number to check</param>
    /// <returns>True if the message can be processed immediately</returns>
    public bool CanProcessMessageImmediately(long sequenceNumber)
    {
        return sequenceNumber == LastProcessedSequenceNumber + 1;
    }

    /// <summary>
    /// Attempts to process any queued out-of-order messages that can now be processed.
    /// Returns a list of messages that were successfully dequeued and can be processed.
    /// </summary>
    /// <returns>List of messages ready for processing in sequence order</returns>
    public List<ChatMessage> ProcessQueuedMessages()
    {
        var processedMessages = new List<ChatMessage>();

        while (OutOfOrderMessageQueue.Count > 0)
        {
            var nextExpectedSequence = LastProcessedSequenceNumber + 1;

            // Check if the next expected message is in the queue
            if (OutOfOrderMessageQueue.TryGetValue(nextExpectedSequence, out var message))
            {
                OutOfOrderMessageQueue.Remove(nextExpectedSequence);
                processedMessages.Add(message);
                LastProcessedSequenceNumber = nextExpectedSequence;

                // Remove any corresponding gap timeout
                SequenceGapTimeouts.Remove(nextExpectedSequence);
            }
            else
            {
                // No consecutive message found, stop processing
                break;
            }
        }

        return processedMessages;
    }

    /// <summary>
    /// Adds a message to the out-of-order queue and tracks any sequence gaps.
    /// </summary>
    /// <param name="message">The message to queue</param>
    /// <param name="sequenceNumber">The sequence number of the message</param>
    public void QueueOutOfOrderMessage(ChatMessage message, long sequenceNumber)
    {
        // Add message to the ordered queue
        OutOfOrderMessageQueue[sequenceNumber] = message;

        // Identify and track sequence gaps
        var expectedNext = LastProcessedSequenceNumber + 1;
        if (sequenceNumber > expectedNext)
        {
            // Track all missing sequence numbers as gaps
            for (long missing = expectedNext; missing < sequenceNumber; missing++)
            {
                if (!SequenceGapTimeouts.ContainsKey(missing))
                {
                    SequenceGapTimeouts[missing] = DateTime.UtcNow.Add(SequenceConfig.SequenceGapTimeout);
                }
            }
        }

        // Maintain queue size limits
        while (OutOfOrderMessageQueue.Count > SequenceConfig.MaxOutOfOrderQueueSize)
        {
            // Remove oldest queued message (lowest sequence number)
            var oldestKey = OutOfOrderMessageQueue.Keys.First();
            OutOfOrderMessageQueue.Remove(oldestKey);
        }
    }

    /// <summary>
    /// Gets a list of sequence numbers that have timed out and should be skipped.
    /// </summary>
    /// <returns>List of sequence numbers to skip due to timeout</returns>
    public List<long> GetTimedOutSequences()
    {
        var timedOut = new List<long>();
        var now = DateTime.UtcNow;

        foreach (var gap in SequenceGapTimeouts.ToList())
        {
            if (now > gap.Value)
            {
                timedOut.Add(gap.Key);
                SequenceGapTimeouts.Remove(gap.Key);
            }
        }

        return [.. timedOut.OrderBy(x => x)];
    }

    /// <summary>
    /// Advances the last processed sequence number to skip missing messages that have timed out.
    /// </summary>
    /// <param name="skipToSequence">The sequence number to advance to</param>
    public void SkipToSequence(long skipToSequence)
    {
        if (skipToSequence > LastProcessedSequenceNumber)
        {
            LastProcessedSequenceNumber = skipToSequence;

            // Remove any gap timeouts for sequences we're skipping
            var keysToRemove = SequenceGapTimeouts.Keys.Where(k => k <= skipToSequence).ToList();
            foreach (var key in keysToRemove)
            {
                SequenceGapTimeouts.Remove(key);
            }
        }
    }
}

/// <summary>
/// Configuration settings specific to a chat instance.
/// Allows customization of chat behavior and limits.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Models.ChatConfiguration")]
public sealed class ChatConfiguration
{
    /// <summary>
    /// Maximum number of recent messages to keep in the Orleans state buffer.
    /// Older messages are automatically moved to external storage via IStateManager.
    /// </summary>
    [Id(0)]
    public int RecentMessageBufferSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of concurrent streams allowed in the chat.
    /// Prevents resource exhaustion from too many simultaneous streams.
    /// </summary>
    [Id(1)]
    public int MaxConcurrentStreams { get; set; } = 5;

    /// <summary>
    /// Timeout in seconds for inactive streams before automatic cleanup.
    /// Streams that don't receive chunks within this time are cancelled.
    /// </summary>
    [Id(2)]
    public int StreamTimeoutSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Maximum number of participants allowed in the chat.
    /// Null means unlimited participants.
    /// </summary>
    [Id(3)]
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// Whether to enable message delivery tracking for this chat.
    /// Affects performance but provides read receipts and delivery confirmations.
    /// </summary>
    [Id(4)]
    public bool EnableDeliveryTracking { get; set; } = true;

    /// <summary>
    /// How long to keep delivery status information before cleanup.
    /// Older delivery status records are automatically removed.
    /// </summary>
    [Id(5)]
    public TimeSpan DeliveryStatusRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Whether to enable detailed metrics collection for this chat.
    /// May impact performance but provides better monitoring.
    /// </summary>
    [Id(6)]
    public bool EnableDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Interval for automatic state cleanup operations.
    /// Cleanup removes expired streams, old delivery status, etc.
    /// </summary>
    [Id(7)]
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Represents a pending operation that needs to be completed.
/// Used for recovery scenarios when operations are interrupted by grain deactivation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Models.PendingOperation")]
public sealed class PendingOperation
{
    /// <summary>
    /// Unique identifier for the operation.
    /// </summary>
    [Id(0)]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Type of operation (ProcessMessage, CompleteStream, etc.).
    /// </summary>
    [Id(1)]
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Serialized operation data needed for recovery.
    /// Contains all information necessary to retry or complete the operation.
    /// </summary>
    [Id(2)]
    public string OperationData { get; set; } = string.Empty;

    /// <summary>
    /// When the operation was created.
    /// Used for timeout and cleanup of stale operations.
    /// </summary>
    [Id(3)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of retry attempts for this operation.
    /// Used to prevent infinite retry loops.
    /// </summary>
    [Id(4)]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// When the operation should be retried next.
    /// Supports exponential backoff for failed operations.
    /// </summary>
    [Id(5)]
    public DateTime? NextRetryAt { get; set; }

    /// <summary>
    /// Last error message if the operation failed.
    /// Used for diagnostics and recovery decision making.
    /// </summary>
    [Id(6)]
    public string? LastError { get; set; }
}

/// <summary>
/// Configuration settings for message sequencing and ordering behavior.
/// Controls how out-of-order messages are handled, timeouts, and recovery policies.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Models.SequenceProcessingConfiguration")]
public sealed class SequenceProcessingConfiguration
{
    /// <summary>
    /// Maximum number of out-of-order messages to queue before dropping oldest.
    /// Prevents memory exhaustion from too many queued messages.
    /// </summary>
    [Id(0)]
    public int MaxOutOfOrderQueueSize { get; set; } = 50;

    /// <summary>
    /// How long to wait for missing messages before considering them lost.
    /// Messages missing longer than this timeout are skipped to maintain flow.
    /// </summary>
    [Id(1)]
    public TimeSpan SequenceGapTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable strict sequence ordering enforcement.
    /// When false, messages may be processed out of order for better performance.
    /// </summary>
    [Id(2)]
    public bool EnableStrictOrdering { get; set; } = true;

    /// <summary>
    /// Maximum sequence gap to tolerate before triggering recovery.
    /// Large gaps may indicate systematic issues requiring special handling.
    /// </summary>
    [Id(3)]
    public long MaxSequenceGap { get; set; } = 100;

    /// <summary>
    /// Whether to log sequence gap events for monitoring and debugging.
    /// Useful for detecting network issues or client problems.
    /// </summary>
    [Id(4)]
    public bool LogSequenceGaps { get; set; } = true;

    /// <summary>
    /// Interval for checking and processing sequence gap timeouts.
    /// More frequent checks provide better responsiveness but use more resources.
    /// </summary>
    [Id(5)]
    public TimeSpan GapProcessingInterval { get; set; } = TimeSpan.FromSeconds(5);
}