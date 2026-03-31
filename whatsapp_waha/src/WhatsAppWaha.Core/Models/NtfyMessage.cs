using System.Text.Json.Serialization;

namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents a message received from the ntfy JSON API when polling for messages.
/// </summary>
public sealed class NtfyMessage
{
    /// <summary>
    /// Unique message identifier from ntfy.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Unix timestamp when the message was published.
    /// </summary>
    [JsonPropertyName("time")]
    public long Timestamp { get; init; }

    /// <summary>
    /// Unix timestamp when the message expires (if applicable).
    /// </summary>
    [JsonPropertyName("expires")]
    public long? Expires { get; init; }

    /// <summary>
    /// The topic this message was published to.
    /// </summary>
    [JsonPropertyName("topic")]
    public string Topic { get; init; } = string.Empty;

    /// <summary>
    /// The message content/payload.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The message title (if provided).
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// Message priority level (1-5, with 5 being highest).
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    /// <summary>
    /// Tags associated with the message.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    /// <summary>
    /// Click action URL if provided.
    /// </summary>
    [JsonPropertyName("click")]
    public string? ClickUrl { get; init; }

    /// <summary>
    /// Attachment information if the message has an attachment.
    /// </summary>
    [JsonPropertyName("attachment")]
    public NtfyAttachment? Attachment { get; init; }

    /// <summary>
    /// Actions associated with the message (buttons, etc.).
    /// </summary>
    [JsonPropertyName("actions")]
    public NtfyAction[]? Actions { get; init; }

    /// <summary>
    /// The event type for this message (e.g., "message", "keepalive").
    /// </summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    /// <summary>
    /// Gets the DateTime representation of the timestamp.
    /// </summary>
    [JsonIgnore]
    public DateTime DateTime => DateTimeOffset.FromUnixTimeSeconds(Timestamp).DateTime;

    /// <summary>
    /// Gets the DateTime representation of the expiration timestamp.
    /// </summary>
    [JsonIgnore]
    public DateTime? ExpirationDateTime => Expires.HasValue 
        ? DateTimeOffset.FromUnixTimeSeconds(Expires.Value).DateTime 
        : null;

    /// <summary>
    /// Indicates whether this is a valid message (not a keepalive or empty).
    /// </summary>
    [JsonIgnore]
    public bool IsValidMessage => !string.IsNullOrWhiteSpace(Id) && 
                                  !string.Equals(Event, "keepalive", StringComparison.OrdinalIgnoreCase) &&
                                  !string.IsNullOrWhiteSpace(Message);
}

/// <summary>
/// Represents an attachment in an ntfy message.
/// </summary>
public sealed class NtfyAttachment
{
    /// <summary>
    /// The attachment filename.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The attachment MIME type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// The attachment size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// The URL to download the attachment.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>
/// Represents an action button in an ntfy message.
/// </summary>
public sealed class NtfyAction
{
    /// <summary>
    /// The action identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The action type (e.g., "view", "http").
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    /// <summary>
    /// The action label displayed to the user.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>
    /// The URL for view actions or HTTP requests.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>
    /// The HTTP method for HTTP actions.
    /// </summary>
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    /// <summary>
    /// Headers for HTTP actions.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Body content for HTTP actions.
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}