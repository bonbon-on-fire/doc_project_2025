// server/DocProject.ServiceDefaults/OpenTelemetryExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for configuring OpenTelemetry in Aspire services.
/// </summary>
internal static class OpenTelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry with tracing, metrics, and logging for the service.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder)
    {
        // Configure logging with OpenTelemetry
        _ = builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // Configure OpenTelemetry services
        _ = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: builder.Environment.ApplicationName,
                    serviceVersion: "1.0.0",
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment",
                        builder.Environment.EnvironmentName)
                ]))
            .WithMetrics(metrics => metrics
                .AddRuntimeInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // Enable more detailed tracing in development
                    _ = tracing.SetSampler(new AlwaysOnSampler());
                }

                _ = tracing.AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddSource("Orleans")
                    .AddSource("Orleans.*")
                    .AddSource("AIChat.*")
                    .AddOtlpExporter();
            });

        // Add OpenTelemetry exporters
        _ = builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Adds OpenTelemetry exporters based on environment configuration.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    private static IHostApplicationBuilder AddOpenTelemetryExporters(
        this IHostApplicationBuilder builder)
    {
        // OTLP exporter is added automatically by Aspire
        // The endpoint is configured through environment variables
        // In development, logs are automatically visible in the console and Aspire dashboard

        return builder;
    }
}
