using AchieveAi.LmDotnetTools.LmConfig.Services;
using AIChat.Orleans.Client.Configuration;
using AIChat.Orleans.Client.Services;
using AIChat.Orleans.Placement;
using AIChat.Orleans.Tracing;
using AIChat.Server.HealthChecks;
using AIChat.Server.Hubs;
using AIChat.Server.Logging;
using AIChat.Server.Middleware;
using AIChat.Server.Models;
using AIChat.Server.Services;
using AIChat.Server.Services.EventStore;
using AIChat.Server.Services.ResponseCaching;
using AIChat.Server.Storage;
using AIChat.Server.Storage.Sqlite;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults for observability and health checks
builder.AddServiceDefaults();

// Configure Serilog for JSON file logging - ensure logs go to project root
// When running from server/AIChat.Server, we need to go up two levels to reach project root
var currentDir = Directory.GetCurrentDirectory();
var projectRoot = currentDir.EndsWith("AIChat.Server", StringComparison.Ordinal)
    ? Directory.GetParent(Directory.GetParent(currentDir)?.FullName ?? currentDir)?.FullName
        ?? currentDir
    : Directory.GetParent(currentDir)?.FullName ?? currentDir;
var logFileName = builder.Environment.EnvironmentName switch
{
    "Development" => Path.Combine(projectRoot, "logs", "server", "app-dev.jsonl"),
    "Test" => Path.Combine(projectRoot, "logs", "server", "app-test.jsonl"),
    _ => Path.Combine(projectRoot, "logs", "server", "app.jsonl"),
};

// Ensure log directory exists
Directory.CreateDirectory(Path.GetDirectoryName(logFileName)!);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.Console(
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .WriteTo.File(
        new CompactJsonFormatter(),
        logFileName,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose,
        buffered: false,
        shared: true)
    .CreateLogger();

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("Application", "AIChat.Server")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

    // Add console sink (Warning and above only - keep console clean)
    configuration.WriteTo.Console(
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture
    );

    // Add file sink for all logs (Verbose level)
    configuration.WriteTo.File(
        new CompactJsonFormatter(),
        logFileName,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose,
        buffered: false,
        shared: true,
        rollingInterval: RollingInterval.Day);


    // Add Seq sink for centralized structured logging (if enabled)
    var enableSeq = context.Configuration.GetValue("Serilog:EnableSeq", true);
    if (enableSeq)
    {
        var seqServerUrl = context.Configuration["Serilog:SeqServerUrl"] ?? "http://localhost:5341";
        configuration.WriteTo.Seq(
            serverUrl: seqServerUrl,
            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug,
            apiKey: context.Configuration["Serilog:SeqApiKey"]);
    }
});

// Add services to the container
builder
    .Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use the same serialization options as MessageSerializationOptions.Default
        options.JsonSerializerOptions.DefaultIgnoreCondition = System
            .Text
            .Json
            .Serialization
            .JsonIgnoreCondition
            .WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = System
            .Text
            .Json
            .JsonNamingPolicy
            .CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.CamelCase
            )
        );
        // Enable formatted/indented JSON output for better readability
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure storage (replace EF)
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();

    var connStr = config.GetConnectionString("DefaultConnection");
    var keepRootOpen = false;

    if (env.IsEnvironment("Test"))
    {
        // Default to in-memory shared cache if not overridden
        if (string.IsNullOrWhiteSpace(connStr))
        {
            connStr = "Data Source=File:aichat_test?mode=memory&cache=shared";
        }
        keepRootOpen = true;
    }
    else if (string.IsNullOrWhiteSpace(connStr))
    {
        // Fallback default for non-Test when not supplied by config
        connStr = "Data Source=aichat.db";
    }

    return new SqliteConnectionFactory(connStr!, keepRootOpen);
});

builder.Services.AddSingleton<ISqliteConnectionFactory>(sp =>
    sp.GetRequiredService<SqliteConnectionFactory>()
);

// Register IChatStorage, ITaskStorage, and IModeStorage
builder.Services.AddScoped<IChatStorage, SqliteChatStorage>();
builder.Services.AddScoped<ITaskStorage, SqliteTaskStorage>();
builder.Services.AddScoped<IModeStorage, SqliteModeStorage>();

