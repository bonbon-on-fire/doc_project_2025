using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for background operation processing and message routing (Phase 3).
/// Handles message processing, operation lifecycle, streaming chunks, and message relay operations.
/// This interface follows the Interface Segregation Principle by focusing solely on operation and message-related functionality.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserOperationGrain")]
public interface IUserOperationGrain : IGrainWithStringKey
{
    /// <summary>
    /// Processes a message with background service integration (Phase 3).
    /// </summary>
    /// <param name="message">Message to process</param>
    /// <returns>Operation ID for tracking</returns>
    [Alias("ProcessMessageWithBackground")]
    Task<string> ProcessMessageWithBackground(ChatMessage message);

    /// <summary>
    /// Notifies grain that an operation has started.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <param name="chatId">Chat where operation is occurring</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("NotifyOperationStarted")]
    Task NotifyOperationStarted(string operationId, string chatId);

    /// <summary>
    /// Notifies grain that an operation has completed.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <param name="success">Whether operation completed successfully</param>
    /// <param name="error">Error message if operation failed</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("NotifyOperationCompleted")]
    Task NotifyOperationCompleted(string operationId, bool success, string? error = null);

    /// <summary>
    /// Cancels a running operation.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <returns>True if operation was successfully cancelled, false if not found or already completed</returns>
    [Alias("CancelOperation")]
    Task<bool> CancelOperation(string operationId);

    /// <summary>
    /// Gets the status of an operation.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <returns>Operation context if found, null if not found</returns>
    [Alias("GetOperationStatus")]
    Task<OperationContext?> GetOperationStatus(string operationId);

    /// <summary>
    /// Relays a message to subscribed connections.
    /// Used in Phase 2 and 3 for active message routing.
    /// </summary>
    /// <param name="message">Message to relay</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("RelayMessage")]
    Task RelayMessage(ChatMessage message);

    /// <summary>
    /// Relays a streaming chunk to subscribed connections.
    /// </summary>
    /// <param name="chunk">Stream chunk to relay</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("RelayStreamChunk")]
    Task RelayStreamChunk(StreamChunk chunk);

    /// <summary>
    /// Processes a chat request with streaming response (Phase 4).
    /// Integrates with ChatService to provide Orleans-First message processing.
    /// </summary>
    /// <param name="request">Chat request containing message and context</param>
    /// <param name="cancellationToken">Cancellation token for stream control</param>
    /// <returns>Async enumerable of stream chunks</returns>
    [Alias("ProcessChatStreamAsync")]
    IAsyncEnumerable<StreamChunk> ProcessChatStreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}