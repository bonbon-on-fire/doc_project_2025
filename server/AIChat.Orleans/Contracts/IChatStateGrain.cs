using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for chat state management.
/// Handles chat initialization, state queries, and persistence operations.
/// This interface follows the Interface Segregation Principle by focusing solely on state-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IChatStateGrain")]
public interface IChatStateGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initializes a new chat session with the provided configuration.
    /// </summary>
    /// <param name="request">Chat initialization request containing configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The initialized chat state</returns>
    /// <exception cref="ChatAlreadyExistsException">Thrown when a chat with the same ID already exists</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    [Alias("InitializeAsync")]
    // Transaction support will be added in implementation phase
    Task<ChatState> InitializeAsync(ChatInitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of the chat.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current chat state</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    [Alias("GetStateAsync")]
    [ReadOnly]
    Task<ChatState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates chat metadata.
    /// </summary>
    /// <param name="metadata">JSON metadata to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated chat state</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    /// <exception cref="ChatArchivedException">Thrown when attempting to update an archived chat</exception>
    [Alias("UpdateMetadataAsync")]
    Task<ChatState> UpdateMetadataAsync(string metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the chat session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    /// <exception cref="ChatArchivedException">Thrown when the chat is already archived</exception>
    [Alias("ArchiveAsync")]
    Task ArchiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chat history with optional pagination.
    /// </summary>
    /// <param name="limit">Maximum number of messages to return</param>
    /// <param name="beforeMessageId">Return messages before this message ID for pagination</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of chat messages</returns>
    /// <exception cref="ChatNotFoundException">Thrown when the chat does not exist</exception>
    [Alias("GetHistoryAsync")]
    [ReadOnly]
    Task<List<ChatMessage>> GetHistoryAsync(int? limit = null, string? beforeMessageId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the chat grain.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health check result with grain status</returns>
    [Alias("CheckHealthAsync")]
    [ReadOnly]
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}