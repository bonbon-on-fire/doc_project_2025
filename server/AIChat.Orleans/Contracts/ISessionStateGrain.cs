using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for session state management.
/// Handles session initialization, state persistence, and lifecycle operations.
/// This interface follows the Interface Segregation Principle by focusing solely on state-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.ISessionStateGrain")]
public interface ISessionStateGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initializes a new session with the provided configuration.
    /// </summary>
    /// <param name="request">Session initialization request containing configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The initialized session state</returns>
    /// <exception cref="SessionAlreadyExistsException">Thrown when a session with the same ID already exists</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="SessionValidationException">Thrown when request validation fails</exception>
    [Alias("InitializeAsync")]
    Task<SessionState> InitializeAsync(SessionInitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of the session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current session state</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetStateAsync")]
    [ReadOnly]
    Task<SessionState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates session metadata.
    /// </summary>
    /// <param name="metadata">Metadata dictionary to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated session state</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionDisconnectedException">Thrown when attempting to update a disconnected session</exception>
    [Alias("UpdateMetadataAsync")]
    Task<SessionState> UpdateMetadataAsync(Dictionary<string, string> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the session for historical reference.
    /// </summary>
    /// <param name="reason">Optional reason for archiving</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("ArchiveAsync")]
    Task ArchiveAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session history including connection events.
    /// </summary>
    /// <param name="limit">Maximum number of events to return</param>
    /// <param name="afterTimestamp">Return events after this timestamp for pagination</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of session events</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetHistoryAsync")]
    [ReadOnly]
    Task<List<SessionEvent>> GetHistoryAsync(int? limit = null, DateTime? afterTimestamp = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the session grain.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health check result with session status</returns>
    [Alias("CheckHealthAsync")]
    [ReadOnly]
    Task<SessionHealthCheck> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a snapshot of the current session state for recovery purposes.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Snapshot identifier for future restoration</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("SaveSnapshotAsync")]
    Task<string> SaveSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores session state from a previously saved snapshot.
    /// </summary>
    /// <param name="snapshotId">Identifier of the snapshot to restore</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Restored session state</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the snapshot does not exist</exception>
    [Alias("RestoreSnapshotAsync")]
    Task<SessionState> RestoreSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if a state transition is allowed.
    /// Convenience method to check if a transition would be valid without actually performing it.
    /// </summary>
    /// <param name="transition">The state transition to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the transition is valid, false otherwise</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("CanTransitionAsync")]
    [ReadOnly]
    Task<bool> CanTransitionAsync(SessionStateTransition transition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a state transition with validation and returns the operation result.
    /// Provides detailed feedback about the transition operation including timing and context.
    /// </summary>
    /// <param name="request">State transition request with details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Operation result with the new session state if successful</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionValidationException">Thrown when the transition is invalid</exception>
    [Alias("TransitionStateAsync")]
    Task<SessionOperationResult<SessionState>> TransitionStateAsync(SessionStateTransitionRequest request, CancellationToken cancellationToken = default);
}
