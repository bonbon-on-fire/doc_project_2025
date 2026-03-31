using System.ComponentModel.DataAnnotations;

namespace WhatsAppWaha.Core.Configuration;

/// <summary>
/// Configuration settings for WAHA (WhatsApp HTTP API) integration.
/// </summary>
public class WahaSettings
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "Waha";

  /// <summary>
  /// Base URL for the WAHA API server.
  /// </summary>
  [Required(ErrorMessage = "WAHA BaseUrl is required")]
  [Url(ErrorMessage = "WAHA BaseUrl must be a valid URL")]
  public string BaseUrl { get; set; } = string.Empty;

  /// <summary>
  /// Session ID for WhatsApp connection.
  /// </summary>
  [Required(ErrorMessage = "WAHA Session is required")]
  [StringLength(50, MinimumLength = 1, ErrorMessage = "WAHA Session must be between 1 and 50 characters")]
  public string Session { get; set; } = string.Empty;

  /// <summary>
  /// Timeout in seconds for HTTP requests to WAHA API.
  /// </summary>
  [Range(5, 300, ErrorMessage = "WAHA Timeout must be between 5 and 300 seconds")]
  public int TimeoutSeconds { get; set; } = 30;

  /// <summary>
  /// Maximum number of retry attempts for failed requests.
  /// </summary>
  [Range(0, 10, ErrorMessage = "WAHA MaxRetryAttempts must be between 0 and 10")]
  public int MaxRetryAttempts { get; set; } = 3;

  /// <summary>
  /// Base delay in milliseconds for exponential backoff retry policy.
  /// </summary>
  [Range(100, 10000, ErrorMessage = "WAHA RetryDelayMs must be between 100 and 10000 milliseconds")]
  public int RetryDelayMs { get; set; } = 1000;

  /// <summary>
  /// API key for WAHA authentication (if required).
  /// </summary>
  public string? ApiKey { get; set; }

  /// <summary>
  /// Whether to verify SSL certificates for WAHA requests.
  /// </summary>
  public bool VerifySsl { get; set; } = true;
}
