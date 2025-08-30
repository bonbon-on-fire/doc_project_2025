namespace AIChat.Orleans.Contracts;

/// <summary>
/// Represents a chat message in the Orleans system.
/// </summary>
[Serializable]
public sealed class ChatMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>

    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Chat room identifier where message belongs.
    /// </summary>

    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// User who sent the message.
    /// </summary>

    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Message content.
    /// </summary>

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Message role (user, assistant, system).
    /// </summary>

    public string Role { get; set; } = "user";

    /// <summary>
    /// Message creation timestamp.
    /// </summary>

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message metadata (JSON).
    /// </summary>

    public string? Metadata { get; set; }

    /// <summary>
    /// Parent message ID for replies/threads.
    /// </summary>

    public string? ParentId { get; set; }

    /// <summary>
    /// Whether this is a streaming message.
    /// </summary>

    public bool IsStreaming { get; set; } = false;
}

/// <summary>
/// Represents a streaming chunk for real-time message delivery.
/// </summary>
[Serializable]
public sealed class StreamChunk
{
    /// <summary>
    /// Operation ID this chunk belongs to.
    /// </summary>

    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Chat ID where chunk is being delivered.
    /// </summary>

    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Content of this chunk.
    /// </summary>

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Index of this chunk in the sequence.
    /// </summary>

    public int ChunkIndex { get; set; } = 0;

    /// <summary>
    /// Whether this is the final chunk.
    /// </summary>

    public bool IsComplete { get; set; } = false;

    /// <summary>
    /// Chunk creation timestamp.
    /// </summary>

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message ID this chunk is building.
    /// </summary>

    public string? MessageId { get; set; }

    /// <summary>
    /// Total expected chunks (if known).
    /// </summary>

    public int? TotalChunks { get; set; }
}

/// <summary>
/// Health check result for grain monitoring.
/// </summary>
[Serializable]
public sealed class HealthCheckResult
{
    /// <summary>
    /// Whether the grain is healthy.
    /// </summary>

    public bool IsHealthy { get; set; } = false;

    /// <summary>
    /// Grain identifier.
    /// </summary>

    public string GrainId { get; set; } = string.Empty;

    /// <summary>
    /// Last activity timestamp.
    /// </summary>

    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Current grain metrics.
    /// </summary>

    public GrainMetrics? Metrics { get; set; }

    /// <summary>
    /// Health check timestamp.
    /// </summary>

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional health information.
    /// </summary>

    public string? AdditionalInfo { get; set; }

    /// <summary>
    /// Any warnings or issues found.
    /// </summary>

    public List<string> Warnings { get; set; } = new();
}