// Register TaskManagerService (using improved version)
builder.Services.AddScoped<ITaskManagerService, ImprovedTaskManagerService>();

// Register EventStore and Snapshot Management services (Phase 2 - ORL-ST-P2-009/010)
// EventStore must be registered before SnapshotStore due to dependencies
builder.Services.AddEventStore();
builder.Services.AddSnapshotStore();

// Add health checks for EventStore and SnapshotStore
builder.Services.AddEventStoreHealthChecks();
builder.Services.AddSnapshotStoreHealthChecks();

// Register Point-in-Time Recovery services (Phase 4 - ORL-ST-P4-006)
builder.Services.AddScoped<AIChat.Server.Services.Recovery.IPointInTimeRecoveryService>(provider =>
{
    var eventStore = provider.GetRequiredService<AIChat.Server.Services.EventStore.IEventStore>();
    var snapshotStore = provider.GetRequiredService<AIChat.Server.Services.EventStore.ISnapshotStore>();
    var recoveryOrchestrator = provider.GetRequiredService<AIChat.Server.Services.Recovery.IStateRecoveryOrchestrator>();
    var logger = provider.GetRequiredService<ILogger<AIChat.Server.Services.Recovery.Implementations.PointInTimeRecoveryService>>();

    // IStateConsistencyVerifier from ORL-ST-P4-005 may not be available yet - make it optional
    var consistencyVerifier = provider.GetService<AIChat.Server.Services.Recovery.IStateConsistencyVerifier>();

    return new AIChat.Server.Services.Recovery.Implementations.PointInTimeRecoveryService(
        eventStore,
        snapshotStore,
        recoveryOrchestrator,
        consistencyVerifier,
        logger);
});
builder.Services.AddScoped<AIChat.Server.Services.Recovery.IRecoveryAuditService,
    AIChat.Server.Services.Recovery.Implementations.RecoveryAuditService>();
builder.Services.AddScoped<AIChat.Server.Services.Recovery.IRecoveryNotificationService,
    AIChat.Server.Services.Recovery.Implementations.RecoveryNotificationService>();

// Add SignalR with configuration-based settings
builder.Services.AddSignalR(hubOptions =>
{
    var signalRConfig = builder.Configuration.GetSection("SignalR:HubOptions");

    // Configure hub options from appsettings or use defaults
    hubOptions.ClientTimeoutInterval =
        signalRConfig.GetValue<TimeSpan?>("ClientTimeoutInterval") ?? TimeSpan.FromMinutes(10);
    hubOptions.KeepAliveInterval =
        signalRConfig.GetValue<TimeSpan?>("KeepAliveInterval") ?? TimeSpan.FromMinutes(4);
    hubOptions.EnableDetailedErrors = signalRConfig.GetValue(
        "EnableDetailedErrors",
        builder.Environment.IsDevelopment()
    );
    hubOptions.MaximumReceiveMessageSize =
        signalRConfig.GetValue<long?>("MaximumReceiveMessageSize") ?? (32 * 1024); // 32KB default
    hubOptions.StreamBufferCapacity = signalRConfig.GetValue("StreamBufferCapacity", 10);

    // Configure for sticky sessions if needed
    if (builder.Configuration.GetValue("SignalR:StickySessions:Enabled", false))
    {
        hubOptions.SupportedProtocols = ["json"]; // JSON protocol for better debugging
    }
});

// Add Feature Management for controlled Orleans rollout
// Check if FeatureManagement section exists, otherwise use defaults
var featureSection = builder.Configuration.GetSection("FeatureManagement");
if (featureSection.Exists())
{
    _ = builder.Services.AddFeatureManagement(featureSection);
}
else
{
    // If no FeatureManagement section, enable Orleans by default in Development
    _ = builder.Services.AddFeatureManagement();
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
    {
        // Enable Orleans feature flag by default in Development when using -UseOrleans
        _ = builder.Services.Configure<FeatureManagementOptions>(options =>
        {
            // This will be checked later based on Orleans configuration
        });
    }
}

