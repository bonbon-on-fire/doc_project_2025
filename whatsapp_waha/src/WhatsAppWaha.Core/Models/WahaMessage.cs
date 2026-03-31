using System.Text.Json.Serialization;

namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents a request payload to WAHA /api/sendText endpoint.
/// Based on WAHA API Reference - supports additional messaging options.
/// </summary>
public sealed class WahaMessage
{
  /// <summary>
  /// WAHA session identifier.
  /// </summary>
  [JsonPropertyName("session")]
  public string Session { get; init; } = string.Empty;

  /// <summary>
  /// Chat identifier in WAHA format (e.g., 15551234567@c.us for WhatsApp).
  /// </summary>
  [JsonPropertyName("chatId")]
  public string ChatId { get; init; } = string.Empty;

  /// <summary>
  /// Text content to send.
  /// </summary>
  [JsonPropertyName("text")]
  public string Text { get; init; } = string.Empty;

  /// <summary>
  /// Optional message ID to reply to.
  /// </summary>
  [JsonPropertyName("replyTo")]
  public string? ReplyTo { get; init; }

  /// <summary>
  /// Optional list of user IDs to mention in the message.
  /// </summary>
  [JsonPropertyName("mentions")]
  public List<string>? Mentions { get; init; }

  /// <summary>
  /// Whether to show link preview (default: true).
  /// </summary>
  [JsonPropertyName("linkPreview")]
  public bool? LinkPreview { get; init; }
}
