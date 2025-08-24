using System.Threading;
using System.Threading.Tasks;
using AIChat.Server.Models;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services;

/// <summary>
/// Facade for ChatService that provides a simplified API and reduces coupling
/// </summary>
public class ChatServiceFacade : IChatServiceFacade
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatServiceFacade> _logger;

    public ChatServiceFacade(
        IChatService chatService,
        ILogger<ChatServiceFacade> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
    {
        _logger.LogInformation("Creating new chat for user {UserId}", request.UserId);
        return await _chatService.CreateChatAsync(request);
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        _logger.LogDebug("Retrieving chat {ChatId}", chatId);
        return await _chatService.GetChatAsync(chatId);
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
    {
        _logger.LogDebug("Retrieving chat history for user {UserId}, page {Page}", userId, page);
        return await _chatService.GetChatHistoryAsync(userId, page, pageSize);
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        _logger.LogInformation("Deleting chat {ChatId}", chatId);
        return await _chatService.DeleteChatAsync(chatId);
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        _logger.LogInformation("Sending message to chat {ChatId}", request.ChatId);
        return await _chatService.SendMessageAsync(request);
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        _logger.LogInformation("Preparing unified stream chat for user {UserId}", request.UserId);
        return await _chatService.PrepareUnifiedStreamChatAsync(request);
    }

    public async Task StreamUnifiedChatCompletionAsync(StreamChatRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Streaming unified chat completion for user {UserId}", request.UserId);
        await _chatService.StreamUnifiedChatCompletionAsync(request, cancellationToken);
    }
}