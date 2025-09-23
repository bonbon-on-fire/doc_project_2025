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
public sealed class ModeState
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
    public Dictionary<string, object> Metadata { get; init; } = [];

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
public sealed class ModeInitRequest
{
    /// <summary>
    /// Display name for the mode.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the mode is optimized for.
    /// </summary>
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Description { get; init; }

    /// <summary>
    /// Initial configuration for the mode.
    /// </summary>
    [Required]
    public required ModeConfiguration Configuration { get; init; }

    /// <summary>
    /// Whether this is a system mode.
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>
    /// User ID creating the mode (required for custom modes).
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Category for organizing modes.
    /// </summary>
    [StringLength(50)]
    public string? Category { get; init; }

    /// <summary>
    /// Initial metadata to attach.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Template ID to base this mode on.
    /// </summary>
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
    public Dictionary<string, object>? Metadata { get; init; }
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
    public Dictionary<string, object>? TransitionData { get; init; }

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
public sealed class ScheduledTransitionRequest
{
    /// <summary>
    /// Target mode to transition to.
    /// </summary>
    [Required]
    public required string TargetModeId { get; init; }

    /// <summary>
    /// When to execute the transition.
    /// </summary>
    public required DateTime ScheduledTimeUtc { get; init; }

    /// <summary>
    /// Reason for the scheduled transition.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Whether to preserve context during transition.
    /// </summary>
    public bool PreserveContext { get; init; } = true;

    /// <summary>
    /// Recurrence pattern if this is a recurring transition.
    /// </summary>
    public RecurrencePattern? Recurrence { get; init; }
}

/// <summary>
/// Result of applying a mode preset.
/// </summary>
public sealed class ModePresetResult
{
    /// <summary>
    /// Whether the preset was applied successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// New mode state after applying preset.
    /// </summary>
    public ModeState? NewState { get; init; }

    /// <summary>
    /// Changes made by the preset.
    /// </summary>
    public List<string> AppliedChanges { get; init; } = [];

    /// <summary>
    /// Error if preset application failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Warnings generated during preset application.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}

// ============================================================================
// Mode Validation Models
// ============================================================================

/// <summary>
/// Result of mode validation.
/// </summary>
public sealed class ModeValidationResult
{
    /// <summary>
    /// Whether the mode is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Mode details if valid.
    /// </summary>
    public ModeState? Mode { get; init; }

    /// <summary>
    /// Validation errors found.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public List<ValidationWarning> Warnings { get; init; } = [];

    /// <summary>
    /// Suggested fixes for validation issues.
    /// </summary>
    public List<string> SuggestedFixes { get; init; } = [];
}

/// <summary>
/// Result of transition validation.
/// </summary>
public sealed class TransitionValidationResult
{
    /// <summary>
    /// Whether the transition is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Whether the transition is allowed.
    /// </summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// Reason if not allowed.
    /// </summary>
    public string? DisallowedReason { get; init; }

    /// <summary>
    /// Validation errors.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Required conditions not met.
    /// </summary>
    public List<string> UnmetConditions { get; init; } = [];

    /// <summary>
    /// Estimated impact of the transition.
    /// </summary>
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
public sealed class ConstraintCheckResult
{
    /// <summary>
    /// Whether all constraints are satisfied.
    /// </summary>
    public required bool AllConstraintsSatisfied { get; init; }

    /// <summary>
    /// List of constraint violations.
    /// </summary>
    public List<ConstraintViolation> Violations { get; init; } = [];

    /// <summary>
    /// Constraints that were checked.
    /// </summary>
    public List<string> CheckedConstraints { get; init; } = [];

    /// <summary>
    /// Suggested resolutions for violations.
    /// </summary>
    public List<string> SuggestedResolutions { get; init; } = [];
}

/// <summary>
/// Result of tool validation.
/// </summary>
public sealed class ToolValidationResult
{
    /// <summary>
    /// Whether all tools are valid.
    /// </summary>
    public required bool AllToolsValid { get; init; }

    /// <summary>
    /// Tools that are valid and available.
    /// </summary>
    public List<string> ValidTools { get; init; } = [];

    /// <summary>
    /// Tools that are invalid or unavailable.
    /// </summary>
    public List<string> InvalidTools { get; init; } = [];

    /// <summary>
    /// Tools that require additional permissions.
    /// </summary>
    public List<string> RequiresPermission { get; init; } = [];

    /// <summary>
    /// Detailed validation messages.
    /// </summary>
    public Dictionary<string, string> ValidationMessages { get; init; } = [];
}

/// <summary>
/// Result of prompt validation.
/// </summary>
public sealed class PromptValidationResult
{
    /// <summary>
    /// Whether the prompt is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Character count of the prompt.
    /// </summary>
    public required int CharacterCount { get; init; }

    /// <summary>
    /// Estimated token count.
    /// </summary>
    public int? EstimatedTokenCount { get; init; }

    /// <summary>
    /// Validation issues found.
    /// </summary>
    public List<PromptIssue> Issues { get; init; } = [];

