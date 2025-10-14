// server/DocProject.ServiceDefaults/ServiceDiscoveryExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for configuring service discovery in Aspire services.
/// </summary>
public static class ServiceDiscoveryExtensions
{
    /// <summary>
    /// Configures Orleans client with Aspire service discovery support.
    /// Falls back to localhost clustering if service discovery is not available.
    /// </summary>
    /// <param name="hostBuilder">The host builder.</param>
    /// <param name="serviceName">The name of the Orleans service to discover (default: "orleans-host").</param>
    /// <returns>The host builder for chaining.</returns>
    public static IHostBuilder UseOrleansClientWithServiceDiscovery(
        this IHostBuilder hostBuilder,
        string serviceName = "orleans-host")
    {
        return hostBuilder.UseOrleansClient((context, clientBuilder) =>
        {
            _ = clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = context.Configuration["Orleans:ClusterId"] ?? "doc-chat-cluster";
                options.ServiceId = context.Configuration["Orleans:ServiceId"] ?? "doc-chat-service";
            });

            // Try to use service discovery for gateway
            var orleansConnection = context.Configuration.GetConnectionString(serviceName);

            if (!string.IsNullOrEmpty(orleansConnection))
            {
                try
                {
                    // Parse Aspire-provided connection string
                    var uri = new Uri(orleansConnection);
                    var gatewayPort = uri.Port > 0 ? uri.Port : 30000;

                    var logger = context.GetService<ILogger<IClusterClient>>();
                    logger?.LogInformation("Using service discovery for Orleans gateway: {Uri}:{Port}",
                        uri.Host, gatewayPort);

                    _ = clientBuilder.UseStaticClustering(
                    [
                        new System.Net.IPEndPoint(
                            System.Net.Dns.GetHostAddresses(uri.Host)[0],
                            gatewayPort)
                    ]);
                }
                catch (Exception ex)
                {
                    var logger = context.GetService<ILogger<IClusterClient>>();
                    logger?.LogWarning(ex,
                        "Failed to parse Orleans connection string, falling back to localhost");

                    // Fallback to localhost
                    _ = clientBuilder.UseLocalhostClustering(30000);
                }
            }
            else
            {
                // Fallback to localhost for standalone runs
                var logger = context.GetService<ILogger<IClusterClient>>();
                logger?.LogInformation("Using fallback localhost Orleans clustering");

                _ = clientBuilder.UseLocalhostClustering(30000);
            }
        });
    }
}

/// <summary>
/// Helper extensions for context-based service retrieval.
/// </summary>
internal static class HostBuilderContextExtensions
{
    /// <summary>
    /// Gets a service from the host builder context if available.
    /// </summary>
    public static T? GetService<T>(this HostBuilderContext context) where T : class
    {
        return context.Properties.TryGetValue("Services", out var services)
            ? (services as IServiceProvider)?.GetService<T>()
            : null;
    }
}
