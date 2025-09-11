using System.Text.Json;
using AIChat.Orleans.Commands;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Services;

/// <summary>
/// Service interface for coordinating background operations and command execution.
/// Encapsulates business logic for Phase 3 operation management.
/// Follows the Single Responsibility Principle by focusing solely on operation coordination.
/// </summary>
public interface IOperationCoordinatorService
{
    /// <summary>
    /// Initiates a background message processing operation.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="message">Message to process</param>
    /// <returns>Updated state, operation ID, and activity record</returns>
    Task<(
        UserGrainState UpdatedState,
        string OperationId,
        ActivityRecord ActivityRecord
    )> InitiateMessageProcessingAsync(UserGrainState state, ChatMessage message);

    /// <summary>
    /// Updates operation status when processing starts.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="operationId">Operation identifier</param>
    /// <param name="chatId">Chat identifier</param>
    /// <returns>Updated state and activity record</returns>
    Task<(UserGrainState UpdatedState, ActivityRecord ActivityRecord)> NotifyOperationStartedAsync(
        UserGrainState state,
        string operationId,
        string chatId
    );

    /// <summary>
    /// Updates operation status when processing completes.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="operationId">Operation identifier</param>
    /// <param name="success">Whether operation succeeded</param>
    /// <param name="error">Error message if failed</param>
    /// <returns>Updated state, activity record, and cleanup delay</returns>
    Task<(
        UserGrainState UpdatedState,
        ActivityRecord ActivityRecord,
        TimeSpan CleanupDelay
    )> NotifyOperationCompletedAsync(
        UserGrainState state,
        string operationId,
        bool success,
        string? error = null
    );

    /// <summary>
    /// Cancels an active operation.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="operationId">Operation identifier</param>
    /// <returns>Updated state, cancellation success, activity record, and cleanup delay</returns>
    Task<(
        UserGrainState UpdatedState,
        bool WasCancelled,
        ActivityRecord ActivityRecord,
        TimeSpan CleanupDelay
    )> CancelOperationAsync(UserGrainState state, string operationId);

    /// <summary>
    /// Gets the status of an operation.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="operationId">Operation identifier</param>
    /// <returns>Operation context if found</returns>
    Task<OperationContext?> GetOperationStatusAsync(UserGrainState state, string operationId);

    /// <summary>
    /// Executes a command using the command pattern.
    /// </summary>
    /// <param name="command">Command to execute</param>
    /// <param name="context">Execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command execution result</returns>
    Task<CommandExecutionResult> ExecuteCommandAsync(
        IOperationCommand command,
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Cleans up a completed operation from state.
    /// </summary>
    /// <param name="state">Current user grain state</param>
    /// <param name="operationId">Operation identifier to clean up</param>
    /// <returns>Updated state and whether operation was found</returns>
    Task<(UserGrainState UpdatedState, bool WasFound)> CleanupOperationAsync(
        UserGrainState state,
        string operationId
    );

    /// <summary>
    /// Validates operation parameters.
    /// </summary>
    /// <param name="operationId">Operation identifier</param>
    /// <param name="chatId">Chat identifier</param>
    /// <param name="operationType">Operation type</param>
    /// <returns>Validation result</returns>
    Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateOperationAsync(
        string operationId,
        string chatId,
        OperationType operationType
    );
}

/// <summary>
/// Default implementation of the operation coordinator service.
/// </summary>
public class OperationCoordinatorService : IOperationCoordinatorService
{
    private readonly ILogger<OperationCoordinatorService> _logger;
#pragma warning disable IDE0052 // Remove unread private members - used in derived classes
    private readonly IOperationCommandFactory _commandFactory;
#pragma warning restore IDE0052

    /// <summary>
    /// Initializes a new instance of the OperationCoordinatorService.
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    /// <param name="commandFactory">Factory for creating commands</param>
    public OperationCoordinatorService(
        ILogger<OperationCoordinatorService> logger,
        IOperationCommandFactory commandFactory
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandFactory = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
    }

