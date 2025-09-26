using AIChat.Server.Models.WebSocket;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Interface for managing active WebSocket sessions and their correlation with Orleans grains.
/// Provides session lifecycle management, connection tracking, and session-to-grain mapping.
/// </summary>
public interface IWebSocketSessionManager
{
    /// <summary>
    /// Creates a new WebSocket session.
    /// </summary>
    /// <param name="webSocket">The WebSocket connection</param>
    /// <param name="connectionId">Unique connection identifier</param>
    /// <param name="userId">User identifier</param>
    /// <param name="protocol">Negotiated protocol</param>
    /// <param name="metadata">Optional session metadata</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created session information</returns>
    Task<WebSocketSessionInfo> CreateSessionAsync(
        NetWebSocket webSocket,
        string connectionId,
        string userId,
        string protocol,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session information by session ID.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Session information if found, null otherwise</returns>
    Task<WebSocketSessionInfo?> GetSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session information by connection ID.
    /// </summary>
    /// <param name="connectionId">The connection identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Session information if found, null otherwise</returns>
    Task<WebSocketSessionInfo?> GetSessionByConnectionIdAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions for a specific user.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of sessions for the user</returns>
    Task<List<WebSocketSessionInfo>> GetUserSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session information.
    /// </summary>
    /// <param name="sessionInfo">The updated session information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateSessionAsync(
        WebSocketSessionInfo sessionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a heartbeat for a session.
    /// Updates the last heartbeat timestamp and connection health.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task RecordHeartbeatAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session activity timestamp.
    /// Called when messages are sent or received.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateActivityAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a session from the manager.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the session was removed, false if not found</returns>
    Task<bool> RemoveSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active sessions.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of all active sessions</returns>
    Task<List<WebSocketSessionInfo>> GetAllSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sessions filtered by protocol.
    /// </summary>
    /// <param name="protocol">The protocol to filter by</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of sessions using the specified protocol</returns>
    Task<List<WebSocketSessionInfo>> GetSessionsByProtocolAsync(
        string protocol,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up stale sessions based on inactivity thresholds.
    /// </summary>
    /// <param name="inactivityThreshold">Maximum allowed inactivity duration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Number of sessions that were cleaned up</returns>
    Task<int> CleanupStaleSessionsAsync(
        TimeSpan inactivityThreshold,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session statistics and metrics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Session management statistics</returns>
    Task<WebSocketSessionStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a session exists and is active.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the session exists and is active</returns>
    Task<bool> IsSessionActiveAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session status.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="status">The new status</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    Task UpdateSessionStatusAsync(
        string sessionId,
        WebSocketConnectionStatus status,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents statistics about WebSocket session management.
/// </summary>
public record WebSocketSessionStatistics
{
    /// <summary>
    /// Gets the total number of active sessions.
    /// </summary>
    public int TotalActiveSessions { get; init; }

    /// <summary>
    /// Gets the number of sessions by protocol.
    /// </summary>
    public Dictionary<string, int> SessionsByProtocol { get; init; } = [];

    /// <summary>
    /// Gets the number of sessions by status.
    /// </summary>
    public Dictionary<WebSocketConnectionStatus, int> SessionsByStatus { get; init; } = [];

    /// <summary>
    /// Gets the total number of sessions created since startup.
    /// </summary>
    public long TotalSessionsCreated { get; init; }

    /// <summary>
    /// Gets the total number of sessions removed since startup.
    /// </summary>
    public long TotalSessionsRemoved { get; init; }

    /// <summary>
    /// Gets the average session duration in minutes.
    /// </summary>
    public double AverageSessionDurationMinutes { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets aggregate metrics across all sessions.
    /// </summary>
    public WebSocketAggregateMetrics AggregateMetrics { get; init; } = new();
}

/// <summary>
/// Represents aggregate metrics across all WebSocket sessions.
/// </summary>
public record WebSocketAggregateMetrics
{
    /// <summary>
    /// Gets the total number of messages sent across all sessions.
    /// </summary>
    public long TotalMessagesSent { get; init; }

    /// <summary>
    /// Gets the total number of messages received across all sessions.
    /// </summary>
    public long TotalMessagesReceived { get; init; }

    /// <summary>
    /// Gets the total bytes sent across all sessions.
    /// </summary>
    public long TotalBytesSent { get; init; }

    /// <summary>
    /// Gets the total bytes received across all sessions.
    /// </summary>
    public long TotalBytesReceived { get; init; }

    /// <summary>
    /// Gets the total number of errors across all sessions.
    /// </summary>
    public long TotalErrors { get; init; }

    /// <summary>
    /// Gets the average latency across all sessions in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; init; }
}