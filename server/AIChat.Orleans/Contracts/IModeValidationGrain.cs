using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for mode validation operations.
/// Handles validation of modes, configurations, transitions, and constraints.
/// This interface follows the Interface Segregation Principle by focusing solely on validation-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.IModeValidationGrain")]
public interface IModeValidationGrain : IGrainWithStringKey
{
    /// <summary>
    /// Validates that a mode exists and is accessible.
    /// </summary>
    /// <param name="modeId">Mode ID to validate</param>
    /// <param name="userId">User ID for permission checking</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with mode details if valid</returns>
    [Alias("ValidateModeAsync")]
    [ReadOnly]
    Task<ModeValidationResult> ValidateModeAsync(string modeId, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a mode transition before execution.
    /// </summary>
    /// <param name="request">Transition request to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with any issues found</returns>
    [Alias("ValidateTransitionAsync")]
    [ReadOnly]
    Task<TransitionValidationResult> ValidateTransitionAsync(ModeTransitionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the validation rules for the mode.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of validation rules</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("GetValidationRulesAsync")]
    [ReadOnly]
    Task<List<ModeValidationRule>> GetValidationRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a configuration meets all mode constraints.
    /// </summary>
    /// <param name="configuration">Configuration to check</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Constraint check result with violations if any</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("CheckConstraintsAsync")]
    [ReadOnly]
    Task<ConstraintCheckResult> CheckConstraintsAsync(ModeConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates tool availability for the mode.
    /// </summary>
    /// <param name="toolIds">List of tool IDs to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Tool validation result with unavailable tools if any</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("ValidateToolsAsync")]
    [ReadOnly]
    Task<ToolValidationResult> ValidateToolsAsync(List<string> toolIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a system prompt for the mode.
    /// </summary>
    /// <param name="prompt">System prompt to validate</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Prompt validation result with issues if any</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("ValidatePromptAsync")]
    [ReadOnly]
    Task<PromptValidationResult> ValidatePromptAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates mode permissions for a user.
    /// </summary>
    /// <param name="userId">User ID to check permissions for</param>
    /// <param name="action">Action to validate permission for</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Permission validation result</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("ValidatePermissionsAsync")]
    [ReadOnly]
    Task<PermissionValidationResult> ValidatePermissionsAsync(string userId, ModeAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs comprehensive validation of the entire mode state.
    /// </summary>
    /// <param name="deep">Whether to perform deep validation including all references</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive validation report</returns>
    /// <exception cref="ModeNotFoundException">Thrown when the mode does not exist</exception>
    [Alias("ValidateStateAsync")]
    [ReadOnly]
    Task<StateValidationReport> ValidateStateAsync(bool deep = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates compatibility between two modes for transition.
    /// </summary>
    /// <param name="sourceModeId">Source mode ID</param>
    /// <param name="targetModeId">Target mode ID</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Compatibility validation result</returns>
    [Alias("ValidateCompatibilityAsync")]
    [ReadOnly]
    Task<CompatibilityValidationResult> ValidateCompatibilityAsync(string sourceModeId, string targetModeId, CancellationToken cancellationToken = default);
}