using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Production implementation of logs router for coordinating between Orleans logging systems and direct file logging.
/// Provides feature flag control, automatic fallback, comprehensive logging, and performance metrics collection.
///
/// Note: This router is designed with direct file operations as the primary path since logging operations
/// typically benefit more from direct I/O than Orleans coordination. Orleans integration is provided
/// for future enhancements such as distributed logging coordination, log aggregation, or audit trails.
/// </summary>
public class LogsRouter : ILogsRouter, IDisposable
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<LogsRouter> _logger;
    private readonly RouterMetricsCollector _metricsCollector;
    private readonly SemaphoreSlim _healthCheckSemaphore;
    private readonly DualModeRouterOptions _options;

    // Feature flag name for Orleans logging coordination
    private const string OrleansLoggingFeatureFlag = "OrleansLogging";

    // Circuit breaker state tracking (minimal for logging)
    private volatile bool _orleansCircuitOpen;
    private DateTime _lastOrleansFailureTime = DateTime.MinValue;
    private readonly object _circuitBreakerLock = new();

    /// <summary>
    /// Initializes a new instance of the LogsRouter class.
    /// </summary>
    /// <param name="featureManager">Feature manager for Orleans feature flag evaluation</param>
    /// <param name="logger">Logger for structured logging</param>
    /// <param name="options">Configuration options for router behavior</param>
    public LogsRouter(
        IFeatureManager featureManager,
        ILogger<LogsRouter> logger,
        IOptions<DualModeRouterOptions> options)
    {
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metricsCollector = new RouterMetricsCollector(_options.MetricsWindowSize);
        _healthCheckSemaphore = new SemaphoreSlim(1, 1);

        _logger.LogInformation("LogsRouter initialized - primary path: direct file operations, Orleans coordination available for future enhancements");
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> orleansOperation,
        Func<Task<T>> directOperation,
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

        _logger.LogDebug("Starting logs operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans logging coordination is enabled (typically will be false for performance)
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    var result = await ExecuteOrleansOperationAsync(
                        orleansOperation,
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans logs operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans logs operation {OperationName} failed, falling back to direct file operation", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct file operation (primary path for logging)
            var directResult = await ExecuteDirectOperationAsync(
                directOperation,
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct logs operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Logs operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for logs operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Logs operation {operationName} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<Task> orleansOperation,
        Func<Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        // Wrap void operations in a Task<object> to reuse the generic implementation
        await ExecuteAsync<object?>(
            async () => { await orleansOperation(); return null; },
            async () => { await directOperation(); return null; },
            operationName,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IActionResult> ExecuteLogEntryAsync(
        JsonElement logEntry,
        Func<JsonElement, Task<IActionResult>>? orleansOperation,
        Func<JsonElement, Task<IActionResult>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting client log entry operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans logging coordination is enabled and an Orleans operation is provided
            if (orleansOperation != null && await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    var result = await ExecuteOrleansLogEntryOperationAsync(
                        logEntry,
                        orleansOperation,
                        operationName,
                        operationId,
                        cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans log entry operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans log entry operation {OperationName} failed, falling back to direct file operation", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct file operation (primary path for client logging)
            var directResult = await ExecuteDirectLogEntryOperationAsync(
                logEntry,
                directOperation,
                operationName,
                operationId,
                cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct log entry operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Log entry operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for log entry operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Log entry operation {operationName} failed in all routing modes", ex)
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
            // Orleans logging coordination is typically disabled for performance reasons
            // Use a separate feature flag for logging specifically
            return await _featureManager.IsEnabledAsync(OrleansLoggingFeatureFlag) && !IsCircuitBreakerOpen();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Orleans logging feature flag, assuming disabled");
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
            var isOrleansHealthy = isOrleansEnabled; // For logging, if enabled then considered healthy
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
                Message = isHealthy ? "Logs router is healthy" : "Logs router is degraded - some operations may fail"
            };

            _logger.LogDebug("Logs router health check completed: {CurrentMode}, Orleans: {OrleansHealth}, Direct: {DirectHealth}",
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
        _logger.LogDebug("Executing Orleans logs operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            var result = await operation();
            ResetCircuitBreaker();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans logs operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private async Task<T> ExecuteDirectOperationAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing direct logs operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct logs operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private async Task<IActionResult> ExecuteOrleansLogEntryOperationAsync(
        JsonElement logEntry,
        Func<JsonElement, Task<IActionResult>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing Orleans log entry operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            var result = await operation(logEntry);
            ResetCircuitBreaker();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans log entry operation {OperationName} [{OperationId}] failed", operationName, operationId);
            throw;
        }
    }

    private async Task<IActionResult> ExecuteDirectLogEntryOperationAsync(
        JsonElement logEntry,
        Func<JsonElement, Task<IActionResult>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Executing direct log entry operation {OperationName} [{OperationId}]", operationName, operationId);

        try
        {
            return await operation(logEntry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct log entry operation {OperationName} [{OperationId}] failed", operationName, operationId);
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

        _logger.LogWarning("Orleans logs circuit breaker opened due to failure at {FailureTime}", _lastOrleansFailureTime);
    }

    private void ResetCircuitBreaker()
    {
        if (_orleansCircuitOpen)
        {
            lock (_circuitBreakerLock)
            {
                _orleansCircuitOpen = false;
                _logger.LogInformation("Orleans logs circuit breaker reset - operations successful");
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
            _logger.LogDebug("Orleans logs circuit breaker timeout expired, allowing retry");
            return false;
        }

        return true;
    }

    private static async Task<bool> CheckDirectServiceHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // For logging, we can test if we can create the logs directory and have write permissions
            var tempLogDir = Path.Combine(Path.GetTempPath(), "health-check-logs");

            // Try to create directory and write a test file
            Directory.CreateDirectory(tempLogDir);
            var testFile = Path.Combine(tempLogDir, "health-check.tmp");
            await File.WriteAllTextAsync(testFile, "health check", cancellationToken);

            // Clean up
            File.Delete(testFile);
            Directory.Delete(tempLogDir);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static RouterMode DetermineCurrentMode(bool isOrleansEnabled, bool isOrleansHealthy, bool isDirectServiceHealthy)
    {
        // For logging, direct service is almost always preferred
        if (isDirectServiceHealthy)
        {
            return isOrleansEnabled && isOrleansHealthy ? RouterMode.Orleans : RouterMode.DirectService;
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