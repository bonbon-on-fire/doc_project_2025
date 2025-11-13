using System.ComponentModel.DataAnnotations;
using Orleans;

namespace AIChat.Orleans.Contracts;

// ============================================================================
// Mode State Models
// ============================================================================

/// <summary>
/// Represents the complete state of a mode in the system.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeState")]
public sealed record ModeState
{
    /// <summary>
    /// Unique identifier for the mode.
    /// </summary>
    [Required]
    [Id(0)]
    public required string ModeId { get; init; }

    /// <summary>
    /// Display name of the mode.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the mode is optimized for.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    [Id(2)]
    public required string Description { get; init; }

    /// <summary>
    /// Current configuration of the mode.
    /// </summary>
    [Required]
    [Id(3)]
    public required ModeConfiguration Configuration { get; init; }

    /// <summary>
    /// Current status of the mode.
    /// </summary>
    [Id(4)]
    public ModeStatus Status { get; init; } = ModeStatus.Active;

    /// <summary>
    /// Whether this is a system-provided mode.
    /// </summary>
    [Id(5)]
    public bool IsSystem { get; init; }

    /// <summary>
    /// User ID of the mode owner (null for system modes).
    /// </summary>
    [Id(6)]
    public string? UserId { get; init; }

    /// <summary>
    /// Category for organizing modes.
    /// </summary>
    [StringLength(50)]
    [Id(7)]
    public string? Category { get; init; }

    /// <summary>
    /// Custom metadata associated with the mode.
    /// </summary>
    [Id(8)]
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Timestamp when the mode was created.
    /// </summary>
    [Id(9)]
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Timestamp when the mode was last modified.
    /// </summary>
    [Id(10)]
    public DateTime LastModifiedUtc { get; init; }

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// </summary>
    [Id(11)]
    public int Version { get; init; }
}

/// <summary>
/// Request to initialize a new mode.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeInitRequest")]
public sealed class ModeInitRequest
{
    /// <summary>
    /// Display name for the mode.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    [Id(0)]
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the mode is optimized for.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    [Id(1)]
    public required string Description { get; init; }

    /// <summary>
    /// Initial configuration for the mode.
    /// </summary>
    [Required]
    [Id(2)]
    public required ModeConfiguration Configuration { get; init; }

    /// <summary>
    /// Whether this is a system mode.
    /// </summary>
    [Id(3)]
    public bool IsSystem { get; init; }

    /// <summary>
    /// User ID creating the mode (required for custom modes).
    /// </summary>
    [Id(4)]
    public string? UserId { get; init; }

    /// <summary>
    /// Category for organizing modes.
    /// </summary>
    [StringLength(50)]
    [Id(5)]
    public string? Category { get; init; }

    /// <summary>
    /// Initial metadata to attach.
    /// </summary>
    [Id(6)]
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Template ID to base this mode on.
    /// </summary>
    [Id(7)]
    public string? TemplateId { get; init; }
}

/// <summary>
/// Represents a change event in mode history.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeChangeEvent")]
public sealed class ModeChangeEvent
{
    /// <summary>
    /// Unique identifier for the event.
    /// </summary>
    [Id(0)]
    public required string EventId { get; init; }

    /// <summary>
    /// Type of change that occurred.
    /// </summary>
    [Id(1)]
    public required ModeChangeType ChangeType { get; init; }

    /// <summary>
    /// Timestamp when the change occurred.
    /// </summary>
    [Id(2)]
    public required DateTime TimestampUtc { get; init; }

    /// <summary>
    /// User who made the change.
    /// </summary>
    [Id(3)]
    public string? UserId { get; init; }

    /// <summary>
    /// Description of the change.
    /// </summary>
    [Id(4)]
    public required string Description { get; init; }

    /// <summary>
    /// Previous value before the change (serialized).
    /// </summary>
    [Id(5)]
    public string? PreviousValue { get; init; }

    /// <summary>
    /// New value after the change (serialized).
    /// </summary>
    [Id(6)]
    public string? NewValue { get; init; }

    /// <summary>
    /// Additional change metadata.
    /// </summary>
    [Id(7)]
    public Dictionary<string, string>? Metadata { get; init; }
}

// ============================================================================
// Mode Configuration Models
// ============================================================================

/// <summary>
/// Represents the configuration of a mode.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeConfiguration")]
public sealed class ModeConfiguration
{
    /// <summary>
    /// System prompt that shapes the AI's behavior.
    /// </summary>
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    [Id(0)]
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Configuration parameters for the mode.
    /// </summary>
    [Id(1)]
    public Dictionary<string, string> Parameters { get; init; } = [];

