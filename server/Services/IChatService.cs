using AchieveAi.LmDotnetTools.LmCore.Core;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

public interface IChatService
{
    // Chat Management
    Task<ChatResult> CreateChatAsync(CreateChatRequest request);
    Task<ChatResult> GetChatAsync(string chatId);
    Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize);
    Task<bool> DeleteChatAsync(string chatId);

    // Message Operations
    Task<MessageResult> SendMessageAsync(SendMessageRequest request);
    Task<MessageResult> AddUserMessageToExistingChatAsync(string chatId, string userId, string message);
    Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request);
    Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request);
    Task StreamChatCompletionAsync(StreamChatRequest request, CancellationToken cancellationToken = default);
    Task StreamUnifiedChatCompletionAsync(StreamChatRequest request, CancellationToken cancellationToken = default);
    Task StreamAssistantResponseAsync(string chatId, CancellationToken cancellationToken = default);
    Task<int> GetNextSequenceNumberAsync(string chatId);
    Task<string> CreateAssistantMessageForStreamingAsync(string chatId, int sequenceNumber);
    Task<string> GetMessageContentAsync(string messageId);

    // Events for real-time notifications
    event Func<MessageCreatedEvent, Task>? MessageCreated;
    event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    event Func<MessageEvent, Task>? MessageReceived;
}

// Request types
public class CreateChatRequest
{
    [JsonPropertyName("userId")]
    public required string UserId { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }
}

public class SendMessageRequest
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; set; }

    [JsonPropertyName("userId")]
    public required string UserId { get; set; }

    [JsonPropertyName("message")]
    public required string Message { get; set; }
}

public record StreamChatRequest
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("chatId")]
    public string? ChatId { get; init; }

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }
}

// Result types
public record ChatResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("chat")]
    public ChatDto? Chat { get; init; }
}

public record MessageResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("userMessage")]
    public MessageDto? UserMessage { get; init; }

    [JsonPropertyName("assistantMessage")]
    public MessageDto? AssistantMessage { get; init; }
}

public record StreamInitResult
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    [JsonPropertyName("userMessageId")]
    public required string UserMessageId { get; init; }

    [JsonPropertyName("userTimestamp")]
    public required DateTime UserTimestamp { get; init; }

    [JsonPropertyName("userSequenceNumber")]
    public required int UserSequenceNumber { get; init; }
}

public record ChatHistoryResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("chats")]
    public List<ChatDto> Chats { get; init; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("totalPages")]
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// Event types for real-time notifications
public record MessageCreatedEvent
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }
    
    [JsonPropertyName("message")]
    public required MessageDto Message { get; init; }
}

public abstract record StreamChunkEvent
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("done")]
    public required bool Done { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sequenceNumber")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("chunkSequenceId")]
    public required int ChunkSequenceId { get; init; }
}

public record ToolsCallUpdateStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("toolCallUpdate")]
    public required ToolCallUpdate ToolCallUpdate { get; init; }
}

public record ToolsCallAggregateStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("toolCalls")]
    public ToolCall[]? ToolCalls { get; init; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; init; }
}

public record ReasoningStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }

    [JsonPropertyName("visibility")]
    public ReasoningVisibility? Visibility { get; init; }
}

public record TextStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
}

public record ToolCallStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
    
    [JsonPropertyName("toolCalls")]
    public object[]? ToolCalls { get; init; }
}

public record ToolResultStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }
    
    [JsonPropertyName("result")]
    public required string Result { get; init; }
    
    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

public record MessageStreamCompleteEvent : StreamChunkEvent
{
}

public abstract record MessageEvent
{
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sequenceNumber")]
    public required int SequenceNumber { get; init; }
}

public record UsageEvent : MessageEvent
{
    [JsonPropertyName("usage")]
    public required Usage Usage { get; init; }
}

public record ReasoningEvent : MessageEvent
{
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
    
    [JsonPropertyName("visibility")]
    public ReasoningVisibility? Visibility { get; init; }
}

public record TextEvent : MessageEvent
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public record ToolCallEvent : MessageEvent
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; init; }
}

public record ToolsCallAggregateEvent : MessageEvent
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; init; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; init; }
}

// DTO types (moved from ChatController for reuse)
public record ChatDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("title")]
    public required string? Title { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }

    [JsonPropertyName("messages")]
    public List<MessageDto> Messages { get; init; } = new();
    
    [JsonPropertyName("tasks")]
    public IList<TaskItem>? Tasks { get; init; }
}

// Enable polymorphic serialization so derived message content (text/reasoning) is included in JSON
[JsonPolymorphic(TypeDiscriminatorPropertyName = "messageType")]
[JsonDerivedType(typeof(TextMessageDto), typeDiscriminator: "text")]
[JsonDerivedType(typeof(ReasoningMessageDto), typeDiscriminator: "reasoning")]
[JsonDerivedType(typeof(ToolCallMessageDto), typeDiscriminator: "tool_call")]
[JsonDerivedType(typeof(ToolResultMessageDto), typeDiscriminator: "tool_result")]
[JsonDerivedType(typeof(ToolsCallAggregateMessageDto), typeDiscriminator: "tools_aggregate")]
[JsonDerivedType(typeof(UsageMessageDto), typeDiscriminator: "usage")]
public class MessageDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("chatId")]
    public required string ChatId { get; set; }

    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("sequenceNumber")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("isHidden")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsHidden { get; set; }
}

// Shared JSON serializer options with polymorphic configuration
public static class MessageSerializationOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        // Enable polymorphic serialization with type discriminator
        WriteIndented = false,
        IncludeFields = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public class TextMessageDto : MessageDto
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

public class ReasoningMessageDto : MessageDto
{
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; set; }

    [JsonPropertyName("visibility")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReasoningVisibility Visibility { get; set; }

    public string? GetText() => Visibility == ReasoningVisibility.Encrypted ? null : Reasoning;
}

public class ToolCallMessageDto : MessageDto
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; set; }
}

public class ToolResultMessageDto : MessageDto
{
    [JsonPropertyName("toolResults")]
    public required ToolCallResult[] ToolResults { get; set; }
}

public class ToolsCallAggregateMessageDto : MessageDto
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; set; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; set; }
}

public class UsageMessageDto : MessageDto
{
    [JsonPropertyName("usage")]
    public required Usage Usage { get; set; }
}