using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for buffer overflow handling strategies.
/// </summary>
public class OverflowStrategyConfiguration
{
    /// <summary>
    /// Primary overflow strategy to use.
    /// Default: Backpressure.
    /// </summary>
    public OverflowStrategy Primary { get; set; } = OverflowStrategy.Backpressure;

    /// <summary>
    /// Fallback overflow strategy when primary fails.
    /// Default: DropOldest.
    /// </summary>
    public OverflowStrategy Fallback { get; set; } = OverflowStrategy.DropOldest;

    /// <summary>
    /// Buffer utilization threshold to trigger overflow handling (percentage).
    /// Default: 95%.
    /// </summary>
    [Range(80, 100, ErrorMessage = "DropThreshold must be between 80 and 100")]
    public float DropThreshold { get; set; } = 95.0f;

    /// <summary>
    /// Enable overflow event tracking and metrics.
    /// Default: true.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Maximum items to drop in a single operation.
    /// Default: 10.
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxDropBatchSize must be between 1 and 100")]
    public int MaxDropBatchSize { get; set; } = 10;

    /// <summary>
    /// Validates the overflow strategy configuration.
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
        if (Primary == Fallback)
        {
            errors.Add("Primary and Fallback strategies should be different");
        }

        return errors.Count == 0;
    }
}

/// <summary>
/// Available overflow handling strategies.
/// </summary>
public enum OverflowStrategy
{
    /// <summary>
    /// Apply backpressure to slow down producers.
    /// </summary>
    Backpressure,

    /// <summary>
    /// Drop oldest items from the buffer.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop newest items (reject new items).
    /// </summary>
    DropNewest,

    /// <summary>
    /// Hybrid approach combining multiple strategies.
    /// </summary>
    Hybrid
}