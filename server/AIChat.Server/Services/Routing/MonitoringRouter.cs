using System.Diagnostics;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Production implementation of monitoring router for seamless switching between Orleans monitoring grains and direct monitoring services.
/// Provides feature flag control, automatic fallback, comprehensive logging, and performance metrics collection.
/// </summary>
public class MonitoringRouter : IMonitoringRouter, IDisposable
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<MonitoringRouter> _logger;
    private readonly IGrainFactory? _grainFactory;
    private readonly ProductionMonitoringService _monitoringService;
    private readonly RouterMetricsCollector _metricsCollector;
    private readonly SemaphoreSlim _healthCheckSemaphore;
    private readonly DualModeRouterOptions _options;

    /// <summary>
    /// Feature flag name for Orleans routing
    /// </summary>
    private const string OrleansFeatureFlag = "Orleans";

    /// <summary>
    /// Circuit breaker state tracking
    /// </summary>
    private volatile bool _orleansCircuitOpen;
    private DateTime _lastOrleansFailureTime = DateTime.MinValue;
    private readonly object _circuitBreakerLock = new();

    /// <summary>
    /// Initializes a new instance of the MonitoringRouter class.
    /// </summary>
    /// <param name="featureManager">Feature manager for Orleans feature flag evaluation</param>
    /// <param name="logger">Logger for structured logging</param>
    /// <param name="monitoringService">Direct monitoring service for fallback operations</param>
    /// <param name="options">Configuration options for router behavior</param>
    /// <param name="grainFactory">Orleans grain factory for grain creation</param>
    public MonitoringRouter(
        IFeatureManager featureManager,
        ILogger<MonitoringRouter> logger,
        ProductionMonitoringService monitoringService,
        IOptions<DualModeRouterOptions> options,
        IGrainFactory? grainFactory = null)
    {
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainFactory = grainFactory; // Allow null for environments without Orleans
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metricsCollector = new RouterMetricsCollector(_options.MetricsWindowSize);
        _healthCheckSemaphore = new SemaphoreSlim(1, 1);

        var orleansStatus = _grainFactory != null ? "with Orleans support" : "direct service only (no Orleans)";
        _logger.LogInformation("MonitoringRouter initialized {OrleansStatus}", orleansStatus);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
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

        _logger.LogDebug("Starting monitoring operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use a system-wide monitoring grain key
                    var sessionMonitoringGrain = _grainFactory!.GetGrain<ISessionMonitoringGrain>("system-monitoring");
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(sessionMonitoringGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans monitoring operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans monitoring operation {OperationName} failed, falling back to direct service", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_monitoringService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct monitoring service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Monitoring operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for monitoring operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Monitoring operation {operationName} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<ISessionMonitoringGrain, Task> orleansOperation,
        Func<ProductionMonitoringService, Task> directOperation,
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
    public async Task<T> ExecuteSystemOperationAsync<T>(
        Func<IHealthCheckGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
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

        _logger.LogDebug("Starting system monitoring operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use system health check grain for system-wide monitoring
                    var healthGrain = _grainFactory!.GetGrain<IHealthCheckGrain>("system-monitoring");
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(healthGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans system monitoring operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans system monitoring operation {OperationName} failed, falling back to direct service", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_monitoringService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct system monitoring service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("System monitoring operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for system monitoring operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"System monitoring operation {operationName} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteSessionOperationAsync<T>(
        string sessionId,
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentNullException(nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting session monitoring operation {OperationName} for session {SessionId} with ID {OperationId}",
            operationName, sessionId, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    // Use session-specific grain key
                    var sessionMonitoringGrain = _grainFactory!.GetGrain<ISessionMonitoringGrain>(sessionId);
                    var result = await ExecuteOrleansOperationAsync(
                        () => orleansOperation(sessionMonitoringGrain),
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans session monitoring operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans session monitoring operation {OperationName} for session {SessionId} failed, falling back to direct service",
                        operationName, sessionId);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(
                () => directOperation(_monitoringService),
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct session monitoring service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Session monitoring operation {OperationName} for session {SessionId} was cancelled", operationName, sessionId);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for session monitoring operation {OperationName} for session {SessionId}",
                operationName, sessionId);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Session monitoring operation {operationName} for session {sessionId} failed in all routing modes", ex)
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
                Message = isHealthy ? "Monitoring router is healthy" : "Monitoring router is degraded - some operations may fail"
            };

            _logger.LogDebug("Monitoring router health check completed: {CurrentMode}, Orleans: {OrleansHealth}, Direct: {DirectHealth}",
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
        _logger.LogDebug("Executing Orleans monitoring operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            var result = await operation();
            ResetCircuitBreaker();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans monitoring operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private async Task<T> ExecuteDirectOperationAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing direct monitoring service operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct monitoring service operation {OperationName} [{OperationId}] failed", operationName, operationId);
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

        _logger.LogWarning("Orleans monitoring circuit breaker opened due to failure at {FailureTime}", _lastOrleansFailureTime);
    }

    private void ResetCircuitBreaker()
    {
        if (_orleansCircuitOpen)
        {
            lock (_circuitBreakerLock)
            {
                _orleansCircuitOpen = false;
                _logger.LogInformation("Orleans monitoring circuit breaker reset - operations successful");
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
            _logger.LogDebug("Orleans monitoring circuit breaker timeout expired, allowing retry");
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
            var healthGrain = _grainFactory.GetGrain<IHealthCheckGrain>("monitoring-router");
            var healthResult = await healthGrain.CheckHealthAsync();
            return healthResult.IsHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans monitoring health check failed");
            return false;
        }
    }

    private async Task<bool> CheckDirectServiceHealthAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress CS1998
        try
        {
            // Test direct service with a simple operation
            var metrics = _monitoringService.GetCurrentMetrics();
            return metrics != null; // If we can get metrics, service is healthy
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct monitoring service health check failed");
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

    #endregion Private Helper Methods

    /// <summary>
    /// Dispose of resources used by the router.
    /// </summary>
    public void Dispose()
    {
        _healthCheckSemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}