using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for message buffering functionality (Phase 3).
/// Handles message buffering, TTL management, and buffer overflow scenarios.
/// This interface follows the Interface Segregation Principle by focusing solely on buffering operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserMessageBufferGrain")]
public interface IUserMessageBufferGrain : IGrainWithStringKey
{
    /// <summary>
    /// Buffers a message for delivery when connections become available.
    /// </summary>
    /// <param name="message">Message to buffer</param>
    /// <param name="priority">Priority level for the message</param>
    /// <returns>Unique buffer entry ID</returns>
    [Alias("BufferMessageAsync")]
    Task<string> BufferMessageAsync(ChatMessage message, BufferPriority priority = BufferPriority.Normal);

    /// <summary>
    /// Buffers a stream chunk for delivery when connections become available.
    /// </summary>
    /// <param name="chunk">Stream chunk to buffer</param>
    /// <param name="priority">Priority level for the chunk</param>
    /// <returns>Unique buffer entry ID</returns>
    [Alias("BufferStreamChunkAsync")]
    Task<string> BufferStreamChunkAsync(StreamChunk chunk, BufferPriority priority = BufferPriority.Normal);

    /// <summary>
    /// Retrieves buffered messages for a specific chat.
    /// </summary>
    /// <param name="chatId">Chat identifier</param>
    /// <param name="limit">Maximum number of messages to retrieve (null for all)</param>
    /// <param name="highPriorityOnly">If true, only return high-priority messages</param>
    /// <returns>Collection of buffered messages</returns>
    [Alias("GetBufferedMessagesAsync")]
    Task<IEnumerable<BufferedMessage>> GetBufferedMessagesAsync(
        string chatId,
        int? limit = null,
        bool highPriorityOnly = false);

    /// <summary>
    /// Retrieves a specific buffered message by ID.
    /// </summary>
    /// <param name="messageId">Unique message identifier</param>
    /// <returns>Buffered message if found, null otherwise</returns>
    [Alias("GetBufferedMessageAsync")]
    Task<BufferedMessage?> GetBufferedMessageAsync(string messageId);

    /// <summary>
    /// Removes a specific buffered message.
    /// </summary>
    /// <param name="messageId">Unique message identifier</param>
    /// <returns>True if message was found and removed, false otherwise</returns>
    [Alias("RemoveBufferedMessageAsync")]
    Task<bool> RemoveBufferedMessageAsync(string messageId);

    /// <summary>
    /// Clears all expired buffered messages across all chats.
    /// </summary>
    /// <returns>Number of messages removed</returns>
    [Alias("ClearExpiredBufferedMessagesAsync")]
    Task<int> ClearExpiredBufferedMessagesAsync();

    /// <summary>
    /// Gets buffer information for a specific chat.
    /// </summary>
    /// <param name="chatId">Chat identifier</param>
    /// <returns>Chat message buffer if exists, null otherwise</returns>
    [Alias("GetChatBufferAsync")]
    Task<ChatMessageBuffer?> GetChatBufferAsync(string chatId);

    /// <summary>
    /// Gets summary information about all message buffers.
    /// </summary>
    /// <returns>Dictionary mapping chat IDs to buffer summaries</returns>
    [Alias("GetBufferSummaryAsync")]
    Task<Dictionary<string, BufferSummary>> GetBufferSummaryAsync();

    /// <summary>
    /// Marks a buffered message as delivered successfully.
    /// </summary>
    /// <param name="messageId">Unique message identifier</param>
    /// <returns>True if message was found and marked as delivered, false otherwise</returns>
    [Alias("MarkMessageDeliveredAsync")]
    Task<bool> MarkMessageDeliveredAsync(string messageId);

    /// <summary>
    /// Records a delivery attempt failure for a buffered message.
    /// </summary>
    /// <param name="messageId">Unique message identifier</param>
    /// <param name="error">Error message from delivery attempt</param>
    /// <returns>True if message was found and attempt was recorded, false otherwise</returns>
    [Alias("RecordDeliveryAttemptAsync")]
    Task<bool> RecordDeliveryAttemptAsync(string messageId, string error);

    /// <summary>
    /// Processes buffered messages for delivery to a specific connection.
    /// </summary>
    /// <param name="connectionId">Target connection ID</param>
    /// <param name="chatId">Chat ID to process (null for all chats)</param>
    /// <param name="maxMessages">Maximum number of messages to process</param>
    /// <returns>Number of messages processed</returns>
    [Alias("ProcessBufferedMessagesAsync")]
    Task<int> ProcessBufferedMessagesAsync(
        string connectionId,
        string? chatId = null,
        int maxMessages = 50);

    /// <summary>
    /// Clears all buffered messages for a specific chat.
    /// </summary>
    /// <param name="chatId">Chat identifier</param>
    /// <returns>Number of messages removed</returns>
    [Alias("ClearChatBufferAsync")]
    Task<int> ClearChatBufferAsync(string chatId);
}
