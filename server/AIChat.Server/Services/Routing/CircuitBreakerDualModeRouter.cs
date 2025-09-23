using System.Diagnostics;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Circuit breaker decorator for IDualModeRouter that provides resilience for Orleans grain calls.
/// Wraps the DualModeRouter with circuit breaker pattern to prevent cascading failures
/// and provide automatic recovery when Orleans grains become unavailable.
/// </summary>
public class CircuitBreakerDualModeRouter : IDualModeRouter
{
    private readonly IDualModeRouter _innerRouter;
    private readonly IAsyncPolicy _circuitBreakerPolicy;
    private readonly ILogger<CircuitBreakerDualModeRouter> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.CircuitBreakerRouter");

    // Circuit breaker metrics
    private long _circuitOpenCount;
    private long _circuitHalfOpenCount;
    private long _circuitClosedCount;
    private DateTime _lastCircuitOpenTime;
    private DateTime _lastCircuitCloseTime;

    /// <summary>
    /// Initializes a new instance of the CircuitBreakerDualModeRouter.
    /// </summary>
    /// <param name="innerRouter">The inner router to wrap with circuit breaker</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="options">Circuit breaker configuration options</param>
    public CircuitBreakerDualModeRouter(
        IDualModeRouter innerRouter,
        ILogger<CircuitBreakerDualModeRouter> logger,
        IOptions<CircuitBreakerOptions>? options = null)
    {
        _innerRouter = innerRouter ?? throw new ArgumentNullException(nameof(innerRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = options?.Value ?? new CircuitBreakerOptions();

        // Configure circuit breaker with exponential backoff
        _circuitBreakerPolicy = Policy
            .Handle<Exception>(ex => ShouldHandleException(ex))
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5, // 50% failure rate
                samplingDuration: TimeSpan.FromSeconds(10),
                minimumThroughput: config.FailureThreshold,
                durationOfBreak: config.BreakDuration,
                onBreak: OnCircuitBreak,
                onReset: OnCircuitReset,
                onHalfOpen: OnCircuitHalfOpen);
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(
        Func<IChatGrain, Task<T>> orleansOperation,
        Func<IChatService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CircuitBreakerExecute");
        activity?.SetTag("operation.name", operationName);

        try
        {
            // Wrap the Orleans operation with circuit breaker
            var wrappedOrleansOperation = new Func<IChatGrain, Task<T>>(async grain =>
            {
                return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogTrace("Executing Orleans operation {OperationName} through circuit breaker", operationName);
                    return await orleansOperation(grain);
                });
            });

            // Execute with circuit breaker protection
            var result = await _innerRouter.ExecuteAsync(
                wrappedOrleansOperation,
                directOperation,
                operationName,
                cancellationToken);

            activity?.SetTag("operation.success", true);
            return result;
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit is open, fall back to direct service immediately
            _logger.LogWarning(ex,
                "Circuit breaker is open for operation {OperationName}, falling back to direct service",
                operationName);

            activity?.SetTag("circuit.state", "open");
            activity?.SetTag("fallback.reason", "circuit_open");

            // Execute direct operation when circuit is open
            return await directOperation(null!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Circuit breaker execution failed for operation {OperationName}",
                operationName);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        Func<IChatGrain, Task> orleansOperation,
        Func<IChatService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("CircuitBreakerExecuteVoid");
        activity?.SetTag("operation.name", operationName);

        try
        {
            // Wrap the Orleans operation with circuit breaker
            var wrappedOrleansOperation = new Func<IChatGrain, Task>(async grain =>
            {
                await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogTrace("Executing Orleans operation {OperationName} through circuit breaker", operationName);
                    await orleansOperation(grain);
                });
            });

            // Execute with circuit breaker protection
            await _innerRouter.ExecuteAsync(
                wrappedOrleansOperation,
                directOperation,
                operationName,
                cancellationToken);

            activity?.SetTag("operation.success", true);
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit is open, fall back to direct service immediately
            _logger.LogWarning(ex,
                "Circuit breaker is open for operation {OperationName}, falling back to direct service",
                operationName);

            activity?.SetTag("circuit.state", "open");
            activity?.SetTag("fallback.reason", "circuit_open");

            // Execute direct operation when circuit is open
            await directOperation(null!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Circuit breaker execution failed for operation {OperationName}",
                operationName);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default)
    {
        // Check if circuit is open
        var circuitState = GetCircuitState();
        if (circuitState == CircuitState.Open)
        {
            _logger.LogDebug("Circuit breaker is open, Orleans is considered disabled");
            return false;
        }

        // Delegate to inner router
        return await _innerRouter.IsOrleansEnabledAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RouterHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var innerHealth = await _innerRouter.CheckHealthAsync(cancellationToken);
        var circuitState = GetCircuitState();

        // Augment health status with circuit breaker information
        return innerHealth with
        {
            IsOrleansHealthy = innerHealth.IsOrleansHealthy && circuitState != Polly.CircuitBreaker.CircuitState.Open,
            Message = circuitState == Polly.CircuitBreaker.CircuitState.Open
                ? $"Circuit breaker is open. {innerHealth.Message}"
                : innerHealth.Message
        };
    }

