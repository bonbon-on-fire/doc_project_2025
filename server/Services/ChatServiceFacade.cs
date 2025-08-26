namespace AIChat.Server.Services;

/// <summary>
/// Facade for ChatService that provides a simplified API and reduces coupling
/// </summary>
public class ChatServiceFacade(IChatService chatService, ILogger<ChatServiceFacade> logger) : IChatServiceFacade
{
    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
    {
        logger.LogInformation("Creating new chat for user {UserId}", request.UserId);
        return await chatService.CreateChatAsync(request);
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        logger.LogDebug("Retrieving chat {ChatId}", chatId);
        return await chatService.GetChatAsync(chatId);
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
    {
        logger.LogDebug("Retrieving chat history for user {UserId}, page {Page}", userId, page);
        return await chatService.GetChatHistoryAsync(userId, page, pageSize);
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        logger.LogInformation("Deleting chat {ChatId}", chatId);
        return await chatService.DeleteChatAsync(chatId);
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        logger.LogInformation("Sending message to chat {ChatId}", request.ChatId);
        return await chatService.SendMessageAsync(request);
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        logger.LogInformation("Preparing unified stream chat for user {UserId}", request.UserId);
        return await chatService.PrepareUnifiedStreamChatAsync(request);
    }

    public async Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation(
            "Streaming unified chat completion for user {UserId}",
            request.UserId
        );
        await chatService.StreamUnifiedChatCompletionAsync(request, cancellationToken);
    }
}
