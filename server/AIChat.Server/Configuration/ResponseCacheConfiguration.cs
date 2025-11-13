using System.ComponentModel.DataAnnotations;

namespace AIChat.Server.Configuration;

/// <summary>
/// Configuration settings for the ResponseCacheManager.
/// Controls compression, health checks, monitoring, and maintenance operations.
/// </summary>
public class ResponseCacheConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "ResponseCache";

    /// <summary>
    /// Threshold size in bytes above which responses are considered for compression.
    /// Responses smaller than this threshold are not compressed to avoid overhead.
    /// Default: 1024 bytes (1KB).
    /// </summary>
    [Range(256, 1048576, ErrorMessage = "CompressionThresholdBytes must be between 256 bytes and 1MB")]
    public int CompressionThresholdBytes { get; set; } = 1024;

    /// <summary>
    /// Minimum compression ratio required to use compressed version.
    /// If compression doesn't achieve at least this ratio, original is used.
    /// Value of 0.8 means compressed size must be at most 80% of original.
    /// Default: 0.8 (80%).
    /// </summary>
    [Range(0.1, 0.95, ErrorMessage = "MinimumCompressionRatio must be between 0.1 and 0.95")]
    public double MinimumCompressionRatio { get; set; } = 0.8;

    /// <summary>
    /// Timeout for health check operations in milliseconds.
    /// If health check takes longer than this, it's considered failed.
    /// Default: 5000ms (5 seconds).
    /// </summary>
    [Range(1000, 30000, ErrorMessage = "HealthCheckTimeoutMs must be between 1 and 30 seconds")]
    public int HealthCheckTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Minimum cache hit ratio before warnings are issued.
    /// Below this ratio, cache is considered to have low performance.
    /// Default: 0.3 (30%).
    /// </summary>
    [Range(0.1, 0.9, ErrorMessage = "MinimumHitRatio must be between 0.1 and 0.9")]
    public double MinimumHitRatio { get; set; } = 0.3;

    /// <summary>
    /// Target cache hit ratio for optimal performance.
    /// Below this ratio, performance warnings are issued.
    /// Default: 0.5 (50%).
    /// </summary>
    [Range(0.2, 0.95, ErrorMessage = "TargetHitRatio must be between 0.2 and 0.95")]
    public double TargetHitRatio { get; set; } = 0.5;

    /// <summary>
    /// Memory usage percentage threshold for performance warnings.
    /// Above this percentage, elevated memory usage warnings are issued.
    /// Default: 75%.
    /// </summary>
    [Range(50, 95, ErrorMessage = "MemoryWarningThresholdPercent must be between 50 and 95")]
    public double MemoryWarningThresholdPercent { get; set; } = 75.0;

    /// <summary>
    /// Memory usage percentage threshold for critical warnings.
    /// Above this percentage, cache is considered unhealthy.
    /// Default: 90%.
    /// </summary>
    [Range(70, 99, ErrorMessage = "MemoryCriticalThresholdPercent must be between 70 and 99")]
    public double MemoryCriticalThresholdPercent { get; set; } = 90.0;

    /// <summary>
    /// Maximum acceptable average cache latency in milliseconds.
    /// Above this latency, high latency warnings are issued.
    /// Default: 10ms.
    /// </summary>
    [Range(1, 1000, ErrorMessage = "MaxAcceptableLatencyMs must be between 1 and 1000")]
    public double MaxAcceptableLatencyMs { get; set; } = 10.0;

    /// <summary>
    /// Memory budget for cache operations in bytes.
    /// Used for calculating memory usage percentages.
    /// Default: 104857600 (100MB).
    /// </summary>
    [Range(10485760, 1073741824, ErrorMessage = "MemoryBudgetBytes must be between 10MB and 1GB")]
    public long MemoryBudgetBytes { get; set; } = 104857600L; // 100MB

    /// <summary>
    /// Interval between cleanup operations in minutes.
    /// Cleanup removes stale metrics and performs maintenance.
    /// Default: 5 minutes.
    /// </summary>
    [Range(1, 60, ErrorMessage = "CleanupIntervalMinutes must be between 1 and 60")]
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Age threshold for stale operation metrics in hours.
    /// Metrics older than this are removed during cleanup.
    /// Default: 1 hour.
    /// </summary>
    [Range(1, 24, ErrorMessage = "StaleMetricsCutoffHours must be between 1 and 24")]
    public int StaleMetricsCutoffHours { get; set; } = 1;

    /// <summary>
    /// Enable automatic compression for large responses.
    /// When disabled, responses are never compressed.
    /// Default: true.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Enable periodic maintenance operations.
    /// When disabled, cleanup timer is not started.
    /// Default: true.
    /// </summary>
    public bool EnableMaintenance { get; set; } = true;

    /// <summary>
    /// Enable detailed health checking with performance scoring.
    /// When disabled, basic health checks only are performed.
    /// Default: true.
    /// </summary>
    public bool EnableDetailedHealthChecks { get; set; } = true;

    /// <summary>
    /// Enable comprehensive metrics collection.
    /// When disabled, basic metrics only are collected.
    /// Default: true.
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Enable cache warmup operations support.
    /// When disabled, warmup operations are skipped.
    /// Default: true.
    /// </summary>
    public bool EnableCacheWarmup { get; set; } = true;

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
        if (TargetHitRatio <= MinimumHitRatio)
        {
            errors.Add("TargetHitRatio must be greater than MinimumHitRatio");
        }

        if (MemoryCriticalThresholdPercent <= MemoryWarningThresholdPercent)
        {
            errors.Add("MemoryCriticalThresholdPercent must be greater than MemoryWarningThresholdPercent");
        }

        if (MinimumCompressionRatio >= 1.0)
        {
            errors.Add("MinimumCompressionRatio must be less than 1.0 to indicate compression benefit");
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Gets the cleanup interval as TimeSpan.
    /// </summary>
    public TimeSpan GetCleanupInterval()
    {
        return TimeSpan.FromMinutes(CleanupIntervalMinutes);
    }

    /// <summary>
    /// Gets the stale metrics cutoff as TimeSpan.
    /// </summary>
    public TimeSpan GetStaleMetricsCutoff()
    {
        return TimeSpan.FromHours(StaleMetricsCutoffHours);
    }

    /// <summary>
    /// Gets the health check timeout as TimeSpan.
    /// </summary>
    public TimeSpan GetHealthCheckTimeout()
    {
        return TimeSpan.FromMilliseconds(HealthCheckTimeoutMs);
    }

    /// <summary>
    /// Calculates memory usage percentage based on current usage and budget.
    /// </summary>
    /// <param name="currentUsageBytes">Current memory usage in bytes</param>
    /// <returns>Memory usage percentage</returns>
    public double CalculateMemoryUsagePercentage(long currentUsageBytes)
    {
        return (double)currentUsageBytes / MemoryBudgetBytes * 100.0;
    }
}
