using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for stream recovery mechanisms.
/// </summary>
public class RecoveryConfiguration
{
    /// <summary>
    /// Enable automatic reconnection on connection loss.
    /// Default: true.
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// Maximum number of reconnection attempts.
    /// Default: 5.
    /// </summary>
    [Range(1, 20, ErrorMessage = "MaxReconnectAttempts must be between 1 and 20")]
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Initial delay for reconnection attempts in milliseconds.
    /// Default: 1000ms (1 second).
    /// </summary>
    [Range(100, 10000, ErrorMessage = "ReconnectDelayMs must be between 100 and 10000")]
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay for reconnection attempts in milliseconds.
    /// Default: 30000ms (30 seconds).
    /// </summary>
    [Range(1000, 60000, ErrorMessage = "MaxReconnectDelayMs must be between 1000 and 60000")]
    public int MaxReconnectDelayMs { get; set; } = 30000;

    /// <summary>
    /// Backoff multiplier for exponential backoff.
    /// Default: 2.0 (double the delay each attempt).
    /// </summary>
    [Range(1.1, 5.0, ErrorMessage = "BackoffMultiplier must be between 1.1 and 5.0")]
    public float BackoffMultiplier { get; set; } = 2.0f;

    /// <summary>
    /// Enable message replay from buffer on recovery.
    /// Default: true.
    /// </summary>
    public bool EnableMessageReplay { get; set; } = true;

    /// <summary>
    /// Size of the replay buffer (number of messages to keep).
    /// Default: 100.
    /// </summary>
    [Range(10, 1000, ErrorMessage = "ReplayBufferSize must be between 10 and 1000")]
    public int ReplayBufferSize { get; set; } = 100;

    /// <summary>
    /// Maximum age of messages to replay in seconds.
    /// Default: 300 (5 minutes).
    /// </summary>
    [Range(10, 3600, ErrorMessage = "ReplayMaxAgeSeconds must be between 10 and 3600")]
    public int ReplayMaxAgeSeconds { get; set; } = 300;

    /// <summary>
    /// Enable checkpoint-based recovery for long streams.
    /// Default: true.
    /// </summary>
    public bool EnableCheckpoints { get; set; } = true;

    /// <summary>
    /// Interval for creating checkpoints (in number of messages).
    /// Default: 50.
    /// </summary>
    [Range(10, 500, ErrorMessage = "CheckpointInterval must be between 10 and 500")]
    public int CheckpointInterval { get; set; } = 50;

    /// <summary>
    /// Timeout for recovery operations in milliseconds.
    /// Default: 60000ms (60 seconds).
    /// </summary>
    [Range(5000, 300000, ErrorMessage = "RecoveryTimeoutMs must be between 5000 and 300000")]
    public int RecoveryTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Validates the recovery configuration.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public bool Validate(out List<string> errors)
    {
        errors = [];

        var validationContext = new ValidationContext(this);
        var validationResults = new List<ValidationResult>();

        if (!Validator.TryValidateObject(this, validationContext, validationResults, true))
        {
            errors.AddRange(
                validationResults.Select(r => r.ErrorMessage ?? "Unknown validation error")
            );
        }

        // Custom validation
        if (ReconnectDelayMs >= MaxReconnectDelayMs)
        {
            errors.Add("ReconnectDelayMs must be less than MaxReconnectDelayMs");
        }

        if (CheckpointInterval > ReplayBufferSize)
        {
            errors.Add("CheckpointInterval should not exceed ReplayBufferSize");
        }

        return errors.Count == 0;
    }
}