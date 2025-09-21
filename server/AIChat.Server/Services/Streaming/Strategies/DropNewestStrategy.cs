namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Overflow strategy that drops the newest items (rejects new items).
/// </summary>
public sealed class DropNewestStrategy : IOverflowStrategy
{
    private long _totalOverflowEvents;
    private long _successfulHandlings;
    private long _failedHandlings;
    private long _totalItemsRejected;
    private DateTime? _lastOverflowTime;
    private readonly Lock _statsLock = new();

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

            context.Logger?.LogWarning(
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

            context.Logger?.LogError(ex, "Failed to reject new items");

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