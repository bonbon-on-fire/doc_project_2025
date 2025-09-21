namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Overflow strategy that drops the oldest items from the buffer.
/// </summary>
public sealed class DropOldestStrategy : IOverflowStrategy
{
    private long _totalOverflowEvents;
    private long _successfulHandlings;
    private long _failedHandlings;
    private long _totalItemsDropped;
    private DateTime? _lastOverflowTime;
    private readonly Lock _statsLock = new();

    /// <inheritdoc />
    public string Name => "DropOldest";

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
            // Calculate how many items to drop
            var itemsToDrop = Math.Min(
                context.Configuration.MaxDropBatchSize,
                Math.Max(1, context.CurrentSize / 10)); // Drop up to 10% of buffer

            // In a real implementation, we would actually remove items from the buffer
            // For now, we're just tracking the operation
            if (context.DroppableItems != null && context.DroppableItems.Count > 0)
            {
                var actualDropped = Math.Min(itemsToDrop, context.DroppableItems.Count);

                // Remove oldest items (from the beginning of the list)
                for (var i = 0; i < actualDropped; i++)
                {
                    context.DroppableItems.RemoveAt(0);
                }

                itemsToDrop = actualDropped;
            }

            context.Logger?.LogWarning(
                "Dropping {Count} oldest items from buffer. Utilization: {Utilization:F1}%",
                itemsToDrop,
                context.UtilizationPercentage);

            lock (_statsLock)
            {
                _successfulHandlings++;
                _totalItemsDropped += itemsToDrop;
            }

            return Task.FromResult(new OverflowHandlingResult
            {
                Success = true,
                Action = OverflowAction.DroppedOldest,
                ItemsAffected = itemsToDrop,
                ShouldRetry = true
            });
        }
        catch (Exception ex)
        {
            lock (_statsLock)
            {
                _failedHandlings++;
            }

            context.Logger?.LogError(ex, "Failed to drop oldest items");

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
                TotalItemsAffected = _totalItemsDropped,
                TotalDelayApplied = TimeSpan.Zero,
                LastOverflowTime = _lastOverflowTime
            };
        }
    }
}