using System.Text.Json.Serialization;
using AIChat.Orleans.Contracts;

namespace AIChat.Server.Models;

/// <summary>
/// Represents a chat operation that can be queued for background processing.
/// Used by BackgroundChatService to manage asynchronous chat operations.
/// </summary>
public class ChatOperation
{
    /// <summary>
    /// Unique identifier for this operation.
    /// Generated when the operation is enqueued.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Type of operation to perform.
    /// </summary>
    [JsonPropertyName("type")]
    public OperationType Type { get; set; }

    /// <summary>
    /// Chat ID where the operation should be performed.
    /// </summary>
    [JsonPropertyName("chatId")]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// User ID who initiated the operation.
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the operation was queued.
    /// </summary>
    [JsonPropertyName("queuedAt")]
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional mode ID for specialized behavior.
    /// </summary>
    [JsonPropertyName("modeId")]
    public string? ModeId { get; set; }

    /// <summary>
    /// Operation-specific payload data.
    /// For SendMessage: the message content and metadata
    /// For RegenerateResponse: the message ID to regenerate
    /// For EditMessage: the message ID and new content
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    /// <summary>
    /// Optional system prompt override for this operation.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Operation priority (higher numbers = higher priority).
    /// Default is 0 (normal priority).
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Maximum time to wait before timing out this operation.
    /// Default is 5 minutes.
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 300000; // 5 minutes
}

/// <summary>
/// Status of a background chat operation.
/// </summary>
public enum OperationStatus
{
    /// <summary>
    /// Operation is waiting in the queue to be processed.
    /// </summary>
    Queued,

    /// <summary>
    /// Operation is currently being processed.
    /// </summary>
    InProgress,

    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Operation failed with an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Operation was cancelled before completion.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Operation timed out.
    /// </summary>
    TimedOut,
}

/// <summary>
/// Information about the current status of a background operation.
/// </summary>
public class OperationStatusInfo
{
    /// <summary>
    /// Operation identifier.
    /// </summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the operation.
    /// </summary>
    [JsonPropertyName("status")]
    public OperationStatus Status { get; set; }

    /// <summary>
    /// Timestamp when operation was queued.
    /// </summary>
    [JsonPropertyName("queuedAt")]
    public DateTime QueuedAt { get; set; }

    /// <summary>
    /// Timestamp when operation started processing (if applicable).
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Timestamp when operation completed (if applicable).
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Progress information (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("progress")]
    public double Progress { get; set; } = 0.0;

    /// <summary>
    /// Human-readable progress description.
    /// </summary>
    [JsonPropertyName("progressDescription")]
    public string? ProgressDescription { get; set; }
}
