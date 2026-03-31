using AchieveAi.LmDotnetTools.Misc.Utils;

namespace AIChat.Server.Storage;

public record ChatTaskState
{
    public required string ChatId { get; init; }
    public required TaskManager TaskManager { get; init; }
    public required int Version { get; init; }
    public required DateTime LastUpdatedUtc { get; init; }
}

public interface ITaskStorage
{
    /// <summary>
    /// Gets the task state for a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The task state if found, null otherwise</returns>
    Task<ChatTaskState?> GetTasksAsync(string chatId, CancellationToken ct = default);

    /// <summary>
    /// Saves or updates the task state for a chat.
    /// </summary>
    /// <param name="chatId">The chat ID</param>
    /// <param name="tasks">The tasks as a JSON element</param>
    /// <param name="expectedVersion">Expected version for optimistic concurrency</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The updated task state with new version</returns>
    /// <exception cref="InvalidOperationException">Thrown when version conflict occurs</exception>
    Task<ChatTaskState> SaveTasksAsync(string chatId, TaskManager taskManager, int expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Deletes all tasks for a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteTasksAsync(string chatId, CancellationToken ct = default);
}