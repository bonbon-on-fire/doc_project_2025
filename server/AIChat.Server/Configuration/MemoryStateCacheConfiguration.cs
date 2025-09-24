using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for the MemoryStateCacheManager.
/// Controls cache behavior, memory estimation, and metrics collection.
/// </summary>
public class MemoryStateCacheConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "MemoryStateCache";

    /// <summary>
    /// Default sliding expiration for cache entries in minutes.
    /// Used when no specific expiry is provided.
    /// Default: 30 minutes.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "DefaultSlidingExpirationMinutes must be between 1 and 1440 (24 hours)")]
    public int DefaultSlidingExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of cache entries to sample for memory estimation.
    /// Higher values provide more accurate estimates but with more CPU cost.
    /// Default: 10.
    /// </summary>
    [Range(1, 100, ErrorMessage = "MemoryEstimationSampleSize must be between 1 and 100")]
    public int MemoryEstimationSampleSize { get; set; } = 10;

    /// <summary>
    /// Fallback memory estimation per entry in bytes when sampling fails.
    /// Used as conservative estimate when actual sampling is not possible.
    /// Default: 512 bytes.
    /// </summary>
    [Range(64, 10240, ErrorMessage = "FallbackMemoryEstimationBytes must be between 64 bytes and 10KB")]
    public long FallbackMemoryEstimationBytes { get; set; } = 512L;

    /// <summary>
    /// Object overhead factor for JSON serialization memory estimation.
    /// Multiplier applied to JSON size to estimate in-memory object size.
    /// Default: 1.4 (40% overhead).
    /// </summary>
    [Range(1.0, 3.0, ErrorMessage = "ObjectOverheadFactor must be between 1.0 and 3.0")]
    public double ObjectOverheadFactor { get; set; } = 1.4;

    /// <summary>
    /// Base overhead per object in bytes for headers, references, etc.
    /// Added to calculated object size regardless of content.
    /// Default: 64 bytes.
    /// </summary>
    [Range(16, 256, ErrorMessage = "BaseObjectOverheadBytes must be between 16 and 256")]
    public long BaseObjectOverheadBytes { get; set; } = 64L;

    /// <summary>
    /// Maximum degree of parallelism for bulk cache operations.
    /// Set to 0 to use Environment.ProcessorCount automatically.
    /// Default: 0 (automatic).
    /// </summary>
    [Range(0, 64, ErrorMessage = "MaxDegreeOfParallelism must be between 0 and 64")]
    public int MaxDegreeOfParallelism { get; set; } = 0;

    /// <summary>
    /// Enable comprehensive logging for cache operations.
    /// When disabled, only warnings and errors are logged.
    /// Default: true.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// Enable atomic metrics collection for cache operations.
    /// When disabled, metrics collection is skipped for better performance.
    /// Default: true.
    /// </summary>
    public bool EnableMetricsCollection { get; set; } = true;

    /// <summary>
    /// Enable automatic cleanup of stale key tracking entries.
    /// When enabled, the key tracker is cleaned up during maintenance operations.
    /// Default: true.
    /// </summary>
    public bool EnableKeyTrackerCleanup { get; set; } = true;

    /// <summary>
    /// Validates the configuration settings.
    /// </summary>
    /// <returns>True if configuration is valid, false otherwise.</returns>
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

        // Additional custom validation
        if (ObjectOverheadFactor < 1.0)
        {
            errors.Add("ObjectOverheadFactor must be at least 1.0 to account for minimum object overhead");
        }

        if (BaseObjectOverheadBytes < 16)
        {
            errors.Add("BaseObjectOverheadBytes must be at least 16 bytes for basic object headers");
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Gets the effective max degree of parallelism.
    /// Returns Environment.ProcessorCount if MaxDegreeOfParallelism is 0.
    /// </summary>
    public int GetEffectiveMaxDegreeOfParallelism()
    {
        return MaxDegreeOfParallelism == 0 ? Environment.ProcessorCount : MaxDegreeOfParallelism;
    }

    /// <summary>
    /// Gets the default sliding expiration as TimeSpan.
    /// </summary>
    public TimeSpan GetDefaultSlidingExpiration()
    {
        return TimeSpan.FromMinutes(DefaultSlidingExpirationMinutes);
    }
}