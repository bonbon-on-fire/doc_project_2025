using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for user activity tracking and monitoring (Phase 1).
/// Handles shadow mode activity recording, state queries, and health checks.
/// This interface follows the Interface Segregation Principle by focusing solely on activity-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IUserActivityGrain")]
public interface IUserActivityGrain : IGrainWithStringKey
{
    /// <summary>
    /// Records user activity for shadow mode tracking.
    /// </summary>
    /// <param name="type">Type of activity performed</param>
    /// <param name="metadata">JSON metadata about the activity</param>
    /// <returns>Task representing the async operation</returns>
    [Alias("RecordActivity")]
    Task RecordActivity(ActivityType type, string metadata);

    /// <summary>
    /// Gets the current state of the user grain.
    /// </summary>
    /// <returns>Current user grain state</returns>
    [Alias("GetState")]
    Task<UserGrainState> GetState();

    /// <summary>
    /// Performs a health check on the grain.
    /// </summary>
    /// <returns>Health check result with grain status</returns>
    [Alias("CheckHealth")]
    Task<HealthCheckResult> CheckHealth();
}
