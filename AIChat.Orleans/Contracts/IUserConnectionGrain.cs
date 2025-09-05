using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for user connection and subscription management (Phase 2).
/// Handles SignalR connections, chat subscriptions, and connection lifecycle operations.
/// This interface follows the Interface Segregation Principle by focusing solely on connection-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserConnectionGrain")]
public interface IUserConnectionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Registers a client connection for this user.
    /// Used in Phase 2 for SignalR integration.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="clientId">Client tab/browser identifier</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("RegisterConnection")]
    Task RegisterConnection(string connectionId, string clientId);

    /// <summary>
    /// Unregisters a client connection for this user.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("UnregisterConnection")]
    Task UnregisterConnection(string connectionId);

    /// <summary>
    /// Subscribes a connection to a specific chat.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("SubscribeToChat")]
    Task SubscribeToChat(string connectionId, string chatId);

    /// <summary>
    /// Unsubscribes a connection from a specific chat.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat room identifier</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("UnsubscribeFromChat")]
    Task UnsubscribeFromChat(string connectionId, string chatId);
}