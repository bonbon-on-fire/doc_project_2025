// server/DocProject.ServiceDefaults/Extensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for adding default service configuration to Aspire services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds default service configuration for Aspire services including OpenTelemetry,
    /// health checks, and service discovery.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Configure OpenTelemetry
        _ = builder.ConfigureOpenTelemetry();

        // Add default health checks
        _ = builder.AddDefaultHealthChecks();

        // Configure service discovery
        _ = builder.Services.AddServiceDiscovery();

        // Configure HTTP client defaults with resilience
        _ = builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Add standard resilience handler
            _ = http.AddStandardResilienceHandler();

            // Add service discovery
            _ = http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Maps default endpoints including health checks for liveness and readiness probes.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The application for chaining.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints (development only by default)
        if (app.Environment.IsDevelopment())
        {
            // All health checks
            _ = app.MapHealthChecks("/health");

            // Liveness probe - checks if the application is running
            _ = app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });

            // Readiness probe - checks if the application is ready to accept traffic
            _ = app.MapHealthChecks("/ready", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("ready")
            });
        }

        return app;
    }
}
