namespace AIChat.Server.Services.EventStore.Orleans;

/// <summary>
/// Interface for grain-level snapshot operations.
/// Integrates snapshot management with Orleans grain lifecycle and state management.
/// Provides high-level operations specifically designed for Orleans grain usage patterns.
/// </summary>
public interface IGrainSnapshotService
{
    /// <summary>
    /// Gets the name of this grain snapshot service.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes snapshot management for a grain.
    /// Called during grain activation to set up snapshot context.
    /// </summary>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier for this grain</param>
    /// <param name="configuration">Snapshot configuration for this grain type</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Initialization result</returns>
    Task<GrainSnapshotInitializationResult> InitializeForGrainAsync(
        IGrainContext grainContext,
        string streamId,
        GrainSnapshotConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to restore grain state from the latest snapshot.
    /// Called during grain activation to potentially avoid full event replay.
    /// </summary>
    /// <typeparam name="TState">The type of grain state</typeparam>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="projection">The projection logic for state reconstruction</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>State restoration result with fallback information</returns>
    Task<GrainStateRestorationResult<TState>> TryRestoreStateAsync<TState>(
        IGrainContext grainContext,
        string streamId,
        IEventProjection<TState> projection,
        CancellationToken cancellationToken = default) where TState : class;

    /// <summary>
    /// Evaluates whether a snapshot should be created based on grain state and policy.
    /// Called periodically or after significant state changes.
    /// </summary>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="currentVersion">The current version of the grain state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if a snapshot should be created</returns>
    Task<bool> ShouldCreateSnapshotForGrainAsync(
        IGrainContext grainContext,
        string streamId,
        long currentVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a snapshot of the current grain state.
    /// Optimized for grain lifecycle and state management patterns.
    /// </summary>
    /// <typeparam name="TState">The type of grain state</typeparam>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="version">The exact version this snapshot represents</param>
    /// <param name="state">The current grain state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Snapshot creation result</returns>
    Task<GrainSnapshotCreationResult> CreateSnapshotForGrainAsync<TState>(
        IGrainContext grainContext,
        string streamId,
        long version,
        TState state,
        CancellationToken cancellationToken = default) where TState : class;

    /// <summary>
    /// Schedules automatic snapshot creation using grain timers.
    /// Integrates with existing grain timer infrastructure.
    /// </summary>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="policy">The snapshot creation policy</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Timer registration result</returns>
    Task<GrainSnapshotTimerResult> ScheduleAutomaticSnapshotsAsync(
        IGrainContext grainContext,
        string streamId,
        SnapshotCreationPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles grain deactivation snapshot operations.
    /// Creates snapshots during grain deactivation if policy requires it.
    /// </summary>
    /// <typeparam name="TState">The type of grain state</typeparam>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="finalVersion">The final version of the grain state</param>
    /// <param name="finalState">The final grain state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Deactivation handling result</returns>
    Task<GrainSnapshotDeactivationResult> HandleGrainDeactivationAsync<TState>(
        IGrainContext grainContext,
        string streamId,
        long finalVersion,
        TState finalState,
        CancellationToken cancellationToken = default) where TState : class;

    /// <summary>
    /// Gets snapshot statistics for a specific grain.
    /// Useful for monitoring and optimization.
    /// </summary>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Grain-specific snapshot statistics</returns>
    Task<GrainSnapshotStatistics> GetGrainSnapshotStatisticsAsync(
        IGrainContext grainContext,
        string streamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles snapshot-related errors in grain operations.
    /// Provides graceful degradation when snapshots fail.
    /// </summary>
    /// <param name="grainContext">The grain context</param>
    /// <param name="streamId">The stream identifier</param>
    /// <param name="exception">The snapshot-related exception</param>
    /// <param name="operation">The operation that failed</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Error handling result with recovery recommendations</returns>
    Task<GrainSnapshotErrorHandlingResult> HandleSnapshotErrorAsync(
        IGrainContext grainContext,
        string streamId,
        Exception exception,
        string operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for grain-level snapshot operations.
/// </summary>
public record GrainSnapshotConfiguration
{
    /// <summary>
    /// Gets the snapshot creation policy for this grain type.
    /// </summary>
    public required SnapshotCreationPolicy CreationPolicy { get; init; }

    /// <summary>
    /// Gets the retention policy for snapshots of this grain type.
    /// </summary>
    public SnapshotRetentionPolicy? RetentionPolicy { get; init; }

    /// <summary>
    /// Gets whether to create snapshots during grain deactivation.
    /// </summary>
    public bool CreateOnDeactivation { get; init; }

    /// <summary>
    /// Gets whether to attempt state restoration from snapshots during activation.
    /// </summary>
    public bool RestoreOnActivation { get; init; } = true;

    /// <summary>
    /// Gets the maximum time to wait for snapshot operations during grain lifecycle events.
    /// </summary>
    public TimeSpan MaxSnapshotOperationTime { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets whether to fail grain activation if snapshot restoration fails.
    /// If false, will fall back to full event replay.
    /// </summary>
    public bool FailActivationOnSnapshotError { get; init; }

    /// <summary>
    /// Gets the grain type name for logging and metrics.
    /// </summary>
    public string? GrainTypeName { get; init; }

    /// <summary>
    /// Gets additional configuration metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a default configuration for a grain type.
    /// </summary>
    /// <param name="grainTypeName">The grain type name</param>
    /// <param name="eventCountThreshold">Events before creating snapshot</param>
    /// <param name="timeThreshold">Time before creating snapshot</param>
    /// <returns>Default grain snapshot configuration</returns>
    public static GrainSnapshotConfiguration CreateDefault(
        string grainTypeName,
        int eventCountThreshold = 100,
        TimeSpan? timeThreshold = null)
    {
        return new GrainSnapshotConfiguration
        {
            GrainTypeName = grainTypeName,
            CreationPolicy = SnapshotCreationPolicy.Combined(
                eventCountThreshold,
                timeThreshold ?? TimeSpan.FromHours(1)),
            RetentionPolicy = new SnapshotRetentionPolicy
            {
                MaxAge = TimeSpan.FromDays(30),
                MaxSnapshotsPerStream = 10
            },
            RestoreOnActivation = true,
            CreateOnDeactivation = false
        };
    }
}

/// <summary>
/// Result of grain snapshot initialization.
/// </summary>
public record GrainSnapshotInitializationResult
{
    /// <summary>
    /// Gets whether initialization was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the snapshot context created for the grain.
    /// </summary>
    public object? SnapshotContext { get; init; }

    /// <summary>
    /// Gets any initialization error.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets additional initialization metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a successful initialization result.
    /// </summary>
    /// <param name="snapshotContext">The created snapshot context</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>Successful initialization result</returns>
    public static GrainSnapshotInitializationResult CreateSuccess(
        object? snapshotContext = null,
        Dictionary<string, object>? metadata = null)
    {
        return new GrainSnapshotInitializationResult
        {
            Success = true,
            SnapshotContext = snapshotContext,
            Metadata = metadata ?? []
        };
    }

    /// <summary>
    /// Creates a failed initialization result.
    /// </summary>
    /// <param name="error">The initialization error</param>
    /// <returns>Failed initialization result</returns>
    public static GrainSnapshotInitializationResult CreateFailure(string error)
    {
        return new GrainSnapshotInitializationResult
        {
            Success = false,
            Error = error
        };
    }
}

/// <summary>
/// Factory methods for creating grain state restoration results.
/// </summary>
public static class GrainStateRestorationResult
{
    /// <summary>
    /// Creates a successful restoration result.
    /// </summary>
    /// <typeparam name="TState">The type of grain state</typeparam>
    /// <param name="state">The restored state</param>
    /// <param name="version">The restored version</param>
    /// <param name="metrics">Performance metrics</param>
    /// <returns>Successful restoration result</returns>
    public static GrainStateRestorationResult<TState> CreateSuccess<TState>(
        TState state,
        long version,
        RestorationMetrics? metrics = null)
        where TState : class
    {
        return new GrainStateRestorationResult<TState>
        {
            Success = true,
            RestoredState = state,
            RestoredVersion = version,
            ShouldFallbackToEventReplay = false,
            Metrics = metrics
        };
    }

    /// <summary>
    /// Creates a failed restoration result with fallback recommendation.
    /// </summary>
    /// <typeparam name="TState">The type of grain state</typeparam>
    /// <param name="error">The restoration error</param>
    /// <param name="shouldFallback">Whether to recommend event replay fallback</param>
    /// <returns>Failed restoration result</returns>
    public static GrainStateRestorationResult<TState> CreateFailure<TState>(
        string error,
        bool shouldFallback = true)
        where TState : class
    {
        return new GrainStateRestorationResult<TState>
        {
            Success = false,
            Error = error,
            ShouldFallbackToEventReplay = shouldFallback
        };
    }
}

/// <summary>
/// Result of grain state restoration from snapshot.
/// </summary>
/// <typeparam name="TState">The type of grain state</typeparam>
public record GrainStateRestorationResult<TState> where TState : class
{
    /// <summary>
    /// Gets whether restoration was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the restored state (null if restoration failed).
    /// </summary>
    public TState? RestoredState { get; init; }

    /// <summary>
    /// Gets the version of the restored state.
    /// </summary>
    public long RestoredVersion { get; init; }

    /// <summary>
    /// Gets whether full event replay is recommended as fallback.
    /// </summary>
    public bool ShouldFallbackToEventReplay { get; init; }

    /// <summary>
    /// Gets the restoration performance metrics.
    /// </summary>
    public RestorationMetrics? Metrics { get; init; }

    /// <summary>
    /// Gets any restoration error.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Performance metrics for state restoration.
/// </summary>
public record RestorationMetrics
{
    /// <summary>
    /// Gets the time taken for snapshot loading.
    /// </summary>
    public TimeSpan SnapshotLoadTime { get; init; }

    /// <summary>
    /// Gets the time taken for event replay after snapshot.
    /// </summary>
    public TimeSpan EventReplayTime { get; init; }

    /// <summary>
    /// Gets the total restoration time.
    /// </summary>
    public TimeSpan TotalRestorationTime => SnapshotLoadTime + EventReplayTime;

    /// <summary>
    /// Gets the number of events replayed after snapshot.
    /// </summary>
    public int EventsReplayed { get; init; }

    /// <summary>
    /// Gets the size of the snapshot that was loaded.
    /// </summary>
    public long SnapshotSize { get; init; }

    /// <summary>
    /// Gets the estimated time saved by using snapshot vs full replay.
    /// </summary>
    public TimeSpan EstimatedTimeSaved { get; init; }
}

/// <summary>
/// Result of grain snapshot creation.
/// </summary>
public record GrainSnapshotCreationResult
{
    /// <summary>
    /// Gets whether snapshot creation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the ID of the created snapshot.
    /// </summary>
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Gets the creation performance metrics.
    /// </summary>
    public CreationMetrics? Metrics { get; init; }

    /// <summary>
    /// Gets any creation error.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets whether the operation should be retried.
    /// </summary>
    public bool ShouldRetry { get; init; }

    /// <summary>
    /// Creates a successful creation result.
    /// </summary>
    /// <param name="snapshotId">The created snapshot ID</param>
    /// <param name="metrics">Creation metrics</param>
    /// <returns>Successful creation result</returns>
    public static GrainSnapshotCreationResult CreateSuccess(
        string snapshotId,
        CreationMetrics? metrics = null)
    {
        return new GrainSnapshotCreationResult
        {
            Success = true,
            SnapshotId = snapshotId,
            Metrics = metrics
        };
    }

    /// <summary>
    /// Creates a failed creation result.
    /// </summary>
    /// <param name="error">The creation error</param>
    /// <param name="shouldRetry">Whether the operation should be retried</param>
    /// <returns>Failed creation result</returns>
    public static GrainSnapshotCreationResult CreateFailure(
        string error,
        bool shouldRetry = false)
    {
        return new GrainSnapshotCreationResult
        {
            Success = false,
            Error = error,
            ShouldRetry = shouldRetry
        };
    }
}

/// <summary>
/// Performance metrics for snapshot creation.
/// </summary>
public record CreationMetrics
{
    /// <summary>
    /// Gets the time taken for state serialization.
    /// </summary>
    public TimeSpan SerializationTime { get; init; }

    /// <summary>
    /// Gets the time taken for compression.
    /// </summary>
    public TimeSpan CompressionTime { get; init; }

    /// <summary>
    /// Gets the time taken for storage operations.
    /// </summary>
    public TimeSpan StorageTime { get; init; }

    /// <summary>
    /// Gets the total creation time.
    /// </summary>
    public TimeSpan TotalCreationTime => SerializationTime + CompressionTime + StorageTime;

    /// <summary>
    /// Gets the size of the serialized state before compression.
    /// </summary>
    public long UncompressedSize { get; init; }

    /// <summary>
    /// Gets the size after compression.
    /// </summary>
    public long CompressedSize { get; init; }

    /// <summary>
    /// Gets the compression ratio achieved.
    /// </summary>
    public double CompressionRatio => UncompressedSize > 0 ? (double)CompressedSize / UncompressedSize : 1.0;

    /// <summary>
    /// Gets whether the snapshot was deduplicated.
    /// </summary>
    public bool WasDeduplicated { get; init; }
}

/// <summary>
/// Result of grain snapshot timer scheduling.
/// </summary>
public record GrainSnapshotTimerResult
{
    /// <summary>
    /// Gets whether timer scheduling was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the timer registration information.
    /// </summary>
    public object? TimerRegistration { get; init; }

    /// <summary>
    /// Gets the next scheduled snapshot time.
    /// </summary>
    public DateTimeOffset? NextSnapshotTime { get; init; }

    /// <summary>
    /// Gets any scheduling error.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Result of grain deactivation snapshot handling.
/// </summary>
public record GrainSnapshotDeactivationResult
{
    /// <summary>
    /// Gets whether deactivation handling was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets whether a snapshot was created during deactivation.
    /// </summary>
    public bool SnapshotCreated { get; init; }

    /// <summary>
    /// Gets the ID of the snapshot created during deactivation.
    /// </summary>
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Gets any deactivation handling error.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Grain-specific snapshot statistics.
/// </summary>
public record GrainSnapshotStatistics
{
    /// <summary>
    /// Gets the grain identifier.
    /// </summary>
    public required string GrainId { get; init; }

    /// <summary>
    /// Gets the grain type name.
    /// </summary>
    public string? GrainTypeName { get; init; }

    /// <summary>
    /// Gets the number of snapshots for this grain.
    /// </summary>
    public int SnapshotCount { get; init; }

    /// <summary>
    /// Gets the latest snapshot version.
    /// </summary>
    public long LatestSnapshotVersion { get; init; }

    /// <summary>
    /// Gets the total time saved by using snapshots.
    /// </summary>
    public TimeSpan TotalTimeSaved { get; init; }

    /// <summary>
    /// Gets the average snapshot creation time.
    /// </summary>
    public TimeSpan AverageCreationTime { get; init; }

    /// <summary>
    /// Gets the average restoration time.
    /// </summary>
    public TimeSpan AverageRestorationTime { get; init; }

    /// <summary>
    /// Gets the success rate for snapshot operations.
    /// </summary>
    public double SuccessRate { get; init; }
}

/// <summary>
/// Result of snapshot error handling in grains.
/// </summary>
public record GrainSnapshotErrorHandlingResult
{
    /// <summary>
    /// Gets whether error handling was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the recommended recovery action.
    /// </summary>
    public required string RecommendedAction { get; init; }

    /// <summary>
    /// Gets whether the grain can continue operating.
    /// </summary>
    public bool CanContinueOperation { get; init; }

    /// <summary>
    /// Gets whether snapshot operations should be disabled for this grain.
    /// </summary>
    public bool ShouldDisableSnapshots { get; init; }

    /// <summary>
    /// Gets the retry delay if retrying is recommended.
    /// </summary>
    public TimeSpan? RetryDelay { get; init; }

    /// <summary>
    /// Gets additional error handling metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}