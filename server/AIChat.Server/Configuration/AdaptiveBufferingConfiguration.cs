using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for adaptive buffer sizing in streaming operations.
/// </summary>
public class AdaptiveBufferingConfiguration
{
    /// <summary>
    /// Enable adaptive buffer sizing.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum buffer size.
    /// Default: 50 items.
    /// </summary>
    [Range(10, 1000, ErrorMessage = "MinSize must be between 10 and 1000")]
    public int MinSize { get; set; } = 50;

    /// <summary>
    /// Maximum buffer size.
    /// Default: 1000 items.
    /// </summary>
    [Range(100, 10000, ErrorMessage = "MaxSize must be between 100 and 10000")]
    public int MaxSize { get; set; } = 1000;

    /// <summary>
    /// Buffer utilization threshold for scaling up (percentage).
    /// Default: 80%.
    /// </summary>
    [Range(50, 95, ErrorMessage = "ScaleUpThreshold must be between 50 and 95")]
    public float ScaleUpThreshold { get; set; } = 80.0f;

    /// <summary>
    /// Buffer utilization threshold for scaling down (percentage).
    /// Default: 30%.
    /// </summary>
    [Range(10, 50, ErrorMessage = "ScaleDownThreshold must be between 10 and 50")]
    public float ScaleDownThreshold { get; set; } = 30.0f;

    /// <summary>
    /// Time window for calculating buffer usage trends (in minutes).
    /// Default: 5 minutes.
    /// </summary>
    [Range(1, 60, ErrorMessage = "WindowSizeMinutes must be between 1 and 60")]
    public int WindowSizeMinutes { get; set; } = 5;

    /// <summary>
    /// Scale factor for buffer size adjustments (1.5 = 50% increase).
    /// Default: 1.5.
    /// </summary>
    [Range(1.1, 3.0, ErrorMessage = "ScaleFactor must be between 1.1 and 3.0")]
    public float ScaleFactor { get; set; } = 1.5f;

    /// <summary>
    /// Cooldown period between scaling operations (in seconds).
    /// Default: 30 seconds.
    /// </summary>
    [Range(10, 300, ErrorMessage = "ScaleCooldownSeconds must be between 10 and 300")]
    public int ScaleCooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the adaptive buffering configuration.
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
        if (MinSize >= MaxSize)
        {
            errors.Add("MinSize must be less than MaxSize");
        }

        if (ScaleDownThreshold >= ScaleUpThreshold)
        {
            errors.Add("ScaleDownThreshold must be less than ScaleUpThreshold");
        }

        return errors.Count == 0;
    }
}
