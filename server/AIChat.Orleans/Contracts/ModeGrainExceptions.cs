namespace AIChat.Orleans.Contracts;

/// <summary>
/// Base exception for all mode grain-related errors.
/// </summary>
public class ModeGrainException : Exception
{
    /// <summary>
    /// Gets the mode ID associated with this exception.
    /// </summary>
    public string? ModeId { get; }

    /// <summary>
    /// Gets the correlation ID for tracing this error.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModeGrainException"/> class.
    /// </summary>
    public ModeGrainException(string message, string? modeId = null)
        : base(message)
    {
        ModeId = modeId;
        CorrelationId = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModeGrainException"/> class with an inner exception.
    /// </summary>
    public ModeGrainException(string message, Exception innerException, string? modeId = null)
        : base(message, innerException)
    {
        ModeId = modeId;
        CorrelationId = Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Exception thrown when a requested mode is not found.
/// </summary>
public class ModeNotFoundException : ModeGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModeNotFoundException"/> class.
    /// </summary>
    public ModeNotFoundException(string modeId)
        : base($"Mode with ID '{modeId}' was not found.", modeId)
    {
    }
}

/// <summary>
/// Exception thrown when attempting to create a mode that already exists.
/// </summary>
public class ModeAlreadyExistsException : ModeGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModeAlreadyExistsException"/> class.
    /// </summary>
    public ModeAlreadyExistsException(string modeId)
        : base($"Mode with ID '{modeId}' already exists.", modeId)
    {
    }
}

/// <summary>
/// Exception thrown when attempting to modify an archived mode.
/// </summary>
public class ModeArchivedException : ModeGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModeArchivedException"/> class.
    /// </summary>
    public ModeArchivedException(string modeId)
        : base($"Mode '{modeId}' is archived and cannot be modified.", modeId)
    {
    }
}

/// <summary>
/// Exception thrown when mode configuration is invalid.
/// </summary>
public class InvalidModeConfigurationException : ModeGrainException
{
    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public List<string> ValidationErrors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidModeConfigurationException"/> class.
    /// </summary>
    public InvalidModeConfigurationException(string modeId, List<string> validationErrors)
        : base($"Mode '{modeId}' configuration is invalid: {string.Join("; ", validationErrors)}", modeId)
    {
        ValidationErrors = validationErrors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidModeConfigurationException"/> class with a single error.
    /// </summary>
    public InvalidModeConfigurationException(string modeId, string error)
        : base($"Mode '{modeId}' configuration is invalid: {error}", modeId)
    {
        ValidationErrors = [error];
    }
}

/// <summary>
/// Exception thrown when a mode transition is invalid.
/// </summary>
public class InvalidModeTransitionException : ModeGrainException
{
    /// <summary>
    /// Gets the source mode ID.
    /// </summary>
    public string SourceModeId { get; }

    /// <summary>
    /// Gets the target mode ID.
    /// </summary>
    public string TargetModeId { get; }