// Configure OpenTelemetry for distributed tracing
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        _ = tracing
            .AddSource(OrleansActivitySource.ActivitySourceName)
            .SetResourceBuilder(
                ResourceBuilder
                    .CreateDefault()
                    .AddService("AIChat.Server", "1.0.0")
                    .AddAttributes(
                        [
                            new KeyValuePair<string, object>(
                                "environment",
                                builder.Environment.EnvironmentName
                            ),
                            new KeyValuePair<string, object>("version", "1.0.0"),
                        ]
                    )
            )
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        // Configure exporters based on environment
        if (builder.Environment.IsDevelopment())
        {
            _ = tracing.AddConsoleExporter();
        }
        else
        {
            // Configure production exporter (OTLP for Jaeger, etc.)
            var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                _ = tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            }
            else
            {
                // Fallback to console in production if no OTLP endpoint configured
                _ = tracing.AddConsoleExporter();
            }
        }

        // Configure sampling - more aggressive in development, conservative in production
        _ = tracing.SetSampler(
            builder.Environment.IsDevelopment()
                ? new AlwaysOnSampler()
                : new TraceIdRatioBasedSampler(0.1)
        ); // Sample 10% in production
    });

// Configure Orleans based on environment
// In Development/Test: Co-host Orleans silo for single-process deployment
// In Production: Use Orleans client to connect to separate silo
var isTestEnvironment = builder.Environment.IsEnvironment("Test");
var isDevelopmentEnvironment = builder.Environment.IsDevelopment();
var orleansDisabled = builder.Configuration.GetValue("Orleans:DisableInTests", false);

if (!orleansDisabled)
{
    try
    {
        // Orleans client-only configuration - Orleans Host runs as separate service (orchestrated by Aspire)
        // In Development/Test: Orleans Host started by Aspire AppHost or manually
        // In Production: Orleans Host runs as independent service
        Log.Information("Configuring Orleans client for {Environment} environment", builder.Environment.EnvironmentName);
        _ = builder.Services.AddOrleansClient(builder.Configuration, builder.Environment);
        Log.Information("Orleans client configured successfully. Connection will be established when services start.");

        // Register Orleans metrics collector (needed for client-side operations)
        // First register the base collector
        builder.Services.AddSingleton<AIChat.Orleans.Metrics.OrleansMetricsCollector>();

        // Then wrap it with resilient decorator for circuit breaker protection
        builder.Services.AddSingleton<AIChat.Orleans.Metrics.IOrleansMetricsCollector>(sp =>
        {
            var baseCollector = sp.GetRequiredService<AIChat.Orleans.Metrics.OrleansMetricsCollector>();
            var logger = sp.GetRequiredService<ILogger<AIChat.Server.Services.Metrics.ResilientMetricsCollectorDecorator>>();
            return new AIChat.Server.Services.Metrics.ResilientMetricsCollectorDecorator(baseCollector, logger);
        });

        // Register Prometheus metrics exporter for Orleans metrics
        builder.Services.AddSingleton<
            AIChat.Server.Services.Metrics.IPrometheusMetricsExporter,
            AIChat.Server.Services.Metrics.PrometheusMetricsExporter
        >();

        // Phase 5: Add placement metrics collection (ORL-ST-P5-001)
        builder.Services.AddPlacementMetrics();

        Log.Information("Orleans configured successfully");

        // Register Orleans Event Relay for ChatHub integration (Phase 3 - ORL-ST-P3-001)
        _ = builder.Services.AddScoped<IOrleansEventRelay, OrleansEventRelay>();

        // Add health checks including Orleans
        _ = builder
            .Services.AddHealthChecks()
            .AddCheck<OrleansClientHealthCheck>("orleans-client")
            .AddCheck<OrleansHealthCheck>("orleans")
            .AddResilientStreamingHealthCheck("resilient-streaming", tags: tags);
    }
    catch (Exception ex)
    {
        // Log warning but don't fail startup - Orleans is optional in Phase 1
        Log.Warning(ex, "Failed to configure Orleans - Orleans integration will be disabled");

        // Register Null Orleans Event Relay when Orleans configuration fails (Phase 3 - ORL-ST-P3-001)
        _ = builder.Services.AddScoped<IOrleansEventRelay, NullOrleansEventRelay>();

        // Add basic health checks without Orleans
        _ = builder.Services.AddHealthChecks();
    }
}
else
{
    Log.Information("Orleans integration explicitly disabled");

    // Register Null Orleans Event Relay when Orleans is disabled (Phase 3 - ORL-ST-P3-001)
    _ = builder.Services.AddScoped<IOrleansEventRelay, NullOrleansEventRelay>();

    // Add basic health checks without Orleans
    _ = builder.Services.AddHealthChecks();
}

