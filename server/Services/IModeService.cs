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
/// Request model for creating a new custom mode
/// </summary>
public sealed class CreateModeRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> Tools { get; init; }
    public string? DefaultModel { get; init; }
    public string? Category { get; init; }
}

/// <summary>
/// Request model for updating an existing custom mode
/// </summary>
public sealed class UpdateModeRequest
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> Tools { get; init; }
    public string? DefaultModel { get; init; }
    public string? Category { get; init; }
}

/// <summary>
/// DTO for mode data returned to clients
/// </summary>
public sealed class ModeDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required IReadOnlyList<string> Tools { get; init; }
    public string? DefaultModel { get; init; }
    public required bool IsSystem { get; init; }
    public string? UserId { get; init; }
    public string? Category { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}