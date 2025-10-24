namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Overflow strategy that drops the newest items (rejects new items).
/// </summary>
public sealed class DropNewestStrategy : IOverflowStrategy
{
    private readonly ILogger<DropNewestStrategy>? _logger;
    private long _totalOverflowEvents;
    private long _successfulHandlings;
    private long _failedHandlings;
    private long _totalItemsRejected;
    private DateTime? _lastOverflowTime;
    private readonly Lock _statsLock = new();

    /// <summary>
    /// Initializes a new instance of the DropNewestStrategy class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics</param>
    public DropNewestStrategy(ILogger<DropNewestStrategy>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DropNewest";

    /// <inheritdoc />
    public Task<OverflowHandlingResult> HandleOverflowAsync(
        OverflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_statsLock)
        {
            _totalOverflowEvents++;
            _lastOverflowTime = DateTime.UtcNow;
        }

        try
        {
            // For DropNewest, we simply reject the pending items
            var itemsToReject = context.PendingItems;

            // Log using injected logger if available, otherwise use context logger
            (_logger ?? context.Logger)?.LogWarning(
                "Rejecting {Count} new items. Buffer at {Utilization:F1}% capacity",
                itemsToReject,
                context.UtilizationPercentage);

            lock (_statsLock)
            {
                _successfulHandlings++;
                _totalItemsRejected += itemsToReject;
            }

            return Task.FromResult(new OverflowHandlingResult
            {
                Success = true,
                Action = OverflowAction.DroppedNewest,
                ItemsAffected = itemsToReject,
                ShouldRetry = false // Don't retry - items are rejected
            });
        }
        catch (Exception ex)
        {
            lock (_statsLock)
            {
                _failedHandlings++;
            }

            // Log using injected logger if available, otherwise use context logger
            (_logger ?? context.Logger)?.LogError(ex, "Failed to reject new items");

            return Task.FromResult(new OverflowHandlingResult
            {
                Success = false,
                Action = OverflowAction.Failed,
                ErrorMessage = ex.Message,
                ShouldRetry = false
            });
        }
    }

    /// <inheritdoc />
    public OverflowStrategyStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new OverflowStrategyStatistics
            {
                TotalOverflowEvents = _totalOverflowEvents,
                SuccessfulHandlings = _successfulHandlings,
                FailedHandlings = _failedHandlings,
                TotalItemsAffected = _totalItemsRejected,
                TotalDelayApplied = TimeSpan.Zero,
                LastOverflowTime = _lastOverflowTime
            };
        }
    }
}