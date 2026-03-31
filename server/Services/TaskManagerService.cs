using AchieveAi.LmDotnetTools.Misc.Utils;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

/// <summary>
/// Service that manages TaskManager instances per chat with persistence
/// </summary>
public interface ITaskManagerService
{
    /// <summary>
    /// Gets or creates a TaskManager for the specified chat
    /// </summary>
    Task<TaskManager> GetTaskManagerAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Saves the current state of a chat's TaskManager
    /// </summary>
    Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Clears the TaskManager for a chat (when chat is deleted)
    /// </summary>
    Task ClearTaskManagerAsync(string chatId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current task state as JSON for a chat
    /// </summary>
    Task<(string, IList<TaskItem>)?> GetTaskStateAsync(string chatId, CancellationToken ct = default);
}