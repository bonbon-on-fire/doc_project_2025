using AIChat.Server.Configuration;

namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Factory for creating overflow strategy instances.
/// </summary>
public sealed class OverflowStrategyFactory : IOverflowStrategyFactory
{
    private readonly ILogger<OverflowStrategyFactory> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<OverflowStrategy, Func<IServiceProvider, IOverflowStrategy>> _strategyFactories;

    /// <summary>
    /// Initializes a new instance of the OverflowStrategyFactory class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    public OverflowStrategyFactory(
        ILogger<OverflowStrategyFactory> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Initialize strategy factories (lazy creation)
        _strategyFactories = new Dictionary<OverflowStrategy, Func<IServiceProvider, IOverflowStrategy>>
        {
            [OverflowStrategy.Backpressure] = sp =>
            {
                var backpressureLogger = sp.GetService(typeof(ILogger<BackpressureStrategy>)) as ILogger<BackpressureStrategy>;
                return new BackpressureStrategy(backpressureLogger);
            },
            [OverflowStrategy.DropOldest] = sp =>
            {
                var dropOldestLogger = sp.GetService(typeof(ILogger<DropOldestStrategy>)) as ILogger<DropOldestStrategy>;
                return new DropOldestStrategy(dropOldestLogger);
            },
            [OverflowStrategy.DropNewest] = sp =>
            {
                var dropNewestLogger = sp.GetService(typeof(ILogger<DropNewestStrategy>)) as ILogger<DropNewestStrategy>;
                return new DropNewestStrategy(dropNewestLogger);
            },
            [OverflowStrategy.Hybrid] = sp =>
            {
                var hybridLogger = sp.GetService(typeof(ILogger<HybridStrategy>)) as ILogger<HybridStrategy>
                    ?? _logger as ILogger<HybridStrategy>
                    ?? throw new InvalidOperationException("Could not create logger for HybridStrategy");
                var backpressureLogger = sp.GetService(typeof(ILogger<BackpressureStrategy>)) as ILogger<BackpressureStrategy>;
                var dropOldestLogger = sp.GetService(typeof(ILogger<DropOldestStrategy>)) as ILogger<DropOldestStrategy>;
                return new HybridStrategy(hybridLogger, new BackpressureStrategy(backpressureLogger), new DropOldestStrategy(dropOldestLogger));
            }
        };

        _logger.LogInformation("OverflowStrategyFactory initialized with {Count} strategy factories", _strategyFactories.Count);
    }

    /// <inheritdoc />
    public IOverflowStrategy GetStrategy(OverflowStrategy strategyType)
    {
        if (_strategyFactories.TryGetValue(strategyType, out var factory))
        {
            _logger.LogDebug("Creating strategy instance for type {Type}", strategyType);
            return factory(_serviceProvider);
        }

        _logger.LogWarning("Unknown strategy type {Type}, returning backpressure as default", strategyType);
        return _strategyFactories[OverflowStrategy.Backpressure](_serviceProvider);
    }

    /// <inheritdoc />
    public Dictionary<string, OverflowStrategyStatistics> GetAllStatistics()
    {
        // Create temporary instances to get statistics
        var statistics = new Dictionary<string, OverflowStrategyStatistics>();

        foreach (var (strategyType, factory) in _strategyFactories)
        {
            var strategy = factory(_serviceProvider);
            statistics[strategyType.ToString()] = strategy.GetStatistics();
        }

        return statistics;
    }

    /// <inheritdoc />
    public void RegisterStrategy(OverflowStrategy strategyType, Func<IServiceProvider, IOverflowStrategy> strategyFactory)
    {
        ArgumentNullException.ThrowIfNull(strategyFactory);

        _strategyFactories[strategyType] = strategyFactory;
        _logger.LogInformation("Registered custom strategy for type {Type}", strategyType);
    }
}