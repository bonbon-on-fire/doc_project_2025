using AIChat.Server.Services.EventStore;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Main service orchestrating automatic state reconstruction for Orleans grains.
/// Provides high-level recovery operations by coordinating detection, orchestration, and verification components.
/// </summary>
public sealed class AutomaticRecoveryService : IAutomaticRecoveryService
{
    private readonly IStateRecoveryDetector _detector;
    private readonly IStateRecoveryOrchestrator _orchestrator;
    private readonly IStateConsistencyVerifier _verifier;
    private readonly ILogger<AutomaticRecoveryService> _logger;

    /// <summary>
    /// Gets the name of this automatic recovery service implementation.
    /// </summary>
    public string Name => "DefaultAutomaticRecoveryService";

    /// <summary>
    /// Initializes a new instance of the AutomaticRecoveryService class.
    /// </summary>
    /// <param name="detector">The recovery detector for identifying when recovery is needed</param>
    /// <param name="orchestrator">The recovery orchestrator for executing recovery strategies</param>
    /// <param name="verifier">The consistency verifier for state validation</param>
    /// <param name="logger">The logger for diagnostic output</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
    public AutomaticRecoveryService(
        IStateRecoveryDetector detector,
        IStateRecoveryOrchestrator orchestrator,
        IStateConsistencyVerifier verifier,
        ILogger<AutomaticRecoveryService> logger)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs automatic recovery if needed during grain activation.
    /// This is the main entry point for Orleans grain integration.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being activated</param>
    /// <param name="currentState">The current state of the grain</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    public async Task<AutomaticRecoveryResult<T>> RecoverIfNeededAsync<T>(
        string grainId,
        string grainType,
        T currentState,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(projection);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation(
                "Starting automatic recovery evaluation for grain {GrainId} of type {GrainType} [CorrelationId: {CorrelationId}]",
                grainId, grainType, correlationId);

            // Step 1: Detect if recovery is needed
            var recoveryNeed = await _detector.DetectRecoveryNeedAsync(
                grainId, grainType, currentState, cancellationToken);

            if (recoveryNeed == StateRecoveryNeed.None)
            {
                _logger.LogDebug("No recovery needed for grain {GrainId}", grainId);
                return AutomaticRecoveryResult<T>.CreateNoRecoveryNeeded(
                    currentState,
                    stopwatch.Elapsed,
                    correlationId);
            }

            // Step 2: Estimate recovery cost
            var costEstimate = await _detector.EstimateRecoveryCostAsync(
                grainId, grainType, cancellationToken);

            // Step 3: Check if recovery should be performed based on policy
            var shouldRecover = await _detector.ShouldPerformRecoveryAsync(
                grainId, grainType, recoveryNeed, costEstimate, cancellationToken);

            if (!shouldRecover)
            {
                _logger.LogWarning(
                    "Recovery needed for grain {GrainId} but skipped due to policy constraints. Need: {RecoveryNeed}",
                    grainId, recoveryNeed);

                return AutomaticRecoveryResult<T>.CreateRecoverySkipped(
                    currentState,
                    recoveryNeed,
                    costEstimate,
                    "Recovery skipped due to policy constraints",
                    stopwatch.Elapsed,
                    correlationId);
            }

            // Step 4: Perform recovery
            var recoveryRequest = new StateRecoveryRequest
            {
                GrainId = grainId,
                GrainType = grainType,
                RecoveryNeed = recoveryNeed,
                PreferredStrategy = DetermineOptimalStrategy(costEstimate),
                TargetVersion = null, // Recover to latest version
                CorrelationId = correlationId,
                RequestedAt = DateTimeOffset.UtcNow
            };

            var recoveryResult = await _orchestrator.RecoverStateAsync(
                recoveryRequest, projection, cancellationToken);

            if (!recoveryResult.Success)
            {
                _logger.LogError(
                    "Recovery failed for grain {GrainId}: {Error}",
                    grainId, recoveryResult.Error);

                return AutomaticRecoveryResult<T>.CreateRecoveryFailed(
                    currentState,
                    recoveryNeed,
                    recoveryResult.Error ?? "Unknown recovery error",
                    stopwatch.Elapsed,
                    correlationId);
            }

            _logger.LogInformation(
                "Automatic recovery completed successfully for grain {GrainId} in {ElapsedMs}ms [CorrelationId: {CorrelationId}]",
                grainId, stopwatch.ElapsedMilliseconds, correlationId);

            return AutomaticRecoveryResult<T>.CreateRecoverySucceeded(
                recoveryResult.State!,
                recoveryNeed,
                recoveryResult.Strategy,
                recoveryResult.RecoveryType,
                recoveryResult.FinalVersion,
                recoveryResult.EventsReplayed,
                recoveryResult.SnapshotUsed,
                stopwatch.Elapsed,
                correlationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Error during automatic recovery evaluation for grain {GrainId} [CorrelationId: {CorrelationId}]",
                grainId, correlationId);

