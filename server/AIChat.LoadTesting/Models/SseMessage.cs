namespace AIChat.LoadTesting.Models;

/// <summary>
/// Represents a single Server-Sent Events (SSE) message.
/// </summary>
public class SseMessage
{
    /// <summary>
    /// Gets or sets the unique identifier for this message.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the event type of the message.
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data payload of the message.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the message was received.
    /// </summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>
    /// Gets or sets the latency in milliseconds from send to receive.
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the retry interval in milliseconds, if specified by the server.
    /// </summary>
    public int? RetryMs { get; set; }

    /// <summary>
    /// Gets or sets any additional metadata associated with the message.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
