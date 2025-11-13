namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Interface for write-only state management operations.
/// Follows the Interface Segregation Principle by providing only write operations.
/// </summary>
/// <typeparam name="T">The type of entity being managed</typeparam>
public interface IStateWriter<T> where T : class
{
    /// <summary>
    /// Creates a new entity in the state store.
    /// </summary>
    /// <param name="entity">The entity to create</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created entity with any generated or updated properties</returns>
    /// <exception cref="ArgumentNullException">Thrown when entity is null</exception>
    /// <exception cref="StateManagementException">Thrown when entity validation fails or duplicate key exists</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<T>> CreateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing entity in the state store.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to update</param>
    /// <param name="entity">The updated entity data</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The updated entity</returns>
    /// <exception cref="ArgumentNullException">Thrown when id is null or empty, or entity is null</exception>
    /// <exception cref="StateManagementException">Thrown when entity is not found or validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<T>> UpdateAsync(string id, T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Partially updates an existing entity in the state store.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to update</param>
    /// <param name="updates">Dictionary of property names and values to update</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The updated entity</returns>
    /// <exception cref="ArgumentNullException">Thrown when id is null or empty, or updates is null</exception>
    /// <exception cref="StateManagementException">Thrown when entity is not found or validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<T>> PatchAsync(string id, Dictionary<string, object> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity from the state store.
    /// </summary>
    /// <param name="id">The unique identifier of the entity to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A result indicating success or failure</returns>
    /// <exception cref="ArgumentNullException">Thrown when id is null or empty</exception>
    /// <exception cref="StateManagementException">Thrown when entity is not found</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple entities in the state store as a batch operation.
    /// </summary>
    /// <param name="entities">The collection of entities to create</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The created entities with any generated or updated properties</returns>
    /// <exception cref="ArgumentNullException">Thrown when entities is null</exception>
    /// <exception cref="StateManagementException">Thrown when entity validation fails or duplicate keys exist</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<IReadOnlyList<T>>> CreateMultipleAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple entities in the state store as a batch operation.
    /// </summary>
    /// <param name="updates">Dictionary mapping entity IDs to updated entity data</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The updated entities</returns>
    /// <exception cref="ArgumentNullException">Thrown when updates is null</exception>
    /// <exception cref="StateManagementException">Thrown when entities are not found or validation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<IReadOnlyList<T>>> UpdateMultipleAsync(Dictionary<string, T> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple entities from the state store as a batch operation.
    /// </summary>
    /// <param name="ids">The collection of unique identifiers of entities to delete</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A result indicating success or failure</returns>
    /// <exception cref="ArgumentNullException">Thrown when ids is null</exception>
    /// <exception cref="StateManagementException">Thrown when entities are not found</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult> DeleteMultipleAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
}
