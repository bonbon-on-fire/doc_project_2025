namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Optional parameters for sending text messages via WAHA API.
/// Based on WAHA API Reference SendTextOptions model.
/// </summary>
public sealed class SendTextOptions
{
    /// <summary>
    /// Message ID to reply to.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// List of user IDs to mention in the message.
    /// </summary>
    public List<string>? Mentions { get; init; }

    /// <summary>
    /// Whether to show link preview (default: true).
    /// </summary>
    public bool? LinkPreview { get; init; }
}