    /// <inheritdoc />
    public Task<(
        UserGrainState UpdatedState,
        string OperationId,
        ActivityRecord ActivityRecord
    )> InitiateMessageProcessingAsync(UserGrainState state, ChatMessage message)
    {
        try
        {
            _logger.LogInformation(
                "Initiating message processing for user {UserId} in chat {ChatId}",
                state.UserId,
                message.ChatId
            );

            var operationId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            // Create operation context
            var operationContext = new OperationContext
            {
                OperationId = operationId,
                ChatId = message.ChatId,
                Type = OperationType.SendMessage,
                StartedAt = now,
                Status = OperationStatus.Queued,
            };

            // Add to state
            state.ActiveOperations[operationId] = operationContext;

            // Update metrics
            state.Metrics.TotalOperationsStarted++;
            state.Metrics.ActiveOperationsCount++;
            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.MessageSent,
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        OperationId = operationId,
                        message.ChatId,
                        MessageId = message.Id,
                        MessageLength = message.Content?.Length ?? 0,
                        MessageRole = message.Role,
                    }
                ),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString(),
            };

            _logger.LogInformation(
                "Message processing initiated with operation ID {OperationId} for user {UserId}",
                operationId,
                state.UserId
            );

            return Task.FromResult((state, operationId, activityRecord));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to initiate message processing for user {UserId}",
                state.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(
        UserGrainState UpdatedState,
        ActivityRecord ActivityRecord
    )> NotifyOperationStartedAsync(UserGrainState state, string operationId, string chatId)
    {
        try
        {
            _logger.LogInformation(
                "Operation {OperationId} started for user {UserId} in chat {ChatId}",
                operationId,
                state.UserId,
                chatId
            );

            var now = DateTime.UtcNow;

            // Update operation status if it exists
            if (state.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                operation.Status = OperationStatus.InProgress;
                operation.StartedAt = now;
            }
            else
            {
                _logger.LogWarning(
                    "Operation {OperationId} not found in state for user {UserId}",
                    operationId,
                    state.UserId
                );
            }

            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.MessageSent,
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationStarted",
                        OperationId = operationId,
                        ChatId = chatId,
                        Status = "InProgress",
                    }
                ),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString(),
            };

            return Task.FromResult((state, activityRecord));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify operation started for {OperationId} and user {UserId}",
                operationId,
                state.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(
        UserGrainState UpdatedState,
        ActivityRecord ActivityRecord,
        TimeSpan CleanupDelay
    )> NotifyOperationCompletedAsync(
        UserGrainState state,
        string operationId,
        bool success,
        string? error = null
    )
    {
        try
        {
            _logger.LogInformation(
                "Operation {OperationId} completed for user {UserId}. Success: {Success}",
                operationId,
                state.UserId,
                success
            );

            if (!state.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                _logger.LogWarning(
                    "Operation {OperationId} not found in state for user {UserId}",
                    operationId,
                    state.UserId
                );

                var warningActivity = new ActivityRecord
                {
                    Type = ActivityType.ErrorOccurred,
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            Event = "OperationCompleted",
                            OperationId = operationId,
                            Status = "NotFound",
                            Success = success,
                            Error = error,
                        }
                    ),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                return Task.FromResult((state, warningActivity, TimeSpan.FromMinutes(5)));
            }

            var now = DateTime.UtcNow;

            // Update operation
            operation.Status = success ? OperationStatus.Completed : OperationStatus.Failed;
            operation.CompletedAt = now;
            operation.Error = error;

            // Update metrics
            if (success)
            {
                state.Metrics.TotalOperationsCompleted++;
            }
            else
            {
                state.Metrics.TotalOperationsFailed++;
            }

            state.Metrics.ActiveOperationsCount--;

            // Calculate duration
            var duration = now - operation.StartedAt;
            state.Metrics.TotalOperationDurationMs += (long)duration.TotalMilliseconds;

            state.LastActivity = now;

            // Create activity record
            var activityType = success ? ActivityType.MessageCompleted : ActivityType.ErrorOccurred;
            var activityRecord = new ActivityRecord
            {
                Type = activityType,
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationCompleted",
                        OperationId = operationId,
                        operation.ChatId,
                        Success = success,
                        Error = error,
                        Duration = duration.TotalMilliseconds,
                        Status = success ? "Completed" : "Failed",
                    }
                ),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString(),
            };

            // Default cleanup delay (configurable)
            var cleanupDelay = TimeSpan.FromMinutes(5);

            _logger.LogInformation(
                "Operation {OperationId} completion processed for user {UserId}. Duration: {Duration}ms",
                operationId,
                state.UserId,
                duration.TotalMilliseconds
            );

            return Task.FromResult((state, activityRecord, cleanupDelay));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify operation completed for {OperationId} and user {UserId}",
                operationId,
                state.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public Task<(
        UserGrainState UpdatedState,
        bool WasCancelled,
        ActivityRecord ActivityRecord,
        TimeSpan CleanupDelay
    )> CancelOperationAsync(UserGrainState state, string operationId)
    {
        try
        {
            _logger.LogInformation(
                "Cancelling operation {OperationId} for user {UserId}",
                operationId,
                state.UserId
            );

            if (!state.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                var notFoundActivity = new ActivityRecord
                {
                    Type = ActivityType.ErrorOccurred,
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            Event = "OperationCancellationFailed",
                            OperationId = operationId,
                            Reason = "NotFound",
                        }
                    ),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                _logger.LogWarning(
                    "Operation {OperationId} not found for user {UserId}. Cannot cancel.",
                    operationId,
                    state.UserId
                );

                return Task.FromResult((state, false, notFoundActivity, TimeSpan.FromMinutes(1)));
            }

            // Check if operation can be cancelled
            if (
                operation.Status
                is OperationStatus.Completed
                    or OperationStatus.Failed
                    or OperationStatus.Cancelled
            )
            {
                var alreadyCompleteActivity = new ActivityRecord
                {
                    Type = ActivityType.ErrorOccurred,
                    Metadata = JsonSerializer.Serialize(
                        new
                        {
                            Event = "OperationCancellationFailed",
                            OperationId = operationId,
                            Reason = "AlreadyCompleted",
                            CurrentStatus = operation.Status.ToString(),
                        }
                    ),
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = Guid.NewGuid().ToString(),
                };

                _logger.LogWarning(
                    "Operation {OperationId} for user {UserId} is already in final state {Status}. Cannot cancel.",
                    operationId,
                    state.UserId,
                    operation.Status
                );

                return Task.FromResult(
                    (state, false, alreadyCompleteActivity, TimeSpan.FromMinutes(1))
                );
            }

            var now = DateTime.UtcNow;

            // Cancel the operation
            operation.Status = OperationStatus.Cancelled;
            operation.CompletedAt = now;
            operation.Error = "Operation cancelled by user";

            // Update metrics
            state.Metrics.TotalOperationsCancelled++;
            state.Metrics.ActiveOperationsCount--;

            var duration = now - operation.StartedAt;
            state.Metrics.TotalOperationDurationMs += (long)duration.TotalMilliseconds;

            state.LastActivity = now;

            // Create activity record
            var activityRecord = new ActivityRecord
            {
                Type = ActivityType.ErrorOccurred,
                Metadata = JsonSerializer.Serialize(
                    new
                    {
                        Event = "OperationCancelled",
                        OperationId = operationId,
                        operation.ChatId,
                        Duration = duration.TotalMilliseconds,
                        Status = "Cancelled",
                    }
                ),
                Timestamp = now,
                CorrelationId = Guid.NewGuid().ToString(),
            };

            var cleanupDelay = TimeSpan.FromMinutes(5);

            _logger.LogInformation(
                "Operation {OperationId} successfully cancelled for user {UserId}",
                operationId,
                state.UserId
            );

            return Task.FromResult((state, true, activityRecord, cleanupDelay));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cancel operation {OperationId} for user {UserId}",
                operationId,
                state.UserId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public Task<OperationContext?> GetOperationStatusAsync(UserGrainState state, string operationId)
    {
        try
        {
            _logger.LogDebug(
                "Getting operation status for {OperationId} and user {UserId}",
                operationId,
                state.UserId
            );

            if (state.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                // Return a copy to prevent external modification
                return Task.FromResult<OperationContext?>(
                    new OperationContext
                    {
                        OperationId = operation.OperationId,
                        ChatId = operation.ChatId,
                        Type = operation.Type,
                        StartedAt = operation.StartedAt,
                        CompletedAt = operation.CompletedAt,
                        Status = operation.Status,
                        Error = operation.Error,
                    }
                );
            }

            _logger.LogDebug(
                "Operation {OperationId} not found for user {UserId}",
                operationId,
                state.UserId
            );

            return Task.FromResult<OperationContext?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get operation status for {OperationId} and user {UserId}",
                operationId,
                state.UserId
            );
            return Task.FromResult<OperationContext?>(null);
        }
    }

    /// <inheritdoc />
    public async Task<CommandExecutionResult> ExecuteCommandAsync(
        IOperationCommand command,
        ICommandExecutionContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger.LogInformation(
                "Executing command {CommandType} with ID {CommandId}",
                command.GetType().Name,
                command.CommandId
            );

            var result = await command.ExecuteAsync(context, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "Command {CommandType} executed successfully in {Duration}ms",
                    command.GetType().Name,
                    result.ExecutionDurationMs
                );
            }
            else
            {
                _logger.LogError(
                    "Command {CommandType} failed: {ErrorMessage}",
                    command.GetType().Name,
                    result.ErrorMessage
                );
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception occurred while executing command {CommandType}",
                command.GetType().Name
            );

            return CommandExecutionResult.Failed(
                $"Command execution failed with exception: {ex.Message}",
                ex.ToString()
            );
        }
    }

    /// <inheritdoc />
    public Task<(UserGrainState UpdatedState, bool WasFound)> CleanupOperationAsync(
        UserGrainState state,
        string operationId
    )
    {
        try
        {
            if (state.ActiveOperations.TryGetValue(operationId, out var operation))
            {
                // Only clean up completed, failed, or cancelled operations
                if (
                    operation.Status
                    is OperationStatus.Completed
                        or OperationStatus.Failed
                        or OperationStatus.Cancelled
                )
                {
                    _ = state.ActiveOperations.Remove(operationId);

                    _logger.LogDebug(
                        "Cleaned up completed operation {OperationId} for user {UserId}",
                        operationId,
                        state.UserId
                    );

                    return Task.FromResult((state, true));
                }
                else
                {
                    _logger.LogWarning(
                        "Attempted to clean up operation {OperationId} with status {Status} for user {UserId}",
                        operationId,
                        operation.Status,
                        state.UserId
                    );

                    return Task.FromResult((state, false));
                }
            }
            else
            {
                _logger.LogDebug(
                    "Operation {OperationId} already removed from state for user {UserId}",
                    operationId,
                    state.UserId
                );

                return Task.FromResult((state, false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to cleanup operation {OperationId} for user {UserId}",
                operationId,
                state.UserId
            );
            return Task.FromResult((state, false));
        }
    }

    /// <inheritdoc />
    public Task<(bool IsValid, string[] Errors, string[] Warnings)> ValidateOperationAsync(
        string operationId,
        string chatId,
        OperationType operationType
    )
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Validate operation ID
            if (string.IsNullOrWhiteSpace(operationId))
            {
                errors.Add("Operation ID is required");
            }
            else if (!Guid.TryParse(operationId, out _))
            {
                warnings.Add("Operation ID is not a valid GUID format");
            }

            // Validate chat ID
            if (string.IsNullOrWhiteSpace(chatId))
            {
                errors.Add("Chat ID is required");
            }

            // Validate operation type
            if (!Enum.IsDefined(operationType))
            {
                errors.Add($"Invalid operation type: {operationType}");
            }

            return Task.FromResult((errors.Count == 0, errors.ToArray(), warnings.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate operation parameters");
            return Task.FromResult(
                (false, new[] { $"Validation failed: {ex.Message}" }, Array.Empty<string>())
            );
        }
    }
}
