using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Storage;
using System.Collections.Concurrent;
using System.Text.Json;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

/// <summary>
/// Simplified TaskManagerService that uses TaskManager's native serialization.
/// Directly stores and restores TaskManager state without markdown translation.
/// </summary>
public class ImprovedTaskManagerService : ITaskManagerService
{
    private readonly ITaskStorage _taskStorage;
    private readonly ILogger<ImprovedTaskManagerService> _logger;
    private readonly ConcurrentDictionary<string, CachedTaskManager> _taskManagers;
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

    public ImprovedTaskManagerService(ITaskStorage taskStorage, ILogger<ImprovedTaskManagerService> logger)
    {
        _taskStorage = taskStorage;
        _logger = logger;
        _taskManagers = new ConcurrentDictionary<string, CachedTaskManager>();
    }

    public async Task<TaskManager> GetTaskManagerAsync(string chatId, CancellationToken ct = default)
    {
        // Check if we already have it in memory
        if (_taskManagers.TryGetValue(chatId, out var cached))
        {
            _logger.LogDebug("Returning cached TaskManager for chat {ChatId}", chatId);
            return cached.Manager;
        }

        // Try to load from storage
        var taskState = await _taskStorage.GetTasksAsync(chatId, ct);
        
        TaskManager taskManager;
        var cachedManager = new CachedTaskManager 
        { 
            Version = 0
        };

        if (taskState?.TaskManager != null)
        {
            _logger.LogInformation("Loading existing tasks for chat {ChatId}, version {Version}", chatId, taskState.Version);
            cachedManager.Version = taskState.Version;
            
            // Use TaskManager's native deserialization
            try
            {
                taskManager = taskState.TaskManager;
                cachedManager.Manager = taskManager;
                cachedManager.LastSerializedState = taskManager.JsonSerializeTasksToJsonElements();
                _logger.LogInformation("Successfully restored TaskManager state for chat {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize TaskManager for chat {ChatId}, creating new instance", chatId);
                taskManager = new TaskManager();
                cachedManager.Manager = taskManager;
            }
        }
        else
        {
            _logger.LogInformation("Creating new TaskManager for chat {ChatId}", chatId);
            taskManager = new TaskManager();
            cachedManager.Manager = taskManager;
        }

        // Cache it
        _taskManagers.TryAdd(chatId, cachedManager);
        
        return taskManager;
    }

    public async Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default)
    {
        if (!_taskManagers.TryGetValue(chatId, out var cached))
        {
            _logger.LogWarning("Cannot save TaskManager state for chat {ChatId} - not loaded", chatId);
            return;
        }

        await _saveLock.WaitAsync(ct);
        try
        {
            // Save to storage with optimistic concurrency (now accepts string directly)
            var newState = await _taskStorage.SaveTasksAsync(chatId, cached.Manager, cached.Version, ct);
            
            // Update cached version
            cached.Version = newState.Version;
            
            _logger.LogInformation("Saved TaskManager state for chat {ChatId}, new version {Version}", 
                chatId, newState.Version);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Version conflict"))
        {
            _logger.LogWarning(ex, "Version conflict when saving tasks for chat {ChatId}, will reload", chatId);
            
            // Clear from cache to force reload next time
            _taskManagers.TryRemove(chatId, out _);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task ClearTaskManagerAsync(string chatId, CancellationToken ct = default)
    {
        _logger.LogInformation("Clearing TaskManager for chat {ChatId}", chatId);
        
        // Remove from cache
        _taskManagers.TryRemove(chatId, out _);
        
        // Delete from storage
        await _taskStorage.DeleteTasksAsync(chatId, ct);
    }

    public async Task<(string, IList<TaskItem>)?> GetTaskStateAsync(string chatId, CancellationToken ct = default)
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

    public async Task<FunctionRegistry> GetFunctionRegistryAsync(string chatId, CancellationToken ct = default)
    {
        var taskManager = await GetTaskManagerAsync(chatId, ct);
        
        var registry = new FunctionRegistry();
        registry.AddFunctionsFromObject(taskManager, "TaskManager");
        
        return registry;
    }
}