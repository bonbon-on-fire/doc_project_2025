namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Aggregate interface for complete state management operations.
/// Combines read, write, and cache management capabilities following the Interface Segregation Principle.
/// This is the main interface that most consumers will use for full state management functionality.
/// </summary>
/// <typeparam name="T">The type of entity being managed</typeparam>
public interface IStateManager<T> : IStateReader<T>, IStateWriter<T>, IStateCacheManager<T>
    where T : class
{
    /// <summary>
    /// Gets the name of the state manager for logging and metrics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the state manager is currently healthy and available.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateManagerHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets performance and usage metrics for the state manager.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current state manager metrics</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateManagerMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a transactional operation on multiple entities.
    /// All operations succeed or all fail atomically.
    /// </summary>
    /// <param name="operations">The collection of operations to perform transactionally</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The results of all operations if successful</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations is null</exception>
    /// <exception cref="StateManagementException">Thrown when any operation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<IReadOnlyList<object>>> ExecuteTransactionAsync(
        IEnumerable<StateOperation<T>> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optimizes the state manager for better performance.
    /// This may include cache warming, index optimization, etc.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the optimization operation</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task OptimizeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the health status of a state manager.
/// </summary>
public record StateManagerHealthStatus
{
    /// <summary>
    /// Gets whether the state manager is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether the underlying storage is healthy.
    /// </summary>
    public required bool IsStorageHealthy { get; init; }

    /// <summary>
    /// Gets whether the cache layer is healthy.
    /// </summary>
    public required bool IsCacheHealthy { get; init; }

    /// <summary>
    /// Gets the current state manager mode (Orleans, DirectDB, etc.).
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Gets any error messages or status details.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets additional health check details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a healthy status.
    /// </summary>
    /// <param name="mode">The current mode of operation</param>
    /// <param name="details">Optional additional details</param>
    /// <returns>A healthy state manager status</returns>
    public static StateManagerHealthStatus Healthy(string mode, Dictionary<string, object>? details = null)
    {
        return new StateManagerHealthStatus
        {
            IsHealthy = true,
            IsStorageHealthy = true,
            IsCacheHealthy = true,
            Mode = mode,
            Details = details
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="mode">The current mode of operation</param>
    /// <param name="message">The error message</param>
    /// <param name="isStorageHealthy">Whether storage is healthy</param>
    /// <param name="isCacheHealthy">Whether cache is healthy</param>
    /// <param name="details">Optional additional details</param>
    /// <returns>An unhealthy state manager status</returns>
    public static StateManagerHealthStatus Unhealthy(
        string mode,
        string message,
        bool isStorageHealthy = false,
        bool isCacheHealthy = false,
        Dictionary<string, object>? details = null)
    {
        return new StateManagerHealthStatus
        {
            IsHealthy = false,
            IsStorageHealthy = isStorageHealthy,
            IsCacheHealthy = isCacheHealthy,
            Mode = mode,
            Message = message,
            Details = details
        };
    }
}

/// <summary>
/// Represents performance and usage metrics for a state manager.
/// </summary>
public record StateManagerMetrics
{
    /// <summary>
    /// Gets the total number of read operations.
    /// </summary>
    public long ReadOperations { get; init; }

    /// <summary>
    /// Gets the total number of write operations.
    /// </summary>
    public long WriteOperations { get; init; }

    /// <summary>
    /// Gets the total number of delete operations.
    /// </summary>
    public long DeleteOperations { get; init; }

    /// <summary>
    /// Gets the total number of failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Gets the average execution time for read operations in milliseconds.
    /// </summary>
    public double AverageReadTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for write operations in milliseconds.
    /// </summary>
    public double AverageWriteTimeMs { get; init; }

    /// <summary>
    /// Gets the cache statistics.
    /// </summary>
    public CacheStatistics? CacheStatistics { get; init; }

    /// <summary>
    /// Gets the timestamp when metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets additional provider-specific metrics.
    /// </summary>
    public Dictionary<string, object>? AdditionalMetrics { get; init; }

    /// <summary>
    /// Gets the total number of operations.
    /// </summary>
    public long TotalOperations => ReadOperations + WriteOperations + DeleteOperations;

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalOperations > 0 ? (double)(TotalOperations - FailedOperations) / TotalOperations * 100 : 100;

    /// <summary>
    /// Creates empty metrics.
    /// </summary>
    /// <returns>Empty state manager metrics</returns>
    public static StateManagerMetrics Empty()
    {
        return new StateManagerMetrics
        {
            ReadOperations = 0,
            WriteOperations = 0,
            DeleteOperations = 0,
            FailedOperations = 0,
            AverageReadTimeMs = 0,
            AverageWriteTimeMs = 0,
            CacheStatistics = CacheStatistics.Empty()
        };
    }
}

/// <summary>
/// Represents a state operation for transactional execution.
/// </summary>
/// <typeparam name="T">The type of entity being operated on</typeparam>
public record StateOperation<T> where T : class
{
    /// <summary>
    /// Gets the type of operation.
    /// </summary>
    public required StateOperationType Type { get; init; }

    /// <summary>
    /// Gets the entity ID for the operation.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Gets the entity data for create/update operations.
    /// </summary>
    public T? Entity { get; init; }

    /// <summary>
    /// Gets partial update data for patch operations.
    /// </summary>
    public Dictionary<string, object>? Updates { get; init; }

    /// <summary>
    /// Gets additional operation metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a create operation.
    /// </summary>
    /// <param name="entity">The entity to create</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A create operation</returns>
    public static StateOperation<T> Create(T entity, Dictionary<string, object>? metadata = null)
    {
        return new StateOperation<T>
        {
            Type = StateOperationType.Create,
            Entity = entity,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates an update operation.
    /// </summary>
    /// <param name="entityId">The ID of the entity to update</param>
    /// <param name="entity">The updated entity</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>An update operation</returns>
    public static StateOperation<T> Update(string entityId, T entity, Dictionary<string, object>? metadata = null)
    {
        return new StateOperation<T>
        {
            Type = StateOperationType.Update,
            EntityId = entityId,
            Entity = entity,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a patch operation.
    /// </summary>
    /// <param name="entityId">The ID of the entity to patch</param>
    /// <param name="updates">The partial updates to apply</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A patch operation</returns>
    public static StateOperation<T> Patch(string entityId, Dictionary<string, object> updates, Dictionary<string, object>? metadata = null)
    {
        return new StateOperation<T>
        {
            Type = StateOperationType.Patch,
            EntityId = entityId,
            Updates = updates,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a delete operation.
    /// </summary>
    /// <param name="entityId">The ID of the entity to delete</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A delete operation</returns>
    public static StateOperation<T> Delete(string entityId, Dictionary<string, object>? metadata = null)
    {
        return new StateOperation<T>
        {
            Type = StateOperationType.Delete,
            EntityId = entityId,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the type of state operation.
/// </summary>
public enum StateOperationType
{
    /// <summary>
    /// Create a new entity.
    /// </summary>
    Create,

    /// <summary>
    /// Update an existing entity.
    /// </summary>
    Update,

    /// <summary>
    /// Partially update an existing entity.
    /// </summary>
    Patch,

    /// <summary>
    /// Delete an existing entity.
    /// </summary>
    Delete
}