    /// <summary>
    /// Gets the reason for the invalid transition.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidModeTransitionException"/> class.
    /// </summary>
    public InvalidModeTransitionException(string sourceModeId, string targetModeId, string reason)
        : base($"Transition from mode '{sourceModeId}' to '{targetModeId}' is not allowed: {reason}", sourceModeId)
    {
        SourceModeId = sourceModeId;
        TargetModeId = targetModeId;
        Reason = reason;
    }
}

/// <summary>
/// Exception thrown when a mode template is not found.
/// </summary>
public class ModeTemplateNotFoundException : ModeGrainException
{
    /// <summary>
    /// Gets the template ID that was not found.
    /// </summary>
    public string TemplateId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModeTemplateNotFoundException"/> class.
    /// </summary>
    public ModeTemplateNotFoundException(string templateId)
        : base($"Mode template with ID '{templateId}' was not found.")
    {
        TemplateId = templateId;
    }
}

/// <summary>
/// Exception thrown when an invalid prompt is provided.
/// </summary>
public class InvalidPromptException : ModeGrainException
{
    /// <summary>
    /// Gets the validation issues with the prompt.
    /// </summary>
    public List<string> Issues { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidPromptException"/> class.
    /// </summary>
    public InvalidPromptException(string modeId, List<string> issues)
        : base($"Invalid prompt for mode '{modeId}': {string.Join("; ", issues)}", modeId)
    {
        Issues = issues;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidPromptException"/> class with a single issue.
    /// </summary>
    public InvalidPromptException(string modeId, string issue)
        : base($"Invalid prompt for mode '{modeId}': {issue}", modeId)
    {
        Issues = [issue];
    }
}

/// <summary>
/// Exception thrown when an invalid tool is referenced.
/// </summary>
public class InvalidToolException : ModeGrainException
{
    /// <summary>
    /// Gets the invalid tool IDs.
    /// </summary>
    public List<string> InvalidToolIds { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidToolException"/> class.
    /// </summary>
    public InvalidToolException(string modeId, List<string> invalidToolIds)
        : base($"Mode '{modeId}' references invalid tools: {string.Join(", ", invalidToolIds)}", modeId)
    {
        InvalidToolIds = invalidToolIds;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidToolException"/> class with a single tool.
    /// </summary>
    public InvalidToolException(string modeId, string invalidToolId)
        : base($"Mode '{modeId}' references invalid tool: '{invalidToolId}'", modeId)
    {
        InvalidToolIds = [invalidToolId];
    }
}

/// <summary>
/// Exception thrown when there's no transition to rollback.
/// </summary>
public class NoTransitionToRollbackException : ModeGrainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoTransitionToRollbackException"/> class.
    /// </summary>
    public NoTransitionToRollbackException(string modeId)
        : base($"Mode '{modeId}' has no transition to rollback.", modeId)
    {
    }
}

/// <summary>
/// Exception thrown when a preset is not found.
/// </summary>
public class PresetNotFoundException : ModeGrainException
{
    /// <summary>
    /// Gets the preset ID that was not found.
    /// </summary>
    public string PresetId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PresetNotFoundException"/> class.
    /// </summary>
    public PresetNotFoundException(string modeId, string presetId)
        : base($"Preset '{presetId}' was not found for mode '{modeId}'.", modeId)
    {
        PresetId = presetId;
    }
}

/// <summary>
/// Exception thrown when a preset is invalid or cannot be applied.
/// </summary>
public class InvalidPresetException : ModeGrainException
{
    /// <summary>
    /// Gets the preset ID.
    /// </summary>
    public string PresetId { get; }

    /// <summary>
    /// Gets the reason why the preset is invalid.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidPresetException"/> class.
    /// </summary>
    public InvalidPresetException(string modeId, string presetId, string reason)
        : base($"Preset '{presetId}' cannot be applied to mode '{modeId}': {reason}", modeId)
    {
        PresetId = presetId;
        Reason = reason;
    }
}

/// <summary>
/// Exception thrown when a scheduled transition has invalid parameters.
/// </summary>
public class InvalidScheduleException : ModeGrainException
{
    /// <summary>
    /// Gets the reason why the schedule is invalid.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidScheduleException"/> class.
    /// </summary>
    public InvalidScheduleException(string modeId, string reason)
        : base($"Invalid schedule for mode '{modeId}': {reason}", modeId)
    {
        Reason = reason;
    }
}

/// <summary>
/// Exception thrown when a scheduled transition is not found.
/// </summary>
public class ScheduleNotFoundException : ModeGrainException
{
    /// <summary>
    /// Gets the schedule ID that was not found.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleNotFoundException"/> class.
    /// </summary>
    public ScheduleNotFoundException(string modeId, string scheduleId)
        : base($"Schedule '{scheduleId}' was not found for mode '{modeId}'.", modeId)
    {
        ScheduleId = scheduleId;
    }
}
