using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Detects when state reconstruction is needed for Orleans grains.
/// Implements core detection logic for identifying recovery scenarios and estimating recovery costs.
/// </summary>
public sealed class StateRecoveryDetector : IStateRecoveryDetector
{
    private readonly IEventStore _eventStore;
    private readonly ISnapshotManager _snapshotManager;
    private readonly ILogger<StateRecoveryDetector> _logger;

    /// <summary>
    /// Gets the name of this recovery detector implementation.
    /// </summary>
    public string Name => "DefaultStateRecoveryDetector";

    /// <summary>
    /// Initializes a new instance of the StateRecoveryDetector class.
    /// </summary>
    /// <param name="eventStore">The event store for accessing event streams</param>
    /// <param name="snapshotManager">The snapshot manager for snapshot operations</param>
    /// <param name="logger">The logger for diagnostic output</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
    public StateRecoveryDetector(
        IEventStore eventStore,
        ISnapshotManager snapshotManager,
        ILogger<StateRecoveryDetector> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines if grain state needs reconstruction during activation.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being checked</param>
    /// <param name="currentState">The current state of the grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The recovery need assessment</returns>
    public async Task<StateRecoveryNeed> DetectRecoveryNeedAsync<T>(
        string grainId,
        string grainType,
        T currentState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            _logger.LogDebug("Detecting recovery need for grain {GrainId} of type {GrainType}",
                grainId, grainType);

            // Check if state is missing or null
            if (currentState == null)
            {
                _logger.LogInformation("State is missing for grain {GrainId}, recovery needed", grainId);
                return StateRecoveryNeed.MissingState;
            }

            // Check if event stream exists
            var streamId = $"{grainType}-{grainId}";
            if (!await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                _logger.LogDebug("No event stream found for grain {GrainId}, no recovery needed", grainId);
                return StateRecoveryNeed.None;
            }

            // Get current stream version
            var streamVersion = await _eventStore.GetStreamVersionAsync(streamId, cancellationToken);

            // For basic detection, assume state has a Version property
            var stateVersion = GetStateVersion(currentState);

            if (stateVersion < streamVersion)
            {
                _logger.LogInformation(
                    "State version {StateVersion} is behind stream version {StreamVersion} for grain {GrainId}",
                    stateVersion, streamVersion, grainId);
                return StateRecoveryNeed.InconsistentState;
            }

            // Basic data integrity check
            if (!ValidateStateStructure(currentState))
            {
                _logger.LogWarning("State structure validation failed for grain {GrainId}", grainId);
                return StateRecoveryNeed.CorruptedState;
            }

            _logger.LogDebug("No recovery needed for grain {GrainId}", grainId);
            return StateRecoveryNeed.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error detecting recovery need for grain {GrainId}", grainId);
            throw new StateRecoveryDetectionException(
                $"Failed to detect recovery need for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Validates state integrity for runtime corruption detection.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The validation result with details about any issues found</returns>
    public async Task<StateValidationResult> ValidateStateIntegrityAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var issues = new List<string>();

            if (state == null)
            {
                return StateValidationResult.Invalid(
                    StateRecoveryNeed.MissingState,
                    issues);
            }

            // Basic structural validation
            if (!ValidateStateStructure(state))
            {
                issues.Add("State structure validation failed");
            }

            // Version consistency check
            var streamId = $"{grainType}-{grainId}";
            if (await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                var streamVersion = await _eventStore.GetStreamVersionAsync(streamId, cancellationToken);
                var stateVersion = GetStateVersion(state);

                if (stateVersion < 0)
                {
                    issues.Add("State version is negative");
                }
                else if (stateVersion > streamVersion)
                {
                    issues.Add($"State version {stateVersion} exceeds stream version {streamVersion}");
                }
            }

            if (issues.Count == 0)
            {
                return StateValidationResult.Valid();
            }

            var recoveryNeed = issues.Any(i => i.Contains("null") || i.Contains("structure"))
                ? StateRecoveryNeed.CorruptedState
                : StateRecoveryNeed.InconsistentState;

            return StateValidationResult.Invalid(
                recoveryNeed,
                issues,
                isStructurallyIntact: !issues.Any(i => i.Contains("structure")),
                passesBusinessRules: !issues.Any(i => i.Contains("null")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error validating state integrity for grain {GrainId}", grainId);
            throw new StateRecoveryDetectionException(
                $"Failed to validate state integrity for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Checks if the state is consistent with the event stream.
    /// </summary>
    /// <typeparam name="T">The type of grain state</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being checked</param>
    /// <param name="state">The state to check</param>
    /// <param name="expectedVersion">The expected version based on events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if state is consistent with event stream, false otherwise</returns>
    public async Task<bool> IsStateConsistentWithEventStreamAsync<T>(
        string grainId,
        string grainType,
        T state,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Suppress CS1998
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            if (state == null)
            {
                return false;
            }

            var stateVersion = GetStateVersion(state);
            return stateVersion == expectedVersion;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error checking state consistency for grain {GrainId}", grainId);
            throw new StateRecoveryDetectionException(
                $"Failed to check state consistency for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Estimates the cost of recovery for a grain based on event stream size.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Recovery cost estimation</returns>
    public async Task<RecoveryCostEstimate> EstimateRecoveryCostAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var streamId = $"{grainType}-{grainId}";

            // Check if stream exists
            if (!await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                return RecoveryCostEstimate.LowCost(50, 0, false);
            }

            // Get stream version to estimate event count
            var streamVersion = await _eventStore.GetStreamVersionAsync(streamId, cancellationToken);
            var eventCount = Math.Max(0, streamVersion + 1); // Versions are 0-based

            // Check for available snapshots
            var snapshotStats = await _snapshotManager.GetStreamStatisticsAsync(streamId, cancellationToken);
            var hasSnapshot = snapshotStats.TotalSnapshots > 0;
            var latestSnapshotVersion = hasSnapshot ? snapshotStats.LatestSnapshotVersion : (long?)null;

            // Calculate events to replay
            var eventsToReplay = hasSnapshot
                ? Math.Max(0, streamVersion - latestSnapshotVersion ?? 0)
                : eventCount;

            // Estimate time (rough calculation: 1ms per event + 100ms base overhead)
            var estimatedTimeMs = 100 + (eventsToReplay * 1.0);

            // Determine complexity
            var complexity = eventsToReplay switch
            {
                <= 100 => RecoveryComplexity.Low,
                <= 1000 => RecoveryComplexity.Medium,
                <= 10000 => RecoveryComplexity.High,
                _ => RecoveryComplexity.VeryHigh
            };

            return new RecoveryCostEstimate
            {
                EstimatedTimeMs = estimatedTimeMs,
                EventsToReplay = eventsToReplay,
                SnapshotAvailable = hasSnapshot,
                LatestSnapshotVersion = latestSnapshotVersion,
                Complexity = complexity,
                ResourceEstimate = new RecoveryResourceEstimate
                {
                    EstimatedCpuUsage = Math.Min(50.0, eventsToReplay * 0.01),
                    EstimatedMemoryUsageMB = Math.Min(500.0, eventsToReplay * 0.1),
                    EstimatedIOOperations = eventsToReplay + (hasSnapshot ? 1 : 0),
                    EstimatedNetworkUsageKB = eventsToReplay * 2.0
                },
                ConfidenceLevel = hasSnapshot ? 0.9 : 0.7
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error estimating recovery cost for grain {GrainId}", grainId);
            throw new StateRecoveryDetectionException(
                $"Failed to estimate recovery cost for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Determines if recovery should be performed based on configuration and policies.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="recoveryNeed">The detected recovery need</param>
    /// <param name="costEstimate">The estimated cost of recovery</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if recovery should be performed, false otherwise</returns>
    public async Task<bool> ShouldPerformRecoveryAsync(
        string grainId,
        string grainType,
        StateRecoveryNeed recoveryNeed,
        RecoveryCostEstimate costEstimate,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Suppress CS1998
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(costEstimate);

        try
        {
            // No recovery needed
            if (recoveryNeed == StateRecoveryNeed.None)
            {
                return false;
            }

            // Always recover missing or corrupted state
            if (recoveryNeed == StateRecoveryNeed.MissingState ||
                recoveryNeed == StateRecoveryNeed.CorruptedState)
            {
                return true;
            }

            // Basic policy: recover if cost is reasonable
            const double maxRecoveryTimeMs = 30000; // 30 seconds
            const long maxEventsToReplay = 10000;

            if (costEstimate.EstimatedTimeMs > maxRecoveryTimeMs)
            {
                _logger.LogWarning(
                    "Recovery skipped for grain {GrainId} due to high time cost: {EstimatedTimeMs}ms",
                    grainId, costEstimate.EstimatedTimeMs);
                return false;
            }

            if (costEstimate.EventsToReplay > maxEventsToReplay)
            {
                _logger.LogWarning(
                    "Recovery skipped for grain {GrainId} due to high event count: {EventsToReplay}",
                    grainId, costEstimate.EventsToReplay);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error evaluating recovery policy for grain {GrainId}", grainId);
            throw new StateRecoveryDetectionException(
                $"Failed to evaluate recovery policy for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Gets recovery detection metrics and statistics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Detection metrics and statistics</returns>
    public Task<RecoveryDetectionMetrics> GetDetectionMetricsAsync(CancellationToken cancellationToken = default)
    {
        // For core functionality, return empty metrics
        // Advanced metrics collection would be implemented in a later task
        var metrics = RecoveryDetectionMetrics.Empty();
        return Task.FromResult(metrics);
    }

    /// <summary>
    /// Extracts version information from state object using reflection or known patterns.
    /// </summary>
    /// <typeparam name="T">The type of state</typeparam>
    /// <param name="state">The state object</param>
    /// <returns>The version number, or -1 if not found</returns>
    private static long GetStateVersion<T>(T state)
    {
        if (state == null)
        {
            return -1;
        }

        // Try to get version using reflection (common property names)
        var type = typeof(T);
        var versionProperty = type.GetProperty("Version") ??
                             type.GetProperty("StateVersion") ??
                             type.GetProperty("EntityVersion");

        if (versionProperty?.PropertyType == typeof(long))
        {
            return (long)(versionProperty.GetValue(state) ?? -1L);
        }

        if (versionProperty?.PropertyType == typeof(int))
        {
            return (int)(versionProperty.GetValue(state) ?? -1);
        }

        // Default version if not found
        return 0;
    }

    /// <summary>
    /// Performs basic structural validation of state object.
    /// </summary>
    /// <typeparam name="T">The type of state</typeparam>
    /// <param name="state">The state object</param>
    /// <returns>True if structure is valid, false otherwise</returns>
    private static bool ValidateStateStructure<T>(T state)
    {
        if (state == null)
        {
            return false;
        }

        try
        {
            // Basic JSON serialization test to validate structure
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            System.Text.Json.JsonSerializer.Deserialize<T>(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}