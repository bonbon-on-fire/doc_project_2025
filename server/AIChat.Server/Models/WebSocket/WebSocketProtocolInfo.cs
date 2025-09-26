using System.Text.Json.Serialization;

namespace AIChat.Server.Models.WebSocket;

/// <summary>
/// Represents information about supported WebSocket protocols and their capabilities.
/// Used during protocol negotiation to establish the communication protocol between client and server.
/// </summary>
public class WebSocketProtocolInfo
{
    /// <summary>
    /// Gets or sets the protocol name (e.g., "chat-v1", "notifications-v1").
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the protocol version.
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>
    /// Gets or sets the human-readable description of the protocol.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the supported capabilities for this protocol.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public Dictionary<string, object> Capabilities { get; set; } = [];

    /// <summary>
    /// Gets or sets the configuration options for this protocol.
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this protocol is currently enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority of this protocol during negotiation.
    /// Higher numbers indicate higher priority.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Represents the result of a protocol negotiation between client and server.
/// </summary>
public class ProtocolNegotiationResult
{
    /// <summary>
    /// Gets or sets whether the negotiation was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the selected protocol information.
    /// Null if negotiation failed.
    /// </summary>
    [JsonPropertyName("selectedProtocol")]
    public WebSocketProtocolInfo? SelectedProtocol { get; set; }

    /// <summary>
    /// Gets or sets any error message if negotiation failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the list of protocols that were considered during negotiation.
    /// </summary>
    [JsonPropertyName("availableProtocols")]
    public List<WebSocketProtocolInfo> AvailableProtocols { get; set; } = [];

    /// <summary>
    /// Gets or sets the negotiated capabilities for the selected protocol.
    /// </summary>
    [JsonPropertyName("negotiatedCapabilities")]
    public Dictionary<string, object> NegotiatedCapabilities { get; set; } = [];

    /// <summary>
    /// Creates a successful negotiation result.
    /// </summary>
    /// <param name="selectedProtocol">The protocol that was selected</param>
    /// <param name="negotiatedCapabilities">The capabilities that were negotiated</param>
    /// <returns>A successful protocol negotiation result</returns>
    public static ProtocolNegotiationResult CreateSuccess(
        WebSocketProtocolInfo selectedProtocol,
        Dictionary<string, object>? negotiatedCapabilities = null)
    {
        return new ProtocolNegotiationResult
        {
            Success = true,
            SelectedProtocol = selectedProtocol,
            NegotiatedCapabilities = negotiatedCapabilities ?? []
        };
    }

    /// <summary>
    /// Creates a failed negotiation result.
    /// </summary>
    /// <param name="errorMessage">The error message describing why negotiation failed</param>
    /// <param name="availableProtocols">The protocols that were available</param>
    /// <returns>A failed protocol negotiation result</returns>
    public static ProtocolNegotiationResult CreateFailure(
        string errorMessage,
        List<WebSocketProtocolInfo>? availableProtocols = null)
    {
        return new ProtocolNegotiationResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            AvailableProtocols = availableProtocols ?? []
        };
    }
}

/// <summary>
/// Defines standard WebSocket protocols supported by the system.
/// </summary>
public static class StandardWebSocketProtocols
{
    /// <summary>
    /// Chat protocol for real-time messaging.
    /// </summary>
    public static readonly WebSocketProtocolInfo ChatV1 = new()
    {
        Name = "chat-v1",
        Version = "1.0",
        Description = "Real-time chat messaging protocol",
        Capabilities = new Dictionary<string, object>
        {
            ["messaging"] = true,
            ["typing-indicators"] = true,
            ["presence"] = true,
            ["message-history"] = true
        },
        Priority = 100
    };

    /// <summary>
    /// Notifications protocol for system notifications.
    /// </summary>
    public static readonly WebSocketProtocolInfo NotificationsV1 = new()
    {
        Name = "notifications-v1",
        Version = "1.0",
        Description = "System notifications and alerts protocol",
        Capabilities = new Dictionary<string, object>
        {
            ["push-notifications"] = true,
            ["alert-levels"] = new[] { "info", "warning", "error" },
            ["acknowledgment"] = true
        },
        Priority = 80
    };

    /// <summary>
    /// File transfer protocol for file uploads/downloads.
    /// </summary>
    public static readonly WebSocketProtocolInfo FileTransferV1 = new()
    {
        Name = "file-transfer-v1",
        Version = "1.0",
        Description = "File transfer and management protocol",
        Capabilities = new Dictionary<string, object>
        {
            ["upload"] = true,
            ["download"] = true,
            ["progress-tracking"] = true,
            ["resume"] = true,
            ["max-file-size"] = 100_000_000 // 100MB
        },
        Priority = 60
    };

    /// <summary>
    /// Generic protocol for basic WebSocket communication.
    /// </summary>
    public static readonly WebSocketProtocolInfo GenericV1 = new()
    {
        Name = "generic-v1",
        Version = "1.0",
        Description = "Generic WebSocket communication protocol",
        Capabilities = new Dictionary<string, object>
        {
            ["text-messages"] = true,
            ["binary-messages"] = true,
            ["heartbeat"] = true
        },
        Priority = 10
    };

    /// <summary>
    /// Gets all available standard protocols.
    /// </summary>
    /// <returns>List of all standard protocols</returns>
    public static List<WebSocketProtocolInfo> GetAllProtocols()
    {
        return
        [
            ChatV1,
            NotificationsV1,
            FileTransferV1,
            GenericV1
        ];
    }

    /// <summary>
    /// Gets a protocol by name.
    /// </summary>
    /// <param name="protocolName">The name of the protocol to find</param>
    /// <returns>The protocol info if found, null otherwise</returns>
    public static WebSocketProtocolInfo? GetProtocol(string protocolName)
    {
        return GetAllProtocols().FirstOrDefault(p =>
            string.Equals(p.Name, protocolName, StringComparison.OrdinalIgnoreCase));
    }
}