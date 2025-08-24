namespace AIChat.Server.Storage;

public interface IModeStorage
{
    Task<(bool Success, string? Error, ModeRecord? Mode)> CreateModeAsync(
        ModeRecord mode,
        CancellationToken ct = default);

    Task<(bool Success, string? Error, ModeRecord? Mode)> GetModeByIdAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);

    Task<(bool Success, string? Error, IReadOnlyList<ModeRecord> Modes)> GetModesByUserAsync(
        string userId,
        CancellationToken ct = default);

    Task<(bool Success, string? Error, ModeRecord? Mode)> UpdateModeAsync(
        string modeId,
        string userId,
        ModeRecord updatedMode,
        CancellationToken ct = default);

    Task<(bool Success, string? Error)> DeleteModeAsync(
        string modeId,
        string userId,
        CancellationToken ct = default);
}

public sealed class ModeRecord
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Prompt { get; init; }
    public required string Tools { get; init; }  // JSON array stored as string
    public string? DefaultModel { get; init; }
    public required string Category { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}