    /// <summary>
    /// List of tool IDs available in this mode. Use ["*"] for all tools.
    /// </summary>
    [Required]
    [MinLength(1)]
    [Id(2)]
    public required List<string> Tools { get; init; }

    /// <summary>
    /// Features explicitly enabled for this mode.
    /// </summary>
    [Id(3)]
    public List<string> EnabledFeatures { get; init; } = [];

    /// <summary>
    /// Features explicitly disabled for this mode.
    /// </summary>
    [Id(4)]
    public List<string> DisabledFeatures { get; init; } = [];

    /// <summary>
    /// Preferred AI model for this mode.
    /// </summary>
    [StringLength(100)]
    [Id(5)]
    public string? DefaultModel { get; init; }

    /// <summary>
    /// Constraints applied to this mode.
    /// </summary>
    [Id(6)]
    public ModeConstraints? Constraints { get; init; }

    /// <summary>
    /// Temperature setting for AI responses.
    /// </summary>
    [Range(0.0, 2.0)]
    [Id(7)]
    public double? Temperature { get; init; }

    /// <summary>
    /// Maximum tokens for responses.
    /// </summary>
    [Range(1, 100000)]
    [Id(8)]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Response format preference.
    /// </summary>
    [Id(9)]
    public ResponseFormat? ResponseFormat { get; init; }
}

/// <summary>
/// Represents constraints for a mode.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeConstraints")]
public sealed class ModeConstraints
{
    /// <summary>
    /// Maximum message length allowed.
    /// </summary>
    [Range(1, 100000)]
    [Id(0)]
    public int? MaxMessageLength { get; init; }

    /// <summary>
    /// Maximum messages per minute rate limit.
    /// </summary>
    [Range(1, 1000)]
    [Id(1)]
    public int? MaxMessagesPerMinute { get; init; }

    /// <summary>
    /// Allowed file types for uploads.
    /// </summary>
    [Id(2)]
    public List<string>? AllowedFileTypes { get; init; }

    /// <summary>
    /// Maximum file size in bytes.
    /// </summary>
    [Range(1, long.MaxValue)]
    [Id(3)]
    public long? MaxFileSize { get; init; }

    /// <summary>
    /// Required user roles to use this mode.
    /// </summary>
    [Id(4)]
    public List<string>? RequiredRoles { get; init; }

    /// <summary>
    /// Time-based access restrictions.
    /// </summary>
    [Id(5)]
    public TimeRestrictions? TimeRestrictions { get; init; }

    /// <summary>
    /// Content filtering level.
    /// </summary>
    [Id(6)]
    public ContentFilterLevel? ContentFilter { get; init; }
}

/// <summary>
/// Represents a mode template that can be instantiated.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeTemplate")]
public sealed class ModeTemplate
{
    /// <summary>
    /// Unique identifier for the template.
    /// </summary>
    [Required]
    [Id(0)]
    public required string TemplateId { get; init; }

    /// <summary>
    /// Display name of the template.
    /// </summary>
    [Required]
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Description of the template.
    /// </summary>
    [Required]
    [Id(2)]
    public required string Description { get; init; }

    /// <summary>
    /// Category this template belongs to.
    /// </summary>
    [Id(3)]
    public string? Category { get; init; }

    /// <summary>
    /// Default configuration for modes created from this template.
    /// </summary>
    [Required]
    [Id(4)]
    public required ModeConfiguration DefaultConfiguration { get; init; }

    /// <summary>
    /// Whether this template is available for use.
    /// </summary>
    [Id(5)]
    public bool IsAvailable { get; init; } = true;

    /// <summary>
    /// Icon or image URL for the template.
    /// </summary>
    [Id(6)]
    public string? IconUrl { get; init; }

    /// <summary>
    /// Tags for searching and filtering.
    /// </summary>
    [Id(7)]
    public List<string> Tags { get; init; } = [];
}

// ============================================================================
// Mode Transition Models
// ============================================================================

/// <summary>
/// Request to transition between modes.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeTransitionRequest")]
public sealed class ModeTransitionRequest
{
    /// <summary>
    /// Target mode ID to transition to.
    /// </summary>
    [Required]
    [Id(0)]
    public required string TargetModeId { get; init; }

    /// <summary>
    /// Reason for the transition.
    /// </summary>
    [StringLength(500)]
    [Id(1)]
    public string? Reason { get; init; }

    /// <summary>
    /// Whether to preserve the current context.
    /// </summary>
    [Id(2)]
    public bool PreserveContext { get; init; } = true;

