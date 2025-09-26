using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// High-level automatic recovery service aggregating all recovery components.
/// Primary interface for Orleans grain integration providing seamless automatic state reconstruction.
/// Follows the Facade pattern to simplify the recovery system for grain consumers.
/// </summary>
public interface IAutomaticRecoveryService : IDisposable
{
    /// <summary>
    /// Gets the name of the automatic recovery service implementation for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Performs automatic recovery during grain activation if needed.
    /// This is the primary method Orleans grains will call during OnActivateAsync.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being recovered</param>
    /// <param name="currentState">The current state of the grain</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The automatic recovery result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or projection is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when automatic recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<AutomaticRecoveryResult<T>> RecoverIfNeededAsync<T>(
        string grainId,
        string grainType,
        T currentState,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs manual recovery for a grain with explicit recovery settings.
    /// Used for administrative recovery operations or specific recovery scenarios.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="request">The manual recovery request with detailed parameters</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The recovery result</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projection is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when manual recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateRecoveryResult<T>> PerformManualRecoveryAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates grain state integrity without performing recovery.
    /// Used for health checks and proactive monitoring.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The state validation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId, grainType, or state is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateValidationResult> ValidateStateAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules periodic integrity checks for active grains.
    /// Enables proactive detection of state corruption during grain operation.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain to monitor</param>
    /// <param name="interval">The interval between integrity checks</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The scheduling result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when interval is invalid</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when scheduling fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<IntegrityCheckSchedulingResult> ScheduleIntegrityCheckAsync(
        string grainId,
        string grainType,
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels scheduled integrity checks for a grain.
    /// Used when grains are deactivated or no longer need monitoring.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain to stop monitoring</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The cancellation result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or grainType is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when cancellation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<IntegrityCheckCancellationResult> CancelIntegrityCheckAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recovery statistics and metrics across all grains.
    /// Used for monitoring system health and recovery performance.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive recovery metrics</returns>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when metrics collection fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryMetrics> GetRecoveryMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recovery health status and system information.
    /// Used for health checks and operational monitoring.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Recovery service health status</returns>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when health check fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryServiceHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures recovery policies and settings for specific grain types.
    /// Allows fine-tuning of recovery behavior per grain type.
    /// </summary>
    /// <param name="grainType">The type of grain to configure</param>
    /// <param name="policy">The recovery policy configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The configuration result</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainType or policy is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when configuration fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryPolicyConfigurationResult> ConfigureRecoveryPolicyAsync(
        string grainType,
        RecoveryPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current recovery policy configuration for a grain type.
    /// </summary>
    /// <param name="grainType">The type of grain to get policy for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The current recovery policy</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainType is null</exception>
    /// <exception cref="AutomaticRecoveryServiceException">Thrown when policy retrieval fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryPolicy> GetRecoveryPolicyAsync(
        string grainType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides event notifications for recovery operations.
    /// Allows monitoring and reacting to recovery events across the system.
    /// </summary>
    event EventHandler<RecoveryEventArgs>? RecoveryEvent;
}

/// <summary>
/// Represents the result of scheduling integrity checks for a grain.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.IntegrityCheckSchedulingResult")]
public record IntegrityCheckSchedulingResult
{
    /// <summary>
    /// Whether the scheduling was successful.
    /// </summary>
    [Id(0)]
    public required bool Success { get; init; }

    /// <summary>
    /// The grain ID for which integrity checks were scheduled.
    /// </summary>
    [Id(1)]
    public required string GrainId { get; init; }

    /// <summary>
    /// The grain type for which integrity checks were scheduled.
    /// </summary>
    [Id(2)]
    public required string GrainType { get; init; }

    /// <summary>
    /// The interval between integrity checks.
    /// </summary>
    [Id(3)]
    public TimeSpan CheckInterval { get; init; }

    /// <summary>
    /// When the first integrity check is scheduled to run.
    /// </summary>
    [Id(4)]
    public DateTimeOffset NextCheckTime { get; init; }

    /// <summary>
    /// Unique identifier for the scheduled integrity check.
    /// </summary>
    [Id(5)]
    public string? ScheduleId { get; init; }

