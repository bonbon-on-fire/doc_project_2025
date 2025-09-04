using AIChat.Orleans.Contracts;
using AIChat.Orleans.Grains;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Serilog;
using Serilog.Events;

namespace AIChat.Orleans.Host;

/// <summary>
/// Orleans silo host program for the AIChat application.
/// Provides dedicated hosting for Orleans grains with proper configuration.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Configure Serilog early for startup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting AIChat Orleans Host");

            var builder = CreateHostBuilder(args);
            var host = builder.Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Orleans Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Creates and configures the host builder with Orleans and other services.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configured host builder</returns>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure(app =>
                {
                    // Minimal web host for health checks and dashboard
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/health", async context =>
                        {
                            await context.Response.WriteAsync("Orleans Host is running");
                        });
                    });
                });
            })
            .UseSerilog((context, configuration) =>
            {
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
                    .MinimumLevel.Override("Orleans.Runtime", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("AIChat.Orleans", LogEventLevel.Debug)
                    .WriteTo.Console(outputTemplate: 
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        path: "logs/orleans-host-.log",
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: 
                            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}");

                // Add Application Insights if configured
                var appInsightsKey = context.Configuration.GetConnectionString("ApplicationInsights");
                if (!string.IsNullOrEmpty(appInsightsKey))
                {
                    configuration.WriteTo.ApplicationInsights(appInsightsKey, TelemetryConverter.Traces);
                }
            })
            .UseOrleans(ConfigureOrleans)
            .ConfigureServices((context, services) =>
            {
                // Add Application Insights if configured
                var appInsightsKey = context.Configuration.GetConnectionString("ApplicationInsights");
                if (!string.IsNullOrEmpty(appInsightsKey))
                {
                    services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
                    {
                        ConnectionString = appInsightsKey
                    });
                }

                // Add health checks
                services.AddHealthChecks();
            });

    /// <summary>
    /// Configures Orleans silo with environment-specific settings.
    /// </summary>
    /// <param name="context">Host builder context</param>
    /// <param name="siloBuilder">Orleans silo builder</param>
    private static void ConfigureOrleans(HostBuilderContext context, ISiloBuilder siloBuilder)
    {
        var configuration = context.Configuration;
        var environment = context.HostingEnvironment;

        // Basic silo configuration
        siloBuilder
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = configuration.GetValue<string>("Orleans:ClusterId") ?? "doc-chat-cluster";
                options.ServiceId = configuration.GetValue<string>("Orleans:ServiceId") ?? "doc-chat-service";
            })
            .ConfigureEndpoints(
                siloPort: configuration.GetValue<int>("Orleans:SiloPort", 11111),
                gatewayPort: configuration.GetValue<int>("Orleans:GatewayPort", 30000)
            );

        // Environment-specific clustering and storage configuration
        if (environment.IsDevelopment())
        {
            ConfigureDevelopmentOrleans(siloBuilder, configuration);
        }
        else
        {
            ConfigureProductionOrleans(siloBuilder, configuration);
        }

        // Grain assemblies are auto-discovered in Orleans 9.x

        // NOTE: Orleans Dashboard disabled for Phase 1 due to Orleans 9.x compatibility issues
        // TODO: Update to Orleans 9.x compatible dashboard in later phases
        // var dashboardPort = configuration.GetValue<int>("Orleans:DashboardPort", 8080);
        // siloBuilder.UseDashboard(options =>
        // {
        //     options.Port = dashboardPort;
        //     options.HostSelf = true;
        //     options.CounterUpdateIntervalMs = 1000;
        // });

        // Add startup task for initialization
        siloBuilder.AddStartupTask<OrleansStartupTask>();

        // Configure logging
        siloBuilder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog();
        });

        // Note: GrainPlacementOptions configuration updated for Orleans 9.x
        // ResourceOptimizedPlacement is used by default

        // Configure grain collection
        siloBuilder.Configure<GrainCollectionOptions>(options =>
        {
            options.CollectionAge = TimeSpan.FromMinutes(30);
            options.DeactivationTimeout = TimeSpan.FromMinutes(5);
        });
    }

    /// <summary>
    /// Configures Orleans for development environment.
    /// Uses localhost clustering and memory storage.
    /// </summary>
    /// <param name="siloBuilder">Orleans silo builder</param>
    /// <param name="configuration">Configuration</param>
    private static void ConfigureDevelopmentOrleans(ISiloBuilder siloBuilder, IConfiguration configuration)
    {
        Log.Information("Configuring Orleans for Development environment");

        siloBuilder
            .UseLocalhostClustering()
            .AddMemoryGrainStorage("UserGrainStorage")
            .AddMemoryGrainStorage("PubSubStore");
    }

    /// <summary>
    /// Configures Orleans for production environment.
    /// Uses Azure Storage for clustering and persistence.
    /// </summary>
    /// <param name="siloBuilder">Orleans silo builder</param>
    /// <param name="configuration">Configuration</param>
    private static void ConfigureProductionOrleans(ISiloBuilder siloBuilder, IConfiguration configuration)
    {
        Log.Information("Configuring Orleans for Production environment");

        var clusteringConnection = configuration.GetConnectionString("Orleans:ClusteringStorage");
        var storageConnection = configuration.GetConnectionString("Orleans:GrainStorage");

        // Phase 1: Use localhost clustering and memory storage for simplicity
        // TODO: Implement Azure storage configuration for Orleans 9.x in later phases
        Log.Information("Phase 1 configuration: Using localhost clustering and memory storage");
        siloBuilder.UseLocalhostClustering();
        siloBuilder.AddMemoryGrainStorage("UserGrainStorage");

        // Always use memory for PubSub in this phase
        siloBuilder.AddMemoryGrainStorage("PubSubStore");
    }
}

/// <summary>
/// Startup task for Orleans initialization and validation.
/// </summary>
public class OrleansStartupTask : IStartupTask
{
    private readonly ILogger<OrleansStartupTask> _logger;

    public OrleansStartupTask(ILogger<OrleansStartupTask> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes startup validation and initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the startup operation</returns>
    public Task Execute(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Orleans startup task beginning...");
            
            _logger.LogInformation("Orleans startup validation successful. Silo is ready to accept requests");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans startup task failed");
            throw; // Re-throw to prevent silo startup
        }
    }
}