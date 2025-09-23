using AIChat.Orleans.Models;
using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for user session management (Phase 2 - ORL-ST-P2-003).
/// Handles session state persistence, lifecycle management, and migration from ConnectionStateTracker.
/// This interface follows the Interface Segregation Principle by focusing solely on session-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserSessionGrain")]
public interface IUserSessionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a new session and adds it to the user's session collection.
    /// </summary>
    /// <param name="session">The session state to create</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with creation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when session is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when session already exists</exception>
    [Alias("CreateSession")]
    Task<StateResult<UserSessionState>> CreateSessionAsync(
        UserSessionState session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing session in the user's session collection.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="session">The updated session state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId or session is null</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("UpdateSession")]
    Task<StateResult<UserSessionState>> UpdateSessionAsync(
        string sessionId,
        UserSessionState session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the session state (lifecycle state) for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="newState">The new session lifecycle state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("UpdateSessionState")]
    Task<StateResult> UpdateSessionStateAsync(
        string sessionId,
        SessionLifecycleState newState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last activity timestamp for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="activityTime">The activity timestamp (defaults to UtcNow)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("UpdateSessionActivity")]
    Task<StateResult> UpdateSessionActivityAsync(
        string sessionId,
        DateTime? activityTime = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a connection event for a session, updating connection timestamps and state.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("RecordSessionConnection")]
    Task<StateResult> RecordSessionConnectionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a disconnection event for a session, updating disconnection timestamps and counts.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="reason">Optional reason for disconnection</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("RecordSessionDisconnection")]
    Task<StateResult> RecordSessionDisconnectionAsync(
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a reconnection attempt for a session, updating reconnection metrics.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="success">Whether the reconnection attempt was successful</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("RecordSessionReconnectionAttempt")]
    Task<StateResult> RecordSessionReconnectionAttemptAsync(
        string sessionId,
        bool success,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a session, marking it as no longer active.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="reason">Optional reason for archiving</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with archive result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("ArchiveSession")]
    Task<StateResult> ArchiveSessionAsync(
        string sessionId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session metrics for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with metrics result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId is null or empty</exception>
    [Alias("GetSessionMetrics")]
    Task<StateResult<UserSessionMetrics?>> GetSessionMetricsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session metrics for a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="metrics">The session metrics to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with update result</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId or metrics is null</exception>
    /// <exception cref="KeyNotFoundException">Thrown when session does not exist</exception>
    [Alias("UpdateSessionMetrics")]
    Task<StateResult> UpdateSessionMetricsAsync(
        string sessionId,
        UserSessionMetrics metrics,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs bulk session operations for migration scenarios.
    /// Allows importing multiple sessions at once during migration from ConnectionStateTracker.
    /// </summary>
    /// <param name="sessions">The sessions to import</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation with import results</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessions is null</exception>
    [Alias("BulkImportSessions")]
    Task<StateResult<IReadOnlyList<UserSessionState>>> BulkImportSessionsAsync(
        List<UserSessionState> sessions,
        CancellationToken cancellationToken = default);
}