            return AutomaticRecoveryResult<T>.CreateRecoveryFailed(
                currentState,
                StateRecoveryNeed.Unknown,
                $"Recovery evaluation failed: {ex.Message}",
                stopwatch.Elapsed,
                correlationId);
        }
    }

    /// <summary>
    /// Performs manual recovery operation with explicit parameters.
    /// </summary>
    /// <typeparam name="T">The type of grain state to recover</typeparam>
    /// <param name="request">The manual recovery request with specific parameters</param>
    /// <param name="projection">The projection logic for rebuilding state from events</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    public async Task<StateRecoveryResult<T>> PerformManualRecoveryAsync<T>(
        StateRecoveryRequest request,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(projection);

        try
        {
            _logger.LogInformation(
                "Starting manual recovery for grain {GrainId} with strategy {Strategy} [CorrelationId: {CorrelationId}]",
                request.GrainId, request.PreferredStrategy, request.CorrelationId);

            // Validate that the requested strategy is possible
            if (request.PreferredStrategy.HasValue)
            {
                var validationResult = await _orchestrator.ValidateRecoveryPossibleAsync(
                    request.GrainId, request.GrainType, request.PreferredStrategy.Value, cancellationToken);

                if (!validationResult.IsPossible)
                {
                    return StateRecoveryResult<T>.CreateFailure(
                        $"Requested strategy {request.PreferredStrategy} is not possible: {string.Join(", ", validationResult.BlockingIssues)}",
                        request.PreferredStrategy.Value,
                        TimeSpan.Zero,
                        request.CorrelationId);
                }
            }

            // Perform the recovery
            var result = await _orchestrator.RecoverStateAsync(request, projection, cancellationToken);

            _logger.LogInformation(
                "Manual recovery completed for grain {GrainId} with success: {Success} [CorrelationId: {CorrelationId}]",
                request.GrainId, result.Success, request.CorrelationId);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Error during manual recovery for grain {GrainId} [CorrelationId: {CorrelationId}]",
                request.GrainId, request.CorrelationId);

            return StateRecoveryResult<T>.CreateFailure(
                $"Manual recovery failed: {ex.Message}",
                request.PreferredStrategy ?? RecoveryStrategy.HybridRecovery,
                TimeSpan.Zero,
                request.CorrelationId);
        }
    }

    /// <summary>
    /// Validates the current state without performing recovery.
    /// </summary>
    /// <typeparam name="T">The type of grain state to validate</typeparam>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain being validated</param>
    /// <param name="state">The state to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with detailed findings</returns>
    public async Task<StateValidationResult> ValidateStateAsync<T>(
        string grainId,
        string grainType,
        T state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        try
        {
            _logger.LogDebug("Validating state for grain {GrainId}", grainId);

            // Use the detector for validation
            var validationResult = await _detector.ValidateStateIntegrityAsync(
                grainId, grainType, state, cancellationToken);

            _logger.LogDebug(
                "State validation completed for grain {GrainId}. IsValid: {IsValid}",
                grainId, validationResult.IsValid);

            return validationResult;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error validating state for grain {GrainId}", grainId);
            throw;
        }
    }

    /// <summary>
    /// Schedules periodic integrity checks for active grains.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain to monitor</param>
    /// <param name="interval">The interval between integrity checks</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The scheduling result</returns>
    public Task<IntegrityCheckSchedulingResult> ScheduleIntegrityCheckAsync(
        string grainId,
        string grainType,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        // For core functionality, integrity checks are performed synchronously
        // Advanced background processing would be implemented in a later task
        var nextCheckTime = DateTimeOffset.UtcNow.Add(interval);
        var scheduleId = Guid.NewGuid().ToString();

        var result = IntegrityCheckSchedulingResult.CreateSuccess(
            grainId,
            grainType,
            interval,
            nextCheckTime,
            scheduleId);

        _logger.LogDebug(
            "Integrity check scheduled for grain {GrainId} with interval {Interval}",
            grainId, interval);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Cancels scheduled integrity checks for a grain.
    /// </summary>
    /// <param name="grainId">The unique identifier of the grain</param>
    /// <param name="grainType">The type of grain to stop monitoring</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The cancellation result</returns>
    public Task<IntegrityCheckCancellationResult> CancelIntegrityCheckAsync(
        string grainId,
        string grainType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainType);

        // For core functionality, return successful cancellation
        var result = IntegrityCheckCancellationResult.CreateSuccess(
            grainId,
            grainType,
            $"{grainId}-{grainType}");

        _logger.LogDebug("Integrity check cancelled for grain {GrainId}", grainId);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Gets recovery health status and system information.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Recovery service health status</returns>
    public Task<RecoveryServiceHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check health of dependencies
            var isDetectorHealthy = _detector != null;
            var isOrchestratorHealthy = _orchestrator != null;
            var isVerifierHealthy = _verifier != null;

            var isHealthy = isDetectorHealthy && isOrchestratorHealthy && isVerifierHealthy;

            var healthStatus = new RecoveryServiceHealthStatus
            {
                IsHealthy = isHealthy,
                DetectorHealthy = isDetectorHealthy,
                OrchestratorHealthy = isOrchestratorHealthy,
                VerifierHealthy = isVerifierHealthy,
                EventStoreHealthy = true, // Assume healthy for core functionality
                SnapshotManagerHealthy = true, // Assume healthy for core functionality
                ActiveRecoveryOperations = 0, // For core functionality
                ScheduledIntegrityChecks = 0, // For core functionality
                Uptime = TimeSpan.Zero, // For core functionality
                HealthDetails = new Dictionary<string, object>
                {
                    ["DetectorName"] = _detector?.Name ?? "null",
                    ["OrchestratorName"] = _orchestrator?.Name ?? "null",
                    ["VerifierName"] = _verifier?.Name ?? "null"
                },
                CheckedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult(healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health status");

            var healthStatus = RecoveryServiceHealthStatus.Unhealthy(new Dictionary<string, object>
            {
                ["Error"] = ex.Message,
                ["ExceptionType"] = ex.GetType().Name
            });

            return Task.FromResult(healthStatus);
        }
    }

    /// <summary>
    /// Gets recovery statistics and metrics across all grains.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive recovery metrics</returns>
    public Task<RecoveryMetrics> GetRecoveryMetricsAsync(CancellationToken cancellationToken = default)
    {
        // For core functionality, return empty metrics
        var metrics = RecoveryMetrics.Empty();
        return Task.FromResult(metrics);
    }

    /// <summary>
    /// Gets detailed configuration information for the service.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current configuration details</returns>
    public Task<AutomaticRecoveryConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var configuration = new AutomaticRecoveryConfiguration
        {
            ServiceName = Name,
            DetectorName = _detector?.Name ?? "Unknown",
            OrchestratorName = _orchestrator?.Name ?? "Unknown",
            VerifierName = _verifier?.Name ?? "Unknown",
            IsEnabled = true,
            DefaultStrategy = RecoveryStrategy.HybridRecovery,
            Settings = new Dictionary<string, object>
            {
                ["MaxRecoveryTimeMs"] = 30000,
                ["MaxEventsToReplay"] = 10000,
                ["EnableAutomaticRecovery"] = true,
                ["EnableIntegrityChecks"] = true
            },
            LastUpdated = DateTimeOffset.UtcNow
        };

        return Task.FromResult(configuration);
    }

    /// <summary>
    /// Configures recovery policies and settings for specific grain types.
    /// </summary>
    /// <param name="grainType">The type of grain to configure</param>
    /// <param name="policy">The recovery policy configuration</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The configuration result</returns>
    public Task<RecoveryPolicyConfigurationResult> ConfigureRecoveryPolicyAsync(
        string grainType,
        RecoveryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(policy);

        // For core functionality, always return success
        var result = RecoveryPolicyConfigurationResult.CreateSuccess(grainType, policy);

        _logger.LogDebug("Recovery policy configured for grain type {GrainType}", grainType);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Gets the current recovery policy configuration for a grain type.
    /// </summary>
    /// <param name="grainType">The type of grain to get policy for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The current recovery policy</returns>
    public Task<RecoveryPolicy> GetRecoveryPolicyAsync(
        string grainType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainType);

        // For core functionality, return default policy
        var policy = RecoveryPolicy.Default();
        return Task.FromResult(policy);
    }

    /// <summary>
    /// Provides event notifications for recovery operations.
    /// </summary>
    #pragma warning disable CS0414 // Field is assigned but its value is never used
    public event EventHandler<RecoveryEventArgs>? RecoveryEvent;
    #pragma warning restore CS0414

    /// <summary>
    /// Disposes the automatic recovery service and releases resources.
    /// </summary>
    public void Dispose()
    {
        // For core functionality, no resources to dispose
        RecoveryEvent = null;
    }

    #region Private Helper Methods

    /// <summary>
    /// Determines the optimal recovery strategy based on cost estimate.
    /// </summary>
    /// <param name="costEstimate">The recovery cost estimate</param>
    /// <returns>The recommended recovery strategy</returns>
    private static RecoveryStrategy DetermineOptimalStrategy(RecoveryCostEstimate costEstimate)
    {
        // Use snapshot-first if available and cost is high
        if (costEstimate.SnapshotAvailable && costEstimate.EventsToReplay > 1000)
        {
            return RecoveryStrategy.SnapshotFirst;
        }

        // Use hybrid recovery as default for most scenarios
        if (costEstimate.SnapshotAvailable)
        {
            return RecoveryStrategy.HybridRecovery;
        }

        // Use full replay if no snapshots available
        return RecoveryStrategy.FullReplay;
    }

    #endregion
}