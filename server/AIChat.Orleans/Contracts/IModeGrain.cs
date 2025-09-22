using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Comprehensive grain interface for mode management in the system.
/// This interface extends all segregated interfaces to provide backward compatibility
/// while supporting the new Interface Segregation Principle-based architecture.
///
/// For new code, prefer using the specific segregated interfaces:
/// - IModeStateGrain for state management and persistence
/// - IModeConfigurationGrain for configuration management
/// - IModeTransitionGrain for mode transitions and switching
/// - IModeValidationGrain for validation operations
/// </summary>
[Alias("AIChat.Orleans.Contracts.IModeGrain")]
public interface IModeGrain
    : IModeStateGrain,
      IModeConfigurationGrain,
      IModeTransitionGrain,
      IModeValidationGrain
{
    // This interface now inherits all methods from the four segregated interfaces.
    // No additional methods are defined here to maintain clean separation of concerns.
    //
    // Inherited from IModeStateGrain:
    // - InitializeAsync(ModeInitRequest request)
    // - GetStateAsync()
    // - UpdateMetadataAsync(Dictionary<string, object> metadata)
    // - ArchiveAsync(string? reason)
    // - ResetToDefaultAsync(bool preserveHistory)
    // - GetHistoryAsync(int? limit)
    // - CheckHealthAsync()
    //
    // Inherited from IModeConfigurationGrain:
    // - GetConfigurationAsync()
    // - UpdateConfigurationAsync(ModeConfiguration configuration)
    // - GetAvailableModesAsync(string? category)
    // - GetModeTemplateAsync(string templateId)
    // - ValidateConfigurationAsync(ModeConfiguration configuration)
    // - UpdateSystemPromptAsync(string systemPrompt)
    // - UpdateToolsAsync(List<string> tools)
    // - GetEffectiveConfigurationAsync(Dictionary<string, object>? overrides)
    //
    // Inherited from IModeTransitionGrain:
    // - TransitionToModeAsync(ModeTransitionRequest request)
    // - GetTransitionHistoryAsync(int? limit)
    // - CanTransitionAsync(string targetModeId)
    // - RollbackTransitionAsync(string? reason)
    // - GetAvailableTransitionsAsync()
    // - ApplyPresetAsync(string presetId, Dictionary<string, object>? parameters)
    // - ScheduleTransitionAsync(ScheduledTransitionRequest request)
    // - CancelScheduledTransitionAsync(string scheduleId)
    //
    // Inherited from IModeValidationGrain:
    // - ValidateModeAsync(string modeId, string? userId)
    // - ValidateTransitionAsync(ModeTransitionRequest request)
    // - GetValidationRulesAsync()
    // - CheckConstraintsAsync(ModeConfiguration configuration)
    // - ValidateToolsAsync(List<string> toolIds)
    // - ValidatePromptAsync(string prompt)
    // - ValidatePermissionsAsync(string userId, ModeAction action)
    // - ValidateStateAsync(bool deep)
    // - ValidateCompatibilityAsync(string sourceModeId, string targetModeId)
}