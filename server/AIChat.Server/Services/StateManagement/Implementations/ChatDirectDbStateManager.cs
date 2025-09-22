using AIChat.Server.Models;
using AIChat.Server.Services.StateManagement.Validation;
using AIChat.Server.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.StateManagement.Implementations;

/// <summary>
/// Direct database state manager for Chat entities.
/// Uses IChatStorage for database operations and provides caching.
/// This is a concrete implementation demonstrating the DirectDbStateManagerBase usage.
/// </summary>
public class ChatDirectDbStateManager : DirectDbStateManagerBase<Chat>
{
    private readonly IChatStorage _chatStorage;

    /// <summary>
    /// Gets the name of this state manager.
    /// </summary>
    public override string Name => "ChatDirectDbStateManager";

    /// <summary>
    /// Initializes a new instance of the ChatDirectDbStateManager class.
    /// </summary>
    /// <param name="chatStorage">The chat storage interface</param>
    /// <param name="logger">The logger instance</param>
    /// <param name="cacheManager">The cache manager</param>
    public ChatDirectDbStateManager(
        IChatStorage chatStorage,
        ILogger<ChatDirectDbStateManager> logger,
        IStateCacheManager<Chat> cacheManager)
        : base(logger, cacheManager)
    {
        _chatStorage = chatStorage ?? throw new ArgumentNullException(nameof(chatStorage));
    }

    #region Protected Implementations

    /// <summary>
    /// Gets a chat entity from the database using IChatStorage.
    /// </summary>
    protected override async Task<StateResult<Chat>> GetFromDatabaseAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var (success, error, chatRecord) = await _chatStorage.GetChatByIdAsync(id, cancellationToken);

            if (!success)
            {
                var errorCode = error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                    ? StateErrorCode.NotFound
                    : StateErrorCode.InternalError;

                return StateResult<Chat>.FromError(error ?? "Unknown error occurred", errorCode);
            }

            if (chatRecord == null)
            {
                return StateResult<Chat>.FromError("Chat not found", StateErrorCode.NotFound);
            }

