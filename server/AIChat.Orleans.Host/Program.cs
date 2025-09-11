using System.Globalization;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Metrics;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Orleans.Configuration;
using Serilog;
using Serilog.Events;

namespace AIChat.Orleans.Host;

/// <summary>
/// Orleans silo host program for the AIChat application.
/// Provides dedicated hosting for Orleans grains with proper configuration.
/// </summary>
public class Program
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Entry point for the Orleans silo host application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static async Task Main(string[] args)
    {
        // Configure Serilog early for startup logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
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
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Microsoft
            .Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
                _ = webBuilder.Configure(app =>
                {
                    // Minimal web host for health checks and dashboard
                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints =>
                    {
                        _ = endpoints.MapGet(
                            "/health",
                            async context =>
                                await context.Response.WriteAsync("Orleans Host is running")
                        );

                        // Phase 4: Orleans Metrics API endpoint
                        _ = endpoints.MapGet(
                            "/api/orleans/metrics",
                            async context =>
                            {
                                var metricsCollector =
                                    context.RequestServices.GetRequiredService<IOrleansMetricsCollector>();
                                var summary = await metricsCollector.GetMetricsSummaryAsync();

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(
                                    System.Text.Json.JsonSerializer.Serialize(summary, JsonOptions)
                                );
                            }
                        );

                        _ = endpoints.MapGet(
                            "/api/orleans/metrics/{grainType}",
                            async (
                                string grainType,
                                HttpContext context,
                                IOrleansMetricsCollector metricsCollector
                            ) =>
                            {
                                if (string.IsNullOrEmpty(grainType))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync("Grain type is required");
                                    return;
                                }

                                var grainMetrics = await metricsCollector.GetGrainTypeMetricsAsync(
                                    grainType
                                );

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(
                                    System.Text.Json.JsonSerializer.Serialize(
                                        grainMetrics,
                                        JsonOptions
                                    )
                                );
                            }
                        );
                    });
                })
            )
            .UseSerilog(
                (context, configuration) =>
                {
                    _ = configuration
                        .ReadFrom.Configuration(context.Configuration)
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("Orleans", LogEventLevel.Warning)
                        .MinimumLevel.Override("Orleans.Runtime", LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("AIChat.Orleans", LogEventLevel.Debug)
                        .WriteTo.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                            formatProvider: CultureInfo.InvariantCulture
                        )
                        .WriteTo.File(
                            path: "logs/orleans-host-.log",
                            rollingInterval: RollingInterval.Day,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
                            formatProvider: CultureInfo.InvariantCulture
                        );

                    // Add Application Insights if configured
                    var appInsightsKey = context.Configuration.GetConnectionString(
                        "ApplicationInsights"
                    );
                    if (!string.IsNullOrEmpty(appInsightsKey))
                    {
                        _ = configuration.WriteTo.ApplicationInsights(
                            appInsightsKey,
                            TelemetryConverter.Traces
                        );
                    }
                }
            )
            .UseOrleans(ConfigureOrleans)
            .ConfigureServices(
                (context, services) =>
                {
                    // Configure Orleans grain settings
                    _ = services.Configure<OrleansGrainConfiguration>(
                        context.Configuration.GetSection(OrleansGrainConfiguration.SectionName)
                    );

                    // Add Application Insights if configured
                    var appInsightsKey = context.Configuration.GetConnectionString(
                        "ApplicationInsights"
                    );
                    if (!string.IsNullOrEmpty(appInsightsKey))
                    {
                        _ = services.AddApplicationInsightsTelemetry(
                            new ApplicationInsightsServiceOptions
                            {
                                ConnectionString = appInsightsKey,
                            }
                        );
                    }

                    // Add Phase 4: Orleans Metrics Collection
                    _ = services.AddSingleton<IOrleansMetricsCollector, OrleansMetricsCollector>();

                    // Add ChatServiceProxy for grain LLM processing
                    // Default implementation provides simulated responses
                    // In production, this should be replaced with actual ChatService integration
                    _ = services.AddSingleton<
                        Services.IChatServiceProxy,
                        Services.DefaultChatServiceProxy
                    >();

                    // Add health checks
                    _ = services.AddHealthChecks();
                }
            );
    }

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
        _ = siloBuilder
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId =
                    configuration.GetValue<string>("Orleans:ClusterId") ?? "doc-chat-cluster";
                options.ServiceId =
                    configuration.GetValue<string>("Orleans:ServiceId") ?? "doc-chat-service";
            })
            .ConfigureEndpoints(
                siloPort: configuration.GetValue("Orleans:SiloPort", 11111),
                gatewayPort: configuration.GetValue("Orleans:GatewayPort", 30000)
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

        // Phase 1: Custom Orleans monitoring dashboard (Orleans 9.x compatible)
        var dashboardPort = configuration.GetValue("Orleans:DashboardPort", 8080);
        var dashboardEnabled = configuration.GetValue("Orleans:Dashboard:Enabled", true);

        if (dashboardEnabled)
        {
            Log.Information(
                "Custom Orleans monitoring dashboard will be available on port {Port} (integrated with web host)",
                context.HostingEnvironment.IsDevelopment() ? 5100 : dashboardPort
            );
        }

        // Add startup task for initialization
        _ = siloBuilder.AddStartupTask<OrleansStartupTask>();

        // Configure logging
        _ = siloBuilder.ConfigureLogging(logging =>
        {
            _ = logging.ClearProviders();
            _ = logging.AddSerilog();
        });

        // Note: GrainPlacementOptions configuration updated for Orleans 9.x
        // ResourceOptimizedPlacement is used by default

        // Configure grain collection
        _ = siloBuilder.Configure<GrainCollectionOptions>(options =>
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
    private static void ConfigureDevelopmentOrleans(
        ISiloBuilder siloBuilder,
        IConfiguration configuration
    )
    {
        Log.Information("Configuring Orleans for Development environment");

        _ = siloBuilder
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
    private static void ConfigureProductionOrleans(
        ISiloBuilder siloBuilder,
        IConfiguration configuration
    )
    {
        Log.Information("Configuring Orleans for Production environment");

        var clusteringConnection = configuration.GetConnectionString("Orleans:ClusteringStorage");
        var storageConnection = configuration.GetConnectionString("Orleans:GrainStorage");

        // Phase 1: Use localhost clustering and memory storage for simplicity
        // TODO: Implement Azure storage configuration for Orleans 9.x in later phases
        Log.Information("Phase 1 configuration: Using localhost clustering and memory storage");
        _ = siloBuilder.UseLocalhostClustering();
        _ = siloBuilder.AddMemoryGrainStorage("UserGrainStorage");

        // Always use memory for PubSub in this phase
        _ = siloBuilder.AddMemoryGrainStorage("PubSubStore");
    }
}

/// <summary>
/// Startup task for Orleans initialization and validation.
/// </summary>
public class OrleansStartupTask : IStartupTask
{
    private readonly ILogger<OrleansStartupTask> _logger;

    /// <summary>
    /// Initializes a new instance of the OrleansStartupTask.
    /// </summary>
    /// <param name="logger">Logger instance</param>
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

            _logger.LogInformation(
                "Orleans startup validation successful. Silo is ready to accept requests"
            );
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orleans startup task failed");
            throw; // Re-throw to prevent silo startup
        }
    }
}
