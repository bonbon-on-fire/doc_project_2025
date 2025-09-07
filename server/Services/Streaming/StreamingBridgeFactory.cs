using AIChat.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Factory implementation for creating StreamingBridge instances.
/// </summary>
public class StreamingBridgeFactory : IStreamingBridgeFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<StreamingConfiguration> _defaultConfiguration;
    private readonly bool _useRefactoredImplementation;

    /// <summary>
    /// Initializes a new instance of the StreamingBridgeFactory class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory for creating loggers</param>
    /// <param name="defaultConfiguration">Default streaming configuration</param>
    public StreamingBridgeFactory(
        ILoggerFactory loggerFactory,
        IOptions<StreamingConfiguration> defaultConfiguration)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _defaultConfiguration = defaultConfiguration ?? throw new ArgumentNullException(nameof(defaultConfiguration));
        
        // Check configuration or environment variable to determine which implementation to use
        _useRefactoredImplementation = Environment.GetEnvironmentVariable("USE_REFACTORED_STREAMING_BRIDGE") == "true";
    }

    /// <inheritdoc />
    public IStreamingBridge CreateBridge()
    {
        if (_useRefactoredImplementation)
        {
            var logger = _loggerFactory.CreateLogger<StreamingBridgeV2>();
            return new StreamingBridgeV2(logger, _defaultConfiguration, _loggerFactory);
        }
        else
        {
            var logger = _loggerFactory.CreateLogger<StreamingBridge>();
            return new StreamingBridge(logger, _defaultConfiguration);
        }
    }

    /// <inheritdoc />
    public IStreamingBridge CreateBridge(Action<StreamingConfiguration> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);

        // Clone the default configuration and apply customizations
        var customConfig = new StreamingConfiguration
        {
            BufferSize = _defaultConfiguration.Value.BufferSize,
            BackpressureThreshold = _defaultConfiguration.Value.BackpressureThreshold,
            WriteTimeoutMs = _defaultConfiguration.Value.WriteTimeoutMs,
            BackpressureDelayMs = _defaultConfiguration.Value.BackpressureDelayMs,
            EnableAdaptiveBackpressure = _defaultConfiguration.Value.EnableAdaptiveBackpressure,
            EnableTelemetry = _defaultConfiguration.Value.EnableTelemetry,
            MaxChunkSize = _defaultConfiguration.Value.MaxChunkSize,
            FlushIntervalMs = _defaultConfiguration.Value.FlushIntervalMs,
            EnableAutoRetry = _defaultConfiguration.Value.EnableAutoRetry,
            MaxRetryAttempts = _defaultConfiguration.Value.MaxRetryAttempts
        };

        configureOptions(customConfig);

        if (_useRefactoredImplementation)
        {
            var logger = _loggerFactory.CreateLogger<StreamingBridgeV2>();
            return new StreamingBridgeV2(logger, Options.Create(customConfig), _loggerFactory);
        }
        else
        {
            var logger = _loggerFactory.CreateLogger<StreamingBridge>();
            return new StreamingBridge(logger, Options.Create(customConfig));
        }
    }
}