    /// <summary>
    /// Whether to preserve conversation history.
    /// </summary>
    [Id(3)]
    public bool PreserveHistory { get; init; } = true;

    /// <summary>
    /// Additional data for the transition.
    /// </summary>
    [Id(4)]
    public Dictionary<string, string>? TransitionData { get; init; }

    /// <summary>
    /// User initiating the transition.
    /// </summary>
    [Id(5)]
    public string? UserId { get; init; }
}

/// <summary>
/// Result of a mode transition.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeTransitionResult")]
public sealed class ModeTransitionResult
{
    /// <summary>
    /// Whether the transition succeeded.
    /// </summary>
    [Id(0)]
    public required bool Success { get; init; }

    /// <summary>
    /// New mode state after transition.
    /// </summary>
    [Id(1)]
    public ModeState? NewState { get; init; }

    /// <summary>
    /// Previous mode state before transition.
    /// </summary>
    [Id(2)]
    public ModeState? PreviousState { get; init; }

    /// <summary>
    /// Transition ID for tracking.
    /// </summary>
    [Id(3)]
    public required string TransitionId { get; init; }

    /// <summary>
    /// Error message if transition failed.
    /// </summary>
    [Id(4)]
    public string? Error { get; init; }

    /// <summary>
    /// Warnings generated during transition.
    /// </summary>
    [Id(5)]
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Time taken for the transition.
    /// </summary>
    [Id(6)]
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Represents a mode transition in history.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeTransition")]
public sealed class ModeTransition
{
    /// <summary>
    /// Unique identifier for the transition.
    /// </summary>
    [Id(0)]
    public required string TransitionId { get; init; }

    /// <summary>
    /// Source mode ID.
    /// </summary>
    [Id(1)]
    public required string SourceModeId { get; init; }

    /// <summary>
    /// Target mode ID.
    /// </summary>
    [Id(2)]
    public required string TargetModeId { get; init; }

    /// <summary>
    /// When the transition occurred.
    /// </summary>
    [Id(3)]
    public required DateTime TimestampUtc { get; init; }

    /// <summary>
    /// User who initiated the transition.
    /// </summary>
    [Id(4)]
    public string? UserId { get; init; }

    /// <summary>
    /// Reason for the transition.
    /// </summary>
    [Id(5)]
    public string? Reason { get; init; }

    /// <summary>
    /// Whether the transition succeeded.
    /// </summary>
    [Id(6)]
    public required bool Success { get; init; }

    /// <summary>
    /// Error if the transition failed.
    /// </summary>
    [Id(7)]
    public string? Error { get; init; }

    /// <summary>
    /// Duration of the transition.
    /// </summary>
    [Id(8)]
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Represents an available transition option.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeTransitionOption")]
public sealed class ModeTransitionOption
{
    /// <summary>
    /// Target mode ID.
    /// </summary>
    [Id(0)]
    public required string TargetModeId { get; init; }

    /// <summary>
    /// Display name of the target mode.
    /// </summary>
    [Id(1)]
    public required string TargetModeName { get; init; }

    /// <summary>
    /// Description of the target mode.
    /// </summary>
    [Id(2)]
    public string? Description { get; init; }

    /// <summary>
    /// Whether this transition is currently allowed.
    /// </summary>
    [Id(3)]
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// Reason if transition is not allowed.
    /// </summary>
    [Id(4)]
    public string? DisallowedReason { get; init; }

    /// <summary>
    /// Estimated cost of the transition.
    /// </summary>
    [Id(5)]
    public TransitionCost? Cost { get; init; }

    /// <summary>
    /// Required conditions for this transition.
    /// </summary>
    [Id(6)]
    public List<string> RequiredConditions { get; init; } = [];
}

/// <summary>
/// Request to schedule a future transition.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ScheduledTransitionRequest")]
public sealed class ScheduledTransitionRequest
{
    /// <summary>
    /// Target mode to transition to.
    /// </summary>
    [Id(0)]
    [Required]
    public required string TargetModeId { get; init; }

    /// <summary>
    /// When to execute the transition.
    /// </summary>
    [Id(1)]
    public required DateTime ScheduledTimeUtc { get; init; }

    /// <summary>
    /// Reason for the scheduled transition.
    /// </summary>
    [Id(2)]
    public string? Reason { get; init; }

    /// <summary>
    /// Whether to preserve context during transition.
    /// </summary>
    [Id(3)]
    public bool PreserveContext { get; init; } = true;

