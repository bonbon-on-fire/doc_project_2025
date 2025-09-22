using System.Diagnostics;
using AIChat.Server.Services.StateManagement.Validation;

namespace AIChat.Server.Services.StateManagement.Implementations;

/// <summary>
/// Base class for Orleans-based state managers providing common functionality.
/// Implements shared concerns like metrics, health checks, and error handling.
/// </summary>
/// <typeparam name="T">The type of entity being managed</typeparam>
public abstract class OrleansStateManagerBase<T> : IStateManager<T> where T : class
{
    /// <summary>
    /// Gets the Orleans grain factory for creating and accessing grains.
    /// </summary>
    protected IGrainFactory GrainFactory { get; }

    /// <summary>
    /// Gets the logger instance for this state manager.
    /// </summary>
    protected ILogger<OrleansStateManagerBase<T>> Logger { get; }

    /// <summary>
    /// Gets the cache manager for this state manager.
    /// </summary>
    protected IStateCacheManager<T> CacheManager { get; }

    /// <summary>
    /// Gets the metrics collector for this state manager.
    /// </summary>
    protected StateManagerMetricsCollector Metrics { get; }

    /// <summary>
    /// Gets the name of this state manager for logging and metrics.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Initializes a new instance of the OrleansStateManagerBase class.
    /// </summary>
    /// <param name="grainFactory">The Orleans grain factory</param>
    /// <param name="logger">The logger instance</param>
    /// <param name="cacheManager">The cache manager</param>
    protected OrleansStateManagerBase(
        IGrainFactory grainFactory,
        ILogger<OrleansStateManagerBase<T>> logger,
        IStateCacheManager<T> cacheManager)
    {
        GrainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        Metrics = new StateManagerMetricsCollector();
    }

    #region IStateReader<T> Implementation

    /// <summary>
    /// Gets a single entity by its identifier.
    /// </summary>
    public async Task<StateResult<T>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Getting entity {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            // Try cache first
            var cached = await CacheManager.GetFromCacheAsync(id, cancellationToken);
            if (cached != null)
            {
                Logger.LogDebug("Cache hit for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                    typeof(T).Name, id, correlationId);

                Metrics.RecordRead(stopwatch.ElapsedMilliseconds, true);
                return StateResult<T>.FromSuccess(cached);
            }

            // Get from Orleans grain
            var result = await GetFromGrainAsync(id, cancellationToken);

            if (result.Success && result.Data != null)
            {
                // Cache the result
                await CacheManager.SetCacheAsync(id, result.Data, cancellationToken: cancellationToken);
            }

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Get operation cancelled for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, false);
            return StateResult<T>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Gets multiple entities by their identifiers.
    /// </summary>
    public async Task<StateResult<IReadOnlyList<T>>> GetMultipleAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return StateResult<IReadOnlyList<T>>.FromSuccess([]);
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Getting multiple entities {EntityType} with {Count} IDs (CorrelationId: {CorrelationId})",
                typeof(T).Name, idList.Count, correlationId);

