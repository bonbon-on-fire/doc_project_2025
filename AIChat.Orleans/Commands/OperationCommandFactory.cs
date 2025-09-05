using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Factory for creating operation commands.
/// Provides a centralized way to create commands with proper validation and logging.
/// Implements the Factory Pattern to encapsulate command creation logic.
/// </summary>
public interface IOperationCommandFactory
{
    /// <summary>
    /// Creates a command for processing a message with background service integration.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="message">The message to process</param>
    /// <returns>Process message command</returns>
    ProcessMessageCommand CreateProcessMessageCommand(string operationId, string chatId, string userId, ChatMessage message);

    /// <summary>
    /// Creates a command for relaying a message to subscribed connections.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="message">The message to relay</param>
    /// <returns>Relay message command</returns>
    RelayMessageCommand CreateRelayMessageCommand(string operationId, string chatId, string userId, ChatMessage message);

    /// <summary>
    /// Creates a command for processing and relaying a stream chunk.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="chunk">The stream chunk to process</param>
    /// <returns>Process stream chunk command</returns>
    ProcessStreamChunkCommand CreateProcessStreamChunkCommand(string operationId, string chatId, string userId, StreamChunk chunk);

    /// <summary>
    /// Creates a command for cancelling an active operation.
    /// </summary>
    /// <param name="operationId">The operation ID (for command tracking)</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="targetOperationId">The operation ID to cancel</param>
    /// <returns>Cancel operation command</returns>
    CancelOperationCommand CreateCancelOperationCommand(string operationId, string chatId, string userId, string targetOperationId);

    /// <summary>
    /// Creates a command based on operation type and parameters.
    /// </summary>
    /// <param name="operationType">The type of operation to create</param>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="parameters">Operation-specific parameters</param>
    /// <returns>Operation command or null if type is not supported</returns>
    IOperationCommand? CreateCommand(OperationType operationType, string operationId, string chatId, string userId, object parameters);

    /// <summary>
    /// Validates command creation parameters.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <returns>Validation result</returns>
    CommandValidationResult ValidateCreationParameters(string operationId, string chatId, string userId);
}

