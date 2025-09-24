using System.Text.Json;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Orleans;

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
    public ProcessMessageCommand(
        string operationId,
        string chatId,
        string userId,
        ChatMessage message
    )
        : base(operationId, chatId, userId, message) { }

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
                errors.Add(
                    $"Message ChatId '{Request.ChatId}' does not match command ChatId '{ChatId}'"
                );
            }

            if (Request.UserId != UserId)
            {
                errors.Add(
                    $"Message UserId '{Request.UserId}' does not match command UserId '{UserId}'"
                );
            }

            if (Request.Content.Length > 100000) // 100KB limit
            {
                warnings.Add("Message content is very large and may impact performance");
            }
        }

        return errors.Count > 0 ? CommandValidationResult.Failed([.. errors])
            : warnings.Count > 0 ? CommandValidationResult.WithWarnings([.. warnings])
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Executes the message processing command.
    /// </summary>
    protected override async Task<CommandExecutionResult<string>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Processing message command for operation {OperationId} in chat {ChatId}",
                OperationId,
                ChatId
            );

            // Get required services from the execution context
            var grainFactory = context.GetRequiredService<IGrainFactory>();

            // Ensure message has an ID
            if (string.IsNullOrWhiteSpace(Request.Id))
            {
                Request.Id = Guid.NewGuid().ToString();
                logger.LogDebug(
                    "Generated message ID {MessageId} for operation {OperationId}",
                    Request.Id,
                    OperationId
                );
            }

            // Validate message content length and sanitize if needed
            if (Request.Content.Length > 50000) // 50KB soft limit
            {
                logger.LogWarning(
                    "Message content length {Length} exceeds recommended limit for operation {OperationId}",
                    Request.Content.Length,
                    OperationId
                );
            }

            // Get the chat grain to process the message directly
            var chatGrain = grainFactory.GetGrain<IChatGrain>(ChatId);

            try
            {
                // Process the message through the chat grain
                var messageResult = await chatGrain.ProcessMessageAsync(Request, cancellationToken);

                logger.LogInformation(
                    "Message processed successfully for operation {OperationId}",
                    OperationId
                );
            }
            catch (Exception processingEx)
            {
                logger.LogWarning(
                    processingEx,
                    "Message processing failed for operation {OperationId}",
                    OperationId
                );
                return CommandExecutionResult<string>.Failed(
                    $"Message processing failed: {processingEx.Message}"
                );
            }

            logger.LogInformation(
                "Message processing command completed for operation {OperationId}. Message ID: {MessageId}",
                OperationId,
                Request.Id
            );

            var executionTime = (DateTime.UtcNow - CreatedAt).TotalMilliseconds;
            return CommandExecutionResult<string>.Success(OperationId, (long)executionTime);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to execute message processing command for operation {OperationId}",
                OperationId
            );

            return CommandExecutionResult<string>.Failed(
                $"Message processing failed: {ex.Message}",
                ex.ToString()
            );
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
                    var messageMetadata = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        Request.Metadata
                    );
                    metadata["MessageMetadata"] = messageMetadata ?? [];
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
    public override async Task<bool> UndoAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Attempting to cancel message processing for operation {OperationId}",
                OperationId
            );

            // Use the CancelOperationCommand to properly cancel this operation
            var cancelCommand = new CancelOperationCommand(
                Guid.NewGuid().ToString(), // New command ID for the cancellation
                ChatId,
                UserId,
                OperationId // Target operation to cancel
            );

            // Execute the cancellation command
            var cancelResult = await cancelCommand.ExecuteAsync(context, cancellationToken);

            if (!cancelResult.IsSuccess)
            {
                logger.LogWarning(
                    "Failed to cancel message processing operation {OperationId}: {Error}",
                    OperationId,
                    cancelResult.ErrorMessage
                );
                return false;
            }

            // Check if the cancellation was successful (the command returns bool data)
            if (cancelResult is CommandExecutionResult<bool> typedResult)
            {
                return typedResult.Data;
            }

            // If we get here, the cancellation command executed but didn't return expected data
            logger.LogInformation(
                "Message processing cancellation completed for operation {OperationId}",
                OperationId
            );

            return true; // Assume success if command executed without errors
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to cancel message processing for operation {OperationId}",
                OperationId
            );

            return false;
        }
    }
}
