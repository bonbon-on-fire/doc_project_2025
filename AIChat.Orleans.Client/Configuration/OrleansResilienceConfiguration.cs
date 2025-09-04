using System.ComponentModel.DataAnnotations;

namespace AIChat.Orleans.Client.Configuration;

/// <summary>
/// Configuration settings for Orleans resilience patterns using Polly.
/// </summary>
public class OrleansResilienceConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "OrleansResilience";

    /// <summary>
    /// Circuit breaker settings.
    /// </summary>
    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Retry policy settings.
    /// </summary>
    public RetryPolicySettings RetryPolicy { get; set; } = new();

    /// <summary>
    /// Timeout settings.
    /// </summary>
    public TimeoutSettings Timeout { get; set; } = new();

    /// <summary>
    /// Bulkhead isolation settings.
    /// </summary>
    public BulkheadSettings Bulkhead { get; set; } = new();
}

/// <summary>
/// Configuration for circuit breaker pattern.
/// </summary>
public class CircuitBreakerSettings
{
    /// <summary>
    /// Number of consecutive failures before opening the circuit.
    /// Default: 5
    /// </summary>
    [Range(1, 20)]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time window in seconds for counting failures.
    /// Default: 60 seconds
    /// </summary>
    [Range(10, 300)]
    public int SamplingDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Minimum throughput of operations before circuit breaker can open.
    /// Default: 10
    /// </summary>
    [Range(1, 100)]
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Duration in seconds the circuit remains open before attempting half-open.
    /// Default: 30 seconds
    /// </summary>
    [Range(5, 120)]
    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Whether circuit breaker is enabled.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for retry policies.
/// </summary>
public class RetryPolicySettings
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// Default: 3
    /// </summary>
    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff.
    /// Default: 100ms
    /// </summary>
    [Range(10, 5000)]
    public int BaseDelayMilliseconds { get; set; } = 100;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff.
    /// Default: 10000ms (10 seconds)
    /// </summary>
    [Range(100, 60000)]
    public int MaxDelayMilliseconds { get; set; } = 10000;

    /// <summary>
    /// Whether to use jitter in retry delays.
    /// Default: true
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Whether retry is enabled.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// List of exception types that should trigger retries.
    /// Empty list means all exceptions are retried.
    /// </summary>
    public List<string> RetryableExceptions { get; set; } = new()
    {
        "Orleans.Runtime.OrleansMessageRejectionException",
        "System.TimeoutException",
        "System.Net.Http.HttpRequestException"
    };
}

/// <summary>
/// Configuration for timeout policies.
/// </summary>
public class TimeoutSettings
{
    /// <summary>
    /// Default timeout in seconds for grain operations.
    /// Default: 30 seconds
    /// </summary>
    [Range(1, 300)]
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout in seconds for health check operations.
    /// Default: 5 seconds
    /// </summary>
    [Range(1, 30)]
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Timeout in seconds for state retrieval operations.
    /// Default: 10 seconds
    /// </summary>
    [Range(1, 60)]
    public int StateRetrievalTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Whether timeout is enabled.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for bulkhead isolation.
/// </summary>
public class BulkheadSettings
{
    /// <summary>
    /// Maximum number of concurrent executions.
    /// Default: 20
    /// </summary>
    [Range(1, 100)]
    public int MaxConcurrency { get; set; } = 20;

    /// <summary>
    /// Maximum number of operations to queue.
    /// Default: 50
    /// </summary>
    [Range(0, 200)]
    public int MaxQueuedItems { get; set; } = 50;

    /// <summary>
    /// Whether bulkhead isolation is enabled.
    /// Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
}