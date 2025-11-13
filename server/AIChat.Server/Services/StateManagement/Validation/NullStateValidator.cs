namespace AIChat.Server.Services.StateManagement.Validation;

/// <summary>
/// Null object pattern implementation of IStateValidator that performs no validation.
/// Used as a default validator when no specific validation logic is required.
/// </summary>
/// <typeparam name="T">The type of entity being validated</typeparam>
public sealed class NullStateValidator<T> : IStateValidator<T> where T : class
{
    /// <inheritdoc />
    public string Name => "NullValidator";

    /// <inheritdoc />
    public Task<StateValidationResult> ValidateCreateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public Task<StateValidationResult> ValidateUpdateAsync(string entityId, T entity, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(entity);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public Task<StateValidationResult> ValidateDeleteAsync(string entityId, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public Task<StateValidationResult> ValidatePatchAsync(string entityId, Dictionary<string, object> updates, T? existingEntity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(updates);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public Task<StateValidationResult> ValidateBatchAsync(IEnumerable<StateOperation<T>> operations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public Task<StateValidationResult> ValidateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Task.FromResult(StateValidationResult.Success());
    }

    /// <inheritdoc />
    public IEnumerable<ValidationRuleInfo> GetValidationRules(ValidationOperation operation)
    {
        return [];
    }

    /// <inheritdoc />
    public StateValidationMetrics GetMetrics()
    {
        return StateValidationMetrics.Empty();
    }
}