// Add LmConfig services
builder.Services.AddLmConfig(builder.Configuration.GetSection("LmConfig"));

// Bind AI model selection options
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

// Configure background processing options
builder.Services.Configure<BackgroundProcessingOptions>(
    builder.Configuration.GetSection(BackgroundProcessingOptions.SectionName)
);

// Configure Orleans resilience options
builder.Services.Configure<OrleansResilienceConfiguration>(
    builder.Configuration.GetSection(OrleansResilienceConfiguration.SectionName)
);

// Configure MCP servers
builder.Services.Configure<McpConfiguration>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<IMcpConfigurationValidator, McpConfigurationValidator>();
builder.Services.AddSingleton<IMcpClientManager, McpClientManager>();

// Phase 3.3: IStreamingAgent registration removed - LLM operations now handled by Orleans ChatGrain
// IStreamingAgent is injected at the Orleans grain level, not at the server application level

// Add CORS for development and test
builder.Services.AddCors(options =>
    options.AddPolicy(
        "AllowSvelteApp",
        policy =>
            _ = policy
                .WithOrigins(
                    "http://localhost:5173",
                    "http://localhost:5174",
                    "http://localhost:5175",
                    "http://localhost:5176",
                    "http://localhost:5177",
                    "http://localhost:5178",
                    "http://localhost:5179",
                    "http://localhost:5180",
                    "http://localhost:5182",
                    "http://localhost:5183",
                    "http://localhost:5183",
                    "http://localhost:4173",
                    "http://localhost:5174"
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
    )
);

// Add timestamped Debug logger for Dev/Test so VS Output shows timestamps
builder.Services.AddLogging(logging =>
{
    // Add our timestamped Debug provider so VS Immediate/Output shows timestamps
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
    {
        _ = logging.AddProvider(new TimestampedDebugLoggerProvider());
    }
});

// Add Server-Sent Events services
builder.Services.AddServerSentEvents();

// Add task management services
// Removed ChatTaskManager - using TaskManager from LmDotNet directly

// Add tooling service
builder.Services.AddScoped<IToolingService, ToolingService>();

// Add chat service with facade
// ChatService is singleton (stateless for background processing)
// ChatServiceFacade is scoped (handles events for controllers)
builder.Services.AddSingleton<ChatService>();
builder.Services.AddScoped<IChatService, ChatServiceFacade>();
builder.Services.AddScoped<IChatServiceFacade, ChatServiceFacade>();
builder.Services.AddScoped<IChatServiceStreaming>(provider =>
    provider.GetRequiredService<ChatService>()
);

// Add mode service
builder.Services.AddScoped<IModeService, ModeService>();

// Add Response Caching infrastructure (Phase 3 - ORL-ST-P3-004)
builder.Services.AddIntelligentResponseCaching();

// Add WebSocket services for Orleans integration (Phase 3 - ORL-ST-P3-005)
// builder.Services.AddWebSocketServices();

// Add SignalR broadcasting service for Orleans integration (Phase 2/3)
builder.Services.AddScoped<
    AIChat.Orleans.Services.ISignalRBroadcastService,
    SignalRBroadcastService
>();

// Add Orleans event relay service for ChatHub integration (Phase 3 - ORL-ST-P3-001)
builder.Services.AddScoped<
    AIChat.Server.Services.IOrleansEventRelay,
    AIChat.Server.Services.OrleansEventRelay
>();

// Add operation tracking service for Orleans background processing (Phase 3)
builder.Services.AddSingleton<IOperationTrackingService, InMemoryOperationTrackingService>();

// Configure StreamingBridge for Orleans-to-SSE conversion (Phase 4)
builder.Services.Configure<AIChat.Server.Configuration.StreamingConfiguration>(
    builder.Configuration.GetSection(AIChat.Server.Configuration.StreamingConfiguration.SectionName)
);
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.IStreamingBridge,
    AIChat.Server.Services.Streaming.StreamingBridge
>();
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.IStreamingBridgeFactory,
    AIChat.Server.Services.Streaming.StreamingBridgeFactory
>();

// Configure Resilient Streaming services (Phase 4 - ORL-P4-004)
builder.Services.Configure<AIChat.Server.Configuration.ResilientStreamingConfiguration>(
    builder.Configuration.GetSection("ResilientStreaming")
);
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.IResilientStreamManager,
    AIChat.Server.Services.Streaming.ResilientStreamManager
>();

// Configure Buffer Management services (Phase 4 - ORL-P4-007)
builder.Services.Configure<AIChat.Server.Services.Streaming.Implementations.FileBasedBufferStoreOptions>(
    builder.Configuration.GetSection("BufferStore")
);
builder.Services.Configure<AIChat.Server.Services.Streaming.Implementations.BufferManagementOptions>(
    builder.Configuration.GetSection("BufferManagement")
);

// Register buffer management components
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.Abstractions.IPersistentBufferStore,
    AIChat.Server.Services.Streaming.Implementations.FileBasedBufferStore
>();

// Note: IStreamBuffer instances are created by BufferManagementService, not injected directly
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.Abstractions.IConnectionStateTracker,
    AIChat.Server.Services.Streaming.Implementations.ConnectionStateTracker
>();
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.Abstractions.IBufferReplayService,
    AIChat.Server.Services.Streaming.Implementations.BufferReplayService
>();
builder.Services.AddSingleton<
    AIChat.Server.Services.Streaming.Abstractions.IBufferManagementService,
    AIChat.Server.Services.Streaming.Implementations.BufferManagementService
>();

// Register BufferManagementService as hosted service for lifecycle management
builder.Services.AddHostedService(provider =>
    (AIChat.Server.Services.Streaming.Implementations.BufferManagementService)
        provider.GetRequiredService<AIChat.Server.Services.Streaming.Abstractions.IBufferManagementService>()
);

// Configure Background Chat Service options
builder.Services.Configure<BackgroundServiceOptions>(options =>
{
    options.MaxConcurrentOperations = builder.Configuration.GetValue(
        "BackgroundService:MaxConcurrentOperations",
        10
    );
    options.DefaultTimeoutMs = builder.Configuration.GetValue(
        "BackgroundService:DefaultTimeoutMs",
        300000
    ); // 5 minutes
    options.OperationHistoryHours = builder.Configuration.GetValue(
        "BackgroundService:OperationHistoryHours",
        2
    );
});

// Add Background Chat Service (Phase 3)
// Register as both IHostedService (for background processing) and IBackgroundChatService (for API access)
builder.Services.AddSingleton<BackgroundChatService>();
builder.Services.AddSingleton<IBackgroundChatService>(provider =>
    provider.GetRequiredService<BackgroundChatService>()
);
builder.Services.AddHostedService(provider => provider.GetRequiredService<BackgroundChatService>());

// Add Production Monitoring Service (Phase 3)
builder.Services.Configure<ProductionMonitoringOptions>(
    builder.Configuration.GetSection("ProductionMonitoring")
);
builder.Services.AddSingleton(provider =>
{
    var logger = provider.GetRequiredService<ILogger<ProductionMonitoringService>>();
    var orleansService = provider.GetService<IOrleansIntegrationService>(); // Can be null
    var grainFactory = provider.GetService<IGrainFactory>(); // Can be null
    var options = provider.GetRequiredService<IOptions<ProductionMonitoringOptions>>();

    return new ProductionMonitoringService(logger, orleansService, grainFactory, provider, options);
});
builder.Services.AddHostedService(provider =>
    provider.GetRequiredService<ProductionMonitoringService>()
);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    _ = app.UseSwagger();
    _ = app.UseSwaggerUI();
}

