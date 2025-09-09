using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AIChat.Orleans.Client.Services;
using AIChat.Server.Storage;

namespace AIChat.Server.Services;

/// <summary>
/// Streaming interface for ChatService that supports background processing scenarios
/// This interface provides callback-based streaming suitable for Orleans grains and other background services
/// </summary>
public interface IChatServiceStreaming
{
    /// <summary>
    /// Process a message with streaming callbacks for background services
    /// This method is stateless and suitable for singleton services
    /// </summary>
    /// <param name="chatId">The chat ID to process the message for</param>
    /// <param name="message">User message content</param>
    /// <param name="userId">User ID for context and authorization</param>
    /// <param name="storage">Chat storage service</param>
    /// <param name="streamingAgent">Streaming agent for AI responses</param>
    /// <param name="toolingService">Service for handling tool calls</param>
    /// <param name="modeService">Service for handling mode-specific behavior</param>
    /// <param name="orleansService">Optional Orleans integration service</param>
    /// <param name="modeId">Optional mode ID for specialized behavior</param>
    /// <param name="systemPrompt">Optional system prompt override</param>
    /// <param name="messageCallback">Callback for complete messages (tool calls, text, reasoning)</param>
    /// <param name="chunkCallback">Callback for streaming chunks (real-time updates)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the completion of message processing</returns>
    Task ProcessMessageWithCallbackAsync(
        string chatId,
        string message,
        string userId,
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        IToolingService toolingService,
        IModeService modeService,
        IOrleansIntegrationService? orleansService = null,
        string? modeId = null,
        string? systemPrompt = null,
        Func<MessageEvent, Task>? messageCallback = null,
        Func<StreamChunkEvent, Task>? chunkCallback = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Create a new chat (stateless version)
    /// </summary>
    /// <param name="request">Chat creation request</param>
    /// <param name="storage">Chat storage service</param>
    /// <param name="streamingAgent">Streaming agent for AI responses</param>
    /// <param name="modeService">Mode service for system prompts</param>
    /// <param name="orleansService">Optional Orleans integration service</param>
    /// <returns>Result containing the created chat or error information</returns>
    Task<ChatResult> CreateChatAsync(
        CreateChatRequest request,
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        IModeService modeService,
        IOrleansIntegrationService? orleansService = null
    );

    /// <summary>
    /// Get chat by ID (stateless version)
    /// </summary>
    /// <param name="chatId">The ID of the chat to retrieve</param>
    /// <param name="storage">Chat storage service</param>
    /// <param name="taskManagerService">Task manager service</param>
    /// <returns>Result containing the chat data or error information</returns>
    Task<ChatResult> GetChatAsync(
        string chatId,
        IChatStorage storage,
        ITaskManagerService taskManagerService
    );

    /// <summary>
    /// Delete a chat (stateless version)
    /// </summary>
    /// <param name="chatId">The ID of the chat to delete</param>
    /// <param name="storage">Chat storage service</param>
    /// <param name="taskManagerService">Task manager service</param>
    /// <returns>True if deletion was successful, false otherwise</returns>
    Task<bool> DeleteChatAsync(
        string chatId,
        IChatStorage storage,
        ITaskManagerService taskManagerService
    );

    /// <summary>
    /// Add a user message to an existing chat (stateless version)
    /// </summary>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="message">The message content</param>
    /// <param name="storage">Chat storage service</param>
    /// <returns>Result containing the added message or error information</returns>
    Task<MessageResult> AddUserMessageToExistingChatAsync(
        string chatId,
        string userId,
        string message,
        IChatStorage storage
    );
}

/// <summary>
/// Context object for managing streaming state in a stateless service
/// </summary>
public class StreamingContext
{
    /// <summary>
    /// Current chat ID being processed
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// Current message ID being processed (for tool correlation)
    /// </summary>
    public string? CurrentMessageId { get; set; }

    /// <summary>
    /// Next sequence number for messages
    /// </summary>
    public int NextSequence { get; set; }

    /// <summary>
    /// Map tool call IDs to their corresponding message IDs and sequence numbers
    /// Thread-safe for concurrent access during streaming
    /// </summary>
    public ConcurrentDictionary<string, (string MessageId, int SequenceNumber)> ToolCallToMessageMap { get; } = new();

    /// <summary>
    /// Map tool call IDs to function names for TaskManager detection
    /// Thread-safe for concurrent access during streaming
    /// </summary>
    public ConcurrentDictionary<string, string> ToolCallToFunctionMap { get; } = new();

    /// <summary>
    /// Last seen tool call ID for sequential streaming updates
    /// </summary>
    public string? LastSeenToolCallId { get; set; }

    /// <summary>
    /// User ID for this streaming context
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Mode ID for this streaming context (optional)
    /// </summary>
    public string? ModeId { get; init; }
}

/// <summary>
/// Request for processing messages in background scenarios
/// </summary>
public record ProcessMessageRequest
{
    /// <summary>
    /// Chat ID to process the message for
    /// </summary>
    [JsonPropertyName("chatId")]
    public required string ChatId { get; init; }

    /// <summary>
    /// User message content
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// User ID for context and authorization
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Optional mode ID for specialized behavior
    /// </summary>
    [JsonPropertyName("modeId")]
    public string? ModeId { get; init; }

    /// <summary>
    /// Optional system prompt override
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }
}
