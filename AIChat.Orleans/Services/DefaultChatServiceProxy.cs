using System.Runtime.CompilerServices;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Services;

/// <summary>
/// Default implementation of IChatServiceProxy that provides simulated responses.
/// This implementation is used when no actual ChatService is available.
/// In production, this should be replaced with an implementation that connects
/// to the actual ChatService.
/// </summary>
public class DefaultChatServiceProxy : IChatServiceProxy
{
    private readonly ILogger<DefaultChatServiceProxy> _logger;

    /// <summary>
    /// Initializes a new instance of the DefaultChatServiceProxy.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    public DefaultChatServiceProxy(ILogger<DefaultChatServiceProxy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamChunk> ProcessChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Using default ChatServiceProxy implementation. This should be replaced with actual ChatService integration. " +
            "ChatId: {ChatId}, UserId: {UserId}",
            request.ChatId, request.UserId);

        var streamId = request.RequestId ?? Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();
        var chunkIndex = 0;

        // Send initial chunk
        yield return new StreamChunk
        {
            OperationId = streamId,
            ChatId = request.ChatId,
            MessageId = messageId,
            Content = "I'm currently running in simulation mode. ",
            ChunkIndex = chunkIndex++,
            IsComplete = false,
            Type = StreamChunkType.Text
        };

        await Task.Delay(100, cancellationToken);

        // Send message content
        var responseText = $"Processing your message: \"{request.Message}\". ";
        yield return new StreamChunk
        {
            OperationId = streamId,
            ChatId = request.ChatId,
            MessageId = messageId,
            Content = responseText,
            ChunkIndex = chunkIndex++,
            IsComplete = false,
            Type = StreamChunkType.Text
        };

        await Task.Delay(100, cancellationToken);

        // Send metadata if present
        if (!string.IsNullOrEmpty(request.ModeId))
        {
            yield return new StreamChunk
            {
                OperationId = streamId,
                ChatId = request.ChatId,
                MessageId = messageId,
                Content = $"(Mode: {request.ModeId}) ",
                ChunkIndex = chunkIndex++,
                IsComplete = false,
                Type = StreamChunkType.Text
            };

            await Task.Delay(100, cancellationToken);
        }

        // Send completion
        yield return new StreamChunk
        {
            OperationId = streamId,
            ChatId = request.ChatId,
            MessageId = messageId,
            Content = "This is a simulated response from the default ChatServiceProxy.",
            ChunkIndex = chunkIndex++,
            IsComplete = false,
            Type = StreamChunkType.Text
        };

        await Task.Delay(100, cancellationToken);

        // Send completion marker
        yield return new StreamChunk
        {
            OperationId = streamId,
            ChatId = request.ChatId,
            MessageId = messageId,
            ChunkIndex = chunkIndex,
            IsComplete = true,
            TotalChunks = chunkIndex + 1,
            Type = StreamChunkType.Complete
        };

        _logger.LogInformation(
            "Completed simulated chat stream for ChatId: {ChatId}. Total chunks: {ChunkCount}",
            request.ChatId, chunkIndex + 1);
    }
}