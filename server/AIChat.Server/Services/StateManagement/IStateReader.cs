namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Interface for read-only state management operations.
/// Follows the Interface Segregation Principle by providing only read operations.
/// </summary>
/// <typeparam name="T">The type of entity being managed</typeparam>
public interface IStateReader<T> where T : class
{
    /// <summary>
    /// Gets a single entity by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the entity</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The entity if found, or a failure result if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when id is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<T>> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple entities by their identifiers.
    /// </summary>
    /// <param name="ids">The collection of unique identifiers</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A collection of found entities (may be less than requested if some are not found)</returns>
    /// <exception cref="ArgumentNullException">Thrown when ids is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<IReadOnlyList<T>>> GetMultipleAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paged list of entities based on the provided query.
    /// </summary>
    /// <param name="query">The query parameters including filters, sorting, and paging</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A paged result containing matching entities</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<PagedResult<T>>> GetPagedAsync(StateQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an entity with the specified identifier exists.
    /// </summary>
    /// <param name="id">The unique identifier of the entity</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the entity exists, false otherwise</returns>
    /// <exception cref="ArgumentNullException">Thrown when id is null or empty</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<bool>> ExistsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the total number of entities matching the specified query.
    /// </summary>
    /// <param name="query">The query parameters for filtering (paging parameters are ignored)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The total count of matching entities</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<StateResult<long>> CountAsync(StateQuery? query = null, CancellationToken cancellationToken = default);
}
