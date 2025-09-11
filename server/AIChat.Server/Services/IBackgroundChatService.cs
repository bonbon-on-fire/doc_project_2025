using AIChat.Server.Models;

namespace AIChat.Server.Services;

/// <summary>
/// Interface for the background chat service that processes chat operations asynchronously.
///
/// This service manages a queue of chat operations and processes them in the background
/// using a worker pool with concurrency limits. It integrates with the existing ChatService
/// to leverage all LLM processing, tool integration, and agentic loop functionality.
/// </summary>
public interface IBackgroundChatService
{
    /// <summary>
    /// Enqueue a chat operation for background processing.
    /// </summary>
    /// <param name="operation">The chat operation to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The unique operation ID for tracking</returns>
    Task<string> EnqueueOperationAsync(
        ChatOperation operation,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Cancel a queued or in-progress operation.
    /// </summary>
    /// <param name="operationId">The operation ID to cancel</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the operation was successfully cancelled</returns>
    Task<bool> CancelOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get the current status of an operation.
    /// </summary>
    /// <param name="operationId">The operation ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current operation status information, or null if not found</returns>
    Task<OperationStatusInfo?> GetOperationStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get the current queue statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue statistics including pending count, active workers, etc.</returns>
    Task<QueueStatistics> GetQueueStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active operations for a specific user.
    /// </summary>
    /// <param name="userId">The user ID to filter by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active operations for the user</returns>
    Task<IEnumerable<OperationStatusInfo>> GetUserOperationsAsync(
        string userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get all active operations for a specific chat.
    /// </summary>
    /// <param name="chatId">The chat ID to filter by</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active operations for the chat</returns>
    Task<IEnumerable<OperationStatusInfo>> GetChatOperationsAsync(
        string chatId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Statistics about the background chat service queue.
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Number of operations currently waiting in the queue.
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// Number of operations currently being processed.
    /// </summary>
    public int ActiveCount { get; set; }

    /// <summary>
    /// Maximum number of concurrent workers.
    /// </summary>
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// Number of operations completed in the last hour.
    /// </summary>
    public int CompletedLastHour { get; set; }

    /// <summary>
    /// Number of operations that failed in the last hour.
    /// </summary>
    public int FailedLastHour { get; set; }

    /// <summary>
    /// Average processing time in milliseconds.
    /// </summary>
    public double AverageProcessingTimeMs { get; set; }

    /// <summary>
    /// Timestamp when these statistics were collected.
    /// </summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
