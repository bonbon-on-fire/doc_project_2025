namespace AIChat.Server.Services;

/// <summary>
/// Service for tracking operation context to enable Orleans grain operation management.
/// Maps operation IDs to user context for cancellation and status queries.
/// </summary>
public interface IOperationTrackingService
{
    /// <summary>
    /// Registers an operation with user context.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="userId">The user ID associated with this operation</param>
    /// <param name="chatId">The chat ID associated with this operation</param>
    /// <param name="operationType">The type of operation (e.g., "SendMessage")</param>
    Task RegisterOperationAsync(string operationId, string userId, string chatId, string operationType);

    /// <summary>
    /// Gets user context for an operation.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <returns>User context if found, null otherwise</returns>
    Task<OperationUserContext?> GetOperationUserContextAsync(string operationId);

    /// <summary>
    /// Unregisters an operation (cleanup after completion/cancellation).
    /// </summary>
    /// <param name="operationId">The operation ID to unregister</param>
    Task UnregisterOperationAsync(string operationId);

    /// <summary>
    /// Gets all operations for a user.
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>List of operation contexts for the user</returns>
    Task<IEnumerable<OperationUserContext>> GetUserOperationsAsync(string userId);

    /// <summary>
    /// Gets all operations for a chat.
    /// </summary>
    /// <param name="chatId">The chat ID</param>
    /// <returns>List of operation contexts for the chat</returns>
    Task<IEnumerable<OperationUserContext>> GetChatOperationsAsync(string chatId);
}

/// <summary>
/// Context information for an operation including user details.
/// </summary>
public class OperationUserContext
{
    /// <summary>
    /// The operation ID.
    /// </summary>
    public required string OperationId { get; set; }

    /// <summary>
    /// The user ID associated with this operation.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// The chat ID associated with this operation.
    /// </summary>
    public required string ChatId { get; set; }

    /// <summary>
    /// The type of operation.
    /// </summary>
    public required string OperationType { get; set; }

    /// <summary>
    /// When this operation was registered.
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
