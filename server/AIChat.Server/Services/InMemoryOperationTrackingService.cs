using System.Collections.Concurrent;

namespace AIChat.Server.Services;

/// <summary>
/// In-memory implementation of IOperationTrackingService.
/// This is suitable for single-instance deployments. For distributed scenarios,
/// consider implementing a Redis-based or database-backed version.
/// </summary>
public class InMemoryOperationTrackingService : IOperationTrackingService
{
    private readonly ConcurrentDictionary<string, OperationUserContext> _operations = new();
    private readonly ILogger<InMemoryOperationTrackingService> _logger;

    /// <summary>
    /// Initializes a new instance of the InMemoryOperationTrackingService.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public InMemoryOperationTrackingService(ILogger<InMemoryOperationTrackingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task RegisterOperationAsync(
        string operationId,
        string userId,
        string chatId,
        string operationType
    )
    {
        try
        {
            var context = new OperationUserContext
            {
                OperationId = operationId,
                UserId = userId,
                ChatId = chatId,
                OperationType = operationType,
                RegisteredAt = DateTime.UtcNow,
            };

            _operations[operationId] = context;

            _logger.LogDebug(
                "Registered operation {OperationId} for user {UserId} in chat {ChatId} of type {OperationType}",
                operationId,
                userId,
                chatId,
                operationType
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to register operation {OperationId} for user {UserId}",
                operationId,
                userId
            );
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OperationUserContext?> GetOperationUserContextAsync(string operationId)
    {
        try
        {
            _ = _operations.TryGetValue(operationId, out var context);

            if (context != null)
            {
                _logger.LogDebug(
                    "Found operation context for {OperationId}: user {UserId}, chat {ChatId}",
                    operationId,
                    context.UserId,
                    context.ChatId
                );
            }
            else
            {
                _logger.LogDebug("No operation context found for {OperationId}", operationId);
            }

            return Task.FromResult(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get operation context for {OperationId}", operationId);
            return Task.FromResult<OperationUserContext?>(null);
        }
    }

    /// <inheritdoc />
    public Task UnregisterOperationAsync(string operationId)
    {
        try
        {
            if (_operations.TryRemove(operationId, out var context))
            {
                _logger.LogDebug(
                    "Unregistered operation {OperationId} for user {UserId}",
                    operationId,
                    context.UserId
                );
            }
            else
            {
                _logger.LogDebug(
                    "Operation {OperationId} was not registered for unregistration",
                    operationId
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister operation {OperationId}", operationId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<OperationUserContext>> GetUserOperationsAsync(string userId)
    {
        try
        {
            var userOperations = _operations
                .Values.Where(op => op.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug(
                "Found {Count} operations for user {UserId}",
                userOperations.Count,
                userId
            );

            return Task.FromResult<IEnumerable<OperationUserContext>>(userOperations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get operations for user {UserId}", userId);
            return Task.FromResult(Enumerable.Empty<OperationUserContext>());
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<OperationUserContext>> GetChatOperationsAsync(string chatId)
    {
        try
        {
            var chatOperations = _operations
                .Values.Where(op => op.ChatId.Equals(chatId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogDebug(
                "Found {Count} operations for chat {ChatId}",
                chatOperations.Count,
                chatId
            );

            return Task.FromResult<IEnumerable<OperationUserContext>>(chatOperations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get operations for chat {ChatId}", chatId);
            return Task.FromResult(Enumerable.Empty<OperationUserContext>());
        }
    }
}
