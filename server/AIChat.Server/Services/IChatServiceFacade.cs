namespace AIChat.Server.Services;

/// <summary>
/// Facade interface for ChatService that provides a simplified API
/// and reduces the number of direct dependencies
/// </summary>
public interface IChatServiceFacade
{
    /// <summary>
    /// Creates a new chat with an initial message
    /// </summary>
    Task<ChatResult> CreateChatAsync(CreateChatRequest request);

    /// <summary>
    /// Retrieves a chat by ID with all messages
    /// </summary>
    Task<ChatResult> GetChatAsync(string chatId);

    /// <summary>
    /// Retrieves chat history for a user
    /// </summary>
    Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize);

    /// <summary>
    /// Deletes a chat by ID
    /// </summary>
    Task<bool> DeleteChatAsync(string chatId);

    /// <summary>
    /// Sends a message to an existing chat
    /// </summary>
    Task<MessageResult> SendMessageAsync(SendMessageRequest request);

    /// <summary>
    /// Prepares a unified stream chat session
    /// </summary>
    Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request);

    /// <summary>
    /// Streams a unified chat completion
    /// </summary>
    Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default
    );
}