    /// <summary>
    /// Any error message if scheduling failed.
    /// </summary>
    [Id(6)]
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful scheduling result.
    /// </summary>
    /// <param name="grainId">The grain ID</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="interval">The check interval</param>
    /// <param name="nextCheckTime">When the first check will run</param>
    /// <param name="scheduleId">Unique schedule identifier</param>
    /// <returns>A successful scheduling result</returns>
    public static IntegrityCheckSchedulingResult CreateSuccess(
        string grainId,
        string grainType,
        TimeSpan interval,
        DateTimeOffset nextCheckTime,
        string scheduleId)
    {
        return new IntegrityCheckSchedulingResult
        {
            Success = true,
            GrainId = grainId,
            GrainType = grainType,
            CheckInterval = interval,
            NextCheckTime = nextCheckTime,
            ScheduleId = scheduleId
        };
    }

    /// <summary>
    /// Creates a failed scheduling result.
    /// </summary>
    /// <param name="grainId">The grain ID</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="error">The error message</param>
    /// <returns>A failed scheduling result</returns>
    public static IntegrityCheckSchedulingResult CreateFailed(string grainId, string grainType, string error)
    {
        return new IntegrityCheckSchedulingResult
        {
            Success = false,
            GrainId = grainId,
            GrainType = grainType,
            Error = error
        };
    }
}

/// <summary>
/// Represents the result of cancelling integrity checks for a grain.
/// </summary>
public record IntegrityCheckCancellationResult
{
    /// <summary>
    /// Whether the cancellation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The grain ID for which integrity checks were cancelled.
    /// </summary>
    public required string GrainId { get; init; }

    /// <summary>
    /// The grain type for which integrity checks were cancelled.
    /// </summary>
    public required string GrainType { get; init; }

    /// <summary>
    /// The schedule ID that was cancelled, if available.
    /// </summary>
    public string? CancelledScheduleId { get; init; }

    /// <summary>
    /// Any error message if cancellation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful cancellation result.
    /// </summary>
    /// <param name="grainId">The grain ID</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="cancelledScheduleId">The cancelled schedule ID</param>
    /// <returns>A successful cancellation result</returns>
    public static IntegrityCheckCancellationResult CreateSuccess(
        string grainId,
        string grainType,
        string? cancelledScheduleId = null)
    {
        return new IntegrityCheckCancellationResult
        {
            Success = true,
            GrainId = grainId,
            GrainType = grainType,
            CancelledScheduleId = cancelledScheduleId
        };
    }

    /// <summary>
    /// Creates a failed cancellation result.
    /// </summary>
    /// <param name="grainId">The grain ID</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="error">The error message</param>
    /// <returns>A failed cancellation result</returns>
    public static IntegrityCheckCancellationResult CreateFailed(string grainId, string grainType, string error)
    {
        return new IntegrityCheckCancellationResult
        {
            Success = false,
            GrainId = grainId,
            GrainType = grainType,
            Error = error
        };
    }
}

/// <summary>
/// Represents the health status of the recovery service.
/// </summary>
public record RecoveryServiceHealthStatus
{
    /// <summary>
    /// Whether the recovery service is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Whether the detection component is healthy.
    /// </summary>
    public bool DetectorHealthy { get; init; } = true;

    /// <summary>
    /// Whether the orchestration component is healthy.
    /// </summary>
    public bool OrchestratorHealthy { get; init; } = true;

    /// <summary>
    /// Whether the consistency verification component is healthy.
    /// </summary>
    public bool VerifierHealthy { get; init; } = true;

    /// <summary>
    /// Whether the Event Store integration is healthy.
    /// </summary>
    public bool EventStoreHealthy { get; init; } = true;

    /// <summary>
    /// Whether the Snapshot Manager integration is healthy.
    /// </summary>
    public bool SnapshotManagerHealthy { get; init; } = true;

    /// <summary>
    /// Number of active recovery operations.
    /// </summary>
    public int ActiveRecoveryOperations { get; init; }

