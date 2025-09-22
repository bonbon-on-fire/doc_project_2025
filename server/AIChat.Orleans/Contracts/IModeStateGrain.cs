using AIChat.Orleans.Contracts.Attributes;
using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for mode state management and persistence.
/// Handles mode initialization, state queries, and lifecycle operations.
/// This interface follows the Interface Segregation Principle by focusing solely on state-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IModeStateGrain")]
public interface IModeStateGrain : IGrainWithStringKey
{
    /// <summary>
    /// Initializes a mode with the provided configuration.
    /// </summary>
    /// <param name="request">Mode initialization request containing configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The initialized mode state</returns>
    /// <exception cref="ModeAlreadyExistsException">Thrown when a mode with the same ID already exists</exception>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// </remarks>
    [Alias("InitializeAsync")]
    [RateLimit(10, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    [Security(requireAuthorization: true, audit: true)]
    Task<ModeState> InitializeAsync(ModeInitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of the mode.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current mode state</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// Results are cacheable for up to 30 seconds to improve performance.
    /// </remarks>
    [Alias("GetStateAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: true, durationSeconds: 30)]
    [RateLimit(100, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Minimal)]
    Task<ModeState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates mode metadata.
    /// </summary>
    /// <param name="metadata">Metadata dictionary to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Updated mode state</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to update an archived mode</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// </remarks>
    [Alias("UpdateMetadataAsync")]
    [RateLimit(20, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    [Security(requireAuthorization: true, audit: true, classification: DataClassification.Internal)]
    Task<ModeState> UpdateMetadataAsync(Dictionary<string, object> metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives the mode, making it read-only.
    /// </summary>
    /// <param name="reason">Optional reason for archiving</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="ModeArchivedException">Thrown when the mode is already archived</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// </remarks>
    [Alias("ArchiveAsync")]
    [RateLimit(5, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    [Security(requireAuthorization: true, requiredRoles: "Admin,ModeManager", audit: true)]
    Task ArchiveAsync(string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the mode to its default configuration.
    /// </summary>
    /// <param name="preserveHistory">Whether to preserve change history</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Reset mode state</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <exception cref="ModeArchivedException">Thrown when attempting to reset an archived mode</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// </remarks>
    [Alias("ResetToDefaultAsync")]
    [RateLimit(5, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Normal, includeParameters: true)]
    [Security(requireAuthorization: true, audit: true)]
    Task<ModeState> ResetToDefaultAsync(bool preserveHistory = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the mode change history.
    /// </summary>
    /// <param name="limit">Maximum number of history entries to return</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of mode change events</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// Results are cacheable for up to 60 seconds to improve performance.
    /// </remarks>
    [Alias("GetHistoryAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: true, durationSeconds: 60)]
    [RateLimit(50, 60, perUser: true)]
    [Telemetry(TelemetryLevel.Minimal)]
    [Security(requireAuthorization: true, audit: false, classification: DataClassification.Internal)]
    Task<List<ModeChangeEvent>> GetHistoryAsync(int? limit = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the mode grain.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health check result with grain status</returns>
    /// <remarks>
    /// This method should use ConfigureAwait(false) when called to avoid capturing the synchronization context.
    /// </remarks>
    [Alias("CheckHealthAsync")]
    [ReadOnly]
    [CacheHint(isCacheable: false)]  // Health checks should always be fresh
    [RateLimit(200, 60, perUser: false)]  // Global rate limit for health checks
    [Telemetry(TelemetryLevel.Minimal)]
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}