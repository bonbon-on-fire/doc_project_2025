using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Orchestrates state recovery workflow using Event Store and Snapshot Manager.
/// Implements recovery strategies with performance optimization focused on core functionality.
/// </summary>
public sealed class StateRecoveryOrchestrator : IStateRecoveryOrchestrator
{
    private readonly IEventStore _eventStore;
    private readonly ISnapshotManager _snapshotManager;
    private readonly ILogger<StateRecoveryOrchestrator> _logger;

    /// <summary>
    /// Gets the name of this recovery orchestrator implementation.
    /// </summary>
    public string Name => "DefaultStateRecoveryOrchestrator";

    /// <summary>
    /// Initializes a new instance of the StateRecoveryOrchestrator class.
    /// </summary>
    /// <param name="eventStore">The event store for accessing events</param>
    /// <param name="snapshotManager">The snapshot manager for snapshot operations</param>
    /// <param name="logger">The logger for diagnostic output</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
    public StateRecoveryOrchestrator(
        IEventStore eventStore,
        ISnapshotManager snapshotManager,
        ILogger<StateRecoveryOrchestrator> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Recovers grain state using optimal strategy (snapshot + events).
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request with details about what to recover</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    public async Task<StateRecoveryResult<T>> RecoverStateAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Starting state recovery for grain {GrainId} with strategy {Strategy}",
                request.GrainId, request.PreferredStrategy);

            // Determine optimal strategy if not specified
            var strategy = request.PreferredStrategy ?? await DetermineOptimalStrategyAsync(request, cancellationToken);

            // Execute recovery with the chosen strategy
            var result = await RecoverWithStrategyAsync(request, projection, strategy, cancellationToken);

