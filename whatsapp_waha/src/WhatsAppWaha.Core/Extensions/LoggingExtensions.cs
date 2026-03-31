using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using WhatsAppWaha.Core.Configuration;

namespace WhatsAppWaha.Core.Extensions;

/// <summary>
/// Extension methods for configuring Serilog logging.
/// </summary>
public static class LoggingExtensions
{
  /// <summary>
  /// Adds Serilog logging configuration to the host builder.
  /// </summary>
  /// <param name="builder">The host builder.</param>
  /// <param name="configuration">The configuration.</param>
  /// <returns>The host builder for chaining.</returns>
  public static IHostBuilder AddSerilogLogging(this IHostBuilder builder, IConfiguration configuration)
  {
    return builder.UseSerilog((context, services, loggerConfiguration) =>
    {
      var loggingSettings = services.GetService<IOptions<LoggingSettings>>()?.Value
                               ?? new LoggingSettings();
      var appSettings = services.GetService<IOptions<AppSettings>>()?.Value
                           ?? new AppSettings();

      ConfigureSerilog(loggerConfiguration, loggingSettings, appSettings, context.HostingEnvironment);
    });
  }

  /// <summary>
  /// Creates a bootstrap Serilog logger for early application startup.
  /// </summary>
  /// <param name="configuration">The configuration.</param>
  /// <returns>The configured logger.</returns>
  public static ILogger CreateBootstrapLogger(IConfiguration configuration)
  {
    var loggingSettings = new LoggingSettings();
    configuration.GetSection(LoggingSettings.SectionName).Bind(loggingSettings);

    var appSettings = new AppSettings();
    configuration.GetSection(AppSettings.SectionName).Bind(appSettings);

    var loggerConfiguration = new LoggerConfiguration();
    ConfigureSerilog(loggerConfiguration, loggingSettings, appSettings, null);

    return loggerConfiguration.CreateLogger();
  }

  /// <summary>
  /// Configures the Serilog logger with appropriate sinks and formatting.
  /// </summary>
  /// <param name="loggerConfiguration">The logger configuration.</param>
  /// <param name="loggingSettings">The logging settings.</param>
  /// <param name="appSettings">The application settings.</param>
  /// <param name="environment">The hosting environment (optional).</param>
  private static void ConfigureSerilog(
      LoggerConfiguration loggerConfiguration,
      LoggingSettings loggingSettings,
      AppSettings appSettings,
      IHostEnvironment? environment)
  {
    // Base configuration
    loggerConfiguration
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", appSettings.Name)
        .Enrich.WithProperty("Version", appSettings.Version)
        .Enrich.WithProperty("Environment", appSettings.Environment);

    // Add correlation ID enricher if enabled
    if (!string.IsNullOrEmpty(appSettings.CorrelationIdHeader))
    {
      loggerConfiguration.Enrich.WithProperty("CorrelationIdHeader", appSettings.CorrelationIdHeader);
    }

    // Console sink configuration
    var consoleLogLevel = ParseLogLevel(loggingSettings.ConsoleLogLevel);
    if (loggingSettings.EnableStructuredLogging)
    {
      loggerConfiguration.WriteTo.Console(
          restrictedToMinimumLevel: consoleLogLevel,
          outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
    }
    else
    {
      loggerConfiguration.WriteTo.Console(
          restrictedToMinimumLevel: consoleLogLevel,
          outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }

    // File sink configuration
    var fileLogLevel = ParseLogLevel(loggingSettings.FileLogLevel);
    loggerConfiguration.WriteTo.File(
        path: loggingSettings.LogFilePathTemplate,
        restrictedToMinimumLevel: fileLogLevel,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: loggingSettings.RetainedLogFileCount,
        fileSizeLimitBytes: loggingSettings.MaxLogFileSizeBytes,
        rollOnFileSizeLimit: true,
        outputTemplate: loggingSettings.EnableStructuredLogging
            ? "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
            : "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    // Enhanced logging for development
    if (environment?.IsDevelopment() == true || appSettings.EnableDetailedLogging)
    {
      loggerConfiguration
          .MinimumLevel.Verbose()
          .Enrich.WithProperty("DetailedLogging", true);
    }
  }

  /// <summary>
  /// Parses a log level string into a LogEventLevel enum.
  /// </summary>
  /// <param name="logLevel">The log level string.</param>
  /// <returns>The parsed log event level.</returns>
  private static LogEventLevel ParseLogLevel(string logLevel)
  {
    return logLevel.ToLowerInvariant() switch
    {
      "verbose" => LogEventLevel.Verbose,
      "debug" => LogEventLevel.Debug,
      "information" or "info" => LogEventLevel.Information,
      "warning" or "warn" => LogEventLevel.Warning,
      "error" => LogEventLevel.Error,
      "fatal" => LogEventLevel.Fatal,
      _ => LogEventLevel.Information
    };
  }
}

/// <summary>
/// Extension methods for structured logging.
/// </summary>
public static class StructuredLoggingExtensions
{
  /// <summary>
  /// Logs a message with correlation ID if available.
  /// </summary>
  /// <param name="logger">The logger.</param>
  /// <param name="level">The log level.</param>
  /// <param name="messageTemplate">The message template.</param>
  /// <param name="correlationId">The correlation ID.</param>
  /// <param name="propertyValues">Additional property values.</param>
  public static void LogWithCorrelation(
      this ILogger logger,
      LogEventLevel level,
      string messageTemplate,
      string? correlationId = null,
      params object[] propertyValues)
  {
    if (!string.IsNullOrEmpty(correlationId))
    {
      logger.ForContext("CorrelationId", correlationId)
            .Write(level, messageTemplate, propertyValues);
    }
    else
    {
      logger.Write(level, messageTemplate, propertyValues);
    }
  }

  /// <summary>
  /// Logs an operation timing with structured data.
  /// </summary>
  /// <param name="logger">The logger.</param>
  /// <param name="operationName">The operation name.</param>
  /// <param name="duration">The operation duration.</param>
  /// <param name="success">Whether the operation was successful.</param>
  /// <param name="correlationId">The correlation ID.</param>
  public static void LogOperationTiming(
      this ILogger logger,
      string operationName,
      TimeSpan duration,
      bool success,
      string? correlationId = null)
  {
    var properties = new object[]
    {
            operationName,
            duration.TotalMilliseconds,
            success
    };

    var messageTemplate = "Operation {OperationName} completed in {DurationMs}ms with success: {Success}";

    logger.LogWithCorrelation(
        success ? LogEventLevel.Information : LogEventLevel.Warning,
        messageTemplate,
        correlationId,
        properties);
  }
}
