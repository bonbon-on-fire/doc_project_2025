using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Base interface for operation commands in the Orleans system.
/// Implements the Command Pattern to encapsulate operation requests as objects.
/// This allows for parameterization, queuing, logging, and undo operations.
/// </summary>
public interface IOperationCommand
{
    /// <summary>
    /// Unique identifier for this command execution.
    /// </summary>
    string CommandId { get; }

    /// <summary>
    /// The operation ID this command is associated with.
    /// </summary>
    string OperationId { get; }

    /// <summary>
    /// The chat ID where this operation is being executed.
    /// </summary>
    string ChatId { get; }

    /// <summary>
    /// The user ID who initiated this operation.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Timestamp when the command was created.
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Validates the command parameters before execution.
    /// </summary>
    /// <returns>Validation result with any errors</returns>
    CommandValidationResult Validate();

    /// <summary>
    /// Executes the command asynchronously.
    /// </summary>
    /// <param name="context">Execution context containing services and dependencies</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Command execution result</returns>
    Task<CommandExecutionResult> ExecuteAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to undo the command if supported.
    /// </summary>
    /// <param name="context">Execution context containing services and dependencies</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>True if undo was successful, false if not supported or failed</returns>
    Task<bool> UndoAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata about this command for logging and monitoring.
    /// </summary>
    /// <returns>Dictionary of key-value pairs containing command metadata</returns>
    Dictionary<string, object> GetMetadata();
}

/// <summary>
/// Generic interface for typed operation commands.
/// </summary>
/// <typeparam name="TRequest">The request data type</typeparam>
/// <typeparam name="TResponse">The response data type</typeparam>
public interface IOperationCommand<TRequest, TResponse> : IOperationCommand
{
    /// <summary>
    /// The request data for this command.
    /// </summary>
    TRequest Request { get; }

    /// <summary>
    /// Executes the command and returns a typed result.
    /// </summary>
    /// <param name="context">Execution context containing services and dependencies</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Typed command execution result</returns>
    new Task<CommandExecutionResult<TResponse>> ExecuteAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of command validation.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Commands.CommandValidationResult")]
public sealed class CommandValidationResult
{
    /// <summary>
    /// Whether the command passed validation.
    /// </summary>
    [Id(0)]
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// List of validation errors if any.
    /// </summary>
    [Id(1)]
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// List of validation warnings if any.
    /// </summary>
    [Id(2)]
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static CommandValidationResult Success()
    {
        return new() { IsValid = true };
    }

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static CommandValidationResult Failed(params string[] errors)
    {
        return new()
        {
            IsValid = false,
            Errors = [.. errors]
        };
    }

    /// <summary>
    /// Creates a validation result with warnings but still valid.
    /// </summary>
    public static CommandValidationResult WithWarnings(params string[] warnings)
    {
        return new()
        {
            IsValid = true,
            Warnings = [.. warnings]
        };
    }
}

/// <summary>
/// Result of command execution.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Commands.CommandExecutionResult")]
public class CommandExecutionResult
{
    /// <summary>
    /// Whether the command executed successfully.
    /// </summary>
    [Id(0)]
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    [Id(1)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Exception details if execution failed.
    /// </summary>
    [Id(2)]
    public string? ExceptionDetails { get; set; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    [Id(3)]
    public long ExecutionDurationMs { get; set; }

    /// <summary>
    /// Additional result metadata.
    /// </summary>
    [Id(4)]
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Timestamp when execution completed.
    /// </summary>
    [Id(5)]
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a successful execution result.
    /// </summary>
    public static CommandExecutionResult Success(long durationMs = 0)
    {
        return new()
        {
            IsSuccess = true,
            ExecutionDurationMs = durationMs
        };
    }

    /// <summary>
    /// Creates a failed execution result.
    /// </summary>
    public static CommandExecutionResult Failed(string errorMessage, string? exceptionDetails = null)
    {
        return new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ExceptionDetails = exceptionDetails
        };
    }
}

/// <summary>
/// Typed result of command execution.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Commands.CommandExecutionResult`1")]
public class CommandExecutionResult<T> : CommandExecutionResult
{
    /// <summary>
    /// The result data if execution was successful.
    /// </summary>
    [Id(6)]
    public T? Data { get; set; }

    /// <summary>
    /// Creates a successful execution result with data.
    /// </summary>
    public static CommandExecutionResult<T> Success(T data, long durationMs = 0)
    {
        return new()
        {
            IsSuccess = true,
            Data = data,
            ExecutionDurationMs = durationMs
        };
    }

    /// <summary>
    /// Creates a failed execution result.
    /// </summary>
    public static new CommandExecutionResult<T> Failed(string errorMessage, string? exceptionDetails = null)
    {
        return new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ExceptionDetails = exceptionDetails
        };
    }
}

/// <summary>
/// Execution context for commands, providing access to services and dependencies.
/// </summary>
public interface ICommandExecutionContext
{
    /// <summary>
    /// Service provider for dependency injection.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Logger for command execution.
    /// </summary>
    Microsoft.Extensions.Logging.ILogger Logger { get; }

    /// <summary>
    /// Gets a service of the specified type.
    /// </summary>
    T GetService<T>() where T : class;

    /// <summary>
    /// Gets a required service of the specified type.
    /// </summary>
    T GetRequiredService<T>() where T : class;

    /// <summary>
    /// Creates a child scope for command execution.
    /// </summary>
    IServiceScope CreateScope();
}