            var result = await GetMultipleFromGrainsAsync(idList, cancellationToken);

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Get multiple operation cancelled for {EntityType} (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting multiple {EntityType} entities (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, false);
            return StateResult<IReadOnlyList<T>>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Gets a paged list of entities based on the provided query.
    /// </summary>
    public async Task<StateResult<PagedResult<T>>> GetPagedAsync(StateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Getting paged entities {EntityType} (Page: {Page}, PageSize: {PageSize}, CorrelationId: {CorrelationId})",
                typeof(T).Name, query.Page, query.PageSize, correlationId);

            var result = await GetPagedFromGrainsAsync(query, cancellationToken);

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Get paged operation cancelled for {EntityType} (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting paged {EntityType} entities (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            Metrics.RecordRead(stopwatch.ElapsedMilliseconds, false);
            return StateResult<PagedResult<T>>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Checks whether an entity with the specified identifier exists.
    /// </summary>
    public async Task<StateResult<bool>> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Checking existence of {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            // Check cache first
            if (await CacheManager.ExistsInCacheAsync(id, cancellationToken))
            {
                return StateResult<bool>.FromSuccess(true);
            }

            // Check grain
            var result = await ExistsInGrainAsync(id, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Exists operation cancelled for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking existence of {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            return StateResult<bool>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Counts the total number of entities matching the specified query.
    /// </summary>
    public async Task<StateResult<long>> CountAsync(StateQuery? query = null, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Counting {EntityType} entities (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            var result = await CountInGrainsAsync(query, cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Count operation cancelled for {EntityType} (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error counting {EntityType} entities (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            return StateResult<long>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    #endregion

    #region IStateWriter<T> Implementation

    /// <summary>
    /// Creates a new entity in the state store.
    /// </summary>
    public async Task<StateResult<T>> CreateAsync(T entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Creating entity {EntityType} (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            var result = await CreateInGrainAsync(entity, cancellationToken);

            if (result.Success && result.Data != null)
            {
                // Update cache
                var entityId = GetEntityId(result.Data);
                if (!string.IsNullOrEmpty(entityId))
                {
                    await CacheManager.SetCacheAsync(entityId, result.Data, cancellationToken: cancellationToken);
                }
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Create operation cancelled for {EntityType} (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating {EntityType} entity (CorrelationId: {CorrelationId})",
                typeof(T).Name, correlationId);

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, false);
            return StateResult<T>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Updates an existing entity in the state store.
    /// </summary>
    public async Task<StateResult<T>> UpdateAsync(string id, T entity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        ArgumentNullException.ThrowIfNull(entity);

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Updating entity {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            var result = await UpdateInGrainAsync(id, entity, cancellationToken);

            if (result.Success && result.Data != null)
            {
                // Update cache
                await CacheManager.SetCacheAsync(id, result.Data, cancellationToken: cancellationToken);
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Update operation cancelled for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, false);
            return StateResult<T>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Partially updates an existing entity in the state store.
    /// </summary>
    public async Task<StateResult<T>> PatchAsync(string id, Dictionary<string, object> updates, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        ArgumentNullException.ThrowIfNull(updates);

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Patching entity {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            var result = await PatchInGrainAsync(id, updates, cancellationToken);

            if (result.Success && result.Data != null)
            {
                // Update cache
                await CacheManager.SetCacheAsync(id, result.Data, cancellationToken: cancellationToken);
            }

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Patch operation cancelled for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error patching {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            Metrics.RecordWrite(stopwatch.ElapsedMilliseconds, false);
            return StateResult<T>.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Deletes an entity from the state store.
    /// </summary>
    public async Task<StateResult> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id));
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            Logger.LogDebug("Deleting entity {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            var result = await DeleteInGrainAsync(id, cancellationToken);

            if (result.Success)
            {
                // Remove from cache
                await CacheManager.RemoveFromCacheAsync(id, cancellationToken);
            }

            Metrics.RecordDelete(stopwatch.ElapsedMilliseconds, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug("Delete operation cancelled for {EntityType} {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting {EntityType} with ID {EntityId} (CorrelationId: {CorrelationId})",
                typeof(T).Name, id, correlationId);

            Metrics.RecordDelete(stopwatch.ElapsedMilliseconds, false);
            return StateResult.FromException(ex, metadata: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    // Note: Batch operations and other IStateWriter methods follow similar patterns
    // They are abbreviated here for brevity but would follow the same structure

    public virtual Task<StateResult<IReadOnlyList<T>>> CreateMultipleAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Batch create not implemented in base class");
    }

    public virtual Task<StateResult<IReadOnlyList<T>>> UpdateMultipleAsync(Dictionary<string, T> updates, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Batch update not implemented in base class");
    }

    public virtual Task<StateResult> DeleteMultipleAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Batch delete not implemented in base class");
    }

    #endregion

    #region IStateCacheManager<T> Implementation

    public Task<T?> GetFromCacheAsync(string key, CancellationToken cancellationToken = default)
        => CacheManager.GetFromCacheAsync(key, cancellationToken);

    public Task SetCacheAsync(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => CacheManager.SetCacheAsync(key, value, expiry, cancellationToken);

    public Task RemoveFromCacheAsync(string key, CancellationToken cancellationToken = default)
        => CacheManager.RemoveFromCacheAsync(key, cancellationToken);

    public Task RemoveFromCacheAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => CacheManager.RemoveFromCacheAsync(keys, cancellationToken);

    public Task<bool> ExistsInCacheAsync(string key, CancellationToken cancellationToken = default)
        => CacheManager.ExistsInCacheAsync(key, cancellationToken);

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default)
        => CacheManager.InvalidateAllAsync(cancellationToken);

    public Task InvalidateByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        => CacheManager.InvalidateByPatternAsync(pattern, cancellationToken);

    public Task<CacheStatistics> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
        => CacheManager.GetCacheStatisticsAsync(cancellationToken);

    #endregion

    #region IStateManager<T> Implementation

    public virtual async Task<StateManagerHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check Orleans connectivity by attempting basic grain factory operations
            // For now, we'll assume Orleans is healthy if we can get this far without exceptions

            var cacheStats = await GetCacheStatisticsAsync(cancellationToken);
            var isCacheHealthy = cacheStats != null;

            return StateManagerHealthStatus.Healthy("Orleans", new Dictionary<string, object>
            {
                ["CacheHealthy"] = isCacheHealthy,
                ["CacheHitRatio"] = cacheStats?.HitRatio ?? 0
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Health check failed for Orleans state manager");
            return StateManagerHealthStatus.Unhealthy("Orleans", ex.Message);
        }
    }

    public async Task<StateManagerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var cacheStats = await GetCacheStatisticsAsync(cancellationToken);
        return Metrics.GetMetrics(cacheStats);
    }

    public virtual Task<StateResult<IReadOnlyList<object>>> ExecuteTransactionAsync(
        IEnumerable<StateOperation<T>> operations,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Transactions not implemented in base class");
    }

    public virtual Task OptimizeAsync(CancellationToken cancellationToken = default)
    {
        // Base implementation does nothing
        return Task.CompletedTask;
    }

    #endregion

    #region Validation Implementation

    /// <inheritdoc />
    public virtual IStateValidator<T> Validator => GetValidator();

    /// <inheritdoc />
    public virtual IStateConsistencyChecker<T> ConsistencyChecker => GetConsistencyChecker();

    /// <inheritdoc />
    public virtual IStateErrorRecovery<T> ErrorRecovery => GetErrorRecovery();

    /// <inheritdoc />
    public virtual async Task<StateValidationResult> ValidateAsync(StateOperation<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            Logger.LogDebug("Validating operation {OperationType} for entity type {EntityType}",
                operation.Type, typeof(T).Name);

            return operation.Type switch
            {
                StateOperationType.Create => operation.Entity != null
                    ? await Validator.ValidateCreateAsync(operation.Entity, cancellationToken)
                    : StateValidationResult.Failed("Entity", "Entity is required for create operation", ValidationErrorCode.Required),

                StateOperationType.Update => operation.EntityId != null && operation.Entity != null
                    ? await Validator.ValidateUpdateAsync(operation.EntityId, operation.Entity, cancellationToken: cancellationToken)
                    : StateValidationResult.Failed("Entity", "EntityId and Entity are required for update operation", ValidationErrorCode.Required),

                StateOperationType.Delete => operation.EntityId != null
                    ? await Validator.ValidateDeleteAsync(operation.EntityId, cancellationToken: cancellationToken)
                    : StateValidationResult.Failed("EntityId", "EntityId is required for delete operation", ValidationErrorCode.Required),

                StateOperationType.Patch => operation.EntityId != null && operation.Updates != null
                    ? await Validator.ValidatePatchAsync(operation.EntityId, operation.Updates, cancellationToken: cancellationToken)
                    : StateValidationResult.Failed("EntityId", "EntityId and Updates are required for patch operation", ValidationErrorCode.Required),

                _ => StateValidationResult.Failed("OperationType", $"Unknown operation type: {operation.Type}", ValidationErrorCode.Custom)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during validation for {EntityType} operation {OperationType}",
                typeof(T).Name, operation.Type);
            return StateValidationResult.Failed("Entity", "Internal validation error", ValidationErrorCode.Custom);
        }
    }

    /// <inheritdoc />
    public virtual async Task<ConsistencyCheckResult> CheckConsistencyAsync(string entityId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityId);

        try
        {
            Logger.LogDebug("Checking consistency for entity {EntityId} of type {EntityType}",
                entityId, typeof(T).Name);

            return await ConsistencyChecker.CheckConsistencyAsync(entityId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during consistency check for {EntityType} entity {EntityId}",
                typeof(T).Name, entityId);

            var issues = new[]
            {
                ConsistencyIssue.Create(entityId, "ConsistencyCheck", ConsistencyIssueType.StructuralMismatch,
                    description: $"Consistency check failed: {ex.Message}",
                    severity: ConsistencyIssueSeverity.Error)
            };

            return ConsistencyCheckResult.Inconsistent(issues, 1, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Gets the validator instance for this state manager.
    /// Override this method to provide a custom validator.
    /// </summary>
    /// <returns>The validator instance</returns>
    protected virtual IStateValidator<T> GetValidator()
    {
        // Return a default no-op validator
        return new NullStateValidator<T>();
    }

    /// <summary>
    /// Gets the consistency checker instance for this state manager.
    /// Override this method to provide a custom consistency checker.
    /// </summary>
    /// <returns>The consistency checker instance</returns>
    protected virtual IStateConsistencyChecker<T> GetConsistencyChecker()
    {
        // Return a default no-op consistency checker
        return new NullStateConsistencyChecker<T>();
    }

    /// <summary>
    /// Gets the error recovery handler instance for this state manager.
    /// Override this method to provide a custom error recovery handler.
    /// </summary>
    /// <returns>The error recovery handler instance</returns>
    protected virtual IStateErrorRecovery<T> GetErrorRecovery()
    {
        // Return a default no-op error recovery handler
        return new NullStateErrorRecovery<T>();
    }

    #endregion

    #region Protected Abstract Methods - To be implemented by derived classes

    /// <summary>
    /// Gets an entity from the appropriate Orleans grain.
    /// </summary>
    protected abstract Task<StateResult<T>> GetFromGrainAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets multiple entities from Orleans grains.
    /// </summary>
    protected abstract Task<StateResult<IReadOnlyList<T>>> GetMultipleFromGrainsAsync(IList<string> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a paged list of entities from Orleans grains.
    /// </summary>
    protected abstract Task<StateResult<PagedResult<T>>> GetPagedFromGrainsAsync(StateQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an entity exists in Orleans grains.
    /// </summary>
    protected abstract Task<StateResult<bool>> ExistsInGrainAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Counts entities in Orleans grains.
    /// </summary>
    protected abstract Task<StateResult<long>> CountInGrainsAsync(StateQuery? query, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an entity in the appropriate Orleans grain.
    /// </summary>
    protected abstract Task<StateResult<T>> CreateInGrainAsync(T entity, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an entity in the appropriate Orleans grain.
    /// </summary>
    protected abstract Task<StateResult<T>> UpdateInGrainAsync(string id, T entity, CancellationToken cancellationToken);

    /// <summary>
    /// Patches an entity in the appropriate Orleans grain.
    /// </summary>
    protected abstract Task<StateResult<T>> PatchInGrainAsync(string id, Dictionary<string, object> updates, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an entity from the appropriate Orleans grain.
    /// </summary>
    protected abstract Task<StateResult> DeleteInGrainAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the entity ID from an entity instance.
    /// </summary>
    protected abstract string? GetEntityId(T entity);

    #endregion
}