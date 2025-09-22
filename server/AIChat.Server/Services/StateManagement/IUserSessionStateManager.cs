using AIChat.Orleans.Contracts;

namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Specialized state manager for user session data within UserGrain.
/// Provides session-specific operations while leveraging the full IStateManager pattern.
/// Manages migration from ConnectionStateTracker to Orleans persistent state.
/// </summary>
public interface IUserSessionStateManager : IStateManager<UserSessionState>
{
    /// <summary>
    /// Gets the active session for a user (if any).
    /// A user can have multiple sessions, this returns the most recently active one.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The active session state, or null if no active session</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<UserSessionState?>> GetActiveSessionAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all sessions for a user, optionally filtered by state.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="state">Optional state filter</param>
    /// <param name="includeArchived">Whether to include archived sessions</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collection of user sessions</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<IReadOnlyList<UserSessionState>>> GetUserSessionsAsync(
        string userId,
        SessionLifecycleState? state = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session for a user.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="streamId">The stream identifier from ConnectionStateTracker</param>
    /// <param name="metadata">Optional session metadata</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The newly created session state</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or streamId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<UserSessionState>> CreateSessionAsync(
        string userId,
        string streamId,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last activity timestamp for a session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> UpdateSessionActivityAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a connection event for a session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> RecordConnectionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a disconnection event for a session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="reason">Optional reason for disconnection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> RecordDisconnectionAsync(
        string userId,
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a reconnection attempt for a session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="success">Whether the reconnection attempt was successful</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> RecordReconnectionAttemptAsync(
        string userId,
        string sessionId,
        bool success,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a session, marking it as no longer active.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="reason">Optional reason for archiving</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> ArchiveSessionAsync(
        string userId,
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session metrics for a specific session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Session metrics data</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId or sessionId is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<UserSessionMetrics?>> GetSessionMetricsAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session metrics for a specific session.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="metrics">The metrics to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId, sessionId, or metrics is null</exception>
    /// <exception cref="StateManagementException">Thrown when the operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> UpdateSessionMetricsAsync(
        string userId,
        string sessionId,
        UserSessionMetrics metrics,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates session data from ConnectionStateTracker to UserGrain.
    /// This is the core migration method for the ORL-ST-P2-003 task.
    /// </summary>
    /// <param name="userId">The user identifier</param>
    /// <param name="streamId">The stream identifier from ConnectionStateTracker</param>
    /// <param name="connectionStateData">The connection state data to migrate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The migrated session state</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null</exception>
    /// <exception cref="StateManagementException">Thrown when the migration fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<UserSessionState>> MigrateConnectionStateAsync(
        string userId,
        string streamId,
        object connectionStateData, // Will be ConnectionStateData from ConnectionStateTracker
        CancellationToken cancellationToken = default);
}