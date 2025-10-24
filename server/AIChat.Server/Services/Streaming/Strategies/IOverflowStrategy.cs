namespace AIChat.Server.Services.Streaming.Strategies;

/// <summary>
/// Interface for buffer overflow handling strategies.
/// </summary>
public interface IOverflowStrategy
{
    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Handles buffer overflow condition.
    /// </summary>
    /// <param name="context">Context containing buffer information and items</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the overflow handling operation</returns>
    Task<OverflowHandlingResult> HandleOverflowAsync(
        OverflowContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for this overflow strategy.
    /// </summary>
    OverflowStrategyStatistics GetStatistics();
}

/// <summary>
/// Context for overflow handling operations.
/// </summary>
public sealed class OverflowContext
{
    /// <summary>
    /// Current buffer utilization percentage (0-100).
    /// </summary>
    public float UtilizationPercentage { get; init; }

    /// <summary>
    /// Current buffer size.
    /// </summary>
    public int CurrentSize { get; init; }

    /// <summary>
    /// Buffer capacity.
    /// </summary>
    public int Capacity { get; init; }

    /// <summary>
    /// Number of items pending to be added.
    /// </summary>
    public int PendingItems { get; init; }

    /// <summary>
    /// Optional: Items that could be dropped (for drop strategies).
    /// </summary>
    public IList<object>? DroppableItems { get; init; }

    /// <summary>
    /// Configuration for the overflow strategy.
    /// </summary>
    public required Configuration.OverflowStrategyConfiguration Configuration { get; init; }

    /// <summary>
    /// Logger for diagnostics.
    /// </summary>
    public ILogger? Logger { get; init; }
}

/// <summary>
/// Result of overflow handling operation.
/// </summary>
public sealed class OverflowHandlingResult
{
    /// <summary>
    /// Whether the overflow was handled successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Action taken to handle the overflow.
    /// </summary>
    public OverflowAction Action { get; init; }

    /// <summary>
    /// Number of items affected (dropped, delayed, etc.).
    /// </summary>
    public int ItemsAffected { get; init; }

    /// <summary>
    /// Delay applied (for backpressure strategies).
    /// </summary>
    public TimeSpan? DelayApplied { get; init; }

    /// <summary>
    /// Error message if handling failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether to retry the operation.
    /// </summary>
    public bool ShouldRetry { get; init; }

    /// <summary>
    /// Suggested retry delay.
    /// </summary>
    public TimeSpan? RetryDelay { get; init; }
}

/// <summary>
/// Action taken to handle overflow.
/// </summary>
public enum OverflowAction
{
    /// <summary>
    /// No action taken.
    /// </summary>
    None = 0,

    /// <summary>
    /// Applied backpressure delay.
    /// </summary>
    BackpressureApplied = 1,

    /// <summary>
    /// Dropped oldest items.
    /// </summary>
    DroppedOldest = 2,

    /// <summary>
    /// Dropped newest items.
    /// </summary>
    DroppedNewest = 3,

    /// <summary>
    /// Used hybrid approach.
    /// </summary>
    HybridAction = 4,

    /// <summary>
    /// Failed to handle overflow.
    /// </summary>
    Failed = 5
}

/// <summary>
/// Statistics for overflow strategy operations.
/// </summary>
public sealed class OverflowStrategyStatistics
{
    /// <summary>
    /// Total number of overflow events handled.
    /// </summary>
    public long TotalOverflowEvents { get; init; }

    /// <summary>
    /// Number of successful overflow handling operations.
    /// </summary>
    public long SuccessfulHandlings { get; init; }

    /// <summary>
    /// Number of failed overflow handling operations.
    /// </summary>
    public long FailedHandlings { get; init; }

    /// <summary>
    /// Total items affected (dropped, delayed, etc.).
    /// </summary>
    public long TotalItemsAffected { get; init; }

    /// <summary>
    /// Total delay applied (for backpressure strategies).
    /// </summary>
    public TimeSpan TotalDelayApplied { get; init; }

    /// <summary>
    /// Last overflow event time.
    /// </summary>
    public DateTime? LastOverflowTime { get; init; }

    /// <summary>
    /// Average items affected per overflow event.
    /// </summary>
    public double AverageItemsAffected => TotalOverflowEvents > 0
        ? (double)TotalItemsAffected / TotalOverflowEvents
        : 0;
}