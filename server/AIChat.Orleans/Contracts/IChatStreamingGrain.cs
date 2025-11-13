using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for chat streaming operations.
/// Handles real-time message streaming, chunks, and SSE operations.
/// This interface follows the Interface Segregation Principle by focusing solely on streaming operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IChatStreamingGrain")]
public interface IChatStreamingGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initiates a streaming message operation.
    /// </summary>
    /// <param name="message">The stream message to process</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Handle for managing the stream</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to stream to an archived chat</exception>
    /// <exception cref="PermissionDeniedException">Thrown when the sender lacks permission to stream</exception>
    [Alias("StartStreamAsync")]
    Task<StreamHandle> StartStreamAsync(StreamMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a streaming chunk.
    /// </summary>
    /// <param name="chunk">The stream chunk to process</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="StreamNotFoundException">Thrown when the stream is not found</exception>
    /// <exception cref="InvalidChatStateException">Thrown when the stream is in an invalid state for this operation</exception>
    [Alias("ProcessStreamChunkAsync")]
    Task ProcessStreamChunkAsync(StreamChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an active stream.
    /// </summary>
    /// <param name="streamId">ID of the stream to complete</param>
    /// <param name="finalContent">Optional final content for the stream</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The completed stream state</returns>
    /// <exception cref="StreamNotFoundException">Thrown when the stream is not found</exception>
    /// <exception cref="InvalidChatStateException">Thrown when the stream is already completed or cancelled</exception>
    [Alias("CompleteStreamAsync")]
    Task<StreamState> CompleteStreamAsync(string streamId, string? finalContent = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active stream.
    /// </summary>
    /// <param name="streamId">ID of the stream to cancel</param>
    /// <param name="reason">Optional cancellation reason</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="StreamNotFoundException">Thrown when the stream is not found</exception>
    /// <exception cref="InvalidChatStateException">Thrown when the stream is already completed or cancelled</exception>
    [Alias("CancelStreamAsync")]
    Task CancelStreamAsync(string streamId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a stream.
    /// </summary>
    /// <param name="streamId">ID of the stream to query</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current stream state or null if not found</returns>
    [Alias("GetStreamStateAsync")]
    [ReadOnly]
    Task<StreamState?> GetStreamStateAsync(string streamId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active streams for this chat.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of active stream states</returns>
    [Alias("GetActiveStreamsAsync")]
    [ReadOnly]
    Task<List<StreamState>> GetActiveStreamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to stream updates via Orleans streams.
    /// </summary>
    /// <param name="streamId">Orleans stream ID to subscribe to</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Stream subscription handle</returns>
    [Alias("SubscribeToStreamAsync")]
    Task<StreamSubscriptionHandle> SubscribeToStreamAsync(Guid streamId, CancellationToken cancellationToken = default);
}
