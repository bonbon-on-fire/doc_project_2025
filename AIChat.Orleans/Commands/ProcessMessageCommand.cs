using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Command for processing messages through background service integration.
/// This command encapsulates the message processing request and coordinates with the background service.
/// </summary>
public class ProcessMessageCommand : OperationCommandBase<ChatMessage, string>
{
    /// <summary>
    /// Initializes a new instance of the ProcessMessageCommand.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="message">The message to process</param>
    public ProcessMessageCommand(string operationId, string chatId, string userId, ChatMessage message)
        : base(operationId, chatId, userId, message)
    {
    }

    /// <summary>
    /// Performs custom validation for message processing.
    /// </summary>
    protected override CommandValidationResult ValidateCustom()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (Request == null)
        {
            errors.Add("Message is required for processing");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Request.Content))
            {
                errors.Add("Message content cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(Request.Id))
            {
                warnings.Add("Message ID is empty - will be generated");
            }

            if (Request.ChatId != ChatId)
            {
                errors.Add($"Message ChatId '{Request.ChatId}' does not match command ChatId '{ChatId}'");
            }

            if (Request.UserId != UserId)
            {
                errors.Add($"Message UserId '{Request.UserId}' does not match command UserId '{UserId}'");
            }

            if (Request.Content.Length > 100000) // 100KB limit
            {
                warnings.Add("Message content is very large and may impact performance");
            }
        }

        if (errors.Count > 0)
        {
            return CommandValidationResult.Failed(errors.ToArray());
        }

        return warnings.Count > 0
            ? CommandValidationResult.WithWarnings(warnings.ToArray())
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Executes the message processing command.
    /// </summary>
    protected override async Task<CommandExecutionResult<string>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Processing message command for operation {OperationId} in chat {ChatId}",
                OperationId, ChatId);

            // TODO: This will be replaced with actual background service integration
            // For now, we simulate the processing and return the operation ID
            
            // Ensure message has an ID
            if (string.IsNullOrWhiteSpace(Request.Id))
            {
                Request.Id = Guid.NewGuid().ToString();
                logger.LogDebug("Generated message ID {MessageId} for operation {OperationId}",
                    Request.Id, OperationId);
            }

            // Validate message content length and sanitize if needed
            if (Request.Content.Length > 50000) // 50KB soft limit
            {
                logger.LogWarning(
                    "Message content length {Length} exceeds recommended limit for operation {OperationId}",
                    Request.Content.Length, OperationId);
            }

            // In the complete implementation, this would:
            // 1. Queue the message for background processing
            // 2. Coordinate with BackgroundChatService
            // 3. Handle streaming responses
            // 4. Manage operation lifecycle
            
            // Simulate processing delay
            await Task.Delay(100, cancellationToken);

            logger.LogInformation(
                "Message processing command completed for operation {OperationId}. Message ID: {MessageId}",
                OperationId, Request.Id);

            return CommandExecutionResult<string>.Success(
                OperationId,
                100); // Duration matches the simulated delay
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to execute message processing command for operation {OperationId}",
                OperationId);

            return CommandExecutionResult<string>.Failed(
                $"Message processing failed: {ex.Message}",
                ex.ToString());
        }
    }

    /// <summary>
    /// Gets metadata specific to message processing.
    /// </summary>
    public override Dictionary<string, object> GetMetadata()
    {
        var metadata = base.GetMetadata();
        
        if (Request != null)
        {
            metadata["MessageId"] = Request.Id ?? "null";
            metadata["MessageRole"] = Request.Role;
            metadata["MessageContentLength"] = Request.Content?.Length ?? 0;
            metadata["MessageTimestamp"] = Request.Timestamp;
            metadata["IsStreamingMessage"] = Request.IsStreaming;

            if (!string.IsNullOrEmpty(Request.ParentId))
            {
                metadata["ParentMessageId"] = Request.ParentId;
            }

            if (!string.IsNullOrEmpty(Request.Metadata))
            {
                try
                {
                    var messageMetadata = JsonSerializer.Deserialize<Dictionary<string, object>>(Request.Metadata);
                    metadata["MessageMetadata"] = messageMetadata ?? new Dictionary<string, object>();
                }
                catch
                {
                    metadata["MessageMetadata"] = "Failed to parse";
                }
            }
        }

        return metadata;
    }

    /// <summary>
    /// Message processing commands support cancellation.
    /// </summary>
    public override async Task<bool> UndoAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Attempting to cancel message processing for operation {OperationId}",
                OperationId);

            // TODO: In complete implementation, this would:
            // 1. Cancel the background processing operation
            // 2. Clean up any partial results
            // 3. Notify connected clients of cancellation
            // 4. Update operation status

            // Simulate cancellation logic
            await Task.Delay(50, cancellationToken);

            logger.LogInformation(
                "Successfully cancelled message processing for operation {OperationId}",
                OperationId);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to cancel message processing for operation {OperationId}",
                OperationId);

            return false;
        }
    }
}