using AIChat.Orleans.Contracts;

namespace AIChat.Orleans.Client.Services;

/// <summary>
/// Service interface for Orleans integration with the main application.
/// Provides abstraction layer between the application and Orleans grains.
/// </summary>
public interface IOrleansIntegrationService
{
    /// <summary>
    /// Records user activity for shadow mode tracking.
    /// Fire-and-forget operation that won't throw exceptions.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="type">Type of activity</param>
    /// <param name="data">Activity metadata object (will be JSON serialized)</param>
    /// <returns>Task representing the async operation</returns>
    Task RecordUserActivityAsync(string userId, ActivityType type, object data);

    /// <summary>
    /// Gets the current state of a user grain.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>User grain state or null if Orleans is unavailable</returns>
    Task<UserGrainState?> GetUserStateAsync(string userId);

    /// <summary>
    /// Performs a health check on Orleans cluster connectivity.
    /// </summary>
    /// <returns>True if Orleans cluster is healthy and responsive</returns>
    Task<bool> IsOrleansHealthyAsync();

    /// <summary>
    /// Performs a health check on a specific user grain.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>Health check result or null if unavailable</returns>
    Task<HealthCheckResult?> CheckUserHealthAsync(string userId);

    /// <summary>
    /// Gets connection status information for Orleans client.
    /// </summary>
    /// <returns>Connection status information</returns>
    Task<OrleansConnectionStatus> GetConnectionStatusAsync();

    #region Phase 2 Methods (Stubbed for Phase 1)

    /// <summary>
    /// Registers a SignalR connection for a user (Phase 2).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="clientId">Client tab/browser identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task RegisterConnectionAsync(string userId, string connectionId, string clientId);

    /// <summary>
    /// Unregisters a SignalR connection for a user (Phase 2).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task UnregisterConnectionAsync(string userId, string connectionId);

    /// <summary>
    /// Subscribes a connection to a chat (Phase 2).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="chatId">Chat identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task SubscribeToChatAsync(string userId, string connectionId, string chatId);

    #endregion

    #region Phase 3 Methods (Stubbed for Phase 1)

    /// <summary>
    /// Processes a message through Orleans background service (Phase 3).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="message">Message to process</param>
    /// <returns>Operation ID for tracking</returns>
    Task<string> ProcessMessageAsync(string userId, ChatMessage message);

    /// <summary>
    /// Cancels an active operation (Phase 3).
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="operationId">Operation identifier</param>
    /// <returns>Task representing the async operation</returns>
    Task CancelOperationAsync(string userId, string operationId);

    #endregion
}

/// <summary>
/// Orleans client connection status information.
/// </summary>
public class OrleansConnectionStatus
{
    /// <summary>
    /// Whether Orleans client is connected to cluster.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    public string ConnectionState { get; set; } = "Unknown";

    /// <summary>
    /// Number of active silos in cluster.
    /// </summary>
    public int ActiveSilos { get; set; }

    /// <summary>
    /// Last successful operation timestamp.
    /// </summary>
    public DateTime? LastSuccessfulOperation { get; set; }

    /// <summary>
    /// Connection establishment timestamp.
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Any connection warnings or issues.
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}