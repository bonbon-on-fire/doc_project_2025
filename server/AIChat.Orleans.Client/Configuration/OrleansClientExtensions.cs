using AIChat.Orleans.Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace AIChat.Orleans.Client.Configuration;

/// <summary>
/// Extension methods for configuring Orleans client in dependency injection.
/// </summary>
public static class OrleansClientExtensions
{
    /// <summary>
    /// Adds Orleans client services to the dependency injection container.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="environment">Hosting environment</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOrleansClient(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        // Add Orleans client
        _ = services.AddOrleansClient(clientBuilder =>
            ConfigureOrleansClient(clientBuilder, configuration, environment)
        );

        // Add Orleans integration service (Singleton because it's used by singleton services)
        _ = services.AddSingleton<IOrleansIntegrationService, OrleansIntegrationService>();

        // Add configuration validators
        _ = services.AddSingleton<
            IValidateOptions<OrleansResilienceConfiguration>,
            OrleansResilienceConfigurationValidator
        >();

        return services;
    }

    /// <summary>
    /// Configures Orleans client with environment-specific settings.
    /// </summary>
    /// <param name="clientBuilder">Orleans client builder</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="environment">Hosting environment</param>
    private static void ConfigureOrleansClient(
        IClientBuilder clientBuilder,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        // Basic client configuration
        _ = clientBuilder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId =
                configuration.GetValue<string>("Orleans:ClusterId") ?? "doc-chat-cluster";
            options.ServiceId =
                configuration.GetValue<string>("Orleans:ServiceId") ?? "doc-chat-service";
        });

        // Environment-specific configuration
        if (environment.IsDevelopment())
        {
            ConfigureDevelopmentClient(clientBuilder, configuration);
        }
        else
        {
            ConfigureProductionClient(clientBuilder, configuration);
        }

        // Configure client connection
        _ = clientBuilder.Configure<GatewayOptions>(options =>
            options.GatewayListRefreshPeriod = TimeSpan.FromMinutes(5)
        );
    }

    /// <summary>
    /// Configures Orleans client for development environment.
    /// </summary>
    /// <param name="clientBuilder">Orleans client builder</param>
    /// <param name="configuration">Configuration</param>
    private static void ConfigureDevelopmentClient(
        IClientBuilder clientBuilder,
        IConfiguration configuration
    )
    {
        var gatewayPort = configuration.GetValue("Orleans:GatewayPort", 30000);

        _ = clientBuilder.UseLocalhostClustering(gatewayPort);
    }

    /// <summary>
    /// Configures Orleans client for production environment.
    /// </summary>
    /// <param name="clientBuilder">Orleans client builder</param>
    /// <param name="configuration">Configuration</param>
    private static void ConfigureProductionClient(
        IClientBuilder clientBuilder,
        IConfiguration configuration
    )
    {
        var clusteringConnection = configuration.GetConnectionString("Orleans:ClusteringStorage");

        if (string.IsNullOrEmpty(clusteringConnection))
        {
            // Fallback to localhost for testing
            var gatewayPort = configuration.GetValue("Orleans:GatewayPort", 30000);
            _ = clientBuilder.UseLocalhostClustering(gatewayPort);
        }
        else
        {
            _ = clientBuilder.UseAzureStorageClustering(options =>
            {
                options.TableServiceClient = new Azure.Data.Tables.TableServiceClient(
                    clusteringConnection
                );
                options.TableName = "OrleansCluster";
            });
        }
    }
}

/// <summary>
/// Orleans client health check implementation.
/// </summary>
public class OrleansClientHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly IOrleansIntegrationService _orleansService;
    private readonly ILogger<OrleansClientHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the OrleansClientHealthCheck.
    /// </summary>
    /// <param name="orleansService">Orleans integration service</param>
    /// <param name="logger">Logger instance</param>
    public OrleansClientHealthCheck(
        IOrleansIntegrationService orleansService,
        ILogger<OrleansClientHealthCheck> logger
    )
    {
        _orleansService = orleansService ?? throw new ArgumentNullException(nameof(orleansService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs Orleans client health check.
    /// </summary>
    /// <param name="context">Health check context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var isHealthy = await _orleansService.IsOrleansHealthyAsync();
            var connectionStatus = await _orleansService.GetConnectionStatusAsync();

            var data = new Dictionary<string, object>
            {
                ["IsConnected"] = connectionStatus.IsConnected,
                ["ConnectionState"] = connectionStatus.ConnectionState,
                ["ActiveSilos"] = connectionStatus.ActiveSilos,
                ["LastSuccessfulOperation"] =
                    connectionStatus.LastSuccessfulOperation?.ToString("O") ?? "Never",
            };

            if (connectionStatus.Warnings.Count > 0)
            {
                data["Warnings"] = string.Join(", ", connectionStatus.Warnings);
            }

            return isHealthy && connectionStatus.IsConnected
                    ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        "Orleans client is connected and responsive",
                        data
                    )
                : connectionStatus.ConnectionState == "Disabled"
                    ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        "Orleans client is disabled via feature flags",
                        data
                    )
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                    $"Orleans client is not healthy: {connectionStatus.ConnectionState}",
                    data: data
                );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans client health check failed");

            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                "Orleans client health check threw an exception",
                exception: ex,
                data: new Dictionary<string, object> { ["Exception"] = ex.Message }
            );
        }
    }
}
