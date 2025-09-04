using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Represents a chat message in the Orleans system.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.ChatMessage")]
public sealed class ChatMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    [Id(0)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Chat room identifier where message belongs.
    /// </summary>
    [Id(1)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// User who sent the message.
    /// </summary>
    [Id(2)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Message content.
    /// </summary>
    [Id(3)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Message role (user, assistant, system).
    /// </summary>
    [Id(4)]
    public string Role { get; set; } = "user";

    /// <summary>
    /// Message creation timestamp.
    /// </summary>
    [Id(5)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message metadata (JSON).
    /// </summary>
    [Id(6)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Parent message ID for replies/threads.
    /// </summary>
    [Id(7)]
    public string? ParentId { get; set; }

    /// <summary>
    /// Whether this is a streaming message.
    /// </summary>
    [Id(8)]
    public bool IsStreaming { get; set; } = false;
}

/// <summary>
/// Represents a streaming chunk for real-time message delivery.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.StreamChunk")]
public sealed class StreamChunk
{
    /// <summary>
    /// Operation ID this chunk belongs to.
    /// </summary>
    [Id(0)]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Chat ID where chunk is being delivered.
    /// </summary>
    [Id(1)]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>
    /// Content of this chunk.
    /// </summary>
    [Id(2)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Index of this chunk in the sequence.
    /// </summary>
    [Id(3)]
    public int ChunkIndex { get; set; } = 0;

    /// <summary>
    /// Whether this is the final chunk.
    /// </summary>
    [Id(4)]
    public bool IsComplete { get; set; } = false;

    /// <summary>
    /// Chunk creation timestamp.
    /// </summary>
    [Id(5)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message ID this chunk is building.
    /// </summary>
    [Id(6)]
    public string? MessageId { get; set; }

    /// <summary>
    /// Total expected chunks (if known).
    /// </summary>
    [Id(7)]
    public int? TotalChunks { get; set; }
}

/// <summary>
/// Health check result for grain monitoring.
/// </summary>
[Serializable]
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("AIChat.Orleans.Contracts.HealthCheckResult")]
public sealed class HealthCheckResult
{
    /// <summary>
    /// Whether the grain is healthy.
    /// </summary>
    [Id(0)]
    public bool IsHealthy { get; set; } = false;

    /// <summary>
    /// Grain identifier.
    /// </summary>
    [Id(1)]
    public string GrainId { get; set; } = string.Empty;

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    [Id(2)]
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// Current grain metrics.
    /// </summary>
    [Id(3)]
    public GrainMetrics? Metrics { get; set; }

    /// <summary>
    /// Health check timestamp.
    /// </summary>
    [Id(4)]
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional health information.
    /// </summary>
    [Id(5)]
    public string? AdditionalInfo { get; set; }

    /// <summary>
    /// Any warnings or issues found.
    /// </summary>
    [Id(6)]
    public List<string> Warnings { get; set; } = new();
}