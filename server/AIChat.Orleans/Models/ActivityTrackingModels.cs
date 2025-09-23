using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Enhanced activity tracking state with analytics integration and privacy compliance.
/// Part of ORL-ST-P2-005: Activity Tracking Enhancement.
/// Extends existing activity tracking with production-ready features.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityTrackingState")]
public sealed class ActivityTrackingState
{
    /// <summary>
    /// Enhanced activity buffer with privacy-compliant records.
    /// Maintains existing circular buffer behavior with privacy enhancements.
    /// </summary>
    [Id(0)]
    public Queue<PrivacyAwareActivityRecord> PrivacyCompliantActivities { get; set; } = new();

    /// <summary>
    /// Analytics export status and configuration.
    /// Tracks integration with telemetry systems and export history.
    /// </summary>
    [Id(1)]
    public AnalyticsIntegrationState AnalyticsState { get; set; } = new();

    /// <summary>
    /// Activity-specific metrics for performance monitoring.
    /// Provides detailed activity tracking performance data.
    /// </summary>
    [Id(2)]
    public ActivityMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Configuration for activity tracking behavior.
    /// Controls retention, privacy, and analytics settings.
    /// </summary>
    [Id(3)]
    public ActivityTrackingConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Last time activity tracking state was cleaned up.
    /// Used for scheduling periodic maintenance operations.
    /// </summary>
    [Id(4)]
    public DateTime LastCleanupAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of activity tracking implementation.
    /// Used for migration and compatibility checks.
    /// </summary>
    [Id(5)]
    public int ActivityTrackingVersion { get; set; } = 1;
}

/// <summary>
/// Privacy and compliance metadata for activity tracking.
/// Tracks consent, retention policies, and privacy audit information.
/// Part of ORL-ST-P2-005: Activity Tracking Enhancement.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityPrivacyMetadata")]
public sealed class ActivityPrivacyMetadata
{
    /// <summary>
    /// User consent settings for different activity types.
    /// Granular control over activity tracking permissions.
    /// </summary>
    [Id(0)]
    public Dictionary<ActivityType, ConsentRecord> ConsentSettings { get; set; } = [];

    /// <summary>
    /// Data retention policy currently applied.
    /// Defines how long different activity types are retained.
    /// </summary>
    [Id(1)]
    public ActivityRetentionPolicy RetentionPolicy { get; set; } = new();

    /// <summary>
    /// Privacy compliance status and audit information.
    /// Tracks compliance with privacy regulations.
    /// </summary>
    [Id(2)]
    public PrivacyComplianceStatus ComplianceStatus { get; set; } = new();

    /// <summary>
    /// PII detection and sanitization history.
    /// Audit trail of privacy protection actions.
    /// </summary>
    [Id(3)]
    public List<PrivacyAuditRecord> PrivacyAuditTrail { get; set; } = [];

    /// <summary>
    /// Data export requests and their status.
    /// Tracks GDPR data subject access requests.
    /// </summary>
    [Id(4)]
    public List<DataExportRequest> ExportRequests { get; set; } = [];

    /// <summary>
    /// Data deletion requests and confirmations.
    /// Tracks "right to be forgotten" implementations.
    /// </summary>
    [Id(5)]
    public List<DataDeletionRecord> DeletionRecords { get; set; } = [];

    /// <summary>
    /// Last privacy compliance check timestamp.
    /// </summary>
    [Id(6)]
    public DateTime LastComplianceCheckAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Privacy-aware activity record with compliance features.
/// Enhanced version of ActivityRecord with PII protection and analytics integration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PrivacyAwareActivityRecord")]
public sealed class PrivacyAwareActivityRecord
{
    /// <summary>
    /// Type of activity performed.
    /// </summary>
    [Id(0)]
    public ActivityType Type { get; set; }

    /// <summary>
    /// Sanitized metadata with PII removed or anonymized.
    /// Safe for analytics export and long-term storage.
    /// </summary>
    [Id(1)]
    public string SanitizedMetadata { get; set; } = string.Empty;

