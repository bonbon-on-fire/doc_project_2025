using AchieveAi.LmDotnetTools.LmConfig.Services;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.Misc.Configuration;
using AchieveAi.LmDotnetTools.Misc.Http;
using AchieveAi.LmDotnetTools.Misc.Storage;
using AchieveAi.LmDotnetTools.OpenAIProvider.Agents;
using AIChat.Orleans.Client.Configuration;
using AIChat.Orleans.Client.Services;
using AIChat.Server.Hubs;
using AIChat.Server.Logging;
using AIChat.Server.Models;
using AIChat.Server.Services;
using AIChat.Server.Services.TestMode;
using AIChat.Server.Storage;
using AIChat.Server.Storage.Sqlite;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.FeatureManagement;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for JSON file logging - ensure logs go to project root
var projectRoot =
    Directory.GetParent(Directory.GetCurrentDirectory())?.FullName
    ?? Directory.GetCurrentDirectory();
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
    .WriteTo.Console()
    .WriteTo.File(
        new CompactJsonFormatter(),
        logFileName,
        shared: true,
        buffered: false,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose
    )
    .CreateLogger();

builder.Host.UseSerilog();

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

// Add SignalR with configuration-based settings
builder.Services.AddSignalR(hubOptions =>
{
    var signalRConfig = builder.Configuration.GetSection("SignalR:HubOptions");
    
    // Configure hub options from appsettings or use defaults
    hubOptions.ClientTimeoutInterval = signalRConfig.GetValue<TimeSpan?>("ClientTimeoutInterval") ?? TimeSpan.FromMinutes(10);
    hubOptions.KeepAliveInterval = signalRConfig.GetValue<TimeSpan?>("KeepAliveInterval") ?? TimeSpan.FromMinutes(4);
    hubOptions.EnableDetailedErrors = signalRConfig.GetValue("EnableDetailedErrors", builder.Environment.IsDevelopment());
    hubOptions.MaximumReceiveMessageSize = signalRConfig.GetValue<long?>("MaximumReceiveMessageSize") ?? 32 * 1024; // 32KB default
    hubOptions.StreamBufferCapacity = signalRConfig.GetValue("StreamBufferCapacity", 10);
    
    // Configure for sticky sessions if needed
    if (builder.Configuration.GetValue("SignalR:StickySessions:Enabled", false))
    {
        hubOptions.SupportedProtocols = new List<string> { "json" }; // JSON protocol for better debugging
    }
});

// Add Feature Management for controlled Orleans rollout
builder.Services.AddFeatureManagement(builder.Configuration.GetSection("FeatureManagement"));

// Add Orleans Client for Phase 1 shadow mode integration
// Only adds client dependency - Orleans silo runs separately
// Check if we're in test environment or Orleans is explicitly disabled
var isTestEnvironment = builder.Environment.IsEnvironment("Test");
var orleansDisabled = builder.Configuration.GetValue("Orleans:DisableInTests", false);

if (!isTestEnvironment && !orleansDisabled)
{
    try
    {
        _ = builder.Services.AddOrleansClient(builder.Configuration, builder.Environment);
        Log.Information("Orleans client configured successfully");

        // Add health checks including Orleans client
        _ = builder.Services.AddHealthChecks()
            .AddCheck<OrleansClientHealthCheck>("orleans-client")
            .AddCheck<OrleansHealthCheck>("orleans");
    }
    catch (Exception ex)
    {
        // Log warning but don't fail startup - Orleans is optional in Phase 1
        Log.Warning(
            ex,
            "Failed to configure Orleans client - Orleans integration will be disabled"
        );

        // Add basic health checks without Orleans
        _ = builder.Services.AddHealthChecks();
    }
}
else
{
    Log.Information("Orleans integration disabled for test environment");

    // Add basic health checks without Orleans
    _ = builder.Services.AddHealthChecks();
}

// Add LmConfig services
builder.Services.AddLmConfig(builder.Configuration.GetSection("LmConfig"));

// Bind AI model selection options
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

// Configure MCP servers
builder.Services.Configure<McpConfiguration>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<IMcpConfigurationValidator, McpConfigurationValidator>();
builder.Services.AddSingleton<IMcpClientManager, McpClientManager>();

// Register IStreamingAgent as scoped service
builder.Services.AddTransient<IStreamingAgent>(provider =>
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
        apiKey?.Length > 10 ? string.Concat(apiKey.AsSpan(0, 10), "...") : "[EMPTY]"
    );

    if (hostEnv.IsEnvironment("Test"))
    {
        // In Test environment, synthesize streaming via TestSseMessageHandler and bypass API key
        var testHandler = new TestSseMessageHandler();
        var testHttpClient = new HttpClient(testHandler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var openClientTest = new OpenClient(testHttpClient, baseUrl, null, logger);
        return new OpenClientAgent("OpenAi", openClientTest);
    }

    // Create an OpenAI client with caching (non-Test environments)
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException("OpenAI API key is required but was not provided.");
    }

    // Create cache infrastructure
    var cacheDirectory = configuration["LlmCache:CacheDirectory"] ?? "./llm-cache";
    var cache = new FileKvStore(cacheDirectory);

    // Configure cache options
    var cacheOptions = new LlmCacheOptions
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
    var cachingHandler = new CachingHttpMessageHandler(
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

    var openClient = new OpenClient(httpClient, baseUrl, null, logger);
    return new OpenClientAgent("OpenAi", openClient);
});

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
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IChatServiceFacade, ChatServiceFacade>();

// Add mode service
builder.Services.AddScoped<IModeService, ModeService>();

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

// Skip HTTPS redirection in Test (HTTP-only)
if (!app.Environment.IsEnvironment("Test"))
{
    _ = app.UseHttpsRedirection();
}

app.MapControllers();
app.MapHub<ChatHub>("/api/chat-hub");

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
                        Description = kvp.Value.Description,
                        Data = kvp.Value.Data,
                    }
                ),
            }
        );
    }
);

app.Run();

public partial class Program { }
