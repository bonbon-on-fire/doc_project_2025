using Microsoft.Extensions.Options;

namespace AIChat.Orleans.Client.Configuration;

/// <summary>
/// Validator for Orleans resilience configuration using IValidateOptions pattern.
/// Provides comprehensive validation of circuit breaker, retry, timeout, and bulkhead settings.
/// </summary>
public partial class OrleansResilienceConfigurationValidator : IValidateOptions<OrleansResilienceConfiguration>
{
    /// <summary>
    /// Validates the Orleans resilience configuration.
    /// </summary>
    /// <param name="name">The name of the options instance being validated</param>
    /// <param name="options">The options instance to validate</param>
    /// <returns>Validation result</returns>
    public ValidateOptionsResult Validate(string? name, OrleansResilienceConfiguration options)
    {
        var failures = new List<string>();

        // Validate Circuit Breaker settings
        ValidateCircuitBreaker(options.CircuitBreaker, failures);

        // Validate Retry Policy settings
        ValidateRetryPolicy(options.RetryPolicy, failures);

        // Validate Timeout settings
        ValidateTimeout(options.Timeout, failures);

        // Validate Bulkhead settings
        ValidateBulkhead(options.Bulkhead, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Validates circuit breaker configuration.
    /// </summary>
    private static void ValidateCircuitBreaker(CircuitBreakerSettings settings, List<string> failures)
    {
        if (settings.FailureThreshold is < 1 or > 20)
        {
            failures.Add($"CircuitBreaker.FailureThreshold must be between 1 and 20, but was {settings.FailureThreshold}");
        }

        if (settings.SamplingDurationSeconds is < 10 or > 300)
        {
            failures.Add($"CircuitBreaker.SamplingDurationSeconds must be between 10 and 300, but was {settings.SamplingDurationSeconds}");
        }

        if (settings.MinimumThroughput is < 1 or > 100)
        {
            failures.Add($"CircuitBreaker.MinimumThroughput must be between 1 and 100, but was {settings.MinimumThroughput}");
        }

        if (settings.BreakDurationSeconds is < 5 or > 120)
        {
            failures.Add($"CircuitBreaker.BreakDurationSeconds must be between 5 and 120, but was {settings.BreakDurationSeconds}");
        }
    }

    /// <summary>
    /// Validates retry policy configuration.
    /// </summary>
    private static void ValidateRetryPolicy(RetryPolicySettings settings, List<string> failures)
    {
        if (settings.MaxRetryAttempts is < 1 or > 10)
        {
            failures.Add($"RetryPolicy.MaxRetryAttempts must be between 1 and 10, but was {settings.MaxRetryAttempts}");
        }

        if (settings.BaseDelayMilliseconds is < 10 or > 5000)
        {
            failures.Add($"RetryPolicy.BaseDelayMilliseconds must be between 10 and 5000, but was {settings.BaseDelayMilliseconds}");
        }

        if (settings.MaxDelayMilliseconds is < 100 or > 60000)
        {
            failures.Add($"RetryPolicy.MaxDelayMilliseconds must be between 100 and 60000, but was {settings.MaxDelayMilliseconds}");
        }

        if (settings.MaxDelayMilliseconds <= settings.BaseDelayMilliseconds)
        {
            failures.Add($"RetryPolicy.MaxDelayMilliseconds ({settings.MaxDelayMilliseconds}) must be greater than BaseDelayMilliseconds ({settings.BaseDelayMilliseconds})");
        }

        // Validate retryable exceptions if specified
        if (settings.RetryableExceptions.Count > 0)
        {
            foreach (var exceptionType in settings.RetryableExceptions)
            {
                if (string.IsNullOrWhiteSpace(exceptionType))
                {
                    failures.Add("RetryPolicy.RetryableExceptions cannot contain null or empty exception type names");
                    break;
                }

                // Validate that it's a valid type name format
                if (!IsValidTypeName(exceptionType))
                {
                    failures.Add($"RetryPolicy.RetryableExceptions contains invalid type name: '{exceptionType}'");
                }
            }
        }
    }

    /// <summary>
    /// Validates timeout configuration.
    /// </summary>
    private static void ValidateTimeout(TimeoutSettings settings, List<string> failures)
    {
        if (settings.DefaultTimeoutSeconds is < 1 or > 300)
        {
            failures.Add($"Timeout.DefaultTimeoutSeconds must be between 1 and 300, but was {settings.DefaultTimeoutSeconds}");
        }

        if (settings.HealthCheckTimeoutSeconds is < 1 or > 30)
        {
            failures.Add($"Timeout.HealthCheckTimeoutSeconds must be between 1 and 30, but was {settings.HealthCheckTimeoutSeconds}");
        }

        if (settings.StateRetrievalTimeoutSeconds is < 1 or > 60)
        {
            failures.Add($"Timeout.StateRetrievalTimeoutSeconds must be between 1 and 60, but was {settings.StateRetrievalTimeoutSeconds}");
        }

        // Logical validation: health check timeout should be less than or equal to default timeout
        if (settings.HealthCheckTimeoutSeconds > settings.DefaultTimeoutSeconds)
        {
            failures.Add($"Timeout.HealthCheckTimeoutSeconds ({settings.HealthCheckTimeoutSeconds}) should not exceed DefaultTimeoutSeconds ({settings.DefaultTimeoutSeconds})");
        }

        // State retrieval timeout should be reasonable compared to default timeout
        if (settings.StateRetrievalTimeoutSeconds > settings.DefaultTimeoutSeconds)
        {
            failures.Add($"Timeout.StateRetrievalTimeoutSeconds ({settings.StateRetrievalTimeoutSeconds}) should not exceed DefaultTimeoutSeconds ({settings.DefaultTimeoutSeconds})");
        }
    }

    /// <summary>
    /// Validates bulkhead configuration.
    /// </summary>
    private static void ValidateBulkhead(BulkheadSettings settings, List<string> failures)
    {
        if (settings.MaxConcurrency is < 1 or > 100)
        {
            failures.Add($"Bulkhead.MaxConcurrency must be between 1 and 100, but was {settings.MaxConcurrency}");
        }

        if (settings.MaxQueuedItems is < 0 or > 200)
        {
            failures.Add($"Bulkhead.MaxQueuedItems must be between 0 and 200, but was {settings.MaxQueuedItems}");
        }

        // Logical validation: queue size should be reasonable compared to concurrency
        if (settings.MaxQueuedItems > settings.MaxConcurrency * 10)
        {
            failures.Add($"Bulkhead.MaxQueuedItems ({settings.MaxQueuedItems}) is excessively large compared to MaxConcurrency ({settings.MaxConcurrency}). Consider reducing queue size.");
        }
    }

    /// <summary>
    /// Validates if a string is a valid .NET type name format.
    /// This is a basic validation - for production, consider more robust type name validation.
    /// </summary>
    private static bool IsValidTypeName(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        // Basic validation: should contain namespace and type name
        // Allow generic types with backtick notation (e.g., List`1)
        var parts = typeName.Split('.');
        return parts.Length >= 2 &&
               parts.All(part => !string.IsNullOrWhiteSpace(part) &&
                               MyRegex().IsMatch(part));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_`]*$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