// Initialize database schema
using (var scope = app.Services.CreateScope())
{
    var env = app.Environment;
    var factory = scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>();
    if (env.IsEnvironment("Test"))
    {
        await TestDatabaseInitializer.InitializeAsync(factory);
    }
    else
    {
        // Idempotent ensure schema and seed users
        await using var conn = await factory.CreateOpenConnectionAsync();
        await SchemaHelper.EnsureSchemaAsync(conn);
        await SchemaHelper.SeedUsersAsync(conn);
    }
}

// Validate MCP configuration at startup
var mcpValidator = app.Services.GetRequiredService<IMcpConfigurationValidator>();
var mcpLogger = app.Services.GetRequiredService<ILogger<Program>>();
if (!mcpValidator.Validate(out var validationErrors))
{
    mcpLogger.LogWarning(
        "MCP configuration validation failed with {ErrorCount} errors:",
        validationErrors.Count
    );
    foreach (var error in validationErrors)
    {
        mcpLogger.LogWarning("  - {Error}", error);
    }
    // Don't fail startup, but log warnings about invalid configuration
}

// Initialize MCP clients at startup (non-blocking)
var mcpClientManager = app.Services.GetRequiredService<IMcpClientManager>();
_ = Task.Run(async () =>
{
    try
    {
        mcpLogger.LogInformation("Starting MCP client initialization...");
        await mcpClientManager.InitializeClientsAsync();
        mcpLogger.LogInformation("MCP client initialization completed");
    }
    catch (Exception ex)
    {
        mcpLogger.LogError(
            ex,
            "Failed to initialize MCP clients at startup. They will be initialized on first use."
        );
    }
});

