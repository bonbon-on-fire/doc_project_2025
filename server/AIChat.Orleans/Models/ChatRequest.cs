using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Request for processing chat messages through Orleans grains.
/// Provides all necessary context for message processing and streaming.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ChatRequest")]
public sealed class ChatRequest
{
    /// <summary>
    /// Chat ID to process the message for.
    /// </summary>
    [Id(0)]
    public required string ChatId { get; set; }

    /// <summary>
    /// User message content to process.
    /// </summary>
    [Id(1)]
    public required string Message { get; set; }

    /// <summary>
    /// User ID for context and authorization.
    /// </summary>
    [Id(2)]
    public required string UserId { get; set; }

    /// <summary>
    /// Optional mode ID for specialized behavior.
    /// </summary>
    [Id(3)]
    public string? ModeId { get; set; }

    /// <summary>
    /// Optional system prompt override.
    /// </summary>
    [Id(4)]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Optional metadata for the request (JSON).
    /// </summary>
    [Id(5)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Request timestamp.
    /// </summary>
    [Id(6)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Unique request ID for tracking.
    /// </summary>
    [Id(7)]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Represents the state of an active stream in the grain.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StreamState")]
public sealed class StreamState
{
    /// <summary>
    /// Unique stream identifier.
    /// </summary>
    [Id(0)]
    public string StreamId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Associated chat ID.
    /// </summary>
    [Id(1)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// User ID who initiated the stream.
    /// </summary>
    [Id(2)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Stream start timestamp.
    /// </summary>
    [Id(3)]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last activity timestamp for timeout tracking.
    /// </summary>
    [Id(4)]
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of chunks sent.
    /// </summary>
    [Id(5)]
    public int ChunksSent { get; set; } = 0;

    /// <summary>
    /// Current stream status.
    /// </summary>
    [Id(6)]
    public StreamStatus Status { get; set; } = StreamStatus.Active;

    /// <summary>
    /// Partial message buffer for recovery.
    /// </summary>
    [Id(7)]
    public string PartialMessage { get; set; } = string.Empty;

    /// <summary>
    /// Error message if stream failed.
    /// </summary>
    [Id(8)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Associated operation ID if any.
    /// </summary>
    [Id(9)]
    public string? OperationId { get; set; }
}

/// <summary>
/// Status of a streaming operation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.StreamStatus")]
public enum StreamStatus
{
    /// <summary>
    /// Stream is actively processing.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Stream completed successfully.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Stream was cancelled.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// Stream failed with error.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Stream timed out.
    /// </summary>
    TimedOut = 4,
}
