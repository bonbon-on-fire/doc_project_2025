using System.ComponentModel.DataAnnotations;

namespace WhatsAppWaha.Core.Configuration;

/// <summary>
/// General application configuration settings.
/// </summary>
public class AppSettings
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "App";

  /// <summary>
  /// Name of the application for logging and monitoring.
  /// </summary>
  [Required(ErrorMessage = "App Name is required")]
  [StringLength(100, MinimumLength = 1, ErrorMessage = "App Name must be between 1 and 100 characters")]
  public string Name { get; set; } = "WhatsApp WAHA Framework";

  /// <summary>
  /// Version of the application.
  /// </summary>
  [Required(ErrorMessage = "App Version is required")]
  [RegularExpression(@"^\d+\.\d+\.\d+(?:\.\d+)?$", ErrorMessage = "App Version must be in format 'x.y.z' or 'x.y.z.w'")]
  public string Version { get; set; } = "1.0.0";

  /// <summary>
  /// Environment name (Development, Staging, Production).
  /// </summary>
  [Required(ErrorMessage = "App Environment is required")]
  [StringLength(50, MinimumLength = 1, ErrorMessage = "App Environment must be between 1 and 50 characters")]
  public string Environment { get; set; } = "Development";

  /// <summary>
  /// Whether detailed logging is enabled.
  /// </summary>
  public bool EnableDetailedLogging { get; set; } = true;

  /// <summary>
  /// Whether performance metrics collection is enabled.
  /// </summary>
  public bool EnableMetrics { get; set; } = true;

  /// <summary>
  /// Correlation ID header name for request tracking.
  /// </summary>
  [StringLength(50, MinimumLength = 1, ErrorMessage = "CorrelationIdHeader must be between 1 and 50 characters")]
  public string CorrelationIdHeader { get; set; } = "X-Correlation-ID";

  /// <summary>
  /// Maximum execution time in seconds for message processing.
  /// </summary>
  [Range(1, 3600, ErrorMessage = "MaxExecutionTimeSeconds must be between 1 and 3600 seconds (1 hour)")]
  public int MaxExecutionTimeSeconds { get; set; } = 300;

  /// <summary>
  /// Whether to enable health checks endpoint.
  /// </summary>
  public bool EnableHealthChecks { get; set; } = true;

  /// <summary>
  /// Shutdown timeout in seconds for graceful application termination.
  /// </summary>
  [Range(1, 300, ErrorMessage = "ShutdownTimeoutSeconds must be between 1 and 300 seconds")]
  public int ShutdownTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Logging-specific configuration settings.
/// </summary>
public class LoggingSettings
{
  /// <summary>
  /// Configuration section name for binding.
  /// </summary>
  public const string SectionName = "Logging";

  /// <summary>
  /// Minimum log level for console output.
  /// </summary>
  [Required(ErrorMessage = "Logging ConsoleLogLevel is required")]
  public string ConsoleLogLevel { get; set; } = "Information";

  /// <summary>
  /// Minimum log level for file output.
  /// </summary>
  [Required(ErrorMessage = "Logging FileLogLevel is required")]
  public string FileLogLevel { get; set; } = "Warning";

  /// <summary>
  /// Whether to enable structured logging (JSON format).
  /// </summary>
  public bool EnableStructuredLogging { get; set; } = true;

  /// <summary>
  /// Log file path template (supports date placeholders).
  /// </summary>
  [StringLength(500, MinimumLength = 1, ErrorMessage = "LogFilePathTemplate must be between 1 and 500 characters")]
  public string LogFilePathTemplate { get; set; } = "logs/whatsapp-waha-{Date}.log";

  /// <summary>
  /// Maximum log file size in bytes before rolling.
  /// </summary>
  [Range(1024, 1073741824, ErrorMessage = "MaxLogFileSizeBytes must be between 1KB and 1GB")]
  public long MaxLogFileSizeBytes { get; set; } = 10485760; // 10 MB

  /// <summary>
  /// Number of log files to retain.
  /// </summary>
  [Range(1, 100, ErrorMessage = "RetainedLogFileCount must be between 1 and 100")]
  public int RetainedLogFileCount { get; set; } = 7;
}
