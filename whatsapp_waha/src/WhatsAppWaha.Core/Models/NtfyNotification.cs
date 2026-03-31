using System.Text.Json.Serialization;

namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents a notification to be sent to ntfy.
/// </summary>
public sealed class NtfyNotification
{
    /// <summary>
    /// The notification title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// The notification message content.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The topic to send the notification to.
    /// </summary>
    [JsonIgnore]
    public string Topic { get; init; } = string.Empty;

    /// <summary>
    /// Message priority (1-5, with 5 being highest).
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; init; }

    /// <summary>
    /// Tags to associate with the notification.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    /// <summary>
    /// Click action URL.
    /// </summary>
    [JsonPropertyName("click")]
    public string? ClickUrl { get; init; }

    /// <summary>
    /// Icon URL or emoji for the notification.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>
    /// Actions to include with the notification.
    /// </summary>
    [JsonPropertyName("actions")]
    public NtfyAction[]? Actions { get; init; }

    /// <summary>
    /// Email address to send the notification to (if email delivery is configured).
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// The timestamp when this notification was created.
    /// </summary>
    [JsonIgnore]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a success notification.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <returns>A success notification.</returns>
    public static NtfyNotification CreateSuccess(string topic, string title, string message)
    {
        return new NtfyNotification
        {
            Topic = topic,
            Title = title,
            Message = message,
            Priority = 3,
            Tags = new[] { "white_check_mark" },
            Icon = "✅"
        };
    }

    /// <summary>
    /// Creates an error notification.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <returns>An error notification.</returns>
    public static NtfyNotification CreateError(string topic, string title, string message)
    {
        return new NtfyNotification
        {
            Topic = topic,
            Title = title,
            Message = message,
            Priority = 4,
            Tags = new[] { "x", "rotating_light" },
            Icon = "❌"
        };
    }

    /// <summary>
    /// Creates an info notification.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <returns>An info notification.</returns>
    public static NtfyNotification CreateInfo(string topic, string title, string message)
    {
        return new NtfyNotification
        {
            Topic = topic,
            Title = title,
            Message = message,
            Priority = 2,
            Tags = new[] { "information_source" },
            Icon = "ℹ️"
        };
    }

    /// <summary>
    /// Creates a warning notification.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <returns>A warning notification.</returns>
    public static NtfyNotification CreateWarning(string topic, string title, string message)
    {
        return new NtfyNotification
        {
            Topic = topic,
            Title = title,
            Message = message,
            Priority = 3,
            Tags = new[] { "warning" },
            Icon = "⚠️"
        };
    }

    /// <summary>
    /// Creates a message processing notification.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="messageCount">The number of messages processed.</param>
    /// <param name="processingTimeMs">The processing time in milliseconds.</param>
    /// <returns>A message processing notification.</returns>
    public static NtfyNotification CreateMessageProcessing(string topic, int messageCount, long processingTimeMs)
    {
        var title = messageCount == 1 
            ? "Message Processed" 
            : $"{messageCount} Messages Processed";

        var message = processingTimeMs > 0 
            ? $"Processing completed in {processingTimeMs}ms"
            : "Processing completed";

        return new NtfyNotification
        {
            Topic = topic,
            Title = title,
            Message = message,
            Priority = 2,
            Tags = new[] { "gear" },
            Icon = "⚙️"
        };
    }

    /// <summary>
    /// Creates a notification for WAHA message sending events.
    /// </summary>
    /// <param name="topic">The topic to send to.</param>
    /// <param name="phoneNumber">The phone number the message was sent to.</param>
    /// <param name="messageText">The message text that was sent.</param>
    /// <param name="success">Whether the sending was successful.</param>
    /// <returns>A WAHA message sending notification.</returns>
    public static NtfyNotification CreateWahaMessageSent(string topic, string phoneNumber, string messageText, bool success)
    {
        var title = success ? "Message Sent" : "Message Send Failed";
        var truncatedText = messageText.Length > 50 ? messageText.Substring(0, 47) + "..." : messageText;
        var message = $"To {phoneNumber}: {truncatedText}";

        return success
            ? CreateSuccess(topic, title, message)
            : CreateError(topic, title, message);
    }
}