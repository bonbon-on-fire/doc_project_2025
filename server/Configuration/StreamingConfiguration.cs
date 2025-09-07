using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for the StreamingBridge component.
/// Controls buffer sizes, backpressure thresholds, and timeout settings.
/// </summary>
public class StreamingConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Streaming";

    /// <summary>
    /// Maximum buffer size for streaming data.
    /// Default: 100 items.
    /// </summary>
    [Range(10, 10000, ErrorMessage = "BufferSize must be between 10 and 10000")]
    public int BufferSize { get; set; } = 100;

    /// <summary>
    /// Backpressure threshold as a percentage of buffer utilization (0-100).
    /// When buffer utilization exceeds this threshold, backpressure is applied.
    /// Default: 80%.
    /// </summary>
    [Range(50, 95, ErrorMessage = "BackpressureThreshold must be between 50 and 95")]
    public float BackpressureThreshold { get; set; } = 80.0f;

    /// <summary>
    /// Timeout for write operations to the HTTP stream in milliseconds.
    /// Default: 30000ms (30 seconds).
    /// </summary>
    [Range(1000, 120000, ErrorMessage = "WriteTimeoutMs must be between 1000 and 120000")]
    public int WriteTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Delay to apply when backpressure is triggered in milliseconds.
    /// Default: 100ms.
    /// </summary>
    [Range(10, 5000, ErrorMessage = "BackpressureDelayMs must be between 10 and 5000")]
    public int BackpressureDelayMs { get; set; } = 100;

    /// <summary>
    /// Enable adaptive backpressure that adjusts delay based on buffer utilization.
    /// Default: true.
    /// </summary>
    public bool EnableAdaptiveBackpressure { get; set; } = true;

    /// <summary>
    /// Enable detailed telemetry for streaming operations.
    /// Default: true.
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;

    /// <summary>
    /// Maximum chunk size for SSE events in bytes.
    /// Default: 32768 (32KB).
    /// </summary>
    [Range(1024, 1048576, ErrorMessage = "MaxChunkSize must be between 1KB and 1MB")]
    public int MaxChunkSize { get; set; } = 32768;

    /// <summary>
    /// Interval for flushing the HTTP response stream in milliseconds.
    /// Default: 100ms.
    /// </summary>
    [Range(10, 1000, ErrorMessage = "FlushIntervalMs must be between 10 and 1000")]
    public int FlushIntervalMs { get; set; } = 100;

    /// <summary>
    /// Enable automatic retry on transient errors.
    /// Default: true.
    /// </summary>
    public bool EnableAutoRetry { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts for transient errors.
    /// Default: 3.
    /// </summary>
    [Range(1, 10, ErrorMessage = "MaxRetryAttempts must be between 1 and 10")]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Validates the configuration settings.
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise.</returns>
    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        var validationContext = new ValidationContext(this);
        var validationResults = new List<ValidationResult>();
        
        if (!Validator.TryValidateObject(this, validationContext, validationResults, true))
        {
            errors.AddRange(validationResults.Select(r => r.ErrorMessage ?? "Unknown validation error"));
        }

        // Additional custom validation
        if (BackpressureDelayMs >= WriteTimeoutMs)
        {
            errors.Add("BackpressureDelayMs must be less than WriteTimeoutMs");
        }

        if (FlushIntervalMs >= WriteTimeoutMs)
        {
            errors.Add("FlushIntervalMs must be less than WriteTimeoutMs");
        }

        return errors.Count == 0;
    }
}