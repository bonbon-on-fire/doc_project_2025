using System.Collections.Concurrent;
using System.Text.Json;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Storage;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

/// <summary>
/// Simplified TaskManagerService that uses TaskManager's native serialization.
/// Directly stores and restores TaskManager state without markdown translation.
/// </summary>
public class ImprovedTaskManagerService(
    ITaskStorage taskStorage,
    ILogger<ImprovedTaskManagerService> logger
) : ITaskManagerService
{
    private readonly ConcurrentDictionary<string, CachedTaskManager> _taskManagers =
        new ConcurrentDictionary<string, CachedTaskManager>();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>
    /// Cached state for a chat's TaskManager
    /// </summary>
    private class CachedTaskManager
    {
        public TaskManager Manager { get; set; } = new();
        public int Version { get; set; }
        public JsonElement? LastSerializedState { get; set; }
    }

    public async Task<TaskManager> GetTaskManagerAsync(
        string chatId,
        CancellationToken ct = default
    )
    {
        // Check if we already have it in memory
        if (_taskManagers.TryGetValue(chatId, out var cached))
        {
            logger.LogDebug("Returning cached TaskManager for chat {ChatId}", chatId);
            return cached.Manager;
        }

        // Try to load from storage
        var taskState = await taskStorage.GetTasksAsync(chatId, ct);

        TaskManager taskManager;
        var cachedManager = new CachedTaskManager { Version = 0 };

        if (taskState?.TaskManager != null)
        {
            logger.LogInformation(
                "Loading existing tasks for chat {ChatId}, version {Version}",
                chatId,
                taskState.Version
            );
            cachedManager.Version = taskState.Version;

            // Use TaskManager's native deserialization
            try
            {
                taskManager = taskState.TaskManager;
                cachedManager.Manager = taskManager;
                cachedManager.LastSerializedState = taskManager.JsonSerializeTasksToJsonElements();
                logger.LogInformation(
                    "Successfully restored TaskManager state for chat {ChatId}",
                    chatId
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to deserialize TaskManager for chat {ChatId}, creating new instance",
                    chatId
                );
                taskManager = new TaskManager();
                cachedManager.Manager = taskManager;
            }
        }
        else
        {
            logger.LogInformation("Creating new TaskManager for chat {ChatId}", chatId);
            taskManager = new TaskManager();
            cachedManager.Manager = taskManager;
        }

        // Cache it
        _ = _taskManagers.TryAdd(chatId, cachedManager);

        return taskManager;
    }

    public async Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default)
    {
        if (!_taskManagers.TryGetValue(chatId, out var cached))
        {
            logger.LogWarning(
                "Cannot save TaskManager state for chat {ChatId} - not loaded",
                chatId
            );
            return;
        }

        await _saveLock.WaitAsync(ct);
        try
        {
            // Save to storage with optimistic concurrency (now accepts string directly)
            var newState = await taskStorage.SaveTasksAsync(
                chatId,
                cached.Manager,
                cached.Version,
                ct
            );

            // Update cached version
            cached.Version = newState.Version;

            logger.LogInformation(
                "Saved TaskManager state for chat {ChatId}, new version {Version}",
                chatId,
                newState.Version
            );
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Version conflict"))
        {
            logger.LogWarning(
                ex,
                "Version conflict when saving tasks for chat {ChatId}, will reload",
                chatId
            );

            // Clear from cache to force reload next time
            _ = _taskManagers.TryRemove(chatId, out _);
        }
        finally
        {
            _ = _saveLock.Release();
        }
    }

    public async Task ClearTaskManagerAsync(string chatId, CancellationToken ct = default)
    {
        logger.LogInformation("Clearing TaskManager for chat {ChatId}", chatId);

        // Remove from cache
        _ = _taskManagers.TryRemove(chatId, out _);

        // Delete from storage
        await taskStorage.DeleteTasksAsync(chatId, ct);
    }

    public async Task<(string, IList<TaskItem>)?> GetTaskStateAsync(
        string chatId,
        CancellationToken ct = default
    )
    {
        // Get or create the TaskManager
        var taskManager = await GetTaskManagerAsync(chatId, ct);

        // Get the serialized state directly from TaskManager
        var serializedState = taskManager.JsonSerializeTasksToJsonElements();

        // Cache the serialized state
        if (_taskManagers.TryGetValue(chatId, out var cached))
        {
            cached.LastSerializedState = serializedState;
        }

        // Parse and return state with markdown for compatibility
        var markdown = taskManager.GetMarkdown();

        return (markdown, taskManager.GetTasks());
    }

    public async Task<FunctionRegistry> GetFunctionRegistryAsync(
        string chatId,
        CancellationToken ct = default
    )
    {
        var taskManager = await GetTaskManagerAsync(chatId, ct);

        var registry = new FunctionRegistry();
        _ = registry.AddFunctionsFromObject(taskManager, "TaskManager");

        return registry;
    }
}
