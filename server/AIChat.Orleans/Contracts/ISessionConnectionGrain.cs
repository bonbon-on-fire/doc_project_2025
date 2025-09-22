using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for session connection management.
/// Handles connection establishment, disconnection, reconnection, and connection health monitoring.
/// This interface follows the Interface Segregation Principle by focusing on connection-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.ISessionConnectionGrain")]
public interface ISessionConnectionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Establishes a new connection for the session.
    /// </summary>
    /// <param name="connectionInfo">Information about the connection to establish</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Established connection details</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionAlreadyConnectedException">Thrown when the session is already connected</exception>
    /// <exception cref="SessionLimitExceededException">Thrown when connection limits are exceeded</exception>
    [Alias("ConnectAsync")]
    Task<SessionConnection> ConnectAsync(ConnectionRequest connectionInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the current session connection.
    /// </summary>
    /// <param name="reason">Reason for disconnection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionDisconnectedException">Thrown when the session is already disconnected</exception>
    [Alias("DisconnectAsync")]
    Task DisconnectAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to reconnect a previously disconnected session.
    /// </summary>
    /// <param name="request">Reconnection request with authentication and state recovery options</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Reconnected session information</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionReconnectionException">Thrown when reconnection fails</exception>
    /// <exception cref="SessionTimeoutException">Thrown when reconnection times out</exception>
    [Alias("ReconnectAsync")]
    Task<SessionConnection> ReconnectAsync(ReconnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current connection status of the session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current connection information</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetConnectionStatusAsync")]
    [ReadOnly]
    Task<ConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a heartbeat/keepalive signal for the connection.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Heartbeat acknowledgment with latency information</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionDisconnectedException">Thrown when the session is disconnected</exception>
    [Alias("HeartbeatAsync")]
    Task<HeartbeatResponse> HeartbeatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connection metrics and statistics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Connection metrics including latency, throughput, and error rates</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetConnectionMetricsAsync")]
    [ReadOnly]
    Task<ConnectionMetrics> GetConnectionMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a connection attempt for tracking and analysis.
    /// </summary>
    /// <param name="attempt">Details of the connection attempt</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("RegisterConnectionAttemptAsync")]
    Task RegisterConnectionAttemptAsync(ConnectionAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up connection resources and releases any held resources.
    /// </summary>
    /// <param name="force">Force cleanup even if connection is active</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("CleanupConnectionAsync")]
    Task CleanupConnectionAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates connection configuration dynamically.
    /// </summary>
    /// <param name="configuration">New connection configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated connection status</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionValidationException">Thrown when configuration is invalid</exception>
    [Alias("UpdateConnectionConfigurationAsync")]
    Task<ConnectionStatus> UpdateConnectionConfigurationAsync(ConnectionConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience method to quickly connect a session with minimal configuration.
    /// Combines initialization and connection in a single call for simple scenarios.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="userId">User identifier</param>
    /// <param name="protocolType">Protocol type (e.g., "SignalR", "SSE")</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result with the connected session state</returns>
    /// <exception cref="SessionValidationException">Thrown when parameters are invalid</exception>
    [Alias("QuickConnectAsync")]
    Task<SessionOperationResult<SessionState>> QuickConnectAsync(
        string sessionId,
        string userId,
        string protocolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces immediate disconnection bypassing graceful shutdown procedures.
    /// Use only in emergency situations where normal disconnect is not responding.
    /// </summary>
    /// <param name="reason">Reason for forced disconnection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result indicating success or failure</returns>
    [Alias("ForceDisconnectAsync")]
    Task<SessionOperationResult<bool>> ForceDisconnectAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the session is ready to accept connections.
    /// Validates prerequisites and connection readiness without attempting to connect.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if ready for connection, false otherwise</returns>
    [Alias("IsReadyForConnectionAsync")]
    [ReadOnly]
    Task<bool> IsReadyForConnectionAsync(CancellationToken cancellationToken = default);
}