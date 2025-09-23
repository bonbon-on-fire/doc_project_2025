using AIChat.Orleans.Services;
using AIChat.Server.Hubs;
using AIChat.Server.Services;
using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Implementations;
using AIChat.Server.Services.SignalRBuffering.Models;
using AIChat.Server.Services.SignalRBuffering.Strategies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.SignalRBuffering;

/// <summary>
/// Extension methods for configuring SignalR buffering services in the dependency injection container.
/// Provides a fluent API for registering all necessary components with proper configuration and lifecycle management.
/// </summary>
public static class SignalRBufferingServiceExtensions
{
    /// <summary>
    /// Adds SignalR buffering services to the service collection with default configuration.
    /// This includes the message buffer, delivery service, background processor, and all necessary dependencies.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configurationSection">Configuration section name for buffer settings (default: "SignalRBuffer")</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBuffering(
        this IServiceCollection services,
        string configurationSection = "SignalRBuffer")
    {
        // Register configuration
        services.AddOptions<SignalRBufferConfiguration>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register core services
        services.TryAddSingleton<IBufferOverflowStrategy, DropOldestStrategy>();
        services.TryAddSingleton<ISignalRMessageBuffer, InMemorySignalRMessageBuffer>();
        services.TryAddSingleton<ISignalRDeliveryService, SignalRDeliveryService>();

        // Register background processor as hosted service
        services.AddHostedService<SignalRBufferProcessorService>();

        // Register the buffered broadcast service as a decorator
        services.Add(ServiceDescriptor.Scoped<ISignalRBroadcastService>(provider =>
        {
            // Create the inner SignalR broadcast service
            var hubContext = provider.GetRequiredService<IHubContext<ChatHub>>();
            var broadcastLogger = provider.GetRequiredService<ILogger<SignalRBroadcastService>>();
            var innerService = new SignalRBroadcastService(hubContext, broadcastLogger);

            // Create the buffered decorator
            var messageBuffer = provider.GetRequiredService<ISignalRMessageBuffer>();
            var config = provider.GetRequiredService<IOptions<SignalRBufferConfiguration>>();
            var decoratorLogger = provider.GetRequiredService<ILogger<BufferedSignalRBroadcastService>>();

            return new BufferedSignalRBroadcastService(innerService, messageBuffer, config, decoratorLogger);
        }));

        return services;
    }

    /// <summary>
    /// Adds SignalR buffering services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure buffer options</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBuffering(
        this IServiceCollection services,
        Action<SignalRBufferConfiguration> configureOptions)
    {
        // Register configuration with custom action
        services.Configure<SignalRBufferConfiguration>(configureOptions);

        // Validate configuration
        services.AddOptions<SignalRBufferConfiguration>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register core services
        services.TryAddSingleton<IBufferOverflowStrategy, DropOldestStrategy>();
        services.TryAddSingleton<ISignalRMessageBuffer, InMemorySignalRMessageBuffer>();
        services.TryAddSingleton<ISignalRDeliveryService, SignalRDeliveryService>();

        // Register background processor as hosted service
        services.AddHostedService<SignalRBufferProcessorService>();

        // Register the buffered broadcast service as a decorator
        services.Add(ServiceDescriptor.Scoped<ISignalRBroadcastService>(provider =>
        {
            // Create the inner SignalR broadcast service
            var hubContext = provider.GetRequiredService<IHubContext<ChatHub>>();
            var broadcastLogger = provider.GetRequiredService<ILogger<SignalRBroadcastService>>();
            var innerService = new SignalRBroadcastService(hubContext, broadcastLogger);

            // Create the buffered decorator
            var messageBuffer = provider.GetRequiredService<ISignalRMessageBuffer>();
            var config = provider.GetRequiredService<IOptions<SignalRBufferConfiguration>>();
            var decoratorLogger = provider.GetRequiredService<ILogger<BufferedSignalRBroadcastService>>();

            return new BufferedSignalRBroadcastService(innerService, messageBuffer, config, decoratorLogger);
        }));

        return services;
    }

    /// <summary>
    /// Adds SignalR buffering services with custom overflow strategy.
    /// </summary>
    /// <typeparam name="TStrategy">The type of overflow strategy to use</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configurationSection">Configuration section name for buffer settings</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBuffering<TStrategy>(
        this IServiceCollection services,
        string configurationSection = "SignalRBuffer")
        where TStrategy : class, IBufferOverflowStrategy
    {
        // Register configuration
        services.AddOptions<SignalRBufferConfiguration>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register core services with custom strategy
        services.TryAddSingleton<IBufferOverflowStrategy, TStrategy>();
        services.TryAddSingleton<ISignalRMessageBuffer, InMemorySignalRMessageBuffer>();
        services.TryAddSingleton<ISignalRDeliveryService, SignalRDeliveryService>();

        // Register background processor as hosted service
        services.AddHostedService<SignalRBufferProcessorService>();

        // Register the buffered broadcast service as a decorator
        services.Add(ServiceDescriptor.Scoped<ISignalRBroadcastService>(provider =>
        {
            // Create the inner SignalR broadcast service
            var hubContext = provider.GetRequiredService<IHubContext<ChatHub>>();
            var broadcastLogger = provider.GetRequiredService<ILogger<SignalRBroadcastService>>();
            var innerService = new SignalRBroadcastService(hubContext, broadcastLogger);

            // Create the buffered decorator
            var messageBuffer = provider.GetRequiredService<ISignalRMessageBuffer>();
            var config = provider.GetRequiredService<IOptions<SignalRBufferConfiguration>>();
            var decoratorLogger = provider.GetRequiredService<ILogger<BufferedSignalRBroadcastService>>();

            return new BufferedSignalRBroadcastService(innerService, messageBuffer, config, decoratorLogger);
        }));

        return services;
    }

    /// <summary>
    /// Adds SignalR buffering services with custom buffer implementation.
    /// </summary>
    /// <typeparam name="TBuffer">The type of message buffer to use</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configurationSection">Configuration section name for buffer settings</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingWithCustomBuffer<TBuffer>(
        this IServiceCollection services,
        string configurationSection = "SignalRBuffer")
        where TBuffer : class, ISignalRMessageBuffer
    {
        // Register configuration
        services.AddOptions<SignalRBufferConfiguration>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register core services with custom buffer
        services.TryAddSingleton<IBufferOverflowStrategy, DropOldestStrategy>();
        services.TryAddSingleton<ISignalRMessageBuffer, TBuffer>();
        services.TryAddSingleton<ISignalRDeliveryService, SignalRDeliveryService>();

        // Register background processor as hosted service
        services.AddHostedService<SignalRBufferProcessorService>();

        // Register the buffered broadcast service as a decorator
        services.Add(ServiceDescriptor.Scoped<ISignalRBroadcastService>(provider =>
        {
            // Create the inner SignalR broadcast service
            var hubContext = provider.GetRequiredService<IHubContext<ChatHub>>();
            var broadcastLogger = provider.GetRequiredService<ILogger<SignalRBroadcastService>>();
            var innerService = new SignalRBroadcastService(hubContext, broadcastLogger);

            // Create the buffered decorator
            var messageBuffer = provider.GetRequiredService<ISignalRMessageBuffer>();
            var config = provider.GetRequiredService<IOptions<SignalRBufferConfiguration>>();
            var decoratorLogger = provider.GetRequiredService<ILogger<BufferedSignalRBroadcastService>>();

            return new BufferedSignalRBroadcastService(innerService, messageBuffer, config, decoratorLogger);
        }));

        return services;
    }

    /// <summary>
    /// Adds SignalR buffering services with full customization options.
    /// </summary>
    /// <typeparam name="TBuffer">The type of message buffer to use</typeparam>
    /// <typeparam name="TStrategy">The type of overflow strategy to use</typeparam>
    /// <typeparam name="TDelivery">The type of delivery service to use</typeparam>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configureOptions">Action to configure buffer options</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingCustom<TBuffer, TStrategy, TDelivery>(
        this IServiceCollection services,
        Action<SignalRBufferConfiguration>? configureOptions = null)
        where TBuffer : class, ISignalRMessageBuffer
        where TStrategy : class, IBufferOverflowStrategy
        where TDelivery : class, ISignalRDeliveryService
    {
        // Register configuration
        if (configureOptions != null)
        {
            services.Configure<SignalRBufferConfiguration>(configureOptions);
        }

        services.AddOptions<SignalRBufferConfiguration>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register custom implementations
        services.TryAddSingleton<IBufferOverflowStrategy, TStrategy>();
        services.TryAddSingleton<ISignalRMessageBuffer, TBuffer>();
        services.TryAddSingleton<ISignalRDeliveryService, TDelivery>();

        // Register background processor as hosted service
        services.AddHostedService<SignalRBufferProcessorService>();

        // Register the buffered broadcast service as a decorator
        services.Add(ServiceDescriptor.Scoped<ISignalRBroadcastService>(provider =>
        {
            // Create the inner SignalR broadcast service
            var hubContext = provider.GetRequiredService<IHubContext<ChatHub>>();
            var broadcastLogger = provider.GetRequiredService<ILogger<SignalRBroadcastService>>();
            var innerService = new SignalRBroadcastService(hubContext, broadcastLogger);

            // Create the buffered decorator
            var messageBuffer = provider.GetRequiredService<ISignalRMessageBuffer>();
            var config = provider.GetRequiredService<IOptions<SignalRBufferConfiguration>>();
            var decoratorLogger = provider.GetRequiredService<ILogger<BufferedSignalRBroadcastService>>();

            return new BufferedSignalRBroadcastService(innerService, messageBuffer, config, decoratorLogger);
        }));

        return services;
    }

    /// <summary>
    /// Adds health checks for SignalR buffering services.
    /// Monitors buffer health, delivery service availability, and overall system status.
    /// </summary>
    /// <param name="services">The service collection to add health checks to</param>
    /// <param name="name">The name of the health check (default: "signalr-buffering")</param>
    /// <param name="tags">Optional tags for the health check</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingHealthChecks(
        this IServiceCollection services,
        string name = "signalr-buffering",
        params string[] tags)
    {
        services.AddHealthChecks()
            .AddCheck<SignalRBufferingHealthCheck>(name, tags: tags);

        return services;
    }

    /// <summary>
    /// Configures SignalR buffering for high-throughput scenarios.
    /// Optimizes settings for production environments with high message volumes.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingForHighThroughput(this IServiceCollection services)
    {
        return services.AddSignalRBuffering(config =>
        {
            config.MaxBufferSize = 50_000;
            config.ProcessingInterval = TimeSpan.FromMilliseconds(50);
            config.BatchSize = 200;
            config.OverflowStrategy = "DropOldest";
            config.EnableMetrics = true;
            config.EnableDeliveryConfirmation = true;
            config.DegradedThresholdPercent = 85;
            config.UnhealthyThresholdPercent = 95;
        });
    }

    /// <summary>
    /// Configures SignalR buffering for low-latency scenarios.
    /// Optimizes settings for real-time applications where latency is critical.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingForLowLatency(this IServiceCollection services)
    {
        return services.AddSignalRBuffering(config =>
        {
            config.MaxBufferSize = 5_000;
            config.ProcessingInterval = TimeSpan.FromMilliseconds(10);
            config.BatchSize = 10;
            config.OverflowStrategy = "Blocking";
            config.EnableMetrics = true;
            config.EnableDeliveryConfirmation = true;
            config.DegradedThresholdPercent = 70;
            config.UnhealthyThresholdPercent = 90;
        });
    }

    /// <summary>
    /// Configures SignalR buffering for development environments.
    /// Includes detailed logging and metrics for debugging purposes.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for fluent chaining</returns>
    public static IServiceCollection AddSignalRBufferingForDevelopment(this IServiceCollection services)
    {
        return services.AddSignalRBuffering(config =>
        {
            config.MaxBufferSize = 1_000;
            config.ProcessingInterval = TimeSpan.FromMilliseconds(200);
            config.BatchSize = 5;
            config.OverflowStrategy = "DropOldest";
            config.EnableMetrics = true;
            config.EnableDeliveryConfirmation = true;
            config.MetricsInterval = TimeSpan.FromSeconds(5);
            config.DeliveryTrackingRetention = TimeSpan.FromMinutes(30);
            config.DegradedThresholdPercent = 60;
            config.UnhealthyThresholdPercent = 80;
        });
    }
}