app.UseCors("AllowSvelteApp");

// Add W3C TraceContext middleware for distributed tracing
// Captures TraceId and SpanId from Activity (OpenTelemetry) or HTTP headers
// and makes them available to all downstream logging and grain calls
app.Use(async (context, next) =>
{
    // Get TraceId from current Activity (set by OpenTelemetry or ASP.NET Core)
    var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    var spanId = System.Diagnostics.Activity.Current?.SpanId.ToString() ?? "root";

    // Push to LogContext for structured logging
    using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
    using (Serilog.Context.LogContext.PushProperty("SpanId", spanId))
    using (Serilog.Context.LogContext.PushProperty("RequestPath", context.Request.Path.Value))
    using (Serilog.Context.LogContext.PushProperty("RequestMethod", context.Request.Method))
    using (Serilog.Context.LogContext.PushProperty("UserId", context.User?.Identity?.Name ?? "Anonymous"))
    {
        await next();
    }
});

// Add Prometheus metrics middleware
app.UseHttpMetrics();

// Add Protocol Negotiation Middleware for SignalR/SSE selection
app.UseProtocolNegotiation();

// Add WebSocket handler middleware (Phase 3 - ORL-ST-P3-005)
// app.UseWebSocketHandler("/api/ws");

// Skip HTTPS redirection in Test (HTTP-only)
if (!app.Environment.IsEnvironment("Test"))
{
    _ = app.UseHttpsRedirection();
}

app.MapControllers();
app.MapHub<ChatHub>("/api/chat-hub");

// Map Prometheus metrics endpoint explicitly
app.MapMetrics("/metrics");

// Add Server-Sent Events endpoint
app.MapServerSentEvents("/api/chat-sse");

// Health check endpoints
app.MapHealthChecks("/api/health");
app.MapGet(
    "/api/health/detailed",
    async (IServiceProvider services) =>
    {
        var healthCheckService =
            services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        var result = await healthCheckService.CheckHealthAsync();

        return Results.Ok(
            new
            {
                Status = result.Status.ToString(),
                Timestamp = DateTime.UtcNow,
                Checks = result.Entries.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        Status = kvp.Value.Status.ToString(),
                        kvp.Value.Description,
                        kvp.Value.Data,
                    }
                ),
            }
        );
    }
);

// Map default Aspire endpoints (health checks, metrics, etc.)
app.MapDefaultEndpoints();

app.Run();

public partial class Program
{
    private static readonly string[] tags = ["streaming"];
}