    /// <summary>
    /// Activity timestamp.
    /// </summary>
    [Id(2)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Unique identifier for correlation and tracing.
    /// </summary>
    [Id(3)]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Anonymized user identifier for analytics correlation.
    /// Allows analytics without exposing actual user ID.
    /// </summary>
    [Id(4)]
    public string? AnonymizedUserId { get; set; }

    /// <summary>
    /// Whether this activity has been exported to analytics systems.
    /// Prevents duplicate exports and tracks export status.
    /// </summary>
    [Id(5)]
    public bool ExportedToAnalytics { get; set; } = false;

    /// <summary>
    /// When this activity record expires based on retention policy.
    /// Null means no expiration or uses default policy.
    /// </summary>
    [Id(6)]
    public DateTime? RetentionExpiresAt { get; set; }

    /// <summary>
    /// Privacy tags and metadata for compliance tracking.
    /// </summary>
    [Id(7)]
    public PrivacyTags PrivacyTags { get; set; } = new();

    /// <summary>
    /// Analytics export attempts and their results.
    /// Tracks export history for debugging and reliability.
    /// </summary>
    [Id(8)]
    public List<AnalyticsExportAttempt> ExportAttempts { get; set; } = [];

    /// <summary>
    /// Hash of original metadata for integrity checking.
    /// Allows validation of sanitization without storing original PII.
    /// </summary>
    [Id(9)]
    public string? OriginalMetadataHash { get; set; }
}

/// <summary>
/// Analytics integration state and configuration.
/// Tracks export status and analytics system connectivity.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.AnalyticsIntegrationState")]
public sealed class AnalyticsIntegrationState
{
    /// <summary>
    /// Whether analytics integration is enabled.
    /// </summary>
    [Id(0)]
    public bool AnalyticsEnabled { get; set; } = true;

    /// <summary>
    /// Last successful export to each analytics system.
    /// </summary>
    [Id(1)]
    public Dictionary<string, DateTime> LastExportToSystem { get; set; } = [];

    /// <summary>
    /// Export queue for failed exports requiring retry.
    /// </summary>
    [Id(2)]
    public Queue<FailedExportRecord> FailedExports { get; set; } = new();

    /// <summary>
    /// Analytics systems that are currently available.
    /// </summary>
    [Id(3)]
    public Dictionary<string, AnalyticsSystemStatus> SystemStatus { get; set; } = [];

    /// <summary>
    /// Total number of activities exported to analytics.
    /// </summary>
    [Id(4)]
    public long TotalActivitiesExported { get; set; } = 0;

    /// <summary>
    /// Number of export failures.
    /// </summary>
    [Id(5)]
    public long TotalExportFailures { get; set; } = 0;

    /// <summary>
    /// Last analytics health check timestamp.
    /// </summary>
    [Id(6)]
    public DateTime LastHealthCheckAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Batch export configuration.
    /// </summary>
    [Id(7)]
    public BatchExportConfiguration BatchConfig { get; set; } = new();
}

/// <summary>
/// Activity-specific performance metrics.
/// Provides detailed tracking of activity recording and processing performance.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityMetrics")]
public sealed class ActivityMetrics
{
    /// <summary>
    /// Total number of activities recorded with privacy compliance.
    /// </summary>
    [Id(0)]
    public long TotalActivitiesRecorded { get; set; } = 0;

    /// <summary>
    /// When the last activity was recorded.
    /// </summary>
    [Id(1)]
    public DateTime LastRecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of activities by type.
    /// </summary>
    [Id(2)]
    public Dictionary<ActivityType, long> ActivitiesByType { get; set; } = [];

    /// <summary>
    /// Average time to sanitize activity metadata (milliseconds).
    /// </summary>
    [Id(3)]
    public double AverageSanitizationTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Average time to export to analytics (milliseconds).
    /// </summary>
    [Id(4)]
    public double AverageExportTimeMs { get; set; } = 0.0;

    /// <summary>
    /// Number of PII patterns detected and sanitized.
    /// </summary>
    [Id(5)]
    public long PIIPatternsDetected { get; set; } = 0;

