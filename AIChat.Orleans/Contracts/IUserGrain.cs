using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface representing a user in the chat system.
/// Provides methods for user activity tracking, connection management, and message processing.
/// </summary>
public interface IUserGrain : IGrainWithStringKey
{
    /// <summary>
    /// Records user activity for shadow mode tracking.
    /// </summary>
    /// <param name="type">Type of activity performed</param>
    /// <param name="metadata">JSON metadata about the activity</param>
    /// <returns>Task representing the async operation</returns>
    Task RecordActivity(ActivityType type, string metadata);

    /// <summary>
    /// Gets the current state of the user grain.
    /// </summary>
    /// <returns>Current user grain state</returns>
    Task<UserGrainState> GetState();

    /// <summary>
    /// Performs a health check on the grain.
    /// </summary>
    /// <returns>Health check result with grain status</returns>
    Task<HealthCheckResult> CheckHealth();

    /// <summary>
    /// Registers a client connection for this user.
    /// Used in Phase 2 for SignalR integration.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="clientId">Client tab/browser identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task RegisterConnection(string connectionId, string clientId);

    /// <summary>
    /// Unregisters a client connection for this user.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task UnregisterConnection(string connectionId);

    /// <summary>
    /// Subscribes a connection to a specific chat.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task SubscribeToChat(string connectionId, string chatId);

    /// <summary>
    /// Unsubscribes a connection from a specific chat.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task UnsubscribeFromChat(string connectionId, string chatId);

    /// <summary>
    /// Relays a message to subscribed connections.
    /// Used in Phase 2 and 3 for active message routing.
    /// </summary>
    /// <param name="message">Message to relay</param>
    /// <returns>Task representing the async operation</returns>
    Task RelayMessage(ChatMessage message);

    /// <summary>
    /// Relays a streaming chunk to subscribed connections.
    /// </summary>
    /// <param name="chunk">Stream chunk to relay</param>
    /// <returns>Task representing the async operation</returns>
    Task RelayStreamChunk(StreamChunk chunk);

    /// <summary>
    /// Processes a message with background service integration (Phase 3).
    /// </summary>
    /// <param name="message">Message to process</param>
    /// <returns>Operation ID for tracking</returns>
    Task<string> ProcessMessageWithBackground(ChatMessage message);

    /// <summary>
    /// Notifies grain that an operation has started.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <param name="chatId">Chat where operation is occurring</param>
    /// <returns>Task representing the async operation</returns>
    Task NotifyOperationStarted(string operationId, string chatId);

    /// <summary>
    /// Notifies grain that an operation has completed.
    /// </summary>
    /// <param name="operationId">Unique operation identifier</param>
    /// <param name="success">Whether operation completed successfully</param>
    /// <param name="error">Error message if operation failed</param>
    /// <returns>Task representing the async operation</returns>
    Task NotifyOperationCompleted(string operationId, bool success, string? error = null);
}