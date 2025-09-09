using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for resilient streaming functionality.
/// Provides settings for reconnection, buffering, circuit breaker, and partial recovery.
/// </summary>
public class ResilientStreamingConfiguration
{
    /// <summary>
    /// Gets or sets whether resilient streaming is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the reconnection configuration.
    /// </summary>
    public ReconnectionConfiguration Reconnection { get; set; } = new();

    /// <summary>
    /// Gets or sets the buffer configuration.
    /// </summary>
    public BufferConfiguration Buffer { get; set; } = new();

    /// <summary>
    /// Gets or sets the circuit breaker configuration.
    /// </summary>
    public CircuitBreakerConfiguration CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Gets or sets the partial recovery configuration.
    /// </summary>
    public PartialRecoveryConfiguration PartialRecovery { get; set; } = new();

    /// <summary>
    /// Gets or sets the health check configuration.
    /// </summary>
    public HealthCheckConfiguration HealthCheck { get; set; } = new();

    /// <summary>
    /// Validates the configuration settings.
    /// </summary>
    /// <param name="errors">List of validation errors</param>
    /// <returns>True if configuration is valid, false otherwise</returns>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (!Reconnection.Validate(out var reconnectionErrors))
        {
            errors.AddRange(reconnectionErrors.Select(e => $"Reconnection: {e}"));
        }

        if (!Buffer.Validate(out var bufferErrors))
        {
            errors.AddRange(bufferErrors.Select(e => $"Buffer: {e}"));
        }

        if (!CircuitBreaker.Validate(out var cbErrors))
        {
            errors.AddRange(cbErrors.Select(e => $"CircuitBreaker: {e}"));
        }

        if (!PartialRecovery.Validate(out var prErrors))
        {
            errors.AddRange(prErrors.Select(e => $"PartialRecovery: {e}"));
        }

        if (!HealthCheck.Validate(out var hcErrors))
        {
            errors.AddRange(hcErrors.Select(e => $"HealthCheck: {e}"));
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Configuration for reconnection behavior.
/// </summary>
public class ReconnectionConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of reconnection attempts.
    /// </summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial delay in milliseconds between reconnection attempts.
    /// </summary>
    [Range(100, 60000)]
    public int InitialDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum delay in milliseconds between reconnection attempts.
    /// </summary>
    [Range(1000, 300000)]
    public int MaxDelayMs { get; set; } = 32000;

    /// <summary>
    /// Gets or sets the jitter in milliseconds to add to delays to prevent thundering herd.
    /// </summary>
    [Range(0, 5000)]
    public int JitterMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets whether to use exponential backoff for reconnection delays.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Validates the reconnection configuration.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (MaxAttempts < 1)
        {
            errors.Add("MaxAttempts must be at least 1");
        }

        if (InitialDelayMs < 100)
        {
            errors.Add("InitialDelayMs must be at least 100ms");
        }

        if (MaxDelayMs < InitialDelayMs)
        {
            errors.Add("MaxDelayMs must be greater than or equal to InitialDelayMs");
        }

        if (JitterMs < 0)
        {
            errors.Add("JitterMs cannot be negative");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Configuration for message buffering during disconnections.
/// </summary>
public class BufferConfiguration
{
    /// <summary>
    /// Gets or sets the maximum buffer size for messages.
    /// </summary>
    [Range(10, 10000)]
    public int Size { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the TTL in minutes for buffered messages.
    /// </summary>
    [Range(1, 60)]
    public int TTLMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets the size of the high-priority buffer for critical messages.
    /// </summary>
    [Range(10, 1000)]
    public int HighPrioritySize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the overflow strategy when buffer is full.
    /// Options: "DropOldest", "DropNewest", "RejectNew"
    /// </summary>
    public string OverflowStrategy { get; set; } = "DropOldest";

    /// <summary>
    /// Validates the buffer configuration.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (Size < 10)
        {
            errors.Add("Buffer Size must be at least 10");
        }

        if (TTLMinutes < 1)
        {
            errors.Add("TTLMinutes must be at least 1");
        }

        if (HighPrioritySize > Size)
        {
            errors.Add("HighPrioritySize cannot exceed total Size");
        }

        var validStrategies = new[] { "DropOldest", "DropNewest", "RejectNew" };
        if (!validStrategies.Contains(OverflowStrategy))
        {
            errors.Add($"OverflowStrategy must be one of: {string.Join(", ", validStrategies)}");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Configuration for circuit breaker pattern.
/// </summary>
public class CircuitBreakerConfiguration
{
    /// <summary>
    /// Gets or sets the number of failures before opening the circuit.
    /// </summary>
    [Range(1, 100)]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the time window in seconds for counting failures.
    /// </summary>
    [Range(10, 300)]
    public int FailureWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the timeout in seconds before attempting to close the circuit.
    /// </summary>
    [Range(5, 300)]
    public int RecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the number of successful operations needed to close the circuit.
    /// </summary>
    [Range(1, 10)]
    public int SuccessThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets whether to use a fallback mechanism when circuit is open.
    /// </summary>
    public bool UseFallback { get; set; } = true;

    /// <summary>
    /// Validates the circuit breaker configuration.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (FailureThreshold < 1)
        {
            errors.Add("FailureThreshold must be at least 1");
        }

        if (FailureWindowSeconds < 10)
        {
            errors.Add("FailureWindowSeconds must be at least 10");
        }

        if (RecoveryTimeoutSeconds < 5)
        {
            errors.Add("RecoveryTimeoutSeconds must be at least 5");
        }

        if (SuccessThreshold < 1)
        {
            errors.Add("SuccessThreshold must be at least 1");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Configuration for partial message recovery.
/// </summary>
public class PartialRecoveryConfiguration
{
    /// <summary>
    /// Gets or sets whether partial recovery is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of partial messages to store.
    /// </summary>
    [Range(1, 100)]
    public int MaxPartialMessages { get; set; } = 10;

    /// <summary>
    /// Gets or sets the timeout in seconds for incomplete chunks.
    /// </summary>
    [Range(10, 300)]
    public int ChunkTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to enable deduplication of recovered chunks.
    /// </summary>
    public bool EnableDeduplication { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum size in KB for storing partial messages.
    /// </summary>
    [Range(100, 10000)]
    public int MaxStorageSizeKb { get; set; } = 1000;

    /// <summary>
    /// Validates the partial recovery configuration.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (MaxPartialMessages < 1)
        {
            errors.Add("MaxPartialMessages must be at least 1");
        }

        if (ChunkTimeoutSeconds < 10)
        {
            errors.Add("ChunkTimeoutSeconds must be at least 10");
        }

        if (MaxStorageSizeKb < 100)
        {
            errors.Add("MaxStorageSizeKb must be at least 100");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Configuration for health check endpoints.
/// </summary>
public class HealthCheckConfiguration
{
    /// <summary>
    /// Gets or sets whether health checks are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the health check endpoint path.
    /// </summary>
    public string EndpointPath { get; set; } = "/api/health/streaming";

    /// <summary>
    /// Gets or sets the interval in seconds between health checks.
    /// </summary>
    [Range(5, 300)]
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether to include detailed metrics in health checks.
    /// </summary>
    public bool IncludeDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Validates the health check configuration.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        if (string.IsNullOrWhiteSpace(EndpointPath))
        {
            errors.Add("EndpointPath cannot be empty");
        }

        if (!EndpointPath.StartsWith('/'))
        {
            errors.Add("EndpointPath must start with /");
        }

        if (CheckIntervalSeconds < 5)
        {
            errors.Add("CheckIntervalSeconds must be at least 5");
        }

        return errors.Count == 0;
    }
}