    /// <summary>
    /// Number of activities removed due to retention policy.
    /// </summary>
    [Id(6)]
    public long ActivitiesRetentionRemoved { get; set; } = 0;

    /// <summary>
    /// Success rate for analytics exports (0.0 to 1.0).
    /// </summary>
    [Id(7)]
    public double AnalyticsExportSuccessRate { get; set; } = 1.0;

    /// <summary>
    /// Performance samples for trending analysis.
    /// </summary>
    [Id(8)]
    public Queue<ActivityPerformanceSample> PerformanceSamples { get; set; } = new();

    /// <summary>
    /// Last metrics reset timestamp.
    /// </summary>
    [Id(9)]
    public DateTime LastResetAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration for activity tracking behavior.
/// Controls all aspects of enhanced activity tracking.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityTrackingConfiguration")]
public sealed class ActivityTrackingConfiguration
{
    /// <summary>
    /// Maximum number of activities to keep in memory buffer.
    /// </summary>
    [Id(0)]
    public int MaxBufferSize { get; set; } = 100;

    /// <summary>
    /// Whether PII detection is enabled.
    /// </summary>
    [Id(1)]
    public bool PIIDetectionEnabled { get; set; } = true;

    /// <summary>
    /// Whether to require explicit consent for activity tracking.
    /// </summary>
    [Id(2)]
    public bool RequireExplicitConsent { get; set; } = false;

    /// <summary>
    /// Default retention period for activities.
    /// </summary>
    [Id(3)]
    public TimeSpan DefaultRetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether to anonymize data for analytics export.
    /// </summary>
    [Id(4)]
    public bool AnonymizeAnalyticsExport { get; set; } = true;

    /// <summary>
    /// Batch size for analytics exports.
    /// </summary>
    [Id(5)]
    public int AnalyticsExportBatchSize { get; set; } = 10;

    /// <summary>
    /// Retry policy for failed analytics exports.
    /// </summary>
    [Id(6)]
    public ExportRetryPolicy RetryPolicy { get; set; } = new();

    /// <summary>
    /// Custom retention policies by activity type.
    /// </summary>
    [Id(7)]
    public Dictionary<ActivityType, TimeSpan> CustomRetentionPolicies { get; set; } = [];

    /// <summary>
    /// PII detection patterns and configuration.
    /// </summary>
    [Id(8)]
    public PIIDetectionConfiguration PIIDetection { get; set; } = new();
}

/// <summary>
/// Privacy tags and metadata for activity records.
/// Provides detailed privacy compliance information.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PrivacyTags")]
public sealed class PrivacyTags
{
    /// <summary>
    /// Whether PII was detected in original metadata.
    /// </summary>
    [Id(0)]
    public bool PIIDetected { get; set; }

    /// <summary>
    /// Whether the activity metadata was sanitized.
    /// </summary>
    [Id(1)]
    public bool WasSanitized { get; set; }

    /// <summary>
    /// Sanitization methods applied.
    /// </summary>
    [Id(2)]
    public List<string> SanitizationMethods { get; set; } = [];

    /// <summary>
    /// Privacy compliance level (High, Medium, Low).
    /// </summary>
    [Id(3)]
    public string ComplianceLevel { get; set; } = "High";

    /// <summary>
    /// Whether analytics export is permitted.
    /// </summary>
    [Id(4)]
    public bool AnalyticsExportPermitted { get; set; } = true;

    /// <summary>
    /// Data classification tags.
    /// </summary>
    [Id(5)]
    public List<string> DataClassification { get; set; } = [];

    /// <summary>
    /// Consent source for this activity.
    /// </summary>
    [Id(6)]
    public string ConsentSource { get; set; } = "Implicit";
}

/// <summary>
/// Record of user consent for activity tracking.
/// Tracks granular consent preferences and their history.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConsentRecord")]
public sealed class ConsentRecord
{
    /// <summary>
    /// Whether consent is granted for this activity type.
    /// </summary>
    [Id(0)]
    public bool ConsentGranted { get; set; }

