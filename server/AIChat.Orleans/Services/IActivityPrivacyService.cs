using AIChat.Orleans.Contracts;
using Orleans;

namespace AIChat.Orleans.Services;

/// <summary>
/// Service for ensuring privacy compliance in activity tracking.
/// Handles PII detection, anonymization, and data retention policies.
/// Part of ORL-ST-P2-005: Activity Tracking Enhancement.
/// </summary>
public interface IActivityPrivacyService
{
    /// <summary>
    /// Validates activity metadata for PII and sanitizes if necessary.
    /// Removes or anonymizes personally identifiable information to ensure privacy compliance.
    /// </summary>
    /// <param name="metadata">Raw activity metadata to validate and sanitize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sanitization result with cleaned metadata</returns>
    Task<PrivacySanitizationResult> SanitizeActivityMetadataAsync(
        string metadata,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies data retention policies to activity records.
    /// Removes expired activities based on configured retention policies.
    /// </summary>
    /// <param name="activities">Collection of activity records to evaluate</param>
    /// <param name="userId">User identifier for retention policy lookup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Retention result with updated activity collection</returns>
    Task<DataRetentionResult> ApplyRetentionPolicyAsync(
        Queue<ActivityRecord> activities,
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks user consent for activity tracking.
    /// Validates whether user has granted consent for specific activity types.
    /// </summary>
    /// <param name="userId">User identifier to check consent for</param>
    /// <param name="activityType">Type of activity requiring consent</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consent validation result</returns>
    Task<ConsentResult> ValidateUserConsentAsync(
        string userId,
        ActivityType activityType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Anonymizes activity data for analytics export.
    /// Creates privacy-compliant versions of activity records for external analytics.
    /// </summary>
    /// <param name="activity">Activity record to anonymize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Anonymization result with privacy-safe activity data</returns>
    Task<AnonymizationResult> AnonymizeActivityForAnalyticsAsync(
        ActivityRecord activity,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Exports user's activity data for GDPR compliance.
    /// Provides complete user activity data for data subject access requests.
    /// </summary>
    /// <param name="userId">User identifier for data export</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Data export result with user's activity information</returns>
    Task<DataExportResult> ExportUserActivityDataAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes user's activity data per user request.
    /// Implements "right to be forgotten" by removing all user activity data.
    /// </summary>
    /// <param name="userId">User identifier for data deletion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Data deletion result with confirmation details</returns>
    Task<DataDeletionResult> DeleteUserActivityDataAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validates privacy configuration and policies.
    /// Ensures privacy settings are correctly configured and compliant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Privacy compliance validation result</returns>
    Task<PrivacyComplianceResult> ValidatePrivacyComplianceAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates user consent preferences for activity tracking.
    /// Allows users to control granular activity tracking permissions.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="consentUpdates">Consent changes to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consent update result</returns>
    Task<ConsentUpdateResult> UpdateUserConsentAsync(
        string userId,
        Dictionary<ActivityType, bool> consentUpdates,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Detects PII patterns in text content.
    /// Identifies potential personally identifiable information for sanitization.
    /// </summary>
    /// <param name="content">Text content to analyze for PII</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>PII detection result with identified patterns</returns>
    Task<PIIDetectionResult> DetectPIIAsync(
        string content,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Result of privacy sanitization operations.
/// Contains sanitized metadata and details about changes made.
/// </summary>
public sealed class PrivacySanitizationResult
{
    /// <summary>
    /// Whether sanitization was successful.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Sanitized metadata with PII removed or anonymized.
    /// </summary>
    public string SanitizedMetadata { get; set; } = string.Empty;

    /// <summary>
    /// Original metadata before sanitization.
    /// </summary>
    public string OriginalMetadata { get; set; } = string.Empty;

    /// <summary>
    /// PII patterns that were detected and sanitized.
    /// </summary>
    public List<PIIPattern> DetectedPII { get; set; } = [];

    /// <summary>
    /// Sanitization actions performed.
    /// </summary>
    public List<SanitizationAction> ActionsPerformed { get; set; } = [];

    /// <summary>
    /// Whether any PII was found and processed.
    /// </summary>
    public bool PIIFound { get; set; }

    /// <summary>
    /// Errors encountered during sanitization.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings about potential privacy issues.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Result of data retention policy application.
/// Details activities removed and retention decisions made.
/// </summary>
public sealed class DataRetentionResult
{
    /// <summary>
    /// Whether retention policy was successfully applied.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Updated activity collection after retention policy.
    /// </summary>
    public Queue<ActivityRecord> UpdatedActivities { get; set; } = new();

    /// <summary>
    /// Number of activities removed due to retention policy.
    /// </summary>
    public int ActivitiesRemoved { get; set; }

    /// <summary>
    /// Activities that were removed (for audit purposes).
    /// </summary>
    public List<ActivityRecord> RemovedActivities { get; set; } = [];

    /// <summary>
    /// Retention policy that was applied.
    /// </summary>
    public string RetentionPolicyApplied { get; set; } = string.Empty;

    /// <summary>
    /// Next retention policy evaluation date.
    /// </summary>
    public DateTime NextEvaluationDate { get; set; }

    /// <summary>
    /// Errors encountered during retention processing.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result of user consent validation.
/// Indicates whether user has granted consent for activity tracking.
/// </summary>
public sealed class ConsentResult
{
    /// <summary>
    /// Whether user has granted consent for the activity type.
    /// </summary>
    public bool ConsentGranted { get; set; }

    /// <summary>
    /// When consent was granted or last updated.
    /// </summary>
    public DateTime? ConsentDate { get; set; }

    /// <summary>
    /// Source of consent (explicit, implicit, default).
    /// </summary>
    public ConsentSource ConsentSource { get; set; }

    /// <summary>
    /// Expiration date for consent (if applicable).
    /// </summary>
    public DateTime? ConsentExpiresAt { get; set; }

    /// <summary>
    /// Whether consent needs to be refreshed.
    /// </summary>
    public bool RequiresRefresh { get; set; }

    /// <summary>
    /// Reason if consent was denied or unavailable.
    /// </summary>
    public string? DenialReason { get; set; }

    /// <summary>
    /// Additional consent metadata.
    /// </summary>
    public Dictionary<string, object> ConsentMetadata { get; set; } = [];
}

/// <summary>
/// Result of activity anonymization for analytics.
/// Contains privacy-safe version of activity data.
/// </summary>
public sealed class AnonymizationResult
{
    /// <summary>
    /// Whether anonymization was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Anonymized activity record safe for analytics export.
    /// </summary>
    public ActivityRecord? AnonymizedActivity { get; set; }

    /// <summary>
    /// Anonymization techniques applied.
    /// </summary>
    public List<string> AnonymizationMethods { get; set; } = [];

    /// <summary>
    /// Fields that were anonymized.
    /// </summary>
    public List<string> AnonymizedFields { get; set; } = [];

    /// <summary>
    /// Anonymization quality score (0.0 to 1.0).
    /// Higher scores indicate better privacy protection.
    /// </summary>
    public double AnonymizationScore { get; set; }

    /// <summary>
    /// Whether the anonymized data maintains analytical utility.
    /// </summary>
    public bool MaintainsAnalyticalUtility { get; set; }

    /// <summary>
    /// Errors encountered during anonymization.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result of user data export for GDPR compliance.
/// Contains complete user activity data for data subject requests.
/// </summary>
public sealed class DataExportResult
{
    /// <summary>
    /// Whether data export was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Exported activity data in structured format.
    /// </summary>
    public UserActivityExport? ActivityData { get; set; }

    /// <summary>
    /// Export format used (JSON, XML, CSV).
    /// </summary>
    public string ExportFormat { get; set; } = "JSON";

    /// <summary>
    /// Size of exported data in bytes.
    /// </summary>
    public long DataSizeBytes { get; set; }

    /// <summary>
    /// When the export was generated.
    /// </summary>
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expiration date for the export data.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Errors encountered during export.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result of user data deletion operations.
/// Confirms successful deletion and provides audit information.
/// </summary>
public sealed class DataDeletionResult
{
    /// <summary>
    /// Whether data deletion was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of activity records deleted.
    /// </summary>
    public int RecordsDeleted { get; set; }

    /// <summary>
    /// Data types that were deleted.
    /// </summary>
    public List<string> DeletedDataTypes { get; set; } = [];

    /// <summary>
    /// When deletion was performed.
    /// </summary>
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Confirmation token for deletion audit.
    /// </summary>
    public string ConfirmationToken { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Whether deletion was complete or partial.
    /// </summary>
    public bool CompleteErasure { get; set; }

    /// <summary>
    /// Systems from which data was deleted.
    /// </summary>
    public List<string> AffectedSystems { get; set; } = [];

    /// <summary>
    /// Errors encountered during deletion.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result of privacy compliance validation.
/// Assesses overall compliance with privacy regulations and policies.
/// </summary>
public sealed class PrivacyComplianceResult
{
    /// <summary>
    /// Whether system is privacy compliant.
    /// </summary>
    public bool IsCompliant { get; set; }

    /// <summary>
    /// Privacy compliance score (0.0 to 1.0).
    /// </summary>
    public double ComplianceScore { get; set; }

    /// <summary>
    /// Compliance checks that passed.
    /// </summary>
    public List<string> PassedChecks { get; set; } = [];

    /// <summary>
    /// Compliance issues that need attention.
    /// </summary>
    public List<string> ComplianceIssues { get; set; } = [];

    /// <summary>
    /// Recommendations for improving compliance.
    /// </summary>
    public List<string> Recommendations { get; set; } = [];

    /// <summary>
    /// When compliance check was performed.
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Regulations assessed (GDPR, CCPA, etc.).
    /// </summary>
    public List<string> AssessedRegulations { get; set; } = [];
}

/// <summary>
/// Represents a detected PII pattern in content.
/// Used for identifying and sanitizing personally identifiable information.
/// </summary>
public sealed class PIIPattern
{
    /// <summary>
    /// Type of PII detected (email, phone, SSN, etc.).
    /// </summary>
    public string PatternType { get; set; } = string.Empty;

    /// <summary>
    /// Detected PII value.
    /// </summary>
    public string DetectedValue { get; set; } = string.Empty;

    /// <summary>
    /// Position in content where PII was found.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Length of detected PII.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Confidence score for PII detection (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Whether this PII should be removed or anonymized.
    /// </summary>
    public bool RequiresSanitization { get; set; } = true;
}

/// <summary>
/// Describes a sanitization action performed on content.
/// Provides audit trail for privacy compliance.
/// </summary>
public sealed class SanitizationAction
{
    /// <summary>
    /// Type of sanitization performed (removal, anonymization, masking).
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Field or content area that was sanitized.
    /// </summary>
    public string TargetField { get; set; } = string.Empty;

    /// <summary>
    /// Original value before sanitization.
    /// </summary>
    public string OriginalValue { get; set; } = string.Empty;

    /// <summary>
    /// Sanitized value after processing.
    /// </summary>
    public string SanitizedValue { get; set; } = string.Empty;

    /// <summary>
    /// Reason for sanitization.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// User activity export data for GDPR compliance.
/// Structured representation of all user activity data.
/// </summary>
public sealed class UserActivityExport
{
    /// <summary>
    /// User identifier.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// All activity records for the user.
    /// </summary>
    public List<ActivityRecord> Activities { get; set; } = [];

    /// <summary>
    /// Activity summary statistics.
    /// </summary>
    public Dictionary<string, object> Summary { get; set; } = [];

    /// <summary>
    /// Date range covered by export.
    /// </summary>
    public DateTimeOffset ExportStartDate { get; set; }

    /// <summary>
    /// End date for export data.
    /// </summary>
    public DateTimeOffset ExportEndDate { get; set; }

    /// <summary>
    /// Data sources included in export.
    /// </summary>
    public List<string> DataSources { get; set; } = [];
}

/// <summary>
/// Source of user consent.
/// Indicates how consent was obtained.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConsentSource")]
public enum ConsentSource
{
    /// <summary>
    /// User explicitly granted consent.
    /// </summary>
    [Id(0)]
    Explicit,

    /// <summary>
    /// Consent implied from user actions.
    /// </summary>
    [Id(1)]
    Implicit,

    /// <summary>
    /// Default system consent setting.
    /// </summary>
    [Id(2)]
    Default,

    /// <summary>
    /// Consent inherited from parent/guardian.
    /// </summary>
    [Id(3)]
    Inherited,

    /// <summary>
    /// Consent status unknown.
    /// </summary>
    [Id(4)]
    Unknown
}

/// <summary>
/// Result of consent update operations.
/// Confirms changes to user consent preferences.
/// </summary>
public sealed class ConsentUpdateResult
{
    /// <summary>
    /// Whether consent update was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Updated consent settings.
    /// </summary>
    public Dictionary<ActivityType, bool> UpdatedConsent { get; set; } = [];

    /// <summary>
    /// Previous consent settings for audit.
    /// </summary>
    public Dictionary<ActivityType, bool> PreviousConsent { get; set; } = [];

    /// <summary>
    /// When consent was updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Errors encountered during update.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result of PII detection operations.
/// Details personally identifiable information found in content.
/// </summary>
public sealed class PIIDetectionResult
{
    /// <summary>
    /// Whether any PII was detected.
    /// </summary>
    public bool PIIDetected { get; set; }

    /// <summary>
    /// Detected PII patterns.
    /// </summary>
    public List<PIIPattern> DetectedPatterns { get; set; } = [];

    /// <summary>
    /// Overall confidence score for detection (0.0 to 1.0).
    /// </summary>
    public double OverallConfidence { get; set; }

    /// <summary>
    /// Content categories that contain PII.
    /// </summary>
    public List<string> AffectedCategories { get; set; } = [];

    /// <summary>
    /// Recommended actions for detected PII.
    /// </summary>
    public List<string> RecommendedActions { get; set; } = [];
}