    /// <summary>
    /// Recurrence pattern if this is a recurring transition.
    /// </summary>
    [Id(4)]
    public RecurrencePattern? Recurrence { get; init; }
}

/// <summary>
/// Result of applying a mode preset.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModePresetResult")]
public sealed class ModePresetResult
{
    /// <summary>
    /// Whether the preset was applied successfully.
    /// </summary>
    [Id(0)]
    public required bool Success { get; init; }

    /// <summary>
    /// New mode state after applying preset.
    /// </summary>
    [Id(1)]
    public ModeState? NewState { get; init; }

    /// <summary>
    /// Changes made by the preset.
    /// </summary>
    [Id(2)]
    public List<string> AppliedChanges { get; init; } = [];

    /// <summary>
    /// Error if preset application failed.
    /// </summary>
    [Id(3)]
    public string? Error { get; init; }

    /// <summary>
    /// Warnings generated during preset application.
    /// </summary>
    [Id(4)]
    public List<string> Warnings { get; init; } = [];
}

// ============================================================================
// Mode Validation Models
// ============================================================================

/// <summary>
/// Result of mode validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeValidationResult")]
public sealed class ModeValidationResult
{
    /// <summary>
    /// Whether the mode is valid.
    /// </summary>
    [Id(0)]
    public required bool IsValid { get; init; }

    /// <summary>
    /// Mode details if valid.
    /// </summary>
    [Id(1)]
    public ModeState? Mode { get; init; }

    /// <summary>
    /// Validation errors found.
    /// </summary>
    [Id(2)]
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Validation warnings.
    /// </summary>
    [Id(3)]
    public List<ValidationWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Suggested fixes for validation issues.
    /// </summary>
    [Id(4)]
    public List<string> SuggestedFixes { get; init; } = [];
}

/// <summary>
/// Result of transition validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TransitionValidationResult")]
public sealed class TransitionValidationResult
{
    /// <summary>
    /// Whether the transition is valid.
    /// </summary>
    [Id(0)]
    public required bool IsValid { get; init; }

    /// <summary>
    /// Whether the transition is allowed.
    /// </summary>
    [Id(1)]
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// Reason if not allowed.
    /// </summary>
    [Id(2)]
    public string? DisallowedReason { get; init; }

    /// <summary>
    /// Validation errors.
    /// </summary>
    [Id(3)]
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Required conditions not met.
    /// </summary>
    [Id(4)]
    public List<string> UnmetConditions { get; init; } = [];

    /// <summary>
    /// Estimated impact of the transition.
    /// </summary>
    [Id(5)]
    public TransitionImpact? Impact { get; init; }
}

/// <summary>
/// Represents a mode validation rule.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeValidationRule")]
public sealed class ModeValidationRule
{
    /// <summary>
    /// Rule identifier.
    /// </summary>
    [Id(0)]
    public required string RuleId { get; init; }

    /// <summary>
    /// Rule name.
    /// </summary>
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Rule description.
    /// </summary>
    [Id(2)]
    public string? Description { get; init; }

    /// <summary>
    /// Rule category.
    /// </summary>
    [Id(3)]
    public required ValidationRuleCategory Category { get; init; }

    /// <summary>
    /// Severity if rule is violated.
    /// </summary>
    [Id(4)]
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Whether this rule is enforced.
    /// </summary>
    [Id(5)]
    public bool IsEnforced { get; init; } = true;

    /// <summary>
    /// Expression or logic for the rule.
    /// </summary>
    [Id(6)]
    public string? Expression { get; init; }
}

/// <summary>
/// Result of constraint checking.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConstraintCheckResult")]
public sealed class ConstraintCheckResult
{
    /// <summary>
    /// Whether all constraints are satisfied.
    /// </summary>
    [Id(0)]
    public required bool AllConstraintsSatisfied { get; init; }

    /// <summary>
    /// List of constraint violations.
    /// </summary>
    [Id(1)]
    public List<ConstraintViolation> Violations { get; init; } = [];

    /// <summary>
    /// Constraints that were checked.
    /// </summary>
    [Id(2)]
    public List<string> CheckedConstraints { get; init; } = [];

    /// <summary>
    /// Suggested resolutions for violations.
    /// </summary>
    [Id(3)]
    public List<string> SuggestedResolutions { get; init; } = [];
}

/// <summary>
/// Result of tool validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ToolValidationResult")]
public sealed class ToolValidationResult
{
    /// <summary>
    /// Whether all tools are valid.
    /// </summary>
    [Id(0)]
    public required bool AllToolsValid { get; init; }