    /// <summary>
    /// When consent was granted or last updated.
    /// </summary>
    [Id(1)]
    public DateTime ConsentDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// How consent was obtained.
    /// </summary>
    [Id(2)]
    public string ConsentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Expiration date for consent (if applicable).
    /// </summary>
    [Id(3)]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Version of consent agreement.
    /// </summary>
    [Id(4)]
    public string ConsentVersion { get; set; } = "1.0";

    /// <summary>
    /// Additional consent metadata.
    /// </summary>
    [Id(5)]
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Data retention policy for activity records.
/// Defines how long different types of activities are kept.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityRetentionPolicy")]
public sealed class ActivityRetentionPolicy
{
    /// <summary>
    /// Default retention period for all activities.
    /// </summary>
    [Id(0)]
    public TimeSpan DefaultRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Specific retention periods by activity type.
    /// </summary>
    [Id(1)]
    public Dictionary<ActivityType, TimeSpan> TypeSpecificRetention { get; set; } = [];

    /// <summary>
    /// Whether to apply hard or soft deletion.
    /// </summary>
    [Id(2)]
    public bool HardDeletion { get; set; } = true;

    /// <summary>
    /// Policy name or identifier.
    /// </summary>
    [Id(3)]
    public string PolicyName { get; set; } = "Default";

    /// <summary>
    /// When this policy was created.
    /// </summary>
    [Id(4)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Policy version for tracking changes.
    /// </summary>
    [Id(5)]
    public string Version { get; set; } = "1.0";
}

/// <summary>
/// Privacy compliance status and audit information.
/// Tracks overall compliance with privacy regulations.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PrivacyComplianceStatus")]
public sealed class PrivacyComplianceStatus
{
    /// <summary>
    /// Overall compliance score (0.0 to 1.0).
    /// </summary>
    [Id(0)]
    public double ComplianceScore { get; set; } = 1.0;

    /// <summary>
    /// Regulations this system complies with.
    /// </summary>
    [Id(1)]
    public List<string> CompliantRegulations { get; set; } = ["GDPR"];

    /// <summary>
    /// Last compliance audit timestamp.
    /// </summary>
    [Id(2)]
    public DateTime LastAuditAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Outstanding compliance issues.
    /// </summary>
    [Id(3)]
    public List<string> OutstandingIssues { get; set; } = [];

    /// <summary>
    /// Compliance certificates or attestations.
    /// </summary>
    [Id(4)]
    public List<string> Certifications { get; set; } = [];

