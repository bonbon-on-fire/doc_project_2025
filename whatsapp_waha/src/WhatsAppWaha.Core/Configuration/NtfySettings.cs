using System.ComponentModel.DataAnnotations;

namespace WhatsAppWaha.Core.Configuration;

/// <summary>
/// Configuration settings for ntfy notification and message relay service.
/// </summary>
public class NtfySettings
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "Ntfy";

  /// <summary>
  /// Base URL for the ntfy service.
  /// </summary>
  [Required(ErrorMessage = "Ntfy BaseUrl is required")]
  [Url(ErrorMessage = "Ntfy BaseUrl must be a valid URL")]
  public string BaseUrl { get; set; } = string.Empty;

  /// <summary>
  /// Topic name for incoming WhatsApp messages (WAHA webhook target).
  /// </summary>
  [Required(ErrorMessage = "Ntfy MessagesTopic is required")]
  [StringLength(100, MinimumLength = 1, ErrorMessage = "Ntfy MessagesTopic must be between 1 and 100 characters")]
  [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Ntfy MessagesTopic can only contain letters, numbers, underscores, and hyphens")]
  public string MessagesTopic { get; set; } = string.Empty;

  /// <summary>
  /// Topic name for sending monitoring notifications.
  /// </summary>
  [Required(ErrorMessage = "Ntfy NotificationsTopic is required")]
  [StringLength(100, MinimumLength = 1, ErrorMessage = "Ntfy NotificationsTopic must be between 1 and 100 characters")]
  [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Ntfy NotificationsTopic can only contain letters, numbers, underscores, and hyphens")]
  public string NotificationsTopic { get; set; } = string.Empty;

  /// <summary>
  /// Polling interval in milliseconds for checking new messages.
  /// </summary>
  [Range(1000, 300000, ErrorMessage = "Ntfy PollingIntervalMs must be between 1000 and 300000 milliseconds (1 second to 5 minutes)")]
  public int PollingIntervalMs { get; set; } = 5000;

  /// <summary>
  /// Maximum number of messages to retrieve in a single polling request.
  /// </summary>
  [Range(1, 100, ErrorMessage = "Ntfy MaxMessagesPerPoll must be between 1 and 100")]
  public int MaxMessagesPerPoll { get; set; } = 10;

  /// <summary>
  /// Timeout in seconds for HTTP requests to ntfy service.
  /// </summary>
  [Range(5, 120, ErrorMessage = "Ntfy Timeout must be between 5 and 120 seconds")]
  public int TimeoutSeconds { get; set; } = 30;

  /// <summary>
  /// Authentication token for ntfy service (if required).
  /// </summary>
  public string? AuthToken { get; set; }

  /// <summary>
  /// Whether to verify SSL certificates for ntfy requests.
  /// </summary>
  public bool VerifySsl { get; set; } = true;

  /// <summary>
  /// Maximum number of processed message IDs to keep for deduplication.
  /// </summary>
  [Range(100, 10000, ErrorMessage = "Ntfy MaxProcessedMessageIds must be between 100 and 10000")]
  public int MaxProcessedMessageIds { get; set; } = 1000;

  /// <summary>
  /// Whether to enable fire-and-forget pattern for notifications (non-blocking).
  /// </summary>
  public bool EnableFireAndForget { get; set; } = true;
}
