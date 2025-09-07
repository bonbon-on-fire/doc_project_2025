namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for managing buffered streaming data.
/// Provides thread-safe buffer operations with capacity management.
/// </summary>
/// <typeparam name="T">The type of data being buffered</typeparam>
public interface IBufferManager<T> : IAsyncDisposable
{
    /// <summary>
    /// Attempts to write an item to the buffer without blocking.
    /// </summary>
    /// <param name="item">The item to write to the buffer</param>
    /// <returns>True if the item was written, false if the buffer is full</returns>
    ValueTask<bool> TryWriteAsync(T item);

    /// <summary>
    /// Writes an item to the buffer, waiting if necessary.
    /// </summary>
    /// <param name="item">The item to write to the buffer</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async write operation</returns>
    ValueTask WriteAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all items from the buffer as they become available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>An async enumerable of buffered items</returns>
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the buffer as complete for writing.
    /// </summary>
    void Complete();

    /// <summary>
    /// Gets the current number of items in the buffer.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the maximum capacity of the buffer.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current buffer utilization as a percentage (0-100).
    /// </summary>
    float UtilizationPercentage { get; }

    /// <summary>
    /// Gets whether the buffer has been marked as complete for writing.
    /// </summary>
    bool IsCompleted { get; }
}