using Polly;

namespace AIChat.Server.Services.Streaming.Abstractions;

/// <summary>
/// Factory interface for creating resilience policies for streaming operations.
/// Abstracts the creation of retry, circuit breaker, and other resilience patterns.
/// </summary>
public interface IResiliencePolicyFactory
{
    /// <summary>
    /// Creates a complete resilience policy for stream processing.
    /// </summary>
    /// <param name="streamId">The stream identifier for context</param>
    /// <param name="options">Options for configuring the resilience policy</param>
    /// <returns>An async policy combining retry and circuit breaker patterns</returns>
    IAsyncPolicy CreateStreamResiliencePolicy(string streamId, StreamResilienceOptions options);

    /// <summary>
    /// Creates a retry policy with exponential backoff.
    /// </summary>
    /// <param name="options">Retry configuration options</param>
    /// <returns>An async retry policy</returns>
    IAsyncPolicy CreateRetryPolicy(RetryPolicyOptions options);

    /// <summary>
    /// Creates a circuit breaker policy.
    /// </summary>
    /// <param name="options">Circuit breaker configuration options</param>
    /// <returns>An async circuit breaker policy</returns>
    IAsyncPolicy CreateCircuitBreakerPolicy(CircuitBreakerOptions options);
}

/// <summary>
/// Options for configuring stream resilience policies.
/// </summary>
public class StreamResilienceOptions
{
    /// <summary>
    /// Gets or sets the retry policy options.
    /// </summary>
    public RetryPolicyOptions Retry { get; set; } = new();

    /// <summary>
    /// Gets or sets the circuit breaker options.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to combine policies.
    /// </summary>
    public bool CombinePolicies { get; set; } = true;

    /// <summary>
    /// Gets or sets the callback for state changes.
    /// </summary>
    public Action<string, ResilienceState>? OnStateChange { get; set; }
}

/// <summary>
/// Options for retry policy configuration.
/// </summary>
public class RetryPolicyOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the initial delay in milliseconds.
    /// </summary>
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum delay in milliseconds.
    /// </summary>
    public int MaxDelayMs { get; set; } = 32000;

    /// <summary>
    /// Gets or sets the jitter in milliseconds.
    /// </summary>
    public int JitterMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets whether to use exponential backoff.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Gets or sets the callback for retry attempts.
    /// </summary>
    public Action<int, TimeSpan, Exception?>? OnRetry { get; set; }
}

/// <summary>
/// Options for circuit breaker configuration.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the failure threshold before opening.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the recovery timeout in seconds.
    /// </summary>
    public int RecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the success threshold for closing.
    /// </summary>
    public int SuccessThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to use fallback.
    /// </summary>
    public bool UseFallback { get; set; } = true;

    /// <summary>
    /// Gets or sets the callback for circuit open.
    /// </summary>
    public Action<TimeSpan>? OnBreak { get; set; }

    /// <summary>
    /// Gets or sets the callback for circuit reset.
    /// </summary>
    public Action? OnReset { get; set; }

    /// <summary>
    /// Gets or sets the callback for half-open state.
    /// </summary>
    public Action? OnHalfOpen { get; set; }
}

/// <summary>
/// Represents the resilience state.
/// </summary>
public enum ResilienceState
{
    /// <summary>
    /// Normal operation.
    /// </summary>
    Normal,

    /// <summary>
    /// Retrying after failure.
    /// </summary>
    Retrying,

    /// <summary>
    /// Circuit is open.
    /// </summary>
    CircuitOpen,

    /// <summary>
    /// Circuit is half-open.
    /// </summary>
    CircuitHalfOpen,

    /// <summary>
    /// Operation failed.
    /// </summary>
    Failed
}