namespace AIChat.Server.Storage;

/// <summary>
/// Storage interface for persisting user-created custom modes.
/// Provides CRUD operations for mode records in the database.
/// </summary>
public interface IModeStorage
{
    /// <summary>
    /// Creates a new mode record in the database.
    /// </summary>
    /// <param name="mode">The mode record to create</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with created mode or error message</returns>
    Task<(bool Success, string? Error, ModeRecord? Mode)> CreateModeAsync(
        ModeRecord mode,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves a mode by its ID for a specific user.
    /// </summary>
    /// <param name="modeId">The mode ID to retrieve</param>
    /// <param name="userId">The user ID for ownership verification</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with mode record or error message</returns>
    Task<(bool Success, string? Error, ModeRecord? Mode)> GetModeByIdAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves all modes belonging to a specific user.
    /// </summary>
    /// <param name="userId">The user ID to get modes for</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with list of mode records or error message</returns>
    Task<(bool Success, string? Error, IReadOnlyList<ModeRecord> Modes)> GetModesByUserAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates an existing mode record. Only the mode owner can update.
    /// </summary>
    /// <param name="modeId">The mode ID to update</param>
    /// <param name="userId">The user ID for ownership verification</param>
    /// <param name="updatedMode">The updated mode data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status with updated mode or error message</returns>
    Task<(bool Success, string? Error, ModeRecord? Mode)> UpdateModeAsync(
        string modeId,
        string userId,
        ModeRecord updatedMode,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a mode record. Only the mode owner can delete.
    /// </summary>
    /// <param name="modeId">The mode ID to delete</param>
    /// <param name="userId">The user ID for ownership verification</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Success status or error message</returns>
    Task<(bool Success, string? Error)> DeleteModeAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);
}

/// <summary>
/// Represents a mode record as stored in the database.
/// Contains all mode configuration and metadata.
/// </summary>
public sealed class ModeRecord
{
    /// <summary>
    /// Unique identifier for the mode.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// User ID of the mode owner.
    /// </summary>
    public required string UserId { get; init; }
    
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
    /// JSON array of tool IDs stored as a string.
    /// </summary>
    public required string Tools { get; init; }
    
    /// <summary>
    /// Optional preferred AI model for this mode.
    /// </summary>
    public string? DefaultModel { get; init; }
    
    /// <summary>
    /// Category for organizing modes (task, role, custom).
    /// </summary>
    public required string Category { get; init; }
    
    /// <summary>
    /// UTC timestamp when the mode was created.
    /// </summary>
    public required DateTime CreatedAtUtc { get; init; }
    
    /// <summary>
    /// UTC timestamp when the mode was last updated.
    /// </summary>
    public required DateTime UpdatedAtUtc { get; init; }
}