    /// <summary>
    /// Next scheduled compliance review.
    /// </summary>
    [Id(5)]
    public DateTime NextReviewAt { get; set; } = DateTime.UtcNow.AddMonths(3);
}

/// <summary>
/// Privacy audit record for tracking compliance actions.
/// Provides audit trail for privacy protection measures.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PrivacyAuditRecord")]
public sealed class PrivacyAuditRecord
{
    /// <summary>
    /// Type of privacy action performed.
    /// </summary>
    [Id(0)]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// When the action was performed.
    /// </summary>
    [Id(1)]
    public DateTime ActionTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Description of the action taken.
    /// </summary>
    [Id(2)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Activity or data affected by the action.
    /// </summary>
    [Id(3)]
    public string AffectedData { get; set; } = string.Empty;

    /// <summary>
    /// Result of the privacy action.
    /// </summary>
    [Id(4)]
    public string ActionResult { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for audit correlation.
    /// </summary>
    [Id(5)]
    public string AuditId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Performance sample for activity tracking metrics.
/// Used for trending and performance analysis.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ActivityPerformanceSample")]
public sealed class ActivityPerformanceSample
{
    /// <summary>
    /// When this sample was recorded.
    /// </summary>
    [Id(0)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of performance metric.
    /// </summary>
    [Id(1)]
    public string MetricType { get; set; } = string.Empty;

    /// <summary>
    /// Value of the performance metric.
    /// </summary>
    [Id(2)]
    public double Value { get; set; }

    /// <summary>
    /// Additional tags for metric categorization.
    /// </summary>
    [Id(3)]
    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// Failed analytics export record for retry processing.
/// Tracks export failures and retry attempts.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.FailedExportRecord")]
public sealed class FailedExportRecord
{
    /// <summary>
    /// Activity that failed to export.
    /// </summary>
    [Id(0)]
    public PrivacyAwareActivityRecord Activity { get; set; } = new();

    /// <summary>
    /// Target analytics system that failed.
    /// </summary>
    [Id(1)]
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Error message from export failure.
    /// </summary>
    [Id(2)]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// When the export first failed.
    /// </summary>
    [Id(3)]
    public DateTime FirstFailureAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When to next attempt export.
    /// </summary>
    [Id(4)]
    public DateTime NextRetryAt { get; set; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    [Id(5)]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Maximum number of retries before giving up.
    /// </summary>
    [Id(6)]
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Analytics export attempt record.
/// Tracks individual export attempts for debugging and reliability.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.AnalyticsExportAttempt")]
public sealed class AnalyticsExportAttempt
{
    /// <summary>
    /// When the export was attempted.
    /// </summary>
    [Id(0)]
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Target analytics system.
    /// </summary>
    [Id(1)]
    public string TargetSystem { get; set; } = string.Empty;

    /// <summary>
    /// Whether the export succeeded.
    /// </summary>
    [Id(2)]
    public bool Success { get; set; }

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    [Id(3)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Duration of the export attempt.
    /// </summary>
    [Id(4)]
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// HTTP status code or similar response indicator.
    /// </summary>
    [Id(5)]
    public int? ResponseCode { get; set; }
}

/// <summary>
/// Additional supporting models referenced in the main models above.
/// These provide configuration and status tracking for various components.
/// </summary>

/// <summary>
/// Status of an analytics system.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.AnalyticsSystemStatus")]
public sealed class AnalyticsSystemStatus
{
    [Id(0)] public bool IsAvailable { get; set; } = true;
    [Id(1)] public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    [Id(2)] public TimeSpan ResponseTime { get; set; }
    [Id(3)] public string? LastError { get; set; }
}

/// <summary>
/// Batch export configuration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.BatchExportConfiguration")]
public sealed class BatchExportConfiguration
{
    [Id(0)] public int BatchSize { get; set; } = 10;
    [Id(1)] public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMinutes(5);
    [Id(2)] public bool EnableBatching { get; set; } = true;
}

/// <summary>
/// Export retry policy configuration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ExportRetryPolicy")]
public sealed class ExportRetryPolicy
{
    [Id(0)] public int MaxRetries { get; set; } = 3;
    [Id(1)] public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    [Id(2)] public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    [Id(3)] public double BackoffMultiplier { get; set; } = 2.0;
}

/// <summary>
/// PII detection configuration.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PIIDetectionConfiguration")]
public sealed class PIIDetectionConfiguration
{
    [Id(0)] public bool Enabled { get; set; } = true;
    [Id(1)] public List<string> PIIPatterns { get; set; } = [];
    [Id(2)] public double ConfidenceThreshold { get; set; } = 0.8;
    [Id(3)] public bool StrictMode { get; set; } = false;
}

/// <summary>
/// Data export request for GDPR compliance.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DataExportRequest")]
public sealed class DataExportRequest
{
    [Id(0)] public string RequestId { get; set; } = Guid.NewGuid().ToString();
    [Id(1)] public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    [Id(2)] public string RequestedBy { get; set; } = string.Empty;
    [Id(3)] public string Status { get; set; } = "Pending";
    [Id(4)] public DateTime? CompletedAt { get; set; }
    [Id(5)] public string? ExportDataUrl { get; set; }
}

/// <summary>
/// Data deletion record for audit purposes.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.DataDeletionRecord")]
public sealed class DataDeletionRecord
{
    [Id(0)] public string DeletionId { get; set; } = Guid.NewGuid().ToString();
    [Id(1)] public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
    [Id(2)] public string RequestedBy { get; set; } = string.Empty;
    [Id(3)] public int RecordsDeleted { get; set; }
    [Id(4)] public List<string> DeletedDataTypes { get; set; } = [];
    [Id(5)] public string ConfirmationToken { get; set; } = Guid.NewGuid().ToString();
}