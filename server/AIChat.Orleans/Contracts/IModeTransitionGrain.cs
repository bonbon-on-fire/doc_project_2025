using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for mode transition management.
/// Handles mode switching, transition validation, and rollback operations.
/// This interface follows the Interface Segregation Principle by focusing solely on transition-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IModeTransitionGrain")]
public interface IModeTransitionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Transitions to a different mode.
    /// </summary>
    /// <param name="request">Mode transition request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of the transition including new state</returns>
    /// <exception cref="ModeNotFoundException">Thrown when source or target mode does not exist</exception>
    /// <exception cref="InvalidModeTransitionException">Thrown when transition is not allowed</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to transition an archived mode</exception>
    [Alias("TransitionToModeAsync")]
    Task<ModeTransitionResult> TransitionToModeAsync(ModeTransitionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the transition history for the mode.
    /// </summary>
    /// <param name="limit">Maximum number of transitions to return</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of mode transitions</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("GetTransitionHistoryAsync")]
    [ReadOnly]
    Task<List<ModeTransition>> GetTransitionHistoryAsync(int? limit = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a transition to a target mode is allowed.
    /// </summary>
    /// <param name="targetModeId">Target mode to check transition to</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Whether the transition is allowed and reason if not</returns>
    /// <exception cref="ModeNotFoundException">Thrown when current or target mode does not exist</exception>
    [Alias("CanTransitionAsync")]
    [ReadOnly]
    Task<(bool Allowed, string? Reason)> CanTransitionAsync(string targetModeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the last mode transition.
    /// </summary>
    /// <param name="reason">Reason for rollback</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>State after rollback</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="NoTransitionToRollbackException">Thrown when there's no transition to rollback</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to rollback an archived mode</exception>
    [Alias("RollbackTransitionAsync")]
    Task<ModeState> RollbackTransitionAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available transitions from the current mode.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available target modes</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("GetAvailableTransitionsAsync")]
    [ReadOnly]
    Task<List<ModeTransitionOption>> GetAvailableTransitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a mode preset which may involve multiple transitions.
    /// </summary>
    /// <param name="presetId">Preset ID to apply</param>
    /// <param name="parameters">Optional parameters for the preset</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result of applying the preset</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="PresetNotFoundException">Thrown when preset does not exist</exception>
    /// <exception cref="InvalidPresetException">Thrown when preset cannot be applied</exception>
    [Alias("ApplyPresetAsync")]
    Task<ModePresetResult> ApplyPresetAsync(string presetId, Dictionary<string, string>? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a future mode transition.
    /// </summary>
    /// <param name="request">Scheduled transition request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Scheduled transition ID</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="InvalidScheduleException">Thrown when schedule is invalid</exception>
    [Alias("ScheduleTransitionAsync")]
    Task<string> ScheduleTransitionAsync(ScheduledTransitionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a scheduled transition.
    /// </summary>
    /// <param name="scheduleId">Schedule ID to cancel</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ScheduleNotFoundException">Thrown when schedule does not exist</exception>
    [Alias("CancelScheduledTransitionAsync")]
    Task CancelScheduledTransitionAsync(string scheduleId, CancellationToken cancellationToken = default);
}
