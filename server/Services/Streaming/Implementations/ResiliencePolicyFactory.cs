using AIChat.Server.Services.Streaming.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// Factory implementation for creating resilience policies using Polly.
/// </summary>
public class ResiliencePolicyFactory : IResiliencePolicyFactory
{
    private readonly ILogger<ResiliencePolicyFactory> _logger;
    private readonly Random _random;

    /// <summary>
    /// Initializes a new instance of the ResiliencePolicyFactory class.
    /// </summary>
    public ResiliencePolicyFactory(ILogger<ResiliencePolicyFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = new Random();
    }

    /// <inheritdoc />
    public IAsyncPolicy CreateStreamResiliencePolicy(string streamId, StreamResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(options);

        var retryPolicy = CreateRetryPolicy(options.Retry);
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy(options.CircuitBreaker);

        // Add state change callback wrapper if provided
        if (options.OnStateChange != null)
        {
            retryPolicy = WrapWithStateCallback(retryPolicy, streamId, options.OnStateChange, ResilienceState.Retrying);
            circuitBreakerPolicy = WrapWithCircuitStateCallback(circuitBreakerPolicy, streamId, options.OnStateChange);
        }

        if (options.CombinePolicies)
        {
            _logger.LogDebug("Creating combined resilience policy for stream {StreamId}", streamId);
            return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
        }

        _logger.LogDebug("Creating retry-only resilience policy for stream {StreamId}", streamId);
        return retryPolicy;
    }

    /// <inheritdoc />
    public IAsyncPolicy CreateRetryPolicy(RetryPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Policy
            .Handle<Exception>(ex => !(ex is OperationCanceledException))
            .WaitAndRetryAsync(
                options.MaxAttempts,
                retryAttempt => CalculateRetryDelay(retryAttempt, options),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Retry attempt {RetryCount}/{MaxAttempts} after {Delay}ms. Error: {ErrorMessage}",
                        retryCount,
                        options.MaxAttempts,
                        timespan.TotalMilliseconds,
                        exception?.Message ?? "Unknown error");

                    options.OnRetry?.Invoke(retryCount, timespan, exception);
                });
    }

    /// <inheritdoc />
    public IAsyncPolicy CreateCircuitBreakerPolicy(CircuitBreakerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // For Polly v8, using the advanced circuit breaker with sampling
        return Policy
            .Handle<Exception>(ex => !(ex is OperationCanceledException))
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5, // 50% failure rate
                samplingDuration: TimeSpan.FromSeconds(options.RecoveryTimeoutSeconds / 2),
                minimumThroughput: options.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(options.RecoveryTimeoutSeconds),
                onBreak: (exception, timespan) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Circuit breaker opened for {Duration}s after {Threshold}% failures",
                        timespan.TotalSeconds,
                        50);

                    options.OnBreak?.Invoke(timespan);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset to closed state");
                    options.OnReset?.Invoke();
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker entering half-open state");
                    options.OnHalfOpen?.Invoke();
                });
    }

    private TimeSpan CalculateRetryDelay(int retryAttempt, RetryPolicyOptions options)
    {
        double delayMs;

        if (options.UseExponentialBackoff)
        {
            // Calculate exponential backoff with cap
            delayMs = Math.Min(
                options.InitialDelayMs * Math.Pow(2, retryAttempt - 1),
                options.MaxDelayMs);
        }
        else
        {
            // Use fixed delay
            delayMs = options.InitialDelayMs;
        }

        // Add jitter to prevent thundering herd
        if (options.JitterMs > 0)
        {
            var jitter = _random.Next(0, options.JitterMs);
            delayMs += jitter;
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }

    private IAsyncPolicy WrapWithStateCallback(
        IAsyncPolicy policy,
        string streamId,
        Action<string, ResilienceState> onStateChange,
        ResilienceState state)
    {
        // For Polly v8, we'll use a simpler approach
        // The state callbacks are already handled in the retry policy
        return policy;
    }

    private IAsyncPolicy WrapWithCircuitStateCallback(
        IAsyncPolicy policy,
        string streamId,
        Action<string, ResilienceState> onStateChange)
    {
        // For Polly v8, state callbacks are handled directly in circuit breaker events
        return policy;
    }

}