    /// <summary>
    /// Number of scheduled integrity checks.
    /// </summary>
    public int ScheduledIntegrityChecks { get; init; }

    /// <summary>
    /// Service uptime duration.
    /// </summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>
    /// Health check details and messages.
    /// </summary>
    public Dictionary<string, object>? HealthDetails { get; init; }

    /// <summary>
    /// Timestamp when health status was collected.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a healthy status.
    /// </summary>
    /// <param name="uptime">Service uptime</param>
    /// <param name="activeOperations">Number of active operations</param>
    /// <param name="scheduledChecks">Number of scheduled checks</param>
    /// <returns>A healthy status</returns>
    public static RecoveryServiceHealthStatus Healthy(
        TimeSpan uptime,
        int activeOperations = 0,
        int scheduledChecks = 0)
    {
        return new RecoveryServiceHealthStatus
        {
            IsHealthy = true,
            Uptime = uptime,
            ActiveRecoveryOperations = activeOperations,
            ScheduledIntegrityChecks = scheduledChecks
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="healthDetails">Details about health issues</param>
    /// <returns>An unhealthy status</returns>
    public static RecoveryServiceHealthStatus Unhealthy(Dictionary<string, object>? healthDetails = null)
    {
        return new RecoveryServiceHealthStatus
        {
            IsHealthy = false,
            DetectorHealthy = false,
            OrchestratorHealthy = false,
            VerifierHealthy = false,
            EventStoreHealthy = false,
            SnapshotManagerHealthy = false,
            HealthDetails = healthDetails
        };
    }
}

/// <summary>
/// Represents recovery policy configuration for grain types.
/// </summary>
public record RecoveryPolicy
{
    /// <summary>
    /// Whether automatic recovery is enabled for this grain type.
    /// </summary>
    public bool AutomaticRecoveryEnabled { get; init; } = true;

    /// <summary>
    /// The preferred recovery strategy for this grain type.
    /// </summary>
    public RecoveryStrategy PreferredStrategy { get; init; } = RecoveryStrategy.HybridRecovery;

    /// <summary>
    /// Maximum time to spend on recovery operations in milliseconds.
    /// </summary>
    public double MaxRecoveryTimeMs { get; init; } = 30000; // 30 seconds

    /// <summary>
    /// Maximum number of events to replay during recovery.
    /// </summary>
    public long MaxEventsToReplay { get; init; } = 10000;

    /// <summary>
    /// Whether to enable periodic integrity checks for this grain type.
    /// </summary>
    public bool EnablePeriodicIntegrityChecks { get; init; }

    /// <summary>
    /// Default interval for periodic integrity checks.
    /// </summary>
    public TimeSpan DefaultIntegrityCheckInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Recovery retry policy configuration.
    /// </summary>
    public RecoveryRetryPolicy RetryPolicy { get; init; } = new();

    /// <summary>
    /// Custom recovery settings specific to the grain type.
    /// </summary>
    public Dictionary<string, object>? CustomSettings { get; init; }

    /// <summary>
    /// Creates a default recovery policy.
    /// </summary>
    /// <returns>A default recovery policy</returns>
    public static RecoveryPolicy Default()
    {
        return new RecoveryPolicy();
    }

    /// <summary>
    /// Creates a conservative recovery policy with longer timeouts.
    /// </summary>
    /// <returns>A conservative recovery policy</returns>
    public static RecoveryPolicy Conservative()
    {
        return new RecoveryPolicy
        {
            PreferredStrategy = RecoveryStrategy.SnapshotFirst,
            MaxRecoveryTimeMs = 60000, // 1 minute
            MaxEventsToReplay = 5000,
            EnablePeriodicIntegrityChecks = true,
            DefaultIntegrityCheckInterval = TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>
    /// Creates an aggressive recovery policy with faster recovery.
    /// </summary>
    /// <returns>An aggressive recovery policy</returns>
    public static RecoveryPolicy Aggressive()
    {
        return new RecoveryPolicy
        {
            PreferredStrategy = RecoveryStrategy.HybridRecovery,
            MaxRecoveryTimeMs = 10000, // 10 seconds
            MaxEventsToReplay = 20000,
            EnablePeriodicIntegrityChecks = true,
            DefaultIntegrityCheckInterval = TimeSpan.FromMinutes(15)
        };
    }
}

/// <summary>
/// Represents retry policy configuration for recovery operations.
/// </summary>
public record RecoveryRetryPolicy
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay between retry attempts.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retry attempts.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exponential backoff multiplier.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Whether to use jittered delays to avoid thundering herd.
    /// </summary>
    public bool UseJitter { get; init; } = true;
}

/// <summary>
/// Represents the result of configuring recovery policy.
/// </summary>
public record RecoveryPolicyConfigurationResult
{
    /// <summary>
    /// Whether the configuration was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The grain type that was configured.
    /// </summary>
    public required string GrainType { get; init; }

    /// <summary>
    /// The recovery policy that was applied.
    /// </summary>
    public RecoveryPolicy? AppliedPolicy { get; init; }

    /// <summary>
    /// Any warnings about the policy configuration.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Any error message if configuration failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful configuration result.
    /// </summary>
    /// <param name="grainType">The grain type</param>
    /// <param name="appliedPolicy">The applied policy</param>
    /// <param name="warnings">Any warnings</param>
    /// <returns>A successful configuration result</returns>
    public static RecoveryPolicyConfigurationResult CreateSuccess(
        string grainType,
        RecoveryPolicy appliedPolicy,
        IReadOnlyList<string>? warnings = null)
    {
        return new RecoveryPolicyConfigurationResult
        {
            Success = true,
            GrainType = grainType,
            AppliedPolicy = appliedPolicy,
            Warnings = warnings ?? []
        };
    }

    /// <summary>
    /// Creates a failed configuration result.
    /// </summary>
    /// <param name="grainType">The grain type</param>
    /// <param name="error">The error message</param>
    /// <returns>A failed configuration result</returns>
    public static RecoveryPolicyConfigurationResult CreateFailed(string grainType, string error)
    {
        return new RecoveryPolicyConfigurationResult
        {
            Success = false,
            GrainType = grainType,
            Error = error
        };
    }
}

/// <summary>
/// Event arguments for recovery operation events.
/// </summary>
public class RecoveryEventArgs : EventArgs
{
    /// <summary>
    /// Gets the type of recovery event.
    /// </summary>
    public RecoveryEventType EventType { get; }

    /// <summary>
    /// Gets the grain ID associated with the event.
    /// </summary>
    public string GrainId { get; }

    /// <summary>
    /// Gets the grain type associated with the event.
    /// </summary>
    public string GrainType { get; }

    /// <summary>
    /// Gets the correlation ID for tracking the recovery operation.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Gets additional event metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Gets the timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of RecoveryEventArgs.
    /// </summary>
    /// <param name="eventType">The type of recovery event</param>
    /// <param name="grainId">The grain ID</param>
    /// <param name="grainType">The grain type</param>
    /// <param name="correlationId">The correlation ID</param>
    /// <param name="metadata">Additional metadata</param>
    public RecoveryEventArgs(
        RecoveryEventType eventType,
        string grainId,
        string grainType,
        string correlationId,
        Dictionary<string, object>? metadata = null)
    {
        EventType = eventType;
        GrainId = grainId;
        GrainType = grainType;
        CorrelationId = correlationId;
        Metadata = metadata;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Types of recovery events that can be raised.
/// </summary>
public enum RecoveryEventType
{
    /// <summary>
    /// Recovery operation started.
    /// </summary>
    RecoveryStarted,

    /// <summary>
    /// Recovery operation completed successfully.
    /// </summary>
    RecoveryCompleted,

    /// <summary>
    /// Recovery operation failed.
    /// </summary>
    RecoveryFailed,

    /// <summary>
    /// Recovery operation was cancelled.
    /// </summary>
    RecoveryCancelled,

    /// <summary>
    /// State corruption detected.
    /// </summary>
    CorruptionDetected,

    /// <summary>
    /// Integrity check completed.
    /// </summary>
    IntegrityCheckCompleted,

    /// <summary>
    /// Recovery policy updated.
    /// </summary>
    PolicyUpdated
}