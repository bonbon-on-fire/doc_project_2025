// server/DocProject.ServiceDefaults/HealthCheckExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for configuring default health checks in Aspire services.
/// </summary>
internal static class HealthCheckExtensions
{
    /// <summary>
    /// Adds default health checks including self health check.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IHostApplicationBuilder AddDefaultHealthChecks(
        this IHostApplicationBuilder builder)
    {
        _ = builder.Services.AddHealthChecks()
            // Basic self health check for liveness
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])

            // Basic ready check
            .AddCheck("ready", () => HealthCheckResult.Healthy(), ["ready"]);

        return builder;
    }
}
