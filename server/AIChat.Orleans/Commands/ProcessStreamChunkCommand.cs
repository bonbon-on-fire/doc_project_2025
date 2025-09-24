using AIChat.Orleans.Contracts;
using AIChat.Orleans.Services;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Command for processing and relaying streaming chunks to subscribed connections.
/// This command handles the real-time delivery of streaming content during message generation.
/// </summary>
public class ProcessStreamChunkCommand : OperationCommandBase<StreamChunk, bool>
{
    /// <summary>
    /// Initializes a new instance of the ProcessStreamChunkCommand.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="chunk">The stream chunk to process</param>
    public ProcessStreamChunkCommand(
        string operationId,
        string chatId,
        string userId,
        StreamChunk chunk
    )
        : base(operationId, chatId, userId, chunk) { }

    /// <summary>
    /// Performs custom validation for stream chunk processing.
    /// </summary>
    protected override CommandValidationResult ValidateCustom()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (Request == null)
        {
            errors.Add("Stream chunk is required for processing");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Request.OperationId))
            {
                errors.Add("Stream chunk OperationId is required");
            }
            else if (Request.OperationId != OperationId)
            {
                errors.Add(
                    $"Stream chunk OperationId '{Request.OperationId}' does not match command OperationId '{OperationId}'"
                );
            }

            if (string.IsNullOrWhiteSpace(Request.ChatId))
            {
                errors.Add("Stream chunk ChatId is required");
            }
            else if (Request.ChatId != ChatId)
            {
                errors.Add(
                    $"Stream chunk ChatId '{Request.ChatId}' does not match command ChatId '{ChatId}'"
                );
            }

            if (Request.ChunkIndex < 0)
            {
                errors.Add("Stream chunk index must be non-negative");
            }

            if (Request.TotalChunks.HasValue && Request.TotalChunks.Value < 1)
            {
                errors.Add("Total chunks must be positive if specified");
            }

            if (Request.TotalChunks.HasValue && Request.ChunkIndex >= Request.TotalChunks.Value)
            {
                errors.Add("Chunk index must be less than total chunks");
            }

            // Content can be empty for certain chunk types (like completion markers)
            if (string.IsNullOrEmpty(Request.Content) && !Request.IsComplete)
            {
                warnings.Add("Stream chunk content is empty for non-completion chunk");
            }

            if (Request.Timestamp == default)
            {
                warnings.Add("Stream chunk timestamp is not set");
            }
        }

        return errors.Count > 0 ? CommandValidationResult.Failed([.. errors])
            : warnings.Count > 0 ? CommandValidationResult.WithWarnings([.. warnings])
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Executes the stream chunk processing command.
    /// </summary>
    protected override async Task<CommandExecutionResult<bool>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var logger = context.Logger;

        try
        {
            logger.LogDebug(
                "Processing stream chunk {ChunkIndex} for operation {OperationId} in chat {ChatId}. IsComplete: {IsComplete}",
                Request.ChunkIndex,
                OperationId,
                ChatId,
                Request.IsComplete
            );

            // Get required services from the execution context
            var grainFactory = context.GetRequiredService<IGrainFactory>();
            var signalRBroadcast = context.GetService<ISignalRBroadcastService>();

            // Get the chat grain to process the stream chunk
            var chatGrain = grainFactory.GetGrain<IChatGrain>(ChatId);

            try
            {
                // Get participants to determine if we should process this chunk
                var participants = await chatGrain.GetParticipantsAsync(cancellationToken);
                var participantCount = participants?.Count ?? 0;

                // Process the chunk through the chat grain's streaming capabilities
                await chatGrain.ProcessStreamChunkAsync(Request, cancellationToken);

                logger.LogDebug(
                    "Processed stream chunk {ChunkIndex} for {ParticipantCount} participants",
                    Request.ChunkIndex,
                    participantCount
                );

                // If this is a completion chunk, do additional cleanup
                if (Request.IsComplete && participantCount > 0)
                {
                    logger.LogInformation(
                        "Stream completed for operation {OperationId} with {ParticipantCount} participants",
                        OperationId,
                        participantCount
                    );
                }
            }
            catch (Exception processingEx)
            {
                logger.LogError(
                    processingEx,
                    "Failed to process stream chunk {ChunkIndex} for operation {OperationId}",
                    Request.ChunkIndex,
                    OperationId
                );
                return CommandExecutionResult<bool>.Failed(
                    $"Stream chunk processing failed: {processingEx.Message}"
                );
            }

            // Log completion or regular chunk processing
            if (Request.IsComplete)
            {
                logger.LogInformation(
                    "Completed processing final chunk {ChunkIndex} for operation {OperationId} in chat {ChatId}",
                    Request.ChunkIndex,
                    OperationId,
                    ChatId
                );
            }
            else
            {
                logger.LogTrace(
                    "Processed stream chunk {ChunkIndex} for operation {OperationId}. Content length: {ContentLength}",
                    Request.ChunkIndex,
                    OperationId,
                    Request.Content?.Length ?? 0
                );
            }

            var executionTime = (DateTime.UtcNow - CreatedAt).TotalMilliseconds;
            return CommandExecutionResult<bool>.Success(true, (long)executionTime);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to process stream chunk {ChunkIndex} for operation {OperationId}",
                Request.ChunkIndex,
                OperationId
            );

            return CommandExecutionResult<bool>.Failed(
                $"Stream chunk processing failed: {ex.Message}",
                ex.ToString()
            );
        }
    }

    /// <summary>
    /// Gets metadata specific to stream chunk processing.
    /// </summary>
    public override Dictionary<string, object> GetMetadata()
    {
        var metadata = base.GetMetadata();

        if (Request != null)
        {
            metadata["StreamOperationId"] = Request.OperationId ?? "null";
            metadata["ChunkIndex"] = Request.ChunkIndex;
            metadata["IsComplete"] = Request.IsComplete;
            metadata["ContentLength"] = Request.Content?.Length ?? 0;
            metadata["ChunkTimestamp"] = Request.Timestamp;

            if (!string.IsNullOrEmpty(Request.MessageId))
            {
                metadata["MessageId"] = Request.MessageId;
            }

            if (Request.TotalChunks.HasValue)
            {
                metadata["TotalChunks"] = Request.TotalChunks.Value;
                metadata["ProgressPercentage"] =
                    (Request.ChunkIndex + 1) * 100.0 / Request.TotalChunks.Value;
            }
        }

        metadata["CommandType"] = "ProcessStreamChunk";

        return metadata;
    }

    /// <summary>
    /// Stream chunk commands don't support undo.
    /// Once a chunk is streamed to clients, it cannot be "un-streamed".
    /// </summary>
    public override Task<bool> UndoAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        var logger = context.Logger;

        logger.LogWarning(
            "Undo operation is not supported for stream chunk commands. Chunk {ChunkIndex} of operation {OperationId} cannot be undone.",
            Request?.ChunkIndex ?? -1,
            OperationId
        );

        // In a streaming scenario, you typically can't "undo" a chunk that's already been sent to clients.
        // The stream is inherently forward-only. If there's an error, you would typically:
        // 1. Send an error chunk to indicate the stream has failed
        // 2. Cancel the remaining chunks
        // 3. Clean up the operation state
        // But this is handled by cancellation, not undo.

        return Task.FromResult(false);
    }
}
