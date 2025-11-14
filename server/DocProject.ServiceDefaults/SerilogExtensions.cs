// server/DocProject.ServiceDefaults/SerilogExtensions.cs
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring Serilog with common settings across services.
/// </summary>
public static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog with common settings for AIChat services.
    /// </summary>
    /// <param name="builder">The host builder</param>
    /// <param name="applicationName">The name of the application (e.g., "AIChat.Server", "AIChat.Orleans.Host")</param>
    /// <param name="configure">Optional action to configure additional Serilog settings</param>
    /// <returns>The host builder for chaining</returns>
    public static IHostBuilder ConfigureCommonSerilog(
        this IHostBuilder builder,
        string applicationName,
        Action<HostBuilderContext, LoggerConfiguration>? configure = null)
    {
        return builder.UseSerilog((context, services, configuration) =>
        {
            // Apply common configuration
            ConfigureCommonSerilogSettings(configuration, context, applicationName);

            // Apply additional custom configuration
            configure?.Invoke(context, configuration);
        });
    }

    /// <summary>
    /// Configures common Serilog settings shared across all services.
    /// </summary>
    /// <param name="configuration">The Serilog logger configuration</param>
    /// <param name="context">The host builder context</param>
    /// <param name="applicationName">The name of the application</param>
    private static void ConfigureCommonSerilogSettings(
        LoggerConfiguration configuration,
        HostBuilderContext context,
        string applicationName)
    {
        _ = configuration
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

        // Add console sink (Warning and above only - keep console clean)
        _ = configuration.WriteTo.Console(
            restrictedToMinimumLevel: LogEventLevel.Warning,
            formatProvider: CultureInfo.InvariantCulture
        );

        // Add Seq sink for centralized structured logging (if enabled)
        var enableSeq = context.Configuration.GetValue("Serilog:EnableSeq", true);
        if (enableSeq)
        {
            var seqServerUrl = context.Configuration["Serilog:SeqServerUrl"] ?? "http://localhost:5341";
            _ = configuration.WriteTo.Seq(
                serverUrl: seqServerUrl,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                apiKey: context.Configuration["Serilog:SeqApiKey"]);
        }
    }

    /// <summary>
    /// Adds file logging with compact JSON formatting (for AIChat.Server style).
    /// </summary>
    /// <param name="configuration">The logger configuration</param>
    /// <param name="context">The host builder context</param>
    /// <param name="logFileName">The log file path</param>
    /// <param name="minimumLevel">Minimum log level for the file sink</param>
    /// <returns>The logger configuration for chaining</returns>
    public static LoggerConfiguration AddCompactJsonFileLogging(
        this LoggerConfiguration configuration,
        HostBuilderContext context,
        string logFileName,
        LogEventLevel minimumLevel = LogEventLevel.Verbose)
    {
        // Ensure log directory exists
        var directory = Path.GetDirectoryName(logFileName);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return configuration.WriteTo.File(
            new CompactJsonFormatter(),
            logFileName,
            restrictedToMinimumLevel: minimumLevel,
            buffered: false,
            shared: true,
            rollingInterval: RollingInterval.Day);
    }

    /// <summary>
    /// Adds file logging with text formatting (for AIChat.Orleans.Host style).
    /// </summary>
    /// <param name="configuration">The logger configuration</param>
    /// <param name="logFileName">The log file path</param>
    /// <param name="outputTemplate">The output template for log messages</param>
    /// <param name="minimumLevel">Minimum log level for the file sink</param>
    /// <returns>The logger configuration for chaining</returns>
    public static LoggerConfiguration AddTextFileLogging(
        this LoggerConfiguration configuration,
        string logFileName,
        string? outputTemplate = null,
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        // Use default Orleans-style template if none provided
        outputTemplate ??= "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}";

        return configuration.WriteTo.File(
            path: logFileName,
            outputTemplate: outputTemplate,
            formatProvider: CultureInfo.InvariantCulture,
            restrictedToMinimumLevel: minimumLevel,
            rollingInterval: RollingInterval.Day);
    }

    /// <summary>
    /// Adds Application Insights telemetry logging.
    /// </summary>
    /// <param name="configuration">The logger configuration</param>
    /// <param name="context">The host builder context</param>
    /// <returns>The logger configuration for chaining</returns>
    public static LoggerConfiguration AddApplicationInsights(
        this LoggerConfiguration configuration,
        HostBuilderContext context)
    {
        var appInsightsKey = context.Configuration.GetConnectionString("ApplicationInsights");
        if (!string.IsNullOrEmpty(appInsightsKey))
        {
            _ = configuration.WriteTo.ApplicationInsights(
                appInsightsKey,
                TelemetryConverter.Traces
            );
        }

        return configuration;
    }

    /// <summary>
    /// Adds minimum level overrides for specific namespaces.
    /// </summary>
    /// <param name="configuration">The logger configuration</param>
    /// <param name="overrides">Dictionary of namespace to minimum level overrides</param>
    /// <returns>The logger configuration for chaining</returns>
    public static LoggerConfiguration AddMinimumLevelOverrides(
        this LoggerConfiguration configuration,
        Dictionary<string, LogEventLevel> overrides)
    {
        foreach (var (nameSpace, level) in overrides)
        {
            _ = configuration.MinimumLevel.Override(nameSpace, level);
        }

        return configuration;
    }
}