    /// <summary>
    /// Suggested improvements.
    /// </summary>
    public List<string> Suggestions { get; init; } = [];

    /// <summary>
    /// Content policy violations if any.
    /// </summary>
    public List<string> PolicyViolations { get; init; } = [];
}

/// <summary>
/// Result of permission validation.
/// </summary>
public sealed class PermissionValidationResult
{
    /// <summary>
    /// Whether the user has permission.
    /// </summary>
    public required bool HasPermission { get; init; }

    /// <summary>
    /// Action that was validated.
    /// </summary>
    public required ModeAction Action { get; init; }

    /// <summary>
    /// Reason if permission is denied.
    /// </summary>
    public string? DenialReason { get; init; }

    /// <summary>
    /// Required permissions that are missing.
    /// </summary>
    public List<string> MissingPermissions { get; init; } = [];

    /// <summary>
    /// User's current permissions.
    /// </summary>
    public List<string> CurrentPermissions { get; init; } = [];
}

/// <summary>
/// Comprehensive state validation report.
/// </summary>
public sealed class StateValidationReport
{
    /// <summary>
    /// Overall validation status.
    /// </summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// Timestamp of validation.
    /// </summary>
    public required DateTime ValidatedAtUtc { get; init; }

    /// <summary>
    /// Configuration validation results.
    /// </summary>
    public ModeValidationResult? ConfigurationValidation { get; init; }

    /// <summary>
    /// Constraint validation results.
    /// </summary>
    public ConstraintCheckResult? ConstraintValidation { get; init; }

    /// <summary>
    /// Tool validation results.
    /// </summary>
    public ToolValidationResult? ToolValidation { get; init; }

    /// <summary>
    /// State consistency checks.
    /// </summary>
    public List<ConsistencyCheck> ConsistencyChecks { get; init; } = [];

    /// <summary>
    /// Overall health score (0-100).
    /// </summary>
    [Range(0, 100)]
    public int HealthScore { get; init; }

    /// <summary>
    /// Recommendations for improvement.
    /// </summary>
    public List<string> Recommendations { get; init; } = [];
}

/// <summary>
/// Result of compatibility validation.
/// </summary>
public sealed class CompatibilityValidationResult
{
    /// <summary>
    /// Whether the modes are compatible.
    /// </summary>
    public required bool IsCompatible { get; init; }

    /// <summary>
    /// Compatibility score (0-100).
    /// </summary>
    [Range(0, 100)]
    public int CompatibilityScore { get; init; }

    /// <summary>
    /// Incompatibilities found.
    /// </summary>
    public List<Incompatibility> Incompatibilities { get; init; } = [];

    /// <summary>
    /// Warnings about the transition.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Data that would be lost in transition.
    /// </summary>
    public List<string> DataLossRisks { get; init; } = [];
}

// ============================================================================
// Supporting Types and Enums
// ============================================================================

/// <summary>
/// Status of a mode.
/// </summary>
public enum ModeStatus
{
    /// <summary>Mode is active and available</summary>
    Active,
    /// <summary>Mode is inactive but available</summary>
    Inactive,
    /// <summary>Mode is archived and read-only</summary>
    Archived,
    /// <summary>Mode is being initialized</summary>
    Initializing,
    /// <summary>Mode is in error state</summary>
    Error,
    /// <summary>Mode is under maintenance</summary>
    Maintenance
}

/// <summary>
/// Type of mode change.
/// </summary>
public enum ModeChangeType
{
    /// <summary>Mode was created</summary>
    Created,
    /// <summary>Configuration was updated</summary>
    ConfigurationUpdated,
    /// <summary>Metadata was updated</summary>
    MetadataUpdated,
    /// <summary>Status was changed</summary>
    StatusChanged,
    /// <summary>Mode was archived</summary>
    Archived,
    /// <summary>Mode was reset</summary>
    Reset,
    /// <summary>Transition occurred</summary>
    Transitioned,
    /// <summary>Tools were updated</summary>
    ToolsUpdated,
    /// <summary>Prompt was updated</summary>
    PromptUpdated
}

/// <summary>
/// Actions that can be performed on modes.
/// </summary>
public enum ModeAction
{
    /// <summary>View mode details</summary>
    View,
    /// <summary>Create new mode</summary>
    Create,
    /// <summary>Update mode configuration</summary>
    Update,
    /// <summary>Delete mode</summary>
    Delete,
    /// <summary>Archive mode</summary>
    Archive,
    /// <summary>Transition to mode</summary>
    Transition,
    /// <summary>Reset mode</summary>
    Reset,
    /// <summary>Export mode configuration</summary>
    Export,
    /// <summary>Import mode configuration</summary>
    Import
}

/// <summary>
/// Category of validation rule.
/// </summary>
public enum ValidationRuleCategory
{
    /// <summary>Configuration validation</summary>
    Configuration,
    /// <summary>Security validation</summary>
    Security,
    /// <summary>Performance validation</summary>
    Performance,
    /// <summary>Compatibility validation</summary>
    Compatibility,
    /// <summary>Content policy validation</summary>
    ContentPolicy,
    /// <summary>Business rule validation</summary>
    BusinessRule
}

/// <summary>
/// Severity of validation issues.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Informational only</summary>
    Info,
    /// <summary>Warning that should be addressed</summary>
    Warning,
    /// <summary>Error that must be fixed</summary>
    Error,
    /// <summary>Critical issue that blocks operation</summary>
    Critical
}

/// <summary>
/// Overall validation status.
/// </summary>
public enum ValidationStatus
{
    /// <summary>Validation passed</summary>
    Valid,
    /// <summary>Validation passed with warnings</summary>
    ValidWithWarnings,
    /// <summary>Validation failed</summary>
    Invalid,
    /// <summary>Validation could not be completed</summary>
    Unknown
}

/// <summary>
/// Response format preference.
/// </summary>
public enum ResponseFormat
{
    /// <summary>Plain text responses</summary>
    Text,
    /// <summary>Markdown formatted responses</summary>
    Markdown,
    /// <summary>HTML formatted responses</summary>
    Html,
    /// <summary>JSON structured responses</summary>
    Json,
    /// <summary>Code-optimized responses</summary>
    Code
}

/// <summary>
/// Content filtering level.
/// </summary>
public enum ContentFilterLevel
{
    /// <summary>No filtering</summary>
    None,
    /// <summary>Basic profanity filter</summary>
    Basic,
    /// <summary>Moderate content filtering</summary>
    Moderate,
    /// <summary>Strict content filtering</summary>
    Strict,
    /// <summary>Custom filtering rules</summary>
    Custom
}

// ============================================================================
// Helper Classes
// ============================================================================

/// <summary>
/// Represents a validation error.
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// Error code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Field or property that caused the error.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Severity of the error.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
}

/// <summary>
/// Represents a validation warning.
/// </summary>
public sealed class ValidationWarning
{
    /// <summary>
    /// Warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Suggested action to address the warning.
    /// </summary>
    public string? SuggestedAction { get; init; }
}

/// <summary>
/// Represents a constraint violation.
/// </summary>
public sealed class ConstraintViolation
{
    /// <summary>
    /// Constraint that was violated.
    /// </summary>
    public required string Constraint { get; init; }

