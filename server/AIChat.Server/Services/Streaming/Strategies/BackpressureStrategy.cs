namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Overflow strategy that applies backpressure to slow down producers.
/// </summary>
public sealed class BackpressureStrategy : IOverflowStrategy
{
    private long _totalOverflowEvents;
    private long _successfulHandlings;
    private long _failedHandlings;
    private long _totalDelayMs;
    private DateTime? _lastOverflowTime;
    private readonly Lock _statsLock = new();

    /// <inheritdoc />
    public string Name => "Backpressure";

    /// <inheritdoc />
    public async Task<OverflowHandlingResult> HandleOverflowAsync(
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
            // Calculate delay based on utilization
            var baseDelay = context.Configuration.Primary == Configuration.OverflowStrategy.Backpressure
                ? 100 // Base delay in ms
                : 50;  // Reduced delay if used as fallback

            // Exponential backoff based on utilization
            var utilizationFactor = Math.Max(0, context.UtilizationPercentage - 80) / 20; // 0-1 scale for 80-100%
            var delay = (int)(baseDelay * Math.Pow(2, utilizationFactor * 3)); // Up to 8x delay at 100%

            // Cap the delay
            delay = Math.Min(delay, 5000); // Max 5 seconds

            context.Logger?.LogWarning(
                "Applying backpressure. Utilization: {Utilization:F1}%, Delay: {Delay}ms",
                context.UtilizationPercentage,
                delay);

            await Task.Delay(delay, cancellationToken);

            lock (_statsLock)
            {
                _successfulHandlings++;
                _totalDelayMs += delay;
            }

            return new OverflowHandlingResult
            {
                Success = true,
                Action = OverflowAction.BackpressureApplied,
                ItemsAffected = context.PendingItems,
                DelayApplied = TimeSpan.FromMilliseconds(delay),
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromMilliseconds(delay / 2)
            };
        }
        catch (OperationCanceledException)
        {
            lock (_statsLock)
            {
                _failedHandlings++;
            }

            return new OverflowHandlingResult
            {
                Success = false,
                Action = OverflowAction.Failed,
                ErrorMessage = "Backpressure cancelled",
                ShouldRetry = false
            };
        }
        catch (Exception ex)
        {
            lock (_statsLock)
            {
                _failedHandlings++;
            }

            context.Logger?.LogError(ex, "Failed to apply backpressure");

            return new OverflowHandlingResult
            {
                Success = false,
                Action = OverflowAction.Failed,
                ErrorMessage = ex.Message,
                ShouldRetry = false
            };
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
                TotalItemsAffected = 0, // Backpressure doesn't drop items
                TotalDelayApplied = TimeSpan.FromMilliseconds(_totalDelayMs),
                LastOverflowTime = _lastOverflowTime
            };
        }
    }
}