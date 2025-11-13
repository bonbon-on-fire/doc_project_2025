using System.Diagnostics;
using AIChat.Orleans.Contracts;

namespace AIChat.Orleans.Services;

/// <summary>
/// Service for exporting and aggregating user activity data for analytics.
/// Integrates with existing telemetry infrastructure while respecting privacy.
/// Part of ORL-ST-P2-005: Activity Tracking Enhancement.
/// </summary>
public interface IActivityAnalyticsService
{
    /// <summary>
    /// Exports activity records to telemetry systems.
    /// Integrates with IChatTelemetry to record participant actions and custom metrics.
    /// </summary>
    /// <param name="userId">User identifier for the activity</param>
    /// <param name="activity">Activity record to export</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating export success</returns>
    Task<AnalyticsExportResult> ExportActivityToTelemetryAsync(
        string userId,
        ActivityRecord activity,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Exports aggregated activity metrics to Orleans metrics collector.
    /// Provides grain-level activity statistics for monitoring dashboards.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="state">Current user grain state</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating metric export success</returns>
    Task<AnalyticsExportResult> ExportActivityMetricsAsync(
        string userId,
        UserGrainState state,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates distributed tracing activities for user actions.
    /// Integrates with OrleansActivitySource for end-to-end request correlation.
    /// </summary>
    /// <param name="userId">User identifier for tracing correlation</param>
    /// <param name="activityType">Type of activity being traced</param>
    /// <param name="tags">Optional additional tags for the trace</param>
    /// <returns>Activity for distributed tracing, or null if tracing disabled</returns>
    Activity? StartUserActivityTrace(
        string userId,
        ActivityType activityType,
        Dictionary<string, object>? tags = null
    );

    /// <summary>
    /// Records activity-specific custom metrics.
    /// Provides fine-grained activity analytics for performance monitoring.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="metricName">Name of the custom metric</param>
    /// <param name="value">Metric value to record</param>
    /// <param name="activityType">Activity type this metric relates to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating metric recording success</returns>
    Task<AnalyticsExportResult> RecordActivityMetricAsync(
        string userId,
        string metricName,
        double value,
        ActivityType activityType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets aggregated activity analytics for dashboards.
    /// Provides summarized activity patterns and insights for user behavior analysis.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="timeRange">Time range for analytics aggregation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated analytics summary</returns>
    Task<ActivityAnalyticsSummary> GetActivityAnalyticsAsync(
        string userId,
        TimeSpan timeRange,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validates analytics configuration and connectivity.
    /// Ensures all analytics integrations are properly configured and accessible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result for analytics integration</returns>
    Task<AnalyticsHealthResult> ValidateAnalyticsIntegrationAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batches multiple activity records for efficient export.
    /// Optimizes analytics export performance by reducing individual telemetry calls.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="activities">Collection of activity records to export</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch export result with individual activity status</returns>
    Task<BatchAnalyticsExportResult> ExportActivityBatchAsync(
        string userId,
        IEnumerable<ActivityRecord> activities,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Result of analytics export operations.
/// Provides detailed status and error information for troubleshooting.
/// </summary>
public sealed class AnalyticsExportResult
{
    /// <summary>
    /// Whether the export operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Exception details if available.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Telemetry systems that received the export.
    /// </summary>
    public List<string> ExportedToSystems { get; set; } = [];

    /// <summary>
    /// Duration of the export operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Additional metadata about the export operation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Creates a successful export result.
    /// </summary>
    /// <param name="duration">Export operation duration</param>
    /// <param name="exportedSystems">Systems that received the export</param>
    /// <returns>Success result</returns>
    public static AnalyticsExportResult CreateSuccess(TimeSpan duration, IEnumerable<string>? exportedSystems = null)
    {
        return new AnalyticsExportResult
        {
            Success = true,
            Duration = duration,
            ExportedToSystems = exportedSystems?.ToList() ?? []
        };
    }

    /// <summary>
    /// Creates a failed export result.
    /// </summary>
    /// <param name="errorMessage">Error description</param>
    /// <param name="exception">Exception if available</param>
    /// <param name="retryCount">Number of retries attempted</param>
    /// <returns>Failure result</returns>
    public static AnalyticsExportResult Failure(string errorMessage, Exception? exception = null, int retryCount = 0)
    {
        return new AnalyticsExportResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Exception = exception,
            RetryCount = retryCount
        };
    }
}

/// <summary>
/// Aggregated activity analytics for dashboard display.
/// Provides insights into user behavior patterns and system usage.
/// </summary>
public sealed class ActivityAnalyticsSummary
{
    /// <summary>
    /// User identifier for this analytics summary.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Time range covered by this analytics summary.
    /// </summary>
    public TimeSpan TimeRange { get; set; }

    /// <summary>
    /// Count of activities by type.
    /// </summary>
    public Dictionary<ActivityType, int> ActivityCounts { get; set; } = [];

    /// <summary>
    /// Average time between activities by type.
    /// Useful for identifying user engagement patterns.
    /// </summary>
    public Dictionary<ActivityType, TimeSpan> AverageTimeBetweenActivities { get; set; } = [];

    /// <summary>
    /// Hour of day when user is most active.
    /// </summary>
    public int MostActiveHour { get; set; }

    /// <summary>
    /// Identified user behavior patterns.
    /// </summary>
    public List<ActivityPattern> IdentifiedPatterns { get; set; } = [];

    /// <summary>
    /// Privacy metadata indicating anonymization status.
    /// </summary>
    public AnalyticsPrivacyMetadata Privacy { get; set; } = new();

    /// <summary>
    /// When this summary was generated.
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of activities in the summary period.
    /// </summary>
    public int TotalActivities { get; set; }

    /// <summary>
    /// Unique activity types observed.
    /// </summary>
    public int UniqueActivityTypes { get; set; }
}

/// <summary>
/// Identified user activity pattern.
/// Represents behavioral insights derived from activity analysis.
/// </summary>
public sealed class ActivityPattern
{
    /// <summary>
    /// Type of pattern identified.
    /// </summary>
    public string PatternType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the pattern.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Confidence level of pattern detection (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Frequency of pattern occurrence.
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// Activities that support this pattern.
    /// </summary>
    public List<ActivityType> SupportingActivities { get; set; } = [];
}

/// <summary>
/// Privacy metadata for analytics data.
/// Tracks anonymization and compliance status.
/// </summary>
public sealed class AnalyticsPrivacyMetadata
{
    /// <summary>
    /// Whether user data has been anonymized.
    /// </summary>
    public bool IsAnonymized { get; set; }

    /// <summary>
    /// Anonymization method used.
    /// </summary>
    public string? AnonymizationMethod { get; set; }

    /// <summary>
    /// Whether this data can be exported for analytics.
    /// </summary>
    public bool ExportAllowed { get; set; } = true;

    /// <summary>
    /// Data retention expiration date.
    /// </summary>
    public DateTime? RetentionExpiresAt { get; set; }
}

/// <summary>
/// Health check result for analytics integration.
/// Validates connectivity and configuration of analytics systems.
/// </summary>
public sealed class AnalyticsHealthResult
{
    /// <summary>
    /// Overall health status of analytics integration.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Health status of individual analytics systems.
    /// </summary>
    public Dictionary<string, bool> SystemHealth { get; set; } = [];

    /// <summary>
    /// Configuration validation results.
    /// </summary>
    public List<string> ConfigurationIssues { get; set; } = [];

    /// <summary>
    /// Connectivity test results.
    /// </summary>
    public List<string> ConnectivityIssues { get; set; } = [];

    /// <summary>
    /// When this health check was performed.
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Response times for analytics systems.
    /// </summary>
    public Dictionary<string, TimeSpan> ResponseTimes { get; set; } = [];
}

/// <summary>
/// Result of batch analytics export operations.
/// Provides detailed status for each activity in the batch.
/// </summary>
public sealed class BatchAnalyticsExportResult
{
    /// <summary>
    /// Overall success status of the batch operation.
    /// </summary>
    public bool OverallSuccess { get; set; }

    /// <summary>
    /// Results for individual activities in the batch.
    /// </summary>
    public Dictionary<string, AnalyticsExportResult> IndividualResults { get; set; } = [];

    /// <summary>
    /// Number of successfully exported activities.
    /// </summary>
    public int SuccessfulExports { get; set; }

    /// <summary>
    /// Number of failed exports.
    /// </summary>
    public int FailedExports { get; set; }

    /// <summary>
    /// Total duration of the batch operation.
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// Errors that affected the entire batch.
    /// </summary>
    public List<string> BatchErrors { get; set; } = [];
}
