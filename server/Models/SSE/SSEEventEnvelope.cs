using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Text.Json.Serialization;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Models.SSE;

/// <summary>
/// Base class for all SSE event envelopes
/// Provides consistent structure and metadata for all events
/// </summary>
public abstract class SSEEventEnvelope
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("ts")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("kind")]
    public required string Kind { get; set; }
}

/// <summary>
/// Envelope for initialization events
/// Contains user message metadata for client setup
/// </summary>
public class InitEventEnvelope : SSEEventEnvelope
{
    [JsonPropertyName("payload")]
    public required InitPayload Payload { get; set; }
}

public class InitPayload
{
    [JsonPropertyName("userMessageId")]
    public required string UserMessageId { get; set; }

    [JsonPropertyName("userTimestamp")]
    public required DateTime UserTimestamp { get; set; }

    [JsonPropertyName("userSequenceNumber")]
    public required int UserSequenceNumber { get; set; }
}

/// <summary>
/// Envelope for streaming chunk events (delta updates)
/// </summary>
public class StreamChunkEventEnvelope : SSEEventEnvelope
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; set; }

    [JsonPropertyName("sequenceId")]
    public required int SequenceId { get; set; }

    [JsonPropertyName("payload")]
    public required object Payload { get; set; }
}

/// <summary>
/// Base class for all streaming chunk payloads
/// </summary>
public abstract class StreamChunkPayload
{
    [JsonPropertyName("delta")]
    public required string Delta { get; set; }
}

/// <summary>
/// Payload for text streaming chunks
/// </summary>
public class TextStreamChunkPayload : StreamChunkPayload
{
    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

/// <summary>
/// Payload for reasoning streaming chunks
/// </summary>
public class ReasoningStreamChunkPayload : StreamChunkPayload
{
    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

/// <summary>
/// Payload for tool call update streaming chunks
/// </summary>
public class ToolCallUpdateStreamChunkPayload : StreamChunkPayload
{
    [JsonPropertyName("toolCallUpdate")]
    public required ToolCallUpdate ToolCallUpdate { get; set; }
}

/// <summary>
/// Payload for tool calls aggregate stream event containing both calls and results
/// </summary>
public class ToolsCallAggregatePayload : StreamChunkPayload
{
    [JsonPropertyName("toolCalls")]
    public ToolCall[]? ToolCalls { get; set; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; set; }
}

/// <summary>
/// Payload for tool result streaming chunks
/// </summary>
public class ToolResultStreamChunkPayload : StreamChunkPayload
{
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; set; }
    
    [JsonPropertyName("result")]
    public required string Result { get; set; }
    
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

/// <summary>
/// Payload for task update streaming chunks
/// </summary>
public class TaskUpdateStreamChunkPayload : StreamChunkPayload
{
    [JsonPropertyName("taskState")]
    public required IList<TaskItem> TaskState { get; set; }
    
    [JsonPropertyName("operationType")]
    public required string OperationType { get; set; }
}

/// <summary>
/// Envelope for complete message events
/// </summary>
public class MessageCompleteEventEnvelope : SSEEventEnvelope
{
    [JsonPropertyName("messageId")]
    public required string MessageId { get; set; }

    [JsonPropertyName("sequenceId")]
    public required int SequenceId { get; set; }

    [JsonPropertyName("payload")]
    public required object Payload { get; set; }
}

/// <summary>
/// Base class for all message completion payloads
/// </summary>
public abstract class MessageCompletePayload
{
}

/// <summary>
/// Payload for completed text messages
/// </summary>
public class TextCompletePayload : MessageCompletePayload
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// Payload for completed reasoning messages
/// </summary>
public class ReasoningCompletePayload : MessageCompletePayload
{
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

/// <summary>
/// Payload for completed tool call messages
/// </summary>
public class ToolCallCompletePayload : MessageCompletePayload
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; set; }
}

/// <summary>
/// Payload for completed tool call aggregate messages
/// </summary>
public class ToolsCallAggregateCompletePayload : MessageCompletePayload
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; set; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; set; }
}

/// <summary>
/// Payload for usage information
/// </summary>
public class UsageCompletePayload : MessageCompletePayload
{
    [JsonPropertyName("usage")]
    public required Dictionary<string, object> Usage { get; set; }
}

/// <summary>
/// Envelope for stream completion events
/// </summary>
public class StreamCompleteEventEnvelope : SSEEventEnvelope
{
    // No additional properties needed beyond base
}

/// <summary>
/// Envelope for error events
/// </summary>
public class ErrorEventEnvelope : SSEEventEnvelope
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("sequenceId")]
    public int? SequenceId { get; set; }

    [JsonPropertyName("payload")]
    public required ErrorPayload Payload { get; set; }
}

public class ErrorPayload
{
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

/// <summary>
/// Envelope for task operation events
/// </summary>
public class TaskOperationEventEnvelope : SSEEventEnvelope
{
    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    [JsonPropertyName("payload")]
    public required TaskOperationPayload Payload { get; set; }
}

/// <summary>
/// Payload for task operation events
/// </summary>
public class TaskOperationPayload
{
    [JsonPropertyName("operationType")]
    public required string OperationType { get; set; } // "start", "complete", "sync"

    [JsonPropertyName("operation")]
    public TaskOperation? Operation { get; set; }

    [JsonPropertyName("taskState")]
    public System.Text.Json.JsonElement? TaskState { get; set; }

    [JsonPropertyName("version")]
    public int? Version { get; set; }
}

/// <summary>
/// Represents a task management operation
/// </summary>
public class TaskOperation
{
    [JsonPropertyName("type")]
    public required string Type { get; set; } // "add", "update", "delete", "list", "manage_notes"

    [JsonPropertyName("function")]
    public required string Function { get; set; }

    [JsonPropertyName("arguments")]
    public System.Text.Json.JsonElement? Arguments { get; set; }

    [JsonPropertyName("result")]
    public System.Text.Json.JsonElement? Result { get; set; }
}