/// <summary>
/// Health check for SignalR buffering services.
/// Monitors the health of the message buffer, delivery service, and background processor.
/// </summary>
public class SignalRBufferingHealthCheck : IHealthCheck
{
    private readonly ISignalRMessageBuffer _messageBuffer;
    private readonly ISignalRDeliveryService _deliveryService;
    private readonly ILogger<SignalRBufferingHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the SignalRBufferingHealthCheck.
    /// </summary>
    /// <param name="messageBuffer">The message buffer to check</param>
    /// <param name="deliveryService">The delivery service to check</param>
    /// <param name="logger">Logger instance</param>
    public SignalRBufferingHealthCheck(
        ISignalRMessageBuffer messageBuffer,
        ISignalRDeliveryService deliveryService,
        ILogger<SignalRBufferingHealthCheck> logger)
    {
        _messageBuffer = messageBuffer ?? throw new ArgumentNullException(nameof(messageBuffer));
        _deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Performs the health check.
    /// </summary>
    /// <param name="context">Health check context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health check result</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check buffer health
            var bufferHealth = await _messageBuffer.GetHealthAsync(cancellationToken);
            var bufferMetrics = await _messageBuffer.GetMetricsAsync(cancellationToken);

            // Check delivery service availability
            var deliveryServiceAvailable = _deliveryService.IsAvailable;

            // Determine overall health
            var isHealthy = bufferHealth.IsHealthy && deliveryServiceAvailable;
            var status = isHealthy ? HealthStatus.Healthy :
                         bufferHealth.Status == BufferHealthStatus.Degraded ? HealthStatus.Degraded :
                         HealthStatus.Unhealthy;

            var data = new Dictionary<string, object>
            {
                ["BufferHealth"] = bufferHealth.Status.ToString(),
                ["BufferUtilization"] = bufferMetrics.BufferUtilizationPercent,
                ["CurrentBufferSize"] = bufferMetrics.CurrentBufferSize,
                ["MaxBufferCapacity"] = bufferMetrics.MaxBufferCapacity,
                ["DeliveryServiceAvailable"] = deliveryServiceAvailable,
                ["DeliverySuccessRate"] = bufferMetrics.DeliverySuccessRate,
                ["MessagesPerSecond"] = bufferMetrics.MessagesPerSecond,
                ["TotalEnqueued"] = bufferMetrics.TotalEnqueued,
                ["TotalDelivered"] = bufferMetrics.TotalDelivered,
                ["TotalDropped"] = bufferMetrics.TotalDropped,
                ["TotalFailed"] = bufferMetrics.TotalFailed
            };

            var description = status switch
            {
                HealthStatus.Healthy => "SignalR buffering is operating normally",
                HealthStatus.Degraded => $"SignalR buffering is degraded: {bufferHealth.Message}",
                HealthStatus.Unhealthy => $"SignalR buffering is unhealthy: {bufferHealth.Message}",
                _ => "SignalR buffering status unknown"
            };

            return new HealthCheckResult(status, description, data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing SignalR buffering health check");

            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                $"Health check failed: {ex.Message}",
                ex);
        }
    }
}