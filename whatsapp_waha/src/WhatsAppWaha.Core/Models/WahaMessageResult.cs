using System.Text.Json.Serialization;

namespace WhatsAppWaha.Core.Models;

/// <summary>
/// Represents the result from WAHA after sending a text message.
/// </summary>
public sealed class WahaMessageResult
{
  /// <summary>
  /// The message identifier returned by WAHA on success.
  /// </summary>
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  /// <summary>
  /// Status returned by WAHA (e.g., "sent", "queued").
  /// </summary>
  [JsonPropertyName("status")]
  public string? Status { get; init; }

  /// <summary>
  /// Unix timestamp of the operation when provided.
  /// </summary>
  [JsonPropertyName("timestamp")]
  public long? Timestamp { get; init; }

  /// <summary>
  /// Error code returned by WAHA if request failed.
  /// </summary>
  [JsonPropertyName("error")]
  public string? Error { get; init; }

  /// <summary>
  /// Human-readable message accompanying an error.
  /// </summary>
  [JsonPropertyName("message")]
  public string? Message { get; init; }

  /// <summary>
  /// Indicates whether the operation is successful based on presence of Id and no error.
  /// </summary>
  [JsonIgnore]
  public bool IsSuccess => !string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Error);
}
