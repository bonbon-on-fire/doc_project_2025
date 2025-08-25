using AIChat.Server.Storage;

namespace AIChat.Server.Services;

/// <summary>
/// Service interface for managing chat modes including system modes and user-created custom modes.
/// Provides functionality for loading system modes from JSON files, managing custom user modes,
/// and filtering available tools based on mode configuration.
/// </summary>
public interface IModeService
{
    /// <summary>
    /// Gets all available modes for a user, combining system modes with user's custom modes.
    /// </summary>
    /// <param name="userId">The user ID to get modes for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with list of all modes or error message</returns>
    Task<(bool Success, string? Error, IReadOnlyList<ModeDto> Modes)> GetAllModesAsync(
        string userId, 
        CancellationToken ct = default);

    /// <summary>
    /// Gets a specific mode by ID, checking both system modes and user custom modes.
    /// </summary>
    /// <param name="modeId">The mode ID to retrieve</param>
    /// <param name="userId">The user ID for permission checking on custom modes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with mode or error message</returns>
    Task<(bool Success, string? Error, ModeDto? Mode)> GetModeByIdAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new custom mode for a user.
    /// </summary>
    /// <param name="mode">The mode data to create</param>
    /// <param name="userId">The user ID creating the mode</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with created mode or error message</returns>
    Task<(bool Success, string? Error, ModeDto? Mode)> CreateCustomModeAsync(
        CreateModeRequest mode,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing custom mode. Only the mode owner can update.
    /// </summary>
    /// <param name="modeId">The mode ID to update</param>
    /// <param name="mode">The updated mode data</param>
    /// <param name="userId">The user ID updating the mode</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with updated mode or error message</returns>
    Task<(bool Success, string? Error, ModeDto? Mode)> UpdateCustomModeAsync(
        string modeId,
        UpdateModeRequest mode,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a custom mode. Only the mode owner can delete.
    /// </summary>
    /// <param name="modeId">The mode ID to delete</param>
    /// <param name="userId">The user ID deleting the mode</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status or error message</returns>
    Task<(bool Success, string? Error)> DeleteCustomModeAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Filters available tools based on a mode's configuration.
    /// </summary>
    /// <param name="modeId">The mode ID to get tool filters for</param>
    /// <param name="userId">The user ID for permission checking</param>
    /// <param name="availableTools">List of all available tools</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with filtered tools list or error message</returns>
    Task<(bool Success, string? Error, IReadOnlyList<string> FilteredTools)> FilterToolsByModeAsync(
        string modeId,
        string userId,
        IReadOnlyList<string> availableTools,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the system prompt for a mode, if one is configured.
    /// </summary>
    /// <param name="modeId">The mode ID to get prompt for</param>
    /// <param name="userId">The user ID for permission checking</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with system prompt or null if none configured</returns>
    Task<(bool Success, string? Error, string? SystemPrompt)> GetModeSystemPromptAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the default model preference for a mode, if one is configured.
    /// </summary>
    /// <param name="modeId">The mode ID to get model preference for</param>
    /// <param name="userId">The user ID for permission checking</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with default model or null if none configured</returns>
    Task<(bool Success, string? Error, string? DefaultModel)> GetModeDefaultModelAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);
}

/// <summary>
/// Request model for creating a new custom mode.
/// </summary>
public sealed class CreateModeRequest
{
    /// <summary>
    /// Display name for the mode (1-100 characters).
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Description of what the mode is optimized for (1-500 characters).
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// System prompt that shapes the AI's behavior (1-2000 characters).
    /// </summary>
    public required string Prompt { get; init; }
    
    /// <summary>
    /// List of tool IDs available in this mode. Use ["*"] for all tools.
    /// </summary>
    public required IReadOnlyList<string> Tools { get; init; }
    
    /// <summary>
    /// Optional preferred AI model for this mode (e.g., "openai/gpt-4").
    /// </summary>
    public string? DefaultModel { get; init; }
    
    /// <summary>
    /// Optional category for organizing modes (task, role, custom). Defaults to "custom".
    /// </summary>
    public string? Category { get; init; }
}

/// <summary>
/// Request model for updating an existing custom mode.
/// </summary>
public sealed class UpdateModeRequest
{
    /// <summary>
    /// Updated display name for the mode (1-100 characters).
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Updated description of what the mode is optimized for (1-500 characters).
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// Updated system prompt that shapes the AI's behavior (1-2000 characters).
    /// </summary>
    public required string Prompt { get; init; }
    
    /// <summary>
    /// Updated list of tool IDs available in this mode. Use ["*"] for all tools.
    /// </summary>
    public required IReadOnlyList<string> Tools { get; init; }
    
    /// <summary>
    /// Updated preferred AI model for this mode (e.g., "openai/gpt-4").
    /// </summary>
    public string? DefaultModel { get; init; }
    
    /// <summary>
    /// Updated category for organizing modes (task, role, custom).
    /// </summary>
    public string? Category { get; init; }
}

/// <summary>
/// Data transfer object for mode data returned to clients.
/// Contains complete mode configuration and metadata.
/// </summary>
public sealed class ModeDto
{
    /// <summary>
    /// Unique identifier for the mode.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Display name of the mode.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Description of what the mode is optimized for.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// System prompt that shapes the AI's behavior.
    /// </summary>
    public required string Prompt { get; init; }
    
    /// <summary>
    /// List of tool IDs available in this mode. ["*"] means all tools.
    /// </summary>
    public required IReadOnlyList<string> Tools { get; init; }
    
    /// <summary>
    /// Optional preferred AI model for this mode.
    /// </summary>
    public string? DefaultModel { get; init; }
    
    /// <summary>
    /// Indicates whether this is a system-provided mode (true) or user-created (false).
    /// </summary>
    public required bool IsSystem { get; init; }
    
    /// <summary>
    /// User ID of the mode owner (null for system modes).
    /// </summary>
    public string? UserId { get; init; }
    
    /// <summary>
    /// Category for organizing modes (task, role, custom).
    /// </summary>
    public string? Category { get; init; }
    
    /// <summary>
    /// UTC timestamp when the mode was created (null for system modes).
    /// </summary>
    public DateTime? CreatedAt { get; init; }
    
    /// <summary>
    /// UTC timestamp when the mode was last updated (null for system modes).
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}