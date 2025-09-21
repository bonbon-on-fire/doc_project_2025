using AIChat.Server.Configuration;
using AIChat.Server.Configuration.Validators;
using AIChat.Server.Services.Abstractions;
using AIChat.Server.Services.Streaming;
using AIChat.Server.Services.Streaming.Strategies;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Extensions;

/// <summary>
/// Extension methods for registering streaming services.
/// </summary>
public static class StreamingServiceExtensions
{
    /// <summary>
    /// Adds streaming services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration root</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddStreamingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration
        services.Configure<StreamingConfiguration>(configuration.GetSection("Streaming"));

        // Register configuration validators
        services.AddSingleton<IValidateOptions<StreamingConfiguration>, AdaptiveBufferingConfigurationValidator>();

        // Register abstractions
        services.AddSingleton<ISystemTime, SystemTime>();
        services.AddSingleton<ITimerFactory, SystemTimerFactory>();

        // Register core streaming services
        services.AddSingleton<ITrendAnalyzer, TrendAnalyzer>();
        services.AddSingleton<IMessageReplayService>(provider =>
        {
            var config = provider.GetService<IOptions<StreamingConfiguration>>()?.Value
                ?? throw new InvalidOperationException("StreamingConfiguration not found");
            var systemTime = provider.GetService<ISystemTime>()
                ?? throw new InvalidOperationException("ISystemTime not found");
            var logger = provider.GetService<ILogger<MessageReplayService>>()
                ?? throw new InvalidOperationException("Logger<MessageReplayService> not found");
            return new MessageReplayService(
                systemTime,
                logger,
                config.Recovery?.ReplayBufferSize ?? StreamingConstants.Recovery.DefaultReplayBufferSize);
        });

        // Register adaptive buffer manager
        services.AddSingleton<IAdaptiveBufferManager>(provider =>
        {
            var options = provider.GetService<IOptions<StreamingConfiguration>>()
                ?? throw new InvalidOperationException("StreamingConfiguration not found");
            var logger = provider.GetService<ILogger<AdaptiveBufferManager>>()
                ?? throw new InvalidOperationException("Logger<AdaptiveBufferManager> not found");
            var trendAnalyzer = provider.GetService<ITrendAnalyzer>()
                ?? throw new InvalidOperationException("ITrendAnalyzer not found");
            var systemTime = provider.GetService<ISystemTime>()
                ?? throw new InvalidOperationException("ISystemTime not found");
            var timerFactory = provider.GetService<ITimerFactory>()
                ?? throw new InvalidOperationException("ITimerFactory not found");
            return new AdaptiveBufferManager(
                logger,
                options,
                trendAnalyzer,
                systemTime,
                timerFactory,
                options.Value.BufferSize);
        });

        // Register stream recovery manager
        services.AddSingleton<IStreamRecoveryManager, StreamRecoveryManager>();

        // Register overflow strategies
        services.AddTransient<BackpressureStrategy>();
        services.AddTransient<DropOldestStrategy>();
        services.AddTransient<DropNewestStrategy>();
        services.AddTransient<HybridStrategy>();

        // Register overflow strategy factory
        services.AddSingleton<IOverflowStrategyFactory, OverflowStrategyFactory>();

        return services;
    }
}