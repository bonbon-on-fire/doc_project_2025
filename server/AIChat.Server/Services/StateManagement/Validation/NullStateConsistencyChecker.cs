namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Null object pattern implementation of IStateConsistencyChecker that performs no consistency checking.
/// Used as a default consistency checker when no specific consistency logic is required.
/// </summary>
/// <typeparam name="T">The type of entity being checked for consistency</typeparam>
public sealed class NullStateConsistencyChecker<T> : IStateConsistencyChecker<T> where T : class
{
    /// <inheritdoc />
    public string Name => "NullConsistencyChecker";

    /// <inheritdoc />
    public Task<ConsistencyCheckResult> CheckConsistencyAsync(string entityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return Task.FromResult(ConsistencyCheckResult.Consistent(1, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<ConsistencyCheckResult> CheckBatchConsistencyAsync(IEnumerable<string> entityIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        var count = entityIds.Count();
        return Task.FromResult(ConsistencyCheckResult.Consistent(count, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<ConsistencyCheckResult> CheckGlobalConsistencyAsync(StateQuery? query = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ConsistencyCheckResult.Consistent(0, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<ReconciliationResult> ReconcileAsync(string entityId, ReconciliationStrategy strategy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return Task.FromResult(ReconciliationResult.Success(strategy, 0, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public ConsistencyCheckMetrics GetMetrics()
    {
        return ConsistencyCheckMetrics.Empty();
    }

    /// <inheritdoc />
    public Task<ConsistencyStatusSummary> GetConsistencyStatusSummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = new ConsistencyStatusSummary
        {
            TotalEntities = 0,
            ConsistentEntities = 0,
            InconsistentEntities = 0,
            UncheckedEntities = 0,
            LastGlobalCheckAt = DateTime.UtcNow
        };
        return Task.FromResult(summary);
    }
}
