using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Command for cancelling active operations.
/// This command encapsulates the operation cancellation logic and cleanup procedures.
/// </summary>
public class CancelOperationCommand : OperationCommandBase<string, bool>
{
    /// <summary>
    /// The operation ID to cancel (same as the command's operation context).
    /// </summary>
    public string TargetOperationId => Request;

    /// <summary>
    /// Initializes a new instance of the CancelOperationCommand.
    /// </summary>
    /// <param name="operationId">The operation ID (for command tracking)</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="targetOperationId">The operation ID to cancel</param>
    public CancelOperationCommand(
        string operationId,
        string chatId,
        string userId,
        string targetOperationId
    )
        : base(operationId, chatId, userId, targetOperationId) { }

    /// <summary>
    /// Performs custom validation for operation cancellation.
    /// </summary>
    protected override CommandValidationResult ValidateCustom()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Request))
        {
            errors.Add("Target operation ID is required for cancellation");
        }

        // Note: We can't validate if the operation exists here because that would require
        // accessing the grain state, which should happen during execution, not validation.
        // The validation phase should only check the command structure, not external state.

        return errors.Count > 0 ? CommandValidationResult.Failed([.. errors])
            : warnings.Count > 0 ? CommandValidationResult.WithWarnings([.. warnings])
            : CommandValidationResult.Success();
    }

    /// <summary>
    /// Executes the operation cancellation command.
    /// </summary>
    protected override async Task<CommandExecutionResult<bool>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Executing cancellation for operation {TargetOperationId} via command operation {CommandOperationId}",
                TargetOperationId,
                OperationId
            );

            // TODO: In complete implementation, this would:
            // 1. Check if the target operation exists in the grain state
            // 2. Verify the operation can be cancelled (not already completed/failed/cancelled)
            // 3. Update the operation status to cancelled
            // 4. Notify the background service to stop processing
            // 5. Send cancellation notifications to connected clients
            // 6. Clean up any resources associated with the operation
            // 7. Update metrics and activity records

            // Simulate cancellation logic
            await Task.Delay(150, cancellationToken);

            var wasCancelled = true; // Simulate successful cancellation

            if (wasCancelled)
            {
                logger.LogInformation(
                    "Successfully cancelled operation {TargetOperationId}",
                    TargetOperationId
                );
            }
            else
            {
                logger.LogWarning(
                    "Operation {TargetOperationId} could not be cancelled (may not exist or already completed)",
                    TargetOperationId
                );
            }

            return CommandExecutionResult<bool>.Success(wasCancelled, 150); // Duration matches the simulated delay
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to cancel operation {TargetOperationId}",
                TargetOperationId
            );

            return CommandExecutionResult<bool>.Failed(
                $"Operation cancellation failed: {ex.Message}",
                ex.ToString()
            );
        }
    }

    /// <summary>
    /// Gets metadata specific to operation cancellation.
    /// </summary>
    public override Dictionary<string, object> GetMetadata()
    {
        var metadata = base.GetMetadata();

        metadata["TargetOperationId"] = TargetOperationId ?? "null";
        metadata["CommandType"] = "CancelOperation";
        metadata["Action"] = "Cancel";

        // If this is a self-cancellation (rare case where operation cancels itself)
        if (TargetOperationId == OperationId)
        {
            metadata["IsSelfCancellation"] = true;
        }

        return metadata;
    }

    /// <summary>
    /// Cancellation commands themselves cannot be undone.
    /// Once an operation is cancelled, you can't "un-cancel" it.
    /// </summary>
    public override Task<bool> UndoAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        var logger = context.Logger;

        logger.LogWarning(
            "Undo operation is not supported for cancellation commands. Operation {TargetOperationId} cannot be un-cancelled.",
            TargetOperationId
        );

        // Cancellation is a terminal operation. Once something is cancelled,
        // it cannot be "un-cancelled" back to its previous state.
        // If you need to restart the operation, it would be a new operation entirely.

        return Task.FromResult(false);
    }
}
