namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Hybrid overflow strategy that combines multiple strategies based on conditions.
/// </summary>
public sealed class HybridStrategy : IOverflowStrategy
{
    private readonly ILogger<HybridStrategy> _logger;
    private readonly IOverflowStrategy _backpressure;
    private readonly IOverflowStrategy _dropOldest;
    private long _totalOverflowEvents;
    private long _successfulHandlings;
    private long _failedHandlings;
    private DateTime? _lastOverflowTime;
    private readonly Lock _statsLock = new();

    public HybridStrategy(
        ILogger<HybridStrategy> logger,
        IOverflowStrategy? backpressureStrategy = null,
        IOverflowStrategy? dropOldestStrategy = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _backpressure = backpressureStrategy ?? new BackpressureStrategy();
        _dropOldest = dropOldestStrategy ?? new DropOldestStrategy();
    }

    /// <inheritdoc />
    public string Name => "Hybrid";

    /// <inheritdoc />
    public async Task<OverflowHandlingResult> HandleOverflowAsync(
        OverflowContext context,
        CancellationToken cancellationToken = default)
    {
        lock (_statsLock)
        {
            _totalOverflowEvents++;
            _lastOverflowTime = DateTime.UtcNow;
        }

        // Hybrid logic: Use backpressure up to drop threshold, then drop oldest
        if (context.UtilizationPercentage < context.Configuration.DropThreshold)
        {
            _logger.LogDebug(
                "Using backpressure strategy for buffer at {Utilization:F1}% utilization",
                context.UtilizationPercentage);

            var result = await _backpressure.HandleOverflowAsync(context, cancellationToken);

            lock (_statsLock)
            {
                if (result.Success)
                {
                    _successfulHandlings++;
                }
                else
                {
                    _failedHandlings++;
                }
            }

            // Wrap result to indicate hybrid action
            return new OverflowHandlingResult
            {
                Success = result.Success,
                Action = OverflowAction.HybridAction,
                ItemsAffected = result.ItemsAffected,
                DelayApplied = result.DelayApplied,
                ErrorMessage = result.ErrorMessage,
                ShouldRetry = result.ShouldRetry,
                RetryDelay = result.RetryDelay
            };
        }

        _logger.LogWarning(
            "Buffer critically full ({Utilization:F1}%), switching to drop strategy",
            context.UtilizationPercentage);

        var dropResult = await _dropOldest.HandleOverflowAsync(context, cancellationToken);

        lock (_statsLock)
        {
            if (dropResult.Success)
            {
                _successfulHandlings++;
            }
            else
            {
                _failedHandlings++;
            }
        }

        // Wrap result to indicate hybrid action
        return new OverflowHandlingResult
        {
            Success = dropResult.Success,
            Action = OverflowAction.HybridAction,
            ItemsAffected = dropResult.ItemsAffected,
            DelayApplied = dropResult.DelayApplied,
            ErrorMessage = dropResult.ErrorMessage,
            ShouldRetry = dropResult.ShouldRetry,
            RetryDelay = dropResult.RetryDelay
        };
    }

    /// <inheritdoc />
    public OverflowStrategyStatistics GetStatistics()
    {
        var backpressureStats = _backpressure.GetStatistics();
        var dropStats = _dropOldest.GetStatistics();

        lock (_statsLock)
        {
            return new OverflowStrategyStatistics
            {
                TotalOverflowEvents = _totalOverflowEvents,
                SuccessfulHandlings = _successfulHandlings,
                FailedHandlings = _failedHandlings,
                TotalItemsAffected = backpressureStats.TotalItemsAffected + dropStats.TotalItemsAffected,
                TotalDelayApplied = backpressureStats.TotalDelayApplied + dropStats.TotalDelayApplied,
                LastOverflowTime = _lastOverflowTime
            };
        }
    }
}