using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Production implementation of mode router for seamless switching between Orleans mode grains and direct mode services.
/// Provides feature flag control, automatic fallback, comprehensive logging, and performance metrics collection.
/// </summary>
public class ModeRouter : IModeRouter, IDisposable
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<ModeRouter> _logger;
    private readonly IGrainFactory? _grainFactory;
    private readonly IModeService _modeService;
    private readonly RouterMetricsCollector _metricsCollector;
    private readonly SemaphoreSlim _healthCheckSemaphore;
    private readonly DualModeRouterOptions _options;

    // Feature flag name for Orleans routing
    private const string OrleansFeatureFlag = "Orleans";

    // Circuit breaker state tracking
    private volatile bool _orleansCircuitOpen;
    private DateTime _lastOrleansFailureTime = DateTime.MinValue;
    private readonly object _circuitBreakerLock = new();

    /// <summary>
    /// Initializes a new instance of the ModeRouter class.
    /// </summary>
    /// <param name="featureManager">Feature manager for Orleans feature flag evaluation</param>
    /// <param name="logger">Logger for structured logging</param>
    /// <param name="grainFactory">Orleans grain factory for grain creation</param>
    /// <param name="modeService">Direct mode service for fallback operations</param>
    /// <param name="options">Configuration options for router behavior</param>
    public ModeRouter(
        IFeatureManager featureManager,
        ILogger<ModeRouter> logger,
        IModeService modeService,
        IOptions<DualModeRouterOptions> options,
        IGrainFactory? grainFactory = null)
    {
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainFactory = grainFactory; // Allow null for environments without Orleans
        _modeService = modeService ?? throw new ArgumentNullException(nameof(modeService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metricsCollector = new RouterMetricsCollector(_options.MetricsWindowSize);
        _healthCheckSemaphore = new SemaphoreSlim(1, 1);

        var orleansStatus = _grainFactory != null ? "with Orleans support" : "direct service only (no Orleans)";
        _logger.LogInformation("ModeRouter initialized {OrleansStatus}", orleansStatus);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting mode operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use a generic mode grain key since this is a general operation
                    var modeGrain = _grainFactory!.GetGrain<IModeGrain>("system");
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(modeGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans mode operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans mode operation {OperationName} failed, falling back to direct service", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_modeService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct mode service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Mode operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for mode operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Mode operation {operationName} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<IModeGrain, Task> orleansOperation,
        Func<IModeService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        // Wrap void operations in a Task<object> to reuse the generic implementation
        await ExecuteAsync<object?>(
            async grain => { await orleansOperation(grain); return null; },
            async service => { await directOperation(service); return null; },
            operationName,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteUserOperationAsync<T>(
        string userId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting user mode operation {OperationName} for user {UserId} with ID {OperationId}",
            operationName, userId, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use user-specific grain key
                    var userModeGrain = _grainFactory!.GetGrain<IModeGrain>($"user:{userId}");
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(userModeGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans user mode operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans user mode operation {OperationName} for user {UserId} failed, falling back to direct service",
                        operationName, userId);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_modeService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct user mode service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("User mode operation {OperationName} for user {UserId} was cancelled", operationName, userId);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for user mode operation {OperationName} for user {UserId}",
                operationName, userId);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"User mode operation {operationName} for user {userId} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteModeOperationAsync<T>(
        string modeId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modeId))
        {
            throw new ArgumentNullException(nameof(modeId));
        }

        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting mode-specific operation {OperationName} for mode {ModeId} with ID {OperationId}",
            operationName, modeId, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use mode-specific grain key
                    var modeGrain = _grainFactory!.GetGrain<IModeGrain>(modeId);
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(modeGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans mode-specific operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans mode-specific operation {OperationName} for mode {ModeId} failed, falling back to direct service",
                        operationName, modeId);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_modeService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct mode-specific service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Mode-specific operation {OperationName} for mode {ModeId} was cancelled", operationName, modeId);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for mode-specific operation {OperationName} for mode {ModeId}",
                operationName, modeId);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Mode-specific operation {operationName} for mode {modeId} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Orleans is only available if grain factory is configured
            if (_grainFactory == null)
            {
                return false;
            }

            return await _featureManager.IsEnabledAsync(OrleansFeatureFlag) && !IsCircuitBreakerOpen();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Orleans feature flag, assuming disabled");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<RouterHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        await _healthCheckSemaphore.WaitAsync(cancellationToken);
        try
        {
            var isOrleansEnabled = await IsOrleansEnabledAsync(cancellationToken);
            var isOrleansHealthy = isOrleansEnabled && await CheckOrleansHealthAsync(cancellationToken);
            var isDirectServiceHealthy = await CheckDirectServiceHealthAsync(cancellationToken);

            var currentMode = DetermineCurrentMode(isOrleansEnabled, isOrleansHealthy, isDirectServiceHealthy);
            var isHealthy = currentMode != RouterMode.Degraded;

            var status = new RouterHealthStatus
            {
                IsHealthy = isHealthy,
                IsOrleansHealthy = isOrleansHealthy,
                IsDirectServiceHealthy = isDirectServiceHealthy,
                IsOrleansEnabled = isOrleansEnabled,
                CurrentMode = currentMode,
                Message = isHealthy ? "Mode router is healthy" : "Mode router is degraded - some operations may fail"
            };

            _logger.LogDebug("Mode router health check completed: {CurrentMode}, Orleans: {OrleansHealth}, Direct: {DirectHealth}",
                currentMode, isOrleansHealthy ? "Healthy" : "Unhealthy", isDirectServiceHealthy ? "Healthy" : "Unhealthy");

            return status;
        }
        finally
        {
            _healthCheckSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_metricsCollector.GetCurrentMetrics());
    }

    #region Private Helper Methods

    private async Task<bool> ShouldUseOrleansAsync(CancellationToken cancellationToken)
    {
        return await IsOrleansEnabledAsync(cancellationToken);
    }

    private async Task<T> ExecuteOrleansOperationAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing Orleans mode operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            var result = await operation();
            ResetCircuitBreaker();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans mode operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private async Task<T> ExecuteDirectOperationAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing direct mode service operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct mode service operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private void HandleOrleansFailure()
    {
        lock (_circuitBreakerLock)
        {
            _lastOrleansFailureTime = DateTime.UtcNow;
            _orleansCircuitOpen = true;
        }

        _logger.LogWarning("Orleans mode circuit breaker opened due to failure at {FailureTime}", _lastOrleansFailureTime);
    }

    private void ResetCircuitBreaker()
    {
        if (_orleansCircuitOpen)
        {
            lock (_circuitBreakerLock)
            {
                _orleansCircuitOpen = false;
                _logger.LogInformation("Orleans mode circuit breaker reset - operations successful");
            }
        }
    }

    private bool IsCircuitBreakerOpen()
    {
        if (!_orleansCircuitOpen)
        {
            return false;
        }

        var timeSinceFailure = DateTime.UtcNow - _lastOrleansFailureTime;
        if (timeSinceFailure >= _options.CircuitBreakerTimeout)
        {
            _logger.LogDebug("Orleans mode circuit breaker timeout expired, allowing retry");
            return false;
        }

        return true;
    }

    private async Task<bool> CheckOrleansHealthAsync(CancellationToken cancellationToken)
    {
        if (_grainFactory == null)
        {
            return false;
        }

        try
        {
            // Test Orleans connectivity with a simple grain operation
            var healthGrain = _grainFactory.GetGrain<IHealthCheckGrain>("mode-router");
            var healthResult = await healthGrain.CheckHealthAsync();
            return healthResult.IsHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans mode health check failed");
            return false;
        }
    }

    private async Task<bool> CheckDirectServiceHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Test direct service with a simple operation
            // Using the existing GetAllModesAsync with a test user ID
            var result = await _modeService.GetAllModesAsync("health-check-user", cancellationToken);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct mode service health check failed");
            return false;
        }
    }

    private static RouterMode DetermineCurrentMode(bool isOrleansEnabled, bool isOrleansHealthy, bool isDirectServiceHealthy)
    {
        if (isOrleansEnabled && isOrleansHealthy)
        {
            return RouterMode.Orleans;
        }

        if (isDirectServiceHealthy)
        {
            return RouterMode.DirectService;
        }

        return RouterMode.Degraded;
    }

    #endregion

    /// <summary>
    /// Dispose of resources used by the router.
    /// </summary>
    public void Dispose()
    {
        _healthCheckSemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}