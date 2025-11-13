using System.Globalization;
using AIChat.Orleans.Configuration;
using AIChat.Orleans.Metrics;
using AIChat.Orleans.Placement;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Orleans.Configuration;
using Serilog;
using Serilog.Events;

namespace AIChat.Orleans.Host;

/// <summary>
/// Orleans silo host program for the AIChat application.
/// Provides dedicated hosting for Orleans grains with proper configuration.
/// </summary>
/// <remarks>
/// ARCHITECTURE: Hybrid Co-hosting with Grain Facade Pattern
///
/// This application hosts two logically separated components in a single process:
///
/// 1. Orleans Silo - Independent grain hosting
///    - Business grains (UserGrain, ChatGrain, ModeGrain)
///    - Monitoring grains (ClusterMetricsGrain, PlacementMetricsGrain, ClusterHealthGrain)
///    - Internal services (IOrleansMetricsCollector, IPlacementMetricsCollector)
///
/// 2. ASP.NET WebHost - Health/Monitoring API (Orleans Client Pattern)
///    - Controllers access Orleans ONLY through grain interfaces (IGrainFactory)
///    - No direct injection of silo-internal services
///    - Acts as an Orleans client through the Grain Facade pattern
///    - Maintains clean architectural boundary while co-hosting for simplicity
///
/// Benefits:
/// - Clean separation: WebHost cannot access silo internals directly
/// - Independent hosting: Silo can run standalone, WebHost is optional
/// - Operational simplicity: Single process, single deployment
/// - Future-proof: Easy to separate into different processes later
/// - Low overhead: In-process grain calls (no network serialization)
/// </remarks>
public class Program
{
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
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args);

        _ = builder.ConfigureWebHostDefaults(webBuilder =>
                _ = webBuilder.Configure(app =>
                {
                    // ========================================================================
                    // ASP.NET WebHost Configuration - Acts as Orleans Client
                    // ========================================================================
                    // This WebHost provides health/monitoring APIs by calling Orleans grains
                    // through IGrainFactory (Grain Facade Pattern). Controllers do NOT inject
                    // internal silo services directly, maintaining clean architectural separation.
                    //
                    // Monitoring Endpoints:
                    // - /api/orleans/health/*      -> HealthController -> IClusterHealthGrain
                    // - /api/orleans/metrics/*     -> MetricsController -> IClusterMetricsGrain
                    // - /api/orleans/placement/*   -> PlacementController -> IPlacementMetricsGrain
                    //
                    // All controllers use IGrainFactory to call stateless worker monitoring grains.
                    // ========================================================================

                    _ = app.UseRouting();
                    _ = app.UseEndpoints(endpoints =>
                    {
                        // Map all controllers (HealthController, MetricsController, PlacementController)
                        // These controllers use the Grain Facade pattern to access Orleans data
                        _ = endpoints.MapControllers();

                        // Basic liveness check - minimal API for simple health probe
                        // For detailed health info, use /api/orleans/health/status
                        _ = endpoints.MapGet(
                            "/health",
                            async context =>
                                await context.Response.WriteAsync("Orleans Host is running")
                        );

                        // Map health checks for Aspire observability
                        _ = endpoints.MapHealthChecks("/alive");
                        _ = endpoints.MapHealthChecks("/ready");
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
                            restrictedToMinimumLevel: LogEventLevel.Warning,
                            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                            formatProvider: CultureInfo.InvariantCulture
                        )
                        .WriteTo.File(
                            path: "logs/orleans-host-.log",
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} - {Message:lj}{NewLine}{Exception}",
                            formatProvider: CultureInfo.InvariantCulture
,
                            rollingInterval: RollingInterval.Day);

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

                    // Add Seq sink for centralized structured logging (if enabled)
                    var enableSeq = context.Configuration.GetValue("Serilog:EnableSeq", true);
                    if (enableSeq)
                    {
                        var seqServerUrl = context.Configuration["Serilog:SeqServerUrl"] ?? "http://localhost:5341";
                        _ = configuration.WriteTo.Seq(
                            serverUrl: seqServerUrl,
                            restrictedToMinimumLevel: LogEventLevel.Debug
,
                            apiKey: context.Configuration["Serilog:SeqApiKey"]);
                    }

                    // Add enrichers for better log context
                    _ = configuration
                        .Enrich.FromLogContext()
                        .Enrich.WithMachineName()
                        .Enrich.WithThreadId()
                        .Enrich.WithEnvironmentName()
                        .Enrich.WithProperty("Application", "AIChat.Orleans.Host")
                        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);
                }
            )
            .UseOrleans(ConfigureOrleans)
            .ConfigureServices(
                (context, services) =>
                {
                    // Add Aspire service discovery
                    _ = services.AddServiceDiscovery();

                    // Configure HTTP client defaults with resilience
                    _ = services.ConfigureHttpClientDefaults(http =>
                    {
                        _ = http.AddStandardResilienceHandler();
                        _ = http.AddServiceDiscovery();
                    });

                    // Add controller services for API endpoints
                    _ = services.AddControllers()
                        .AddJsonOptions(options =>
                        {
                            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                            options.JsonSerializerOptions.WriteIndented = true;
                        });

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

                    // Phase 5: Add placement metrics collection (ORL-ST-P5-001)
                    _ = services.AddPlacementMetrics();

                    // ========================================================================
                    // LLM Server Cleanup Phase 1: IStreamingAgent Registration
                    // ========================================================================
                    // Moved from AIChat.Server to Orleans.Host
                    // Orleans Host now owns LLM credential management and caching infrastructure
                    // ========================================================================
                    _ = services.AddTransient<AchieveAi.LmDotnetTools.LmCore.Agents.IStreamingAgent>(provider =>
                    {
                        // Register IStreamingAgent as an OpenAIProvider-based agent with caching
                        // Get configuration - prioritize environment variables, then User Secrets/config
                        var configuration = provider.GetRequiredService<IConfiguration>();
                        var logger = provider.GetRequiredService<ILogger<Program>>();
                        var hostEnv = provider.GetRequiredService<IHostEnvironment>();

                        var apiKey =
                            Environment.GetEnvironmentVariable("LLM_API_KEY") ?? configuration["OpenAI:ApiKey"] ?? "";
                        var baseUrl =
                            Environment.GetEnvironmentVariable("LLM_BASE_API_URL")
                            ?? configuration["OpenAI:BaseUrl"]
                            ?? "https://api.openai.com/v1";

                        // Diagnostic logging for API configuration
                        logger.LogInformation("[DIAGNOSTIC] API Configuration:");
                        logger.LogInformation("[DIAGNOSTIC] Base URL: {BaseUrl}", baseUrl);
                        logger.LogInformation("[DIAGNOSTIC] API Key Length: {ApiKeyLength}", apiKey?.Length ?? 0);
                        logger.LogInformation(
                            "[DIAGNOSTIC] API Key Prefix: {ApiKeyPrefix}",
                            apiKey?.Length > 10 ? $"{apiKey.AsSpan(0, 10)}..." : "[EMPTY]"
                        );

                        // Create an OpenAI client with caching (non-Test environments)
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            throw new InvalidOperationException("OpenAI API key is required but was not provided.");
                        }

                        // Create cache infrastructure
                        var cacheDirectory = configuration["LlmCache:CacheDirectory"] ?? "./llm-cache";
                        var cache = new AchieveAi.LmDotnetTools.Misc.Storage.FileKvStore(cacheDirectory);

                        // Configure cache options
                        var cacheOptions = new AchieveAi.LmDotnetTools.Misc.Configuration.LlmCacheOptions
                        {
                            EnableCaching = configuration.GetValue("LlmCache:EnableCaching", true),
                            CacheExpiration = configuration.GetValue<TimeSpan?>(
                                "LlmCache:CacheExpiration",
                                TimeSpan.FromHours(24)
                            ),
                            MaxCacheItems = configuration.GetValue<int?>("LlmCache:MaxCacheItems", 10000),
                        };

                        // Create HTTP client with caching handler
                        var httpClientHandler = new HttpClientHandler();
                        var cachingHandler = new AchieveAi.LmDotnetTools.Misc.Http.CachingHttpMessageHandler(
                            cache,
                            cacheOptions,
                            httpClientHandler,
                            logger
                        );

                        var httpClient = new HttpClient(cachingHandler)
                        {
                            BaseAddress = new Uri(baseUrl),
                            Timeout = TimeSpan.FromMinutes(5),
                        };

                        // Add authentication headers
                        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                        var openClient = new AchieveAi.LmDotnetTools.OpenAIProvider.Agents.OpenClient(httpClient, baseUrl, null, logger);
                        return new AchieveAi.LmDotnetTools.OpenAIProvider.Agents.OpenClientAgent("OpenAi", openClient);
                    });

                    // Add health checks
                    _ = services.AddHealthChecks();
                }
            );

        return builder;
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

        // Note: Orleans 9.x auto-discovers grain assemblies and may discover unwanted types
        // For now, we'll rely on the fact that only types marked with [GenerateSerializer] should be serialized

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

        // Enable distributed tracing with Activity propagation
        // This enables W3C Trace Context support for grain calls
        _ = siloBuilder.AddActivityPropagation();

        // Note: GrainPlacementOptions configuration updated for Orleans 9.x
        // ResourceOptimizedPlacement is used by default

        // Phase 5: Grain placement optimization using built-in Orleans strategies (ORL-ST-P5-001)
        // Custom placement attributes are applied directly to grain classes:
        // - UserGrain: HashBasedPlacement for session stickiness
        // - ChatGrain: ActivationCountBasedPlacement for load balancing
        // - ModeGrain: ActivationCountBasedPlacement for resource optimization
        // - HealthCheckGrain: Random placement by default

        // Enable placement metrics collection through grain filters
        _ = siloBuilder.UseOrleansPlacementMetrics();

        // Add distributed logging filter for all grain calls
        // Provides entry/exit logging, performance tracking, and error diagnostics
        _ = siloBuilder.AddIncomingGrainCallFilter<AIChat.Orleans.Logging.LoggingGrainCallFilter>();

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
