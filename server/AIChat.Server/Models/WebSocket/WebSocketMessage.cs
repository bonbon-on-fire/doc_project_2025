using System.Text.Json.Serialization;

namespace AIChat.Server.Models.WebSocket;

/// <summary>
/// Represents a structured WebSocket message that can be sent between client and server.
/// This message format provides a standardized envelope for different types of WebSocket communications.
/// </summary>
public class WebSocketMessage
{
    /// <summary>
    /// Gets or sets the type of message being sent.
    /// Used to determine how the message should be processed.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the session identifier associated with this message.
    /// Used to correlate messages with Orleans session grains.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the protocol being used for this message.
    /// Optional - used during protocol negotiation and for protocol-specific handling.
    /// </summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    /// <summary>
    /// Gets or sets the message payload.
    /// The structure varies based on the message type.
    /// </summary>
    [JsonPropertyName("payload")]
    public required object Payload { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the message was created.
    /// Used for message ordering and debugging.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the unique identifier for this message.
    /// Used for message correlation and deduplication.
    /// </summary>
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the correlation identifier for request-response patterns.
    /// Optional - used to match responses to their originating requests.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Defines the standard message types supported by the WebSocket handler.
/// </summary>
public static class WebSocketMessageTypes
{
    /// <summary>
    /// Message type for protocol negotiation between client and server.
    /// </summary>
    public const string ProtocolNegotiation = "protocol-negotiation";

    /// <summary>
    /// Message type for chat-related communications.
    /// </summary>
    public const string ChatMessage = "chat-message";

    /// <summary>
    /// Message type for heartbeat/keepalive signals.
    /// </summary>
    public const string Heartbeat = "heartbeat";

    /// <summary>
    /// Message type for control and administrative commands.
    /// </summary>
    public const string Control = "control";

    /// <summary>
    /// Message type for error notifications from server to client.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// Message type for successful operation acknowledgments.
    /// </summary>
    public const string Acknowledgment = "acknowledgment";
}

/// <summary>
/// Represents the payload for protocol negotiation messages.
/// </summary>
public class ProtocolNegotiationPayload
{
    /// <summary>
    /// Gets or sets the list of protocols supported or requested by the client.
    /// </summary>
    [JsonPropertyName("supportedProtocols")]
    public List<string> SupportedProtocols { get; set; } = new();

    /// <summary>
    /// Gets or sets the preferred protocol for the connection.
    /// </summary>
    [JsonPropertyName("preferredProtocol")]
    public string? PreferredProtocol { get; set; }

    /// <summary>
    /// Gets or sets the client capabilities for protocol features.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public Dictionary<string, object> Capabilities { get; set; } = new();
}

/// <summary>
/// Represents the payload for heartbeat messages.
/// </summary>
public class HeartbeatPayload
{
    /// <summary>
    /// Gets or sets the client timestamp when the heartbeat was sent.
    /// Used for latency calculation.
    /// </summary>
    [JsonPropertyName("clientTimestamp")]
    public DateTime ClientTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets any additional heartbeat data.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Represents the payload for error messages.
/// </summary>
public class ErrorPayload
{
    /// <summary>
    /// Gets or sets the error code for categorization.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public required string ErrorCode { get; set; }

    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets additional error details.
    /// </summary>
    [JsonPropertyName("details")]
    public Dictionary<string, object>? Details { get; set; }

    /// <summary>
    /// Gets or sets whether the error is recoverable.
    /// </summary>
    [JsonPropertyName("recoverable")]
    public bool Recoverable { get; set; } = true;
}