    /// <summary>
    /// Actual value that violated the constraint.
    /// </summary>
    public object? ActualValue { get; init; }

    /// <summary>
    /// Expected value or range.
    /// </summary>
    public object? ExpectedValue { get; init; }

    /// <summary>
    /// Violation message.
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// Represents a prompt validation issue.
/// </summary>
public sealed class PromptIssue
{
    /// <summary>
    /// Issue type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Issue description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Position in prompt where issue occurs.
    /// </summary>
    public int? Position { get; init; }

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public ValidationSeverity Severity { get; init; }
}

/// <summary>
/// Represents a consistency check result.
/// </summary>
public sealed class ConsistencyCheck
{
    /// <summary>
    /// Check name.
    /// </summary>
    public required string CheckName { get; init; }

    /// <summary>
    /// Whether the check passed.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// Details about the check.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Represents an incompatibility between modes.
/// </summary>
public sealed class Incompatibility
{
    /// <summary>
    /// Type of incompatibility.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Description of the incompatibility.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Severity of the incompatibility.
    /// </summary>
    public ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Possible resolution.
    /// </summary>
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
public sealed class TransitionImpact
{
    /// <summary>
    /// Features that will be added.
    /// </summary>
    public List<string> AddedFeatures { get; init; } = [];

    /// <summary>
    /// Features that will be removed.
    /// </summary>
    public List<string> RemovedFeatures { get; init; } = [];

    /// <summary>
    /// Configuration changes.
    /// </summary>
    public List<string> ConfigurationChanges { get; init; } = [];

    /// <summary>
    /// User experience impact.
    /// </summary>
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
public sealed class RecurrencePattern
{
    /// <summary>
    /// Type of recurrence.
    /// </summary>
    public RecurrenceType Type { get; init; }

    /// <summary>
    /// Interval between occurrences.
    /// </summary>
    public int Interval { get; init; } = 1;

    /// <summary>
    /// Days of week for weekly recurrence.
    /// </summary>
    public List<DayOfWeek>? DaysOfWeek { get; init; }

    /// <summary>
    /// Day of month for monthly recurrence.
    /// </summary>
    public int? DayOfMonth { get; init; }

    /// <summary>
    /// Maximum number of occurrences.
    /// </summary>
    public int? MaxOccurrences { get; init; }

    /// <summary>
    /// End date for recurrence.
    /// </summary>
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// Type of recurrence pattern.
/// </summary>
public enum RecurrenceType
{
    /// <summary>Daily recurrence</summary>
    Daily,
    /// <summary>Weekly recurrence</summary>
    Weekly,
    /// <summary>Monthly recurrence</summary>
    Monthly,
    /// <summary>Custom recurrence pattern</summary>
    Custom
}