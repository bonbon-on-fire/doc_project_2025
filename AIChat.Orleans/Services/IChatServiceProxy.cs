using AIChat.Orleans.Contracts;

namespace AIChat.Orleans.Services;

/// <summary>
/// Proxy interface for accessing chat processing services from Orleans grains.
/// This interface provides a way for grains to access LLM processing capabilities
/// without direct dependency on the ChatService implementation.
/// </summary>
public interface IChatServiceProxy
{
    /// <summary>
    /// Process a chat message and return streaming chunks.
    /// </summary>
    /// <param name="request">The chat request to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of stream chunks</returns>
    IAsyncEnumerable<StreamChunk> ProcessChatStreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}