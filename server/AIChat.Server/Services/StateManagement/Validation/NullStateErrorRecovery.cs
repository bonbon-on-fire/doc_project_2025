namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Null object pattern implementation of IStateErrorRecovery that performs no error recovery.
/// Used as a default error recovery handler when no specific recovery logic is required.
/// </summary>
/// <typeparam name="T">The type of entity for error recovery</typeparam>
public sealed class NullStateErrorRecovery<T> : IStateErrorRecovery<T> where T : class
{
    /// <inheritdoc />
    public string Name => "NullErrorRecovery";

    /// <inheritdoc />
    public Task<RecoveryResult> AttemptAutomaticRecoveryAsync(StateOperation<T> operation, StateValidationResult validationFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(validationFailure);

        // No automatic recovery is performed by the null implementation
        return Task.FromResult(RecoveryResult.Failed(
            RecoveryStrategy.ManualIntervention,
            "No automatic recovery available",
            validationFailure.Errors.Count,
            TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<IEnumerable<RecoveryOption>> GetRecoveryOptionsAsync(StateOperation<T> operation, StateValidationResult validationFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(validationFailure);

        // No recovery options are provided by the null implementation
        var options = Array.Empty<RecoveryOption>();
        return Task.FromResult<IEnumerable<RecoveryOption>>(options);
    }

    /// <inheritdoc />
    public Task<RecoveryResult> ExecuteRecoveryAsync(StateOperation<T> operation, RecoveryOption recoveryOption, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(recoveryOption);

        // No recovery execution is performed by the null implementation
        return Task.FromResult(RecoveryResult.Failed(
            recoveryOption.Strategy,
            "No recovery execution available",
            0,
            TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<RecoveryResult> RollbackAsync(StateOperation<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // No rollback is performed by the null implementation
        return Task.FromResult(RecoveryResult.Failed(
            RecoveryStrategy.Rollback,
            "No rollback capability available",
            0,
            TimeSpan.Zero));
    }

    /// <inheritdoc />
    public Task<bool> IsRecoveryOptionApplicableAsync(RecoveryOption recoveryOption, StateValidationResult validationFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryOption);
        ArgumentNullException.ThrowIfNull(validationFailure);

        // No recovery options are applicable in the null implementation
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public ErrorRecoveryMetrics GetMetrics()
    {
        return ErrorRecoveryMetrics.Empty();
    }

    /// <inheritdoc />
    public Task<IEnumerable<RecoveryHistoryEntry>> GetRecoveryHistoryAsync(string entityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        // No recovery history is maintained by the null implementation
        var history = Array.Empty<RecoveryHistoryEntry>();
        return Task.FromResult<IEnumerable<RecoveryHistoryEntry>>(history);
    }
}