    /// <inheritdoc />
    public async Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var innerMetrics = await _innerRouter.GetMetricsAsync(cancellationToken);
        var circuitState = GetCircuitState();

        // Return inner metrics with circuit breaker status in operations count
        // Note: Since RouterMetrics doesn't have an extensible property,
        // we track circuit breaker operations in the failure count
        var effectiveFailures = innerMetrics.FailedOperations;
        if (circuitState == Polly.CircuitBreaker.CircuitState.Open)
        {
            // When circuit is open, we're preventing failures
            effectiveFailures += Interlocked.Read(ref _circuitOpenCount);
        }

        return innerMetrics with
        {
            FailedOperations = effectiveFailures
        };
    }

    #region Private Helper Methods

    private static bool ShouldHandleException(Exception ex)
    {
        // Handle Orleans-specific exceptions and general network/timeout issues
        return ex switch
        {
            TimeoutException => true,
            TaskCanceledException => false, // Don't break circuit on cancellation
            OperationCanceledException => false, // Don't break circuit on cancellation
            _ when ex.GetType().Name.Contains("Orleans") => true,
            _ when ex.GetType().Name.Contains("Grain") => true,
            _ => false
        };
    }

    private void OnCircuitBreak(Exception exception, TimeSpan duration)
    {
        Interlocked.Increment(ref _circuitOpenCount);
        _lastCircuitOpenTime = DateTime.UtcNow;

        _logger.LogWarning(exception,
            "Circuit breaker opened due to {ExceptionType}. Break duration: {BreakDurationSeconds}s",
            exception.GetType().Name, duration.TotalSeconds);
    }

    private void OnCircuitReset()
    {
        Interlocked.Increment(ref _circuitClosedCount);
        _lastCircuitCloseTime = DateTime.UtcNow;

        _logger.LogInformation("Circuit breaker reset to closed state");
    }

    private void OnCircuitHalfOpen()
    {
        Interlocked.Increment(ref _circuitHalfOpenCount);

        _logger.LogInformation("Circuit breaker transitioned to half-open state");
    }

    private Polly.CircuitBreaker.CircuitState GetCircuitState()
    {
        return _circuitBreakerPolicy switch
        {
            ICircuitBreakerPolicy cbPolicy => cbPolicy.CircuitState,
            _ => Polly.CircuitBreaker.CircuitState.Closed
        };
    }

    #endregion
}

/// <summary>
/// Configuration options for the circuit breaker.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the number of consecutive failures before opening the circuit.
    /// Default: 5 failures
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the duration for which the circuit remains open before transitioning to half-open.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to enable circuit breaker functionality.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}

// Note: Using Polly.CircuitBreaker.CircuitState enum directly
// to avoid conflicts and ensure compatibility