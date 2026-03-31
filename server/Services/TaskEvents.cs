using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

/// <summary>
/// Stream event for task state updates sent via SSE
/// </summary>
public record TaskUpdateStreamEvent : StreamChunkEvent
{
    /// <summary>
    /// The current state of all tasks in the chat
    /// </summary>
    public required IList<TaskItem> TaskState { get; set; }
    
    /// <summary>
    /// The type of operation that triggered this update
    /// </summary>
    public required string OperationType { get; set; } // "sync", "add", "update", "delete", etc.
}