            _logger.LogInformation(
                "Completed state recovery for grain {GrainId} in {ElapsedMs}ms with strategy {Strategy}",
                request.GrainId, stopwatch.ElapsedMilliseconds, strategy);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error recovering state for grain {GrainId}", request.GrainId);
            return StateRecoveryResult.CreateFailure<T>(
                $"Recovery failed: {ex.Message}",
                request.PreferredStrategy ?? RecoveryStrategy.HybridRecovery,
                stopwatch.Elapsed,
                request.CorrelationId);
        }
    }

    /// <summary>
    /// Gets available recovery strategies for a grain based on available data.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of available recovery strategies ordered by preference</returns>
    public async Task<IReadOnlyList<RecoveryStrategy>> GetAvailableStrategiesAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var strategies = new List<RecoveryStrategy>();
            var streamId = $"{grainType}-{grainId}";

            // Check if event stream exists
            var streamExists = await _eventStore.StreamExistsAsync(streamId, cancellationToken);
            if (!streamExists)
            {
                strategies.Add(RecoveryStrategy.EmptyState);
                return strategies;
            }

            // Check if snapshots are available
            var snapshotStats = await _snapshotManager.GetStreamStatisticsAsync(streamId, cancellationToken);
            var hasSnapshot = snapshotStats.TotalSnapshots > 0;

            // Add strategies based on available data
            if (hasSnapshot)
            {
                strategies.Add(RecoveryStrategy.SnapshotFirst);
                strategies.Add(RecoveryStrategy.HybridRecovery);
            }

            strategies.Add(RecoveryStrategy.FullReplay);
            strategies.Add(RecoveryStrategy.EmptyState);

            return strategies;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error getting available strategies for grain {GrainId}", grainId);
            throw new StateRecoveryOrchestrationException(
                $"Failed to get available strategies for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Recovers state using a specific recovery strategy.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="strategy">The specific recovery strategy to use</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    public async Task<StateRecoveryResult<T>> RecoverWithStrategyAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var streamId = $"{request.GrainType}-{request.GrainId}";

        try
        {
            _logger.LogDebug(
                "Executing recovery strategy {Strategy} for grain {GrainId}",
                strategy, request.GrainId);

            return strategy switch
            {
                RecoveryStrategy.SnapshotFirst => await RecoverFromSnapshotFirstAsync(
                    request, projection, streamId, stopwatch, cancellationToken),

                RecoveryStrategy.FullReplay => await RecoverFromFullReplayAsync(
                    request, projection, streamId, stopwatch, cancellationToken),

                RecoveryStrategy.HybridRecovery => await RecoverWithHybridStrategyAsync(
                    request, projection, streamId, stopwatch, cancellationToken),

                RecoveryStrategy.EmptyState => RecoverWithEmptyState(
                    request, projection, stopwatch),

                _ => throw new ArgumentException($"Unsupported recovery strategy: {strategy}", nameof(strategy))
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error executing recovery strategy {Strategy} for grain {GrainId}",
                strategy, request.GrainId);

            return StateRecoveryResult.CreateFailure<T>(
                $"Strategy {strategy} failed: {ex.Message}",
                strategy,
                stopwatch.Elapsed,
                request.CorrelationId);
        }
    }

    /// <summary>
    /// Performs a dry run of recovery to estimate time and resources without actually recovering.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The recovery request</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="strategy">The recovery strategy to simulate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Simulation results without performing actual recovery</returns>
    public async Task<RecoverySimulationResult> SimulateRecoveryAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);

        try
        {
            var streamId = $"{request.GrainType}-{request.GrainId}";

            // Check if stream exists
            if (!await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                return RecoverySimulationResult.CreateSuccess(
                    strategy, 50, 0, false);
            }

            var streamVersion = await _eventStore.GetStreamVersionAsync(streamId, cancellationToken);
            var eventCount = Math.Max(0, streamVersion + 1);

            var snapshotStats = await _snapshotManager.GetStreamStatisticsAsync(streamId, cancellationToken);
            var hasSnapshot = snapshotStats.TotalSnapshots > 0;

            var (eventsToReplay, wouldUseSnapshot, snapshotVersion) = strategy switch
            {
                RecoveryStrategy.SnapshotFirst when hasSnapshot =>
                    (Math.Max(0, streamVersion - snapshotStats.LatestSnapshotVersion), true, (long?)snapshotStats.LatestSnapshotVersion),

                RecoveryStrategy.HybridRecovery when hasSnapshot =>
                    (Math.Max(0, streamVersion - snapshotStats.LatestSnapshotVersion), true, (long?)snapshotStats.LatestSnapshotVersion),

                _ => (eventCount, false, (long?)null)
            };

            var estimatedTimeMs = 100 + (eventsToReplay * 1.0) + (wouldUseSnapshot ? 50 : 0);

            return RecoverySimulationResult.CreateSuccess(
                strategy,
                estimatedTimeMs,
                eventsToReplay,
                wouldUseSnapshot,
                snapshotVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error simulating recovery for grain {GrainId}", request.GrainId);
            return RecoverySimulationResult.CreateFailure(ex.Message, strategy);
        }
    }

    /// <summary>
    /// Validates that recovery is possible for a grain before attempting it.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain</param>
    /// <param name="strategy">The recovery strategy to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result indicating whether recovery is possible</returns>
    public async Task<RecoveryValidationResult> ValidateRecoveryPossibleAsync(
        string grainId,
        string grainType,
        RecoveryStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            var streamId = $"{grainType}-{grainId}";

            // EmptyState is always possible
            if (strategy == RecoveryStrategy.EmptyState)
            {
                return RecoveryValidationResult.Possible(strategy);
            }

            // Check if stream exists for event-based strategies
            if (!await _eventStore.StreamExistsAsync(streamId, cancellationToken))
            {
                var blockingIssues = new List<string> { "Event stream does not exist" };
                var alternatives = new List<RecoveryStrategy> { RecoveryStrategy.EmptyState };

                return RecoveryValidationResult.NotPossible(strategy, blockingIssues, alternatives);
            }

            // Validate snapshot-based strategies
            if (strategy == RecoveryStrategy.SnapshotFirst)
            {
                var snapshotStats = await _snapshotManager.GetStreamStatisticsAsync(streamId, cancellationToken);
                if (snapshotStats.TotalSnapshots == 0)
                {
                    var blockingIssues = new List<string> { "No snapshots available for SnapshotFirst strategy" };
                    var alternatives = new List<RecoveryStrategy>
                    {
                        RecoveryStrategy.FullReplay,
                        RecoveryStrategy.EmptyState
                    };

                    return RecoveryValidationResult.NotPossible(strategy, blockingIssues, alternatives);
                }
            }

            return RecoveryValidationResult.Possible(strategy);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error validating recovery possibility for grain {GrainId}", grainId);
            throw new StateRecoveryOrchestrationException(
                $"Failed to validate recovery possibility for grain {grainId}",
                grainId,
                grainType,
                ex);
        }
    }

    /// <summary>
    /// Monitors the progress of an ongoing recovery operation.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the recovery operation to monitor</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current progress and status of the recovery operation</returns>
    public Task<RecoveryProgressStatus> GetRecoveryProgressAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // For core functionality, recovery operations are synchronous
        // Advanced progress monitoring would be implemented in a later task
        var status = new RecoveryProgressStatus
        {
            CorrelationId = correlationId,
            Status = RecoveryOperationStatus.Completed,
            ProgressPercentage = 100.0,
            CurrentStep = "Recovery completed"
        };

        return Task.FromResult(status);
    }

    /// <summary>
    /// Cancels an ongoing recovery operation.
    /// </summary>
    /// <param name="correlationId">The correlation ID of the recovery operation to cancel</param>
    /// <param name="cancellationToken">Token to cancel the cancellation operation</param>
    /// <returns>Result of the cancellation attempt</returns>
    public Task<RecoveryCancellationResult> CancelRecoveryAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // For core functionality, recovery operations are synchronous and cannot be cancelled mid-operation
        var result = RecoveryCancellationResult.CreateSuccess(
            correlationId,
            RecoveryOperationStatus.Completed);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Gets recovery orchestration metrics and statistics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Orchestration metrics and statistics</returns>
    public Task<RecoveryOrchestrationMetrics> GetOrchestrationMetricsAsync(CancellationToken cancellationToken = default)
    {
        // For core functionality, return empty metrics
        var metrics = RecoveryOrchestrationMetrics.Empty();
        return Task.FromResult(metrics);
    }

    #region Private Helper Methods

    /// <summary>
    /// Determines the optimal recovery strategy based on available data.
    /// </summary>
    private async Task<RecoveryStrategy> DetermineOptimalStrategyAsync(
        StateRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var strategies = await GetAvailableStrategiesAsync(
            request.GrainId, request.GrainType, cancellationToken);

        // Return the first available strategy (they're ordered by preference)
        return strategies.FirstOrDefault(RecoveryStrategy.EmptyState);
    }

    /// <summary>
    /// Recovers state using snapshot-first strategy.
    /// </summary>
    private async Task<StateRecoveryResult<T>> RecoverFromSnapshotFirstAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        string streamId,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting snapshot-first recovery for stream {StreamId}", streamId);

        var restoreResult = await _snapshotManager.RestoreFromSnapshotAsync(
            streamId, projection, request.TargetVersion, cancellationToken);

        if (!restoreResult.Success)
        {
            throw new StateRecoveryOrchestrationException(
                $"Snapshot restoration failed: {restoreResult.Error}",
                request.GrainId,
                request.GrainType);
        }

        return StateRecoveryResult.CreateSuccess(
            restoreResult.State!,
            RecoveryType.SnapshotWithReplay,
            RecoveryStrategy.SnapshotFirst,
            restoreResult.FinalVersion,
            restoreResult.EventsReplayed,
            stopwatch.Elapsed,
            snapshotUsed: true,
            snapshotVersion: restoreResult.SnapshotVersion,
            correlationId: request.CorrelationId);
    }

    /// <summary>
    /// Recovers state using full replay strategy.
    /// </summary>
    private async Task<StateRecoveryResult<T>> RecoverFromFullReplayAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        string streamId,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting full replay recovery for stream {StreamId}", streamId);

        var replayResult = request.TargetVersion.HasValue
            ? await _eventStore.ReplayAsync(streamId, request.TargetVersion.Value, projection, cancellationToken)
            : await _eventStore.ReplayAsync(streamId, projection, cancellationToken);

        if (!replayResult.Success)
        {
            throw new StateRecoveryOrchestrationException(
                $"Event replay failed: {replayResult.Error}",
                request.GrainId,
                request.GrainType);
        }

        return StateRecoveryResult.CreateSuccess(
            replayResult.State,
            RecoveryType.FullEventReplay,
            RecoveryStrategy.FullReplay,
            replayResult.Version,
            replayResult.EventsProcessed,
            stopwatch.Elapsed,
            correlationId: request.CorrelationId);
    }

    /// <summary>
    /// Recovers state using hybrid strategy (snapshot first, fallback to full replay).
    /// </summary>
    private async Task<StateRecoveryResult<T>> RecoverWithHybridStrategyAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        string streamId,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting hybrid recovery for stream {StreamId}", streamId);

        try
        {
            // Try snapshot first
            var snapshotStats = await _snapshotManager.GetStreamStatisticsAsync(streamId, cancellationToken);
            if (snapshotStats.TotalSnapshots > 0)
            {
                return await RecoverFromSnapshotFirstAsync(request, projection, streamId, stopwatch, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot recovery failed in hybrid strategy, falling back to full replay");
        }

        // Fallback to full replay
        return await RecoverFromFullReplayAsync(request, projection, streamId, stopwatch, cancellationToken);
    }

    /// <summary>
    /// Recovers state using empty state strategy.
    /// </summary>
    private StateRecoveryResult<T> RecoverWithEmptyState<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        System.Diagnostics.Stopwatch stopwatch)
    {
        _logger.LogDebug("Using empty state recovery for grain {GrainId}", request.GrainId);

        var initialState = projection.CreateInitialState();

        return StateRecoveryResult.CreateSuccess(
            initialState,
            RecoveryType.DefaultInitialization,
            RecoveryStrategy.EmptyState,
            finalVersion: 0,
            eventsReplayed: 0,
            recoveryTime: stopwatch.Elapsed,
            correlationId: request.CorrelationId);
    }

    #endregion Private Helper Methods
}