    /// <summary>
    /// Tools that are valid and available.
    /// </summary>
    [Id(1)]
    public List<string> ValidTools { get; init; } = [];

    /// <summary>
    /// Tools that are invalid or unavailable.
    /// </summary>
    [Id(2)]
    public List<string> InvalidTools { get; init; } = [];

    /// <summary>
    /// Tools that require additional permissions.
    /// </summary>
    [Id(3)]
    public List<string> RequiresPermission { get; init; } = [];

    /// <summary>
    /// Detailed validation messages.
    /// </summary>
    [Id(4)]
    public Dictionary<string, string> ValidationMessages { get; init; } = [];
}

/// <summary>
/// Result of prompt validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PromptValidationResult")]
public sealed class PromptValidationResult
{
    /// <summary>
    /// Whether the prompt is valid.
    /// </summary>
    [Id(0)]
    public required bool IsValid { get; init; }

    /// <summary>
    /// Character count of the prompt.
    /// </summary>
    [Id(1)]
    public required int CharacterCount { get; init; }

    /// <summary>
    /// Estimated token count.
    /// </summary>
    [Id(2)]
    public int? EstimatedTokenCount { get; init; }

    /// <summary>
    /// Validation issues found.
    /// </summary>
    [Id(3)]
    public List<PromptIssue> Issues { get; init; } = [];

    /// <summary>
    /// Suggested improvements.
    /// </summary>
    [Id(4)]
    public List<string> Suggestions { get; init; } = [];

    /// <summary>
    /// Content policy violations if any.
    /// </summary>
    [Id(5)]
    public List<string> PolicyViolations { get; init; } = [];
}

/// <summary>
/// Result of permission validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PermissionValidationResult")]
public sealed class PermissionValidationResult
{
    /// <summary>
    /// Whether the user has permission.
    /// </summary>
    [Id(0)]
    public required bool HasPermission { get; init; }

    /// <summary>
    /// Action that was validated.
    /// </summary>
    [Id(1)]
    public required ModeAction Action { get; init; }

    /// <summary>
    /// Reason if permission is denied.
    /// </summary>
    [Id(2)]
    public string? DenialReason { get; init; }

    /// <summary>
    /// Required permissions that are missing.
    /// </summary>
    [Id(3)]
    public List<string> MissingPermissions { get; init; } = [];

    /// <summary>
    /// User's current permissions.
    /// </summary>
    [Id(4)]
    public List<string> CurrentPermissions { get; init; } = [];
}

/// <summary>
/// Comprehensive state validation report.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StateValidationReport")]
public sealed class StateValidationReport
{
    /// <summary>
    /// Overall validation status.
    /// </summary>
    [Id(0)]
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// Timestamp of validation.
    /// </summary>
    [Id(1)]
    public required DateTime ValidatedAtUtc { get; init; }

    /// <summary>
    /// Configuration validation results.
    /// </summary>
    [Id(2)]
    public ModeValidationResult? ConfigurationValidation { get; init; }

    /// <summary>
    /// Constraint validation results.
    /// </summary>
    [Id(3)]
    public ConstraintCheckResult? ConstraintValidation { get; init; }

    /// <summary>
    /// Tool validation results.
    /// </summary>
    [Id(4)]
    public ToolValidationResult? ToolValidation { get; init; }

    /// <summary>
    /// State consistency checks.
    /// </summary>
    [Id(5)]
    public List<ConsistencyCheck> ConsistencyChecks { get; init; } = [];

    /// <summary>
    /// Overall health score (0-100).
    /// </summary>
    [Range(0, 100)]
    [Id(6)]
    public int HealthScore { get; init; }

    /// <summary>
    /// Recommendations for improvement.
    /// </summary>
    [Id(7)]
    public List<string> Recommendations { get; init; } = [];
}

/// <summary>
/// Result of compatibility validation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.CompatibilityValidationResult")]
public sealed class CompatibilityValidationResult
{
    /// <summary>
    /// Whether the modes are compatible.
    /// </summary>
    [Id(0)]
    public required bool IsCompatible { get; init; }

    /// <summary>
    /// Compatibility score (0-100).
    /// </summary>
    [Range(0, 100)]
    [Id(1)]
    public int CompatibilityScore { get; init; }

    /// <summary>
    /// Incompatibilities found.
    /// </summary>
    [Id(2)]
    public List<Incompatibility> Incompatibilities { get; init; } = [];

