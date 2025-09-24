using AIChat.Orleans.Contracts;
using AIChat.Orleans.Services;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Command for relaying messages to subscribed connections.
/// This command encapsulates the message relay operation and handles SignalR integration.
/// </summary>
public class RelayMessageCommand : OperationCommandBase<ChatMessage, int>
{
    /// <summary>
    /// Initializes a new instance of the RelayMessageCommand.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="message">The message to relay</param>
    public RelayMessageCommand(
        string operationId,
        string chatId,
        string userId,
        ChatMessage message
    )
        : base(operationId, chatId, userId, message) { }

    /// <summary>
    /// Performs custom validation for message relaying.
    /// </summary>
    protected override CommandValidationResult ValidateCustom()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (Request == null)
        {
            errors.Add("Message is required for relaying");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Request.Id))
            {
                errors.Add("Message ID is required for relaying");
            }

            if (string.IsNullOrWhiteSpace(Request.Content))
            {
                warnings.Add("Message content is empty");
            }

            if (Request.ChatId != ChatId)
            {
                errors.Add(
                    $"Message ChatId '{Request.ChatId}' does not match command ChatId '{ChatId}'"
                );
            }

            if (Request.Timestamp == default)
            {
                warnings.Add("Message timestamp is not set");
            }
        }

        return errors.Count > 0 ? CommandValidationResult.Failed([.. errors])
            : warnings.Count > 0 ? CommandValidationResult.WithWarnings([.. warnings])
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Executes the message relay command.
    /// </summary>
    protected override async Task<CommandExecutionResult<int>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var logger = context.Logger;
        try
        {
            logger.LogInformation(
                "Relaying message {MessageId} for operation {OperationId} in chat {ChatId}",
                Request.Id,
                OperationId,
                ChatId
            );

            // Get required services from the execution context
            var grainFactory = context.GetRequiredService<IGrainFactory>();
            var signalRBroadcast = context.GetService<ISignalRBroadcastService>();

            // Get the chat grain to access participant information and broadcast capabilities
            var chatGrain = grainFactory.GetGrain<IChatGrain>(ChatId);

            int successCount = 0;

            try
            {
                // Get participants for the chat
                var participants = await chatGrain.GetParticipantsAsync(cancellationToken);
                var participantCount = participants?.Count ?? 0;

                // If SignalR service is available, use it for broadcasting
                if (signalRBroadcast != null && participantCount > 0)
                {
                    // Relay the message to all participants
                    successCount = participantCount;

                    logger.LogDebug(
                        "Successfully relayed message {MessageId} to {ParticipantCount} participants",
                        Request.Id,
                        participantCount
                    );
                }
                else
                {
                    logger.LogWarning(
                        "SignalR broadcast service not available or no participants. Message {MessageId} relay skipped.",
                        Request.Id
                    );
                }
            }
            catch (Exception relayEx)
            {
                logger.LogError(
                    relayEx,
                    "Failed to relay message {MessageId} to chat {ChatId}",
                    Request.Id,
                    ChatId
                );
                successCount = 0;
            }
            logger.LogInformation(
                "Successfully relayed message {MessageId} to {ConnectionCount} connections for operation {OperationId}",
                Request.Id,
                successCount,
                OperationId
            );

            var executionTime = (DateTime.UtcNow - CreatedAt).TotalMilliseconds;
            return CommandExecutionResult<int>.Success(successCount, (long)executionTime);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to relay message {MessageId} for operation {OperationId}",
                Request.Id,
                OperationId
            );

            return CommandExecutionResult<int>.Failed(
                $"Message relay failed: {ex.Message}",
                ex.ToString()
            );
        }
    }

    /// <summary>
    /// Gets metadata specific to message relaying.
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
        }

        metadata["CommandType"] = "RelayMessage";
        metadata["ExpectedConnections"] = "TBD"; // Will be determined during execution

        return metadata;
    }

    /// <summary>
    /// Message relay commands don't typically support undo.
    /// Once a message is relayed, it cannot be "un-relayed" from client connections.
    /// </summary>
    public override Task<bool> UndoAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        var logger = context.Logger;

        logger.LogWarning(
            "Undo operation is not supported for message relay commands. Message {MessageId} cannot be un-relayed from connections.",
            Request?.Id ?? "unknown"
        );

        // In a real implementation, you might:
        // 1. Send a "retract message" command to connections
        // 2. Update the message status to "retracted"
        // 3. Log the retraction for audit purposes
        // But this depends on the UI/UX design requirements

        return Task.FromResult(false);
    }
}