/// <summary>
/// Default implementation of the operation command factory.
/// </summary>
public class OperationCommandFactory : IOperationCommandFactory
{
    private readonly ILogger<OperationCommandFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the OperationCommandFactory.
    /// </summary>
    /// <param name="logger">Logger for factory operations</param>
    public OperationCommandFactory(ILogger<OperationCommandFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ProcessMessageCommand CreateProcessMessageCommand(string operationId, string chatId, string userId, ChatMessage message)
    {
        var validation = ValidateCreationParameters(operationId, chatId, userId);
        if (!validation.IsValid)
        {
            var errorMessage = $"Invalid parameters for ProcessMessageCommand: {string.Join(", ", validation.Errors)}";
            _logger.LogError(errorMessage);
            throw new ArgumentException(errorMessage);
        }

        if (message == null)
        {
            _logger.LogError("Message is required for ProcessMessageCommand");
            throw new ArgumentNullException(nameof(message));
        }

        _logger.LogDebug(
            "Creating ProcessMessageCommand for operation {OperationId} in chat {ChatId} for user {UserId}",
            operationId, chatId, userId);

        return new ProcessMessageCommand(operationId, chatId, userId, message);
    }

    /// <inheritdoc />
    public RelayMessageCommand CreateRelayMessageCommand(string operationId, string chatId, string userId, ChatMessage message)
    {
        var validation = ValidateCreationParameters(operationId, chatId, userId);
        if (!validation.IsValid)
        {
            var errorMessage = $"Invalid parameters for RelayMessageCommand: {string.Join(", ", validation.Errors)}";
            _logger.LogError(errorMessage);
            throw new ArgumentException(errorMessage);
        }

        if (message == null)
        {
            _logger.LogError("Message is required for RelayMessageCommand");
            throw new ArgumentNullException(nameof(message));
        }

        _logger.LogDebug(
            "Creating RelayMessageCommand for operation {OperationId} in chat {ChatId} for user {UserId}",
            operationId, chatId, userId);

        return new RelayMessageCommand(operationId, chatId, userId, message);
    }

    /// <inheritdoc />
    public ProcessStreamChunkCommand CreateProcessStreamChunkCommand(string operationId, string chatId, string userId, StreamChunk chunk)
    {
        var validation = ValidateCreationParameters(operationId, chatId, userId);
        if (!validation.IsValid)
        {
            var errorMessage = $"Invalid parameters for ProcessStreamChunkCommand: {string.Join(", ", validation.Errors)}";
            _logger.LogError(errorMessage);
            throw new ArgumentException(errorMessage);
        }

        if (chunk == null)
        {
            _logger.LogError("Stream chunk is required for ProcessStreamChunkCommand");
            throw new ArgumentNullException(nameof(chunk));
        }

        _logger.LogTrace(
            "Creating ProcessStreamChunkCommand for operation {OperationId} in chat {ChatId} for user {UserId}, chunk {ChunkIndex}",
            operationId, chatId, userId, chunk.ChunkIndex);

        return new ProcessStreamChunkCommand(operationId, chatId, userId, chunk);
    }

    /// <inheritdoc />
    public CancelOperationCommand CreateCancelOperationCommand(string operationId, string chatId, string userId, string targetOperationId)
    {
        var validation = ValidateCreationParameters(operationId, chatId, userId);
        if (!validation.IsValid)
        {
            var errorMessage = $"Invalid parameters for CancelOperationCommand: {string.Join(", ", validation.Errors)}";
            _logger.LogError(errorMessage);
            throw new ArgumentException(errorMessage);
        }

        if (string.IsNullOrWhiteSpace(targetOperationId))
        {
            _logger.LogError("Target operation ID is required for CancelOperationCommand");
            throw new ArgumentException("Target operation ID cannot be null or empty", nameof(targetOperationId));
        }

        _logger.LogDebug(
            "Creating CancelOperationCommand for operation {OperationId} to cancel {TargetOperationId} in chat {ChatId} for user {UserId}",
            operationId, targetOperationId, chatId, userId);

        return new CancelOperationCommand(operationId, chatId, userId, targetOperationId);
    }

    /// <inheritdoc />
    public IOperationCommand? CreateCommand(OperationType operationType, string operationId, string chatId, string userId, object parameters)
    {
        try
        {
            _logger.LogDebug(
                "Creating command for operation type {OperationType} with operation {OperationId}",
                operationType, operationId);

            return operationType switch
            {
                OperationType.SendMessage => CreateProcessMessageCommandFromParameters(operationId, chatId, userId, parameters),
                OperationType.RegenerateResponse => CreateProcessMessageCommandFromParameters(operationId, chatId, userId, parameters),
                OperationType.EditMessage => CreateProcessMessageCommandFromParameters(operationId, chatId, userId, parameters),
                OperationType.DeleteMessage => CreateProcessMessageCommandFromParameters(operationId, chatId, userId, parameters),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create command for operation type {OperationType} with operation {OperationId}",
                operationType, operationId);
            return null;
        }
    }

    /// <inheritdoc />
    public CommandValidationResult ValidateCreationParameters(string operationId, string chatId, string userId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(operationId))
        {
            errors.Add("Operation ID is required");
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            errors.Add("Chat ID is required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            errors.Add("User ID is required");
        }

        // Basic format validation
        if (!string.IsNullOrWhiteSpace(operationId) && !IsValidGuid(operationId))
        {
            errors.Add("Operation ID should be a valid GUID");
        }

        return errors.Count > 0
            ? CommandValidationResult.Failed(errors.ToArray())
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Creates a ProcessMessageCommand from generic parameters.
    /// </summary>
    private ProcessMessageCommand CreateProcessMessageCommandFromParameters(string operationId, string chatId, string userId, object parameters)
    {
        if (parameters is not ChatMessage message)
        {
            throw new ArgumentException("Parameters must be a ChatMessage for message processing operations", nameof(parameters));
        }

        return CreateProcessMessageCommand(operationId, chatId, userId, message);
    }

    /// <summary>
    /// Validates if a string is a valid GUID format.
    /// </summary>
    private static bool IsValidGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }
}