    /// <summary>
    /// Warnings about the transition.
    /// </summary>
    [Id(3)]
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Data that would be lost in transition.
    /// </summary>
    [Id(4)]
    public List<string> DataLossRisks { get; init; } = [];
}

// ============================================================================
// Supporting Types and Enums
// ============================================================================

/// <summary>
/// Status of a mode.
/// </summary>
[GenerateSerializer]
public enum ModeStatus
{
    /// <summary>Mode is active and available</summary>
    [Id(0)]
    Active,
    /// <summary>Mode is inactive but available</summary>
    [Id(1)]
    Inactive,
    /// <summary>Mode is archived and read-only</summary>
    [Id(2)]
    Archived,
    /// <summary>Mode is being initialized</summary>
    [Id(3)]
    Initializing,
    /// <summary>Mode is in error state</summary>
    [Id(4)]
    Error,
    /// <summary>Mode is under maintenance</summary>
    [Id(5)]
    Maintenance
}

/// <summary>
/// Type of mode change.
/// </summary>
[GenerateSerializer]
public enum ModeChangeType
{
    /// <summary>Mode was created</summary>
    [Id(0)]
    Created,
    /// <summary>Configuration was updated</summary>
    [Id(1)]
    ConfigurationUpdated,
    /// <summary>Metadata was updated</summary>
    [Id(2)]
    MetadataUpdated,
    /// <summary>Status was changed</summary>
    [Id(3)]
    StatusChanged,
    /// <summary>Mode was archived</summary>
    [Id(4)]
    Archived,
    /// <summary>Mode was reset</summary>
    [Id(5)]
    Reset,
    /// <summary>Transition occurred</summary>
    [Id(6)]
    Transitioned,
    /// <summary>Mode was rolled back</summary>
    [Id(7)]
    RolledBack,
    /// <summary>Tools were updated</summary>
    [Id(8)]
    ToolsUpdated,
    /// <summary>Prompt was updated</summary>
    [Id(9)]
    PromptUpdated
}

/// <summary>
/// Actions that can be performed on modes.
/// </summary>
[GenerateSerializer]
public enum ModeAction
{
    /// <summary>View mode details</summary>
    [Id(0)]
    View,
    /// <summary>Create new mode</summary>
    [Id(1)]
    Create,
    /// <summary>Update mode configuration</summary>
    [Id(2)]
    Update,
    /// <summary>Delete mode</summary>
    [Id(3)]
    Delete,
    /// <summary>Archive mode</summary>
    [Id(4)]
    Archive,
    /// <summary>Transition to mode</summary>
    [Id(5)]
    Transition,
    /// <summary>Reset mode</summary>
    [Id(6)]
    Reset,
    /// <summary>Export mode configuration</summary>
    [Id(7)]
    Export,
    /// <summary>Import mode configuration</summary>
    [Id(8)]
    Import
}

/// <summary>
/// Category of validation rule.
/// </summary>
[GenerateSerializer]
public enum ValidationRuleCategory
{
    /// <summary>Configuration validation</summary>
    [Id(0)]
    Configuration,
    /// <summary>Security validation</summary>
    [Id(1)]
    Security,
    /// <summary>Performance validation</summary>
    [Id(2)]
    Performance,
    /// <summary>Compatibility validation</summary>
    [Id(3)]
    Compatibility,
    /// <summary>Content policy validation</summary>
    [Id(4)]
    ContentPolicy,
    /// <summary>Business rule validation</summary>
    [Id(5)]
    BusinessRule
}

/// <summary>
/// Severity of validation issues.
/// </summary>
[GenerateSerializer]
public enum ValidationSeverity
{
    /// <summary>Informational only</summary>
    [Id(0)]
    Info,
    /// <summary>Warning that should be addressed</summary>
    [Id(1)]
    Warning,
    /// <summary>Error that must be fixed</summary>
    [Id(2)]
    Error,
    /// <summary>Critical issue that blocks operation</summary>
    [Id(3)]
    Critical
}

/// <summary>
/// Overall validation status.
/// </summary>
[GenerateSerializer]
public enum ValidationStatus
{
    /// <summary>Validation passed</summary>
    [Id(0)]
    Valid,
    /// <summary>Validation passed with warnings</summary>
    [Id(1)]
    ValidWithWarnings,
    /// <summary>Validation failed</summary>
    [Id(2)]
    Invalid,
    /// <summary>Validation could not be completed</summary>
    [Id(3)]
    Unknown
}

/// <summary>
/// Response format preference.
/// </summary>
[GenerateSerializer]
public enum ResponseFormat
{
    /// <summary>Plain text responses</summary>
    [Id(0)]
    Text,
    /// <summary>Markdown formatted responses</summary>
    [Id(1)]
    Markdown,
    /// <summary>HTML formatted responses</summary>
    [Id(2)]
    Html,
    /// <summary>JSON structured responses</summary>
    [Id(3)]
    Json,
    /// <summary>Code-optimized responses</summary>
    [Id(4)]
    Code
}

/// <summary>
/// Content filtering level.
/// </summary>
[GenerateSerializer]
public enum ContentFilterLevel
{
    /// <summary>No filtering</summary>
    [Id(0)]
    None,
    /// <summary>Basic profanity filter</summary>
    [Id(1)]
    Basic,
    /// <summary>Moderate content filtering</summary>
    [Id(2)]
    Moderate,
    /// <summary>Strict content filtering</summary>
    [Id(3)]
    Strict,
    /// <summary>Custom filtering rules</summary>
    [Id(4)]
    Custom
}

// ============================================================================
// Helper Classes
// ============================================================================

/// <summary>
/// Represents a validation error.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ValidationError")]
public sealed class ValidationError
{
    /// <summary>
    /// Error code.
    /// </summary>
    [Id(0)]
    public required string Code { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    [Id(1)]
    public required string Message { get; init; }

    /// <summary>
    /// Field or property that caused the error.
    /// </summary>
    [Id(2)]
    public string? Field { get; init; }

    /// <summary>
    /// Severity of the error.
    /// </summary>
    [Id(3)]
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
}

/// <summary>
/// Represents a validation warning.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ValidationWarning")]
public sealed class ValidationWarning
{
    /// <summary>
    /// Warning code.
    /// </summary>
    [Id(0)]
    public required string Code { get; init; }

    /// <summary>
    /// Warning message.
    /// </summary>
    [Id(1)]
    public required string Message { get; init; }

    /// <summary>
    /// Suggested action to address the warning.
    /// </summary>
    [Id(2)]
    public string? SuggestedAction { get; init; }
}

/// <summary>
/// Represents a constraint violation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConstraintViolation")]
public sealed class ConstraintViolation
{
    /// <summary>
    /// Constraint that was violated.
    /// </summary>
    [Id(0)]
    public required string Constraint { get; init; }

    /// <summary>
    /// Actual value that violated the constraint.
    /// </summary>
    [Id(1)]
    public object? ActualValue { get; init; }

    /// <summary>
    /// Expected value or range.
    /// </summary>
    [Id(2)]
    public object? ExpectedValue { get; init; }

    /// <summary>
    /// Violation message.
    /// </summary>
    [Id(3)]
    public required string Message { get; init; }
}

/// <summary>
/// Represents a prompt validation issue.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PromptIssue")]
public sealed class PromptIssue
{
    /// <summary>
    /// Issue type.
    /// </summary>
    [Id(0)]
    public required string Type { get; init; }

    /// <summary>
    /// Issue description.
    /// </summary>
    [Id(1)]
    public required string Description { get; init; }

    /// <summary>
    /// Position in prompt where issue occurs.
    /// </summary>
    [Id(2)]
    public int? Position { get; init; }

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    [Id(3)]
    public ValidationSeverity Severity { get; init; }
}

/// <summary>
/// Represents a consistency check result.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ConsistencyCheck")]
public sealed class ConsistencyCheck
{
    /// <summary>
    /// Check name.
    /// </summary>
    [Id(0)]
    public required string CheckName { get; init; }

    /// <summary>
    /// Whether the check passed.
    /// </summary>
    [Id(1)]
    public required bool Passed { get; init; }

    /// <summary>
    /// Details about the check.
    /// </summary>
    [Id(2)]
    public string? Details { get; init; }
}

/// <summary>
/// Represents an incompatibility between modes.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.Incompatibility")]
public sealed class Incompatibility
{
    /// <summary>
    /// Type of incompatibility.
    /// </summary>
    [Id(0)]
    public required string Type { get; init; }

    /// <summary>
    /// Description of the incompatibility.
    /// </summary>
    [Id(1)]
    public required string Description { get; init; }

    /// <summary>
    /// Severity of the incompatibility.
    /// </summary>
    [Id(2)]
    public ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Possible resolution.
    /// </summary>
    [Id(3)]
    public string? Resolution { get; init; }
}

/// <summary>
/// Represents the cost of a transition.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TransitionCost")]
public sealed class TransitionCost
{
    /// <summary>
    /// Estimated time for transition.
    /// </summary>
    [Id(0)]
    public TimeSpan? EstimatedTime { get; init; }

    /// <summary>
    /// Data that might be lost.
    /// </summary>
    [Id(1)]
    public List<string> DataLoss { get; init; } = [];

    /// <summary>
    /// Features that will be unavailable.
    /// </summary>
    [Id(2)]
    public List<string> UnavailableFeatures { get; init; } = [];

    /// <summary>
    /// Performance impact description.
    /// </summary>
    [Id(3)]
    public string? PerformanceImpact { get; init; }
}

/// <summary>
/// Represents the impact of a transition.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TransitionImpact")]
public sealed class TransitionImpact
{
    /// <summary>
    /// Features that will be added.
    /// </summary>
    [Id(0)]
    public List<string> AddedFeatures { get; init; } = [];

    /// <summary>
    /// Features that will be removed.
    /// </summary>
    [Id(1)]
    public List<string> RemovedFeatures { get; init; } = [];

    /// <summary>
    /// Configuration changes.
    /// </summary>
    [Id(2)]
    public List<string> ConfigurationChanges { get; init; } = [];

    /// <summary>
    /// User experience impact.
    /// </summary>
    [Id(3)]
    public string? UserExperienceImpact { get; init; }
}

/// <summary>
/// Time-based access restrictions.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.TimeRestrictions")]
public sealed class TimeRestrictions
{
    /// <summary>
    /// Days of week when mode is available.
    /// </summary>
    [Id(0)]
    public List<DayOfWeek>? AllowedDays { get; init; }

    /// <summary>
    /// Start time of availability window (UTC).
    /// </summary>
    [Id(1)]
    public TimeOnly? StartTime { get; init; }

    /// <summary>
    /// End time of availability window (UTC).
    /// </summary>
    [Id(2)]
    public TimeOnly? EndTime { get; init; }

    /// <summary>
    /// Timezone for time restrictions.
    /// </summary>
    [Id(3)]
    public string? TimeZone { get; init; }
}

/// <summary>
/// Recurrence pattern for scheduled transitions.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.RecurrencePattern")]
public sealed class RecurrencePattern
{
    /// <summary>
    /// Type of recurrence.
    /// </summary>
    [Id(0)]
    public RecurrenceType Type { get; init; }

    /// <summary>
    /// Interval between occurrences.
    /// </summary>
    [Id(1)]
    public int Interval { get; init; } = 1;

    /// <summary>
    /// Days of week for weekly recurrence.
    /// </summary>
    [Id(2)]
    public List<DayOfWeek>? DaysOfWeek { get; init; }

    /// <summary>
    /// Day of month for monthly recurrence.
    /// </summary>
    [Id(3)]
    public int? DayOfMonth { get; init; }

    /// <summary>
    /// Maximum number of occurrences.
    /// </summary>
    [Id(4)]
    public int? MaxOccurrences { get; init; }

    /// <summary>
    /// End date for recurrence.
    /// </summary>
    [Id(5)]
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// Type of recurrence pattern.
/// </summary>
[GenerateSerializer]
public enum RecurrenceType
{
    /// <summary>Daily recurrence</summary>
    [Id(0)]
    Daily,
    /// <summary>Weekly recurrence</summary>
    [Id(1)]
    Weekly,
    /// <summary>Monthly recurrence</summary>
    [Id(2)]
    Monthly,
    /// <summary>Custom recurrence pattern</summary>
    [Id(3)]
    Custom
}

/// <summary>
/// Information about a potential mode for transition evaluation.
/// Used internally for determining available transitions.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.PotentialModeInfo")]
public sealed class PotentialModeInfo
{
    /// <summary>
    /// Mode identifier.
    /// </summary>
    [Id(0)]
    public required string ModeId { get; init; }

    /// <summary>
    /// Display name of the mode.
    /// </summary>
    [Id(1)]
    public required string Name { get; init; }

    /// <summary>
    /// Description of the mode.
    /// </summary>
    [Id(2)]
    public required string Description { get; init; }

    /// <summary>
    /// Category of the mode.
    /// </summary>
    [Id(3)]
    public required string Category { get; init; }

    /// <summary>
    /// Features provided by this mode.
    /// </summary>
    [Id(4)]
    public required List<string> Features { get; init; }

    /// <summary>
    /// Required permissions to use this mode.
    /// </summary>
    [Id(5)]
    public required List<string> RequiredPermissions { get; init; }

    /// <summary>
    /// Estimated time in milliseconds for transition to this mode.
    /// </summary>
    [Id(6)]
    public required int EstimatedTransitionTimeMs { get; init; }
}
