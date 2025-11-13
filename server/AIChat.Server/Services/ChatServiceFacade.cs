using AchieveAi.LmDotnetTools.LmCore.Agents;
using AIChat.Orleans.Client.Services;
using AIChat.Server.Storage;

namespace AIChat.Server.Services;

/// <summary>
/// Facade for ChatService that provides a simplified API and handles events
/// for backward compatibility with existing controllers
/// </summary>
public class ChatServiceFacade(
    ChatService chatService,
    ILogger<ChatServiceFacade> logger,
    IChatStorage storage,
    IStreamingAgent streamingAgent,
    IModeService modeService,
    ITaskManagerService taskManagerService,
    IToolingService toolingService,
    IOrleansIntegrationService? orleansService = null
) : IChatServiceFacade, IChatService
{
    // Events for real-time notifications (backward compatibility)
#pragma warning disable CS0067 // Event is never used - reserved for future implementations
    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    public event Func<MessageEvent, Task>? MessageReceived;
#pragma warning restore CS0067
    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
    {
        logger.LogInformation("Creating new chat for user {UserId}", request.UserId);
        return await chatService.CreateChatAsync(
            request,
            storage,
            streamingAgent,
            modeService,
            orleansService
        );
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        logger.LogDebug("Retrieving chat {ChatId}", chatId);
        return await chatService.GetChatAsync(chatId, storage, taskManagerService);
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
    {
        logger.LogDebug("Retrieving chat history for user {UserId}, page {Page}", userId, page);
        return await chatService.GetChatHistoryAsync(userId, page, pageSize, storage);
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        logger.LogInformation("Deleting chat {ChatId}", chatId);
        return await chatService.DeleteChatAsync(chatId, storage, taskManagerService);
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        logger.LogInformation("Sending message to chat {ChatId}", request.ChatId);
        return await chatService.SendMessageAsync(
            request,
            storage,
            streamingAgent,
            modeService,
            orleansService
        );
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        logger.LogInformation("Preparing unified stream chat for user {UserId}", request.UserId);
        return await chatService.PrepareUnifiedStreamChatAsync(request, storage, modeService);
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
        await chatService.StreamUnifiedChatCompletionAsync(
            request,
            storage,
            modeService,
            streamingAgent,
            toolingService,
            orleansService,
            cancellationToken
        );
    }

    /// <summary>
    /// Additional IChatService methods
    /// </summary>
    /// <param name="chatId"></param>
    /// <param name="userId"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public async Task<MessageResult> AddUserMessageToExistingChatAsync(
        string chatId,
        string userId,
        string message
    )
    {
        logger.LogInformation("Adding user message to existing chat {ChatId}", chatId);
        return await chatService.AddUserMessageToExistingChatAsync(
            chatId,
            userId,
            message,
            storage
        );
    }

    public async Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request)
    {
        logger.LogInformation("Preparing stream chat for user {UserId}", request.UserId);
        return await chatService.PrepareStreamChatAsync(request, storage, modeService);
    }

    public async Task StreamChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        logger.LogInformation("Streaming chat completion for user {UserId}", request.UserId);
        await chatService.StreamChatCompletionAsync(
            request,
            storage,
            modeService,
            streamingAgent,
            toolingService,
            orleansService,
            cancellationToken
        );
    }

    /// <summary>
    /// DEPRECATED: Phase 3 migration complete - Direct service streaming removed.
    /// Use Orleans ChatGrain via router for streaming operations.
    /// </summary>
    [Obsolete("Phase 3: Direct service streaming deprecated. Use Orleans ChatGrain.StartStreamAsync() via router.")]
    public async Task StreamAssistantResponseAsync(
        string chatId,
        CancellationToken cancellationToken = default
    )
    {
        // Phase 3: This method is deprecated - Orleans ChatGrain handles streaming
        await Task.CompletedTask;
        throw new NotSupportedException(
            "Phase 3: Direct service streaming deprecated. Use Orleans ChatGrain.StartStreamAsync() via router.");
    }

    public async Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        return await chatService.GetNextSequenceNumberAsync(chatId, storage);
    }

    public async Task<string> CreateAssistantMessageForStreamingAsync(
        string chatId,
        int sequenceNumber
    )
    {
        return await chatService.CreateAssistantMessageForStreamingAsync(
            chatId,
            sequenceNumber,
            storage
        );
    }

    public async Task<string> GetMessageContentAsync(string messageId)
    {
        return await chatService.GetMessageContentAsync(messageId, storage);
    }
}