            // Convert ChatRecord to Chat model
            var chat = ConvertFromChatRecord(chatRecord);
            return StateResult<Chat>.FromSuccess(chat);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting chat {ChatId} from database", id);
            return StateResult<Chat>.FromException(ex);
        }
    }

    /// <summary>
    /// Gets multiple chat entities from the database.
    /// </summary>
    protected override async Task<StateResult<IReadOnlyList<Chat>>> GetMultipleFromDatabaseAsync(IList<string> ids, CancellationToken cancellationToken)
    {
        try
        {
            var chats = new List<Chat>();

            foreach (var id in ids)
            {
                var result = await GetFromDatabaseAsync(id, cancellationToken);
                if (result.Success && result.Data != null)
                {
                    chats.Add(result.Data);
                }
                // Note: We continue with other IDs even if one fails
            }

            return StateResult<IReadOnlyList<Chat>>.FromSuccess(chats);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting multiple chats from database");
            return StateResult<IReadOnlyList<Chat>>.FromException(ex);
        }
    }

    /// <summary>
    /// Gets a paged list of chat entities from the database.
    /// </summary>
    protected override async Task<StateResult<PagedResult<Chat>>> GetPagedFromDatabaseAsync(StateQuery query, CancellationToken cancellationToken)
    {
        try
        {
            // Extract user ID from filters if provided
            string? userId = null;
            if (query.Filters?.ContainsKey("userId") == true)
            {
                userId = query.Filters["userId"]?.ToString();
            }

            if (string.IsNullOrEmpty(userId))
            {
                return StateResult<PagedResult<Chat>>.FromError(
                    "UserId filter is required for chat queries",
                    StateErrorCode.ValidationError);
            }

            var (success, error, chatRecords, totalCount) = await _chatStorage.GetChatHistoryByUserAsync(
                userId, query.Page, query.PageSize, cancellationToken);

            if (!success)
            {
                return StateResult<PagedResult<Chat>>.FromError(error ?? "Unknown error occurred", StateErrorCode.InternalError);
            }

            var chats = chatRecords.Select(ConvertFromChatRecord).ToList();
            var pagedResult = PagedResult<Chat>.FromQuery(chats, totalCount, query);

            return StateResult<PagedResult<Chat>>.FromSuccess(pagedResult);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting paged chats from database");
            return StateResult<PagedResult<Chat>>.FromException(ex);
        }
    }

    /// <summary>
    /// Checks if a chat entity exists in the database.
    /// </summary>
    protected override async Task<StateResult<bool>> ExistsInDatabaseAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetFromDatabaseAsync(id, cancellationToken);
            return StateResult<bool>.FromSuccess(result.Success && result.Data != null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error checking chat existence in database for ID {ChatId}", id);
            return StateResult<bool>.FromException(ex);
        }
    }

    /// <summary>
    /// Counts chat entities in the database.
    /// </summary>
    protected override async Task<StateResult<long>> CountInDatabaseAsync(StateQuery? query, CancellationToken cancellationToken)
    {
        try
        {
            // For now, we can't easily count all chats without a user context
            // This would require a new method in IChatStorage
            // For demonstration, we'll return a simple implementation

            if (query?.Filters?.ContainsKey("userId") == true)
            {
                var userId = query.Filters["userId"]?.ToString();
                if (!string.IsNullOrEmpty(userId))
                {
                    // Get a large page to count
                    var (success, error, _, totalCount) = await _chatStorage.GetChatHistoryByUserAsync(
                        userId, 1, 1, cancellationToken);

                    if (success)
                    {
                        return StateResult<long>.FromSuccess(totalCount);
                    }

                    return StateResult<long>.FromError(error ?? "Unknown error occurred", StateErrorCode.InternalError);
                }
            }

            return StateResult<long>.FromError("UserId filter is required for chat count", StateErrorCode.ValidationError);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error counting chats in database");
            return StateResult<long>.FromException(ex);
        }
    }

    /// <summary>
    /// Creates a chat entity in the database.
    /// </summary>
    protected override async Task<StateResult<Chat>> CreateInDatabaseAsync(Chat entity, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(entity);

            var (success, error, chatRecord) = await _chatStorage.CreateChatAsync(
                entity.UserId,
                entity.Title,
                entity.CreatedAt,
                entity.UpdatedAt,
                null, // ChatJson - could be serialized entity if needed
                cancellationToken);

            if (!success)
            {
                var errorCode = error?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
                    ? StateErrorCode.DuplicateKey
                    : StateErrorCode.InternalError;

                return StateResult<Chat>.FromError(error ?? "Unknown error occurred", errorCode);
            }

            if (chatRecord == null)
            {
                return StateResult<Chat>.FromError("Created chat record is null", StateErrorCode.InternalError);
            }

            var createdChat = ConvertFromChatRecord(chatRecord);
            return StateResult<Chat>.FromSuccess(createdChat);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating chat in database");
            return StateResult<Chat>.FromException(ex);
        }
    }

    /// <summary>
    /// Updates a chat entity in the database.
    /// </summary>
    protected override async Task<StateResult<Chat>> UpdateInDatabaseAsync(string id, Chat entity, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(entity);

            // First, check if the chat exists
            var existsResult = await ExistsInDatabaseAsync(id, cancellationToken);
            if (!existsResult.Success || !existsResult.Data)
            {
                return StateResult<Chat>.FromError("Chat not found", StateErrorCode.NotFound);
            }

            // Update the UpdatedAt timestamp
            var (success, error) = await _chatStorage.UpdateChatUpdatedAtAsync(id, DateTime.UtcNow, cancellationToken);

            if (!success)
            {
                return StateResult<Chat>.FromError(error ?? "Unknown error occurred", StateErrorCode.InternalError);
            }

            // Return the updated entity (with new timestamp)
            entity.Id = id;
            entity.UpdatedAt = DateTime.UtcNow;
            var updatedEntity = entity;
            return StateResult<Chat>.FromSuccess(updatedEntity);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating chat {ChatId} in database", id);
            return StateResult<Chat>.FromException(ex);
        }
    }

    /// <summary>
    /// Patches a chat entity in the database.
    /// </summary>
    protected override async Task<StateResult<Chat>> PatchInDatabaseAsync(string id, Dictionary<string, object> updates, CancellationToken cancellationToken)
    {
        try
        {
            // Get current entity
            var currentResult = await GetFromDatabaseAsync(id, cancellationToken);
            if (!currentResult.Success || currentResult.Data == null)
            {
                return StateResult<Chat>.FromError("Chat not found", StateErrorCode.NotFound);
            }

            var current = currentResult.Data;

            // Apply updates (simplified - in a real implementation you'd use reflection or a mapper)
            var updated = current;
            foreach (var update in updates)
            {
                switch (update.Key.ToLowerInvariant())
                {
                    case "title":
                        updated.Title = update.Value?.ToString() ?? updated.Title;
                        break;
                    case "updatedat":
                        if (DateTime.TryParse(update.Value?.ToString(), out var updatedAt))
                        {
                            updated.UpdatedAt = updatedAt;
                        }
                        break;
                        // Add more properties as needed
                }
            }

            // Update in database
            return await UpdateInDatabaseAsync(id, updated, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error patching chat {ChatId} in database", id);
            return StateResult<Chat>.FromException(ex);
        }
    }

    /// <summary>
    /// Deletes a chat entity from the database.
    /// </summary>
    protected override async Task<StateResult> DeleteInDatabaseAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var (success, error) = await _chatStorage.DeleteChatAsync(id, cancellationToken);

            if (!success)
            {
                var errorCode = error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                    ? StateErrorCode.NotFound
                    : StateErrorCode.InternalError;

                return StateResult.FromError(error ?? "Unknown error occurred", errorCode);
            }

            return StateResult.FromSuccess();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting chat {ChatId} from database", id);
            return StateResult.FromException(ex);
        }
    }

    /// <summary>
    /// Gets the entity ID from a chat entity.
    /// </summary>
    protected override string? GetEntityId(Chat entity)
    {
        return entity?.Id;
    }

    /// <summary>
    /// Performs a basic health check on the database connection.
    /// </summary>
    protected override async Task CheckDatabaseHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Try a simple operation to verify database connectivity
            // We'll try to get a non-existent chat - this should fail gracefully
            _ = await _chatStorage.GetChatByIdAsync("health-check-non-existent", cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Database health check failed");
            throw;
        }
    }

    #endregion

    #region Validation Implementation

    /// <summary>
    /// Gets the validator instance for Chat entities.
    /// Returns a concrete ChatValidator that performs real validation logic.
    /// </summary>
    /// <returns>The ChatValidator instance</returns>
    protected override IStateValidator<Chat> GetValidator()
    {
        // For demonstration purposes, create a simple validator instance
        // In a real implementation, you would resolve these dependencies from DI container
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ChatValidator>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        return new ChatValidator(logger, serviceProvider);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Converts a ChatRecord to a Chat model.
    /// </summary>
    private static Chat ConvertFromChatRecord(ChatRecord record)
    {
        return new Chat
        {
            Id = record.Id,
            UserId = record.UserId,
            Title = record.Title,
            CreatedAt = record.CreatedAtUtc,
            UpdatedAt = record.UpdatedAtUtc
        };
    }

    #endregion
}