using System.Collections.Concurrent;
using System.Diagnostics;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Production implementation of dual-mode router for seamless switching between Orleans grains and direct services.
/// Provides feature flag control, automatic fallback, comprehensive logging, and performance metrics collection.
/// </summary>
public class DualModeRouter : IDualModeRouter, IDisposable
{
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<DualModeRouter> _logger;
    private readonly IGrainFactory? _grainFactory;
    private readonly IChatService _chatService;
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
    /// Initializes a new instance of the DualModeRouter class.
    /// </summary>
    /// <param name="featureManager">Feature manager for Orleans feature flag evaluation</param>
    /// <param name="logger">Logger for structured logging</param>
    /// <param name="chatService">Direct chat service for fallback operations</param>
    /// <param name="options">Configuration options for router behavior</param>
    /// <param name="grainFactory">Orleans grain factory for grain creation</param>
    public DualModeRouter(
        IFeatureManager featureManager,
        ILogger<DualModeRouter> logger,
        IChatService chatService,
        IOptions<DualModeRouterOptions> options,
        IGrainFactory? grainFactory = null)
    {
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _grainFactory = grainFactory; // Allow null for environments without Orleans
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metricsCollector = new RouterMetricsCollector(_options.MetricsWindowSize);
        _healthCheckSemaphore = new SemaphoreSlim(1, 1);

        var orleansStatus = _grainFactory != null ? "with Orleans support" : "direct service only (no Orleans)";
        _logger.LogInformation("DualModeRouter initialized {OrleansStatus}", orleansStatus);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteAsync<T>(
        Func<IChatGrain, Task<T>> orleansOperation,
        Func<IChatService, Task<T>> directOperation,
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

        _logger.LogDebug("Starting operation {OperationName} with ID {OperationId}", operationName, operationId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    var result = await ExecuteOrleansOperationAsync(orleansOperation, operationName, operationId, cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans operation {OperationName} completed successfully in {ElapsedMs}ms",
                        operationName, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans operation {OperationName} failed, falling back to direct service", operationName);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(directOperation, operationName, operationId, cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct service operation {OperationName} completed successfully in {ElapsedMs}ms",
                operationName, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Operation {OperationName} was cancelled", operationName);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for operation {OperationName}", operationName);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Operation {operationName} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<IChatGrain, Task> orleansOperation,
        Func<IChatService, Task> directOperation,
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
    public async Task<T> ExecuteAsync<T>(
        Func<IChatGrain, Task<T>> orleansOperation,
        Func<IChatService, Task<T>> directOperation,
        string operationName,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }
        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new ArgumentNullException(nameof(chatId));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting chat-specific operation {OperationName} with ID {OperationId} for ChatId {ChatId}",
            operationName, operationId, chatId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    var result = await ExecuteOrleansOperationAsync(orleansOperation, operationName, operationId, chatId, cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans operation {OperationName} for ChatId {ChatId} completed successfully in {ElapsedMs}ms",
                        operationName, chatId, stopwatch.ElapsedMilliseconds);

                    return result;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans operation {OperationName} for ChatId {ChatId} failed, falling back to direct service",
                        operationName, chatId);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            var directResult = await ExecuteDirectOperationAsync(directOperation, operationName, operationId, cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct service operation {OperationName} for ChatId {ChatId} completed successfully in {ElapsedMs}ms",
                operationName, chatId, stopwatch.ElapsedMilliseconds);

            return directResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Operation {OperationName} for ChatId {ChatId} was cancelled", operationName, chatId);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for operation {OperationName} for ChatId {ChatId}", operationName, chatId);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Operation {operationName} for ChatId {chatId} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
        }
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        Func<IChatGrain, Task> orleansOperation,
        Func<IChatService, Task> directOperation,
        string operationName,
        string chatId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orleansOperation);
        ArgumentNullException.ThrowIfNull(directOperation);
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new ArgumentNullException(nameof(operationName));
        }
        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new ArgumentNullException(nameof(chatId));
        }

        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("Starting chat-specific operation {OperationName} with ID {OperationId} for ChatId {ChatId}",
            operationName, operationId, chatId);

        var orleansAttempted = false;

        try
        {
            // Check if Orleans is enabled and available
            if (await ShouldUseOrleansAsync(cancellationToken))
            {
                orleansAttempted = true;
                try
                {
                    await ExecuteOrleansOperationAsync(orleansOperation, operationName, operationId, chatId, cancellationToken);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: true);

                    _logger.LogDebug("Orleans operation {OperationName} for ChatId {ChatId} completed successfully in {ElapsedMs}ms",
                        operationName, chatId, stopwatch.ElapsedMilliseconds);

                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Orleans operation {OperationName} for ChatId {ChatId} failed, falling back to direct service",
                        operationName, chatId);
                    _metricsCollector.RecordOrleansOperation(stopwatch.Elapsed, success: false);
                    HandleOrleansFailure();

                    // Reset stopwatch for fallback operation timing
                    stopwatch.Restart();
                }
            }

            // Execute direct service operation (either as primary or fallback)
            await ExecuteDirectOperationAsync(directOperation, operationName, operationId, cancellationToken);
            _metricsCollector.RecordDirectServiceOperation(stopwatch.Elapsed, success: true, isFallback: orleansAttempted);

            _logger.LogDebug("Direct service operation {OperationName} for ChatId {ChatId} completed successfully in {ElapsedMs}ms",
                operationName, chatId, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Operation {OperationName} for ChatId {ChatId} was cancelled", operationName, chatId);
            _metricsCollector.RecordFailedOperation();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All routing options failed for operation {OperationName} for ChatId {ChatId}", operationName, chatId);
            _metricsCollector.RecordFailedOperation();

            throw new RouterException($"Operation {operationName} for ChatId {chatId} failed in all routing modes", ex)
            {
                OperationName = operationName,
                OrleansAttempted = orleansAttempted,
                DirectServiceAttempted = true
            };
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
                Message = isHealthy ? "Router is healthy" : "Router is degraded - some operations may fail"
            };

            _logger.LogDebug("Health check completed: {CurrentMode}, Orleans: {OrleansHealth}, Direct: {DirectHealth}",
                currentMode, isOrleansHealthy ? "Healthy" : "Unhealthy", isDirectServiceHealthy ? "Healthy" : "Unhealthy");

            return status;
        }
        finally
        {
            _healthCheckSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var metrics = _metricsCollector.GetCurrentMetrics();
        _logger.LogDebug("Retrieved router metrics: {TotalOps} total, {OrleansOps} Orleans, {DirectOps} direct, {FallbackOps} fallback",
            metrics.TotalOperations, metrics.OrleansOperations, metrics.DirectServiceOperations, metrics.FallbackOperations);

        return Task.FromResult(metrics);
    }

    private async Task<T> ExecuteOrleansOperationAsync<T>(
        Func<IChatGrain, Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Defensive check - this should never happen if ShouldUseOrleansAsync is working correctly
            if (_grainFactory == null)
            {
                throw new InvalidOperationException("Orleans operation attempted but grain factory is null");
            }

            // Use a single well-known grain key for all chat operations
            // This ensures proper Orleans grain usage while maintaining backward compatibility
            // Future enhancement: Use actual chat session IDs when full Orleans integration is implemented
            const string grainKey = "default-chat-grain";
            var grain = _grainFactory.GetGrain<IChatGrain>(grainKey);

            _logger.LogDebug("Executing Orleans operation {OperationName} on grain {GrainKey}", operationName, grainKey);

            var result = await operation(grain);

            // Reset circuit breaker on successful operation
            ResetCircuitBreaker();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans operation {OperationName} failed with grain factory", operationName);
            throw;
        }
    }

    private async Task<T> ExecuteOrleansOperationAsync<T>(
        Func<IChatGrain, Task<T>> operation,
        string operationName,
        string operationId,
        string chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Defensive check - this should never happen if ShouldUseOrleansAsync is working correctly
            if (_grainFactory == null)
            {
                throw new InvalidOperationException("Orleans operation attempted but grain factory is null");
            }

            // Use the actual chat ID for Orleans grain routing
            // This enables proper per-chat state management and eliminates the facade pattern
            var grainKey = chatId;
            var grain = _grainFactory.GetGrain<IChatGrain>(grainKey);

            _logger.LogDebug("Executing Orleans operation {OperationName} on chat grain {GrainKey}", operationName, grainKey);

            var result = await operation(grain);

            // Reset circuit breaker on successful operation
            ResetCircuitBreaker();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans operation {OperationName} for ChatId {ChatId} failed with grain factory", operationName, chatId);
            throw;
        }
    }

    private async Task ExecuteOrleansOperationAsync(
        Func<IChatGrain, Task> operation,
        string operationName,
        string operationId,
        string chatId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Defensive check - this should never happen if ShouldUseOrleansAsync is working correctly
            if (_grainFactory == null)
            {
                throw new InvalidOperationException("Orleans operation attempted but grain factory is null");
            }

            // Use the actual chat ID for Orleans grain routing
            // This enables proper per-chat state management and eliminates the facade pattern
            var grainKey = chatId;
            var grain = _grainFactory.GetGrain<IChatGrain>(grainKey);

            _logger.LogDebug("Executing Orleans operation {OperationName} on chat grain {GrainKey}", operationName, grainKey);

            await operation(grain);

            // Reset circuit breaker on successful operation
            ResetCircuitBreaker();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans operation {OperationName} for ChatId {ChatId} failed with grain factory", operationName, chatId);
            throw;
        }
    }

    private async Task<T> ExecuteDirectOperationAsync<T>(
        Func<IChatService, Task<T>> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing direct service operation {OperationName}", operationName);
            return await operation(_chatService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct service operation {OperationName} failed", operationName);
            throw;
        }
    }

    private async Task ExecuteDirectOperationAsync(
        Func<IChatService, Task> operation,
        string operationName,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing direct service operation {OperationName}", operationName);
            await operation(_chatService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct service operation {OperationName} failed", operationName);
            throw;
        }
    }

    private async Task<bool> ShouldUseOrleansAsync(CancellationToken cancellationToken)
    {
        return await IsOrleansEnabledAsync(cancellationToken);
    }

    private async Task<bool> CheckOrleansHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if grain factory is available
            if (_grainFactory == null)
            {
                return false;
            }

            // Use dedicated health check grain following established patterns
            var healthGrain = _grainFactory.GetGrain<IHealthCheckGrain>("orleans-health-check");

            using var timeoutCts = new CancellationTokenSource(_options.OrleansHealthCheckTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Perform actual Orleans health check using the dedicated grain
            var result = await healthGrain.CheckHealthAsync().WaitAsync(combinedCts.Token);

            _logger.LogDebug("Orleans health check completed: {IsHealthy}, Grain: {GrainId}",
                result.IsHealthy, result.GrainId);

            return result.IsHealthy;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Orleans health check timed out after 5 seconds");
            return false;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Orleans health check was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans health check failed");
            return false;
        }
    }

    private async Task<bool> CheckDirectServiceHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if the chat service is properly configured and responsive
            if (_chatService == null)
            {
                _logger.LogWarning("Direct chat service is null");
                return false;
            }

            // Perform a lightweight operation to verify service responsiveness
            // Use a very short timeout to avoid impacting performance
            using var timeoutCts = new CancellationTokenSource(_options.DirectServiceHealthCheckTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Try to get the next sequence number for a test chat - this is a lightweight operation
            // that verifies database connectivity and basic service functionality
            try
            {
                await _chatService.GetNextSequenceNumberAsync("health-check-test-chat");
                _logger.LogDebug("Direct service health check passed");
                return true;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Direct service health check timed out after 2 seconds");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direct service health check failed");
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

    private void HandleOrleansFailure()
    {
        lock (_circuitBreakerLock)
        {
            _orleansCircuitOpen = true;
            _lastOrleansFailureTime = DateTime.UtcNow;
            _logger.LogWarning("Orleans circuit breaker opened due to failure");
        }
    }

    private void ResetCircuitBreaker()
    {
        lock (_circuitBreakerLock)
        {
            if (_orleansCircuitOpen)
            {
                _orleansCircuitOpen = false;
                _logger.LogInformation("Orleans circuit breaker reset after successful operation");
            }
        }
    }

    private bool IsCircuitBreakerOpen()
    {
        lock (_circuitBreakerLock)
        {
            if (!_orleansCircuitOpen)
            {
                return false;
            }

            if (DateTime.UtcNow - _lastOrleansFailureTime > _options.CircuitBreakerTimeout)
            {
                _orleansCircuitOpen = false;
                _logger.LogInformation("Orleans circuit breaker automatically reset after timeout");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Disposes of the router resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose pattern implementation
    /// </summary>
    /// <param name="disposing">True if disposing managed resources</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _healthCheckSemaphore?.Dispose();
        }
    }
}

/// <summary>
/// Internal metrics collector for router performance tracking.
/// Thread-safe implementation for concurrent access scenarios.
/// </summary>
internal sealed class RouterMetricsCollector
{
    private long _totalOperations;
    private long _orleansOperations;
    private long _directServiceOperations;
    private long _fallbackOperations;
    private long _failedOperations;

    private readonly ConcurrentQueue<double> _orleansExecutionTimes = new();
    private readonly ConcurrentQueue<double> _directServiceExecutionTimes = new();
    private readonly object _metricsLock = new();
    private readonly int _metricsWindowSize;

    /// <summary>
    /// Initializes a new instance of the RouterMetricsCollector.
    /// </summary>
    /// <param name="metricsWindowSize">Maximum number of execution time measurements to keep</param>
    public RouterMetricsCollector(int metricsWindowSize = 1000)
    {
        _metricsWindowSize = metricsWindowSize;
    }

    public void RecordOrleansOperation(TimeSpan executionTime, bool success)
    {
        if (success)
        {
            Interlocked.Increment(ref _totalOperations);
            Interlocked.Increment(ref _orleansOperations);
            _orleansExecutionTimes.Enqueue(executionTime.TotalMilliseconds);

            // Keep only last N measurements for memory efficiency
            while (_orleansExecutionTimes.Count > _metricsWindowSize)
            {
                _orleansExecutionTimes.TryDequeue(out _);
            }
        }
        // Note: Failed Orleans operations don't increment total operations
        // Total operations are incremented only for successful operations or final failures
    }

    public void RecordDirectServiceOperation(TimeSpan executionTime, bool success, bool isFallback)
    {
        if (!success)
        {
            return;
        }

        Interlocked.Increment(ref _totalOperations);
        Interlocked.Increment(ref _directServiceOperations);

        if (isFallback)
        {
            Interlocked.Increment(ref _fallbackOperations);
        }

        _directServiceExecutionTimes.Enqueue(executionTime.TotalMilliseconds);

        // Keep only last N measurements for memory efficiency
        while (_directServiceExecutionTimes.Count > _metricsWindowSize)
        {
            _directServiceExecutionTimes.TryDequeue(out _);
        }
    }

    public void RecordFailedOperation()
    {
        Interlocked.Increment(ref _totalOperations);
        Interlocked.Increment(ref _failedOperations);
    }

    public RouterMetrics GetCurrentMetrics()
    {
        lock (_metricsLock)
        {
            return new RouterMetrics
            {
                TotalOperations = _totalOperations,
                OrleansOperations = _orleansOperations,
                DirectServiceOperations = _directServiceOperations,
                FallbackOperations = _fallbackOperations,
                FailedOperations = _failedOperations,
                OrleansAverageExecutionTimeMs = CalculateAverage(_orleansExecutionTimes),
                DirectServiceAverageExecutionTimeMs = CalculateAverage(_directServiceExecutionTimes)
            };
        }
    }

    private static double CalculateAverage(ConcurrentQueue<double> values)
    {
        var array = values.ToArray();
        return array.Length > 0 ? array.Average() : 0.0;
    }
}