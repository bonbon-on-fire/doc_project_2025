using AIChat.Orleans.Metrics;

namespace AIChat.Server.Services.Metrics;

/// <summary>
/// Decorator for IOrleansMetricsCollector that implements circuit breaker pattern
/// to ensure metrics collection failures don't impact grain operations.
/// </summary>
public class ResilientMetricsCollectorDecorator : IOrleansMetricsCollector
{
    private readonly IOrleansMetricsCollector _inner;
    private readonly ILogger<ResilientMetricsCollectorDecorator> _logger;

    private int _consecutiveFailures;
    private DateTime _circuitOpenedAt = DateTime.MinValue;
    private readonly int _failureThreshold = 5;
    private readonly TimeSpan _circuitBreakerTimeout = TimeSpan.FromMinutes(1);

    private enum CircuitState
    {
        Closed = 0,
        Open = 1,
        HalfOpen = 2
    }

    public ResilientMetricsCollectorDecorator(
        IOrleansMetricsCollector inner,
        ILogger<ResilientMetricsCollectorDecorator> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private CircuitState GetCircuitState()
    {
        if (_consecutiveFailures < _failureThreshold)
        {
            return CircuitState.Closed;
        }

        var timeSinceOpen = DateTime.UtcNow - _circuitOpenedAt;
        if (timeSinceOpen > _circuitBreakerTimeout)
        {
            return CircuitState.HalfOpen;
        }

        return CircuitState.Open;
    }

    public async Task RecordGrainActivationAsync(string grainType, string grainId, double activationTime)
    {
        await ExecuteWithCircuitBreakerAsync(
            () => _inner.RecordGrainActivationAsync(grainType, grainId, activationTime),
            "RecordGrainActivation");
    }

    public async Task RecordGrainDeactivationAsync(string grainType, string grainId, double lifetimeMinutes)
    {
        await ExecuteWithCircuitBreakerAsync(
            () => _inner.RecordGrainDeactivationAsync(grainType, grainId, lifetimeMinutes),
            "RecordGrainDeactivation");
    }

    public async Task RecordGrainOperationAsync(
        string grainType,
        string operationType,
        double duration,
        bool success)
    {
        await ExecuteWithCircuitBreakerAsync(
            () => _inner.RecordGrainOperationAsync(grainType, operationType, duration, success),
            "RecordGrainOperation");
    }

    public async Task RecordGrainStateMetricsAsync(
        string grainType,
        string grainId,
        long stateSize,
        int connectionCount,
        int operationCount)
    {
        await ExecuteWithCircuitBreakerAsync(
            () => _inner.RecordGrainStateMetricsAsync(grainType, grainId, stateSize, connectionCount, operationCount),
            "RecordGrainStateMetrics");
    }

    public async Task<OrleansMetricsSummary> GetMetricsSummaryAsync()
    {
        return await ExecuteWithCircuitBreakerAsync(
            _inner.GetMetricsSummaryAsync,
            "GetMetricsSummary",
            () => Task.FromResult(new OrleansMetricsSummary()));
    }

    public async Task<GrainTypeMetrics> GetGrainTypeMetricsAsync(string grainType)
    {
        return await ExecuteWithCircuitBreakerAsync(
            () => _inner.GetGrainTypeMetricsAsync(grainType),
            "GetGrainTypeMetrics",
            () => Task.FromResult(new GrainTypeMetrics { GrainType = grainType }));
    }

    public async Task ResetMetricsAsync()
    {
        await ExecuteWithCircuitBreakerAsync(
            _inner.ResetMetricsAsync,
            "ResetMetrics");
    }

    private async Task ExecuteWithCircuitBreakerAsync(Func<Task> action, string operationName)
    {
        var state = GetCircuitState();

        if (state == CircuitState.Open)
        {
            _logger.LogDebug("Circuit breaker is open, skipping {OperationName}", operationName);
            return;
        }

        try
        {
            await action();

            if (state == CircuitState.HalfOpen)
            {
                _logger.LogInformation("Circuit breaker successfully completed operation in half-open state, closing circuit");
                ResetFailureCount();
            }
        }
        catch (Exception ex)
        {
            IncrementFailureCount();

            if (_consecutiveFailures == _failureThreshold)
            {
                _circuitOpenedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    ex,
                    "Circuit breaker opened after {FailureThreshold} consecutive failures for {OperationName}",
                    _failureThreshold,
                    operationName);
            }
            else
            {
                _logger.LogDebug(
                    ex,
                    "Metrics operation {OperationName} failed ({Failures}/{Threshold})",
                    operationName,
                    _consecutiveFailures,
                    _failureThreshold);
            }
        }
    }

    private async Task<T> ExecuteWithCircuitBreakerAsync<T>(
        Func<Task<T>> action,
        string operationName,
        Func<Task<T>>? fallbackAction = null)
    {
        var state = GetCircuitState();

        if (state == CircuitState.Open)
        {
            _logger.LogDebug("Circuit breaker is open, using fallback for {OperationName}", operationName);
            return fallbackAction != null ? await fallbackAction() : default!;
        }

        try
        {
            var result = await action();

            if (state == CircuitState.HalfOpen)
            {
                _logger.LogInformation("Circuit breaker successfully completed operation in half-open state, closing circuit");
                ResetFailureCount();
            }

            return result;
        }
        catch (Exception ex)
        {
            IncrementFailureCount();

            if (_consecutiveFailures == _failureThreshold)
            {
                _circuitOpenedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    ex,
                    "Circuit breaker opened after {FailureThreshold} consecutive failures for {OperationName}",
                    _failureThreshold,
                    operationName);
            }
            else
            {
                _logger.LogDebug(
                    ex,
                    "Metrics operation {OperationName} failed ({Failures}/{Threshold})",
                    operationName,
                    _consecutiveFailures,
                    _failureThreshold);
            }

            return fallbackAction != null ? await fallbackAction() : default!;
        }
    }

    private void ResetFailureCount()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        _circuitOpenedAt = DateTime.MinValue;
    }

    private void IncrementFailureCount()
    {
        Interlocked.Increment(ref _consecutiveFailures);
    }
}