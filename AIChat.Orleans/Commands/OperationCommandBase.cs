using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIChat.Orleans.Commands;

/// <summary>
/// Base implementation for operation commands.
/// Provides common functionality and enforces command pattern best practices.
/// </summary>
public abstract class OperationCommandBase : IOperationCommand
{
    /// <inheritdoc />
    public string CommandId { get; } = Guid.NewGuid().ToString();

    /// <inheritdoc />
    public string OperationId { get; protected set; }

    /// <inheritdoc />
    public string ChatId { get; protected set; }

    /// <inheritdoc />
    public string UserId { get; protected set; }

    /// <inheritdoc />
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the command base.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    protected OperationCommandBase(string operationId, string chatId, string userId)
    {
        OperationId = operationId ?? throw new ArgumentNullException(nameof(operationId));
        ChatId = chatId ?? throw new ArgumentNullException(nameof(chatId));
        UserId = userId ?? throw new ArgumentNullException(nameof(userId));
    }

    /// <inheritdoc />
    public virtual CommandValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(OperationId))
            errors.Add("OperationId is required");

        if (string.IsNullOrWhiteSpace(ChatId))
            errors.Add("ChatId is required");

        if (string.IsNullOrWhiteSpace(UserId))
            errors.Add("UserId is required");

        // Allow derived classes to add their own validation
        var customValidation = ValidateCustom();
        if (!customValidation.IsValid)
        {
            errors.AddRange(customValidation.Errors);
        }

        return errors.Count > 0
            ? CommandValidationResult.Failed(errors.ToArray())
            : customValidation.IsValid && customValidation.Warnings.Count > 0
                ? CommandValidationResult.WithWarnings(customValidation.Warnings.ToArray())
                : CommandValidationResult.Success();
    }

    /// <summary>
    /// Performs custom validation specific to the command implementation.
    /// </summary>
    /// <returns>Custom validation result</returns>
    protected virtual CommandValidationResult ValidateCustom()
    {
        return CommandValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<CommandExecutionResult> ExecuteAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var logger = context.Logger;

        try
        {
            logger.LogInformation(
                "Executing command {CommandType} with ID {CommandId} for operation {OperationId}",
                GetType().Name, CommandId, OperationId);

            // Validate before execution
            var validation = Validate();
            if (!validation.IsValid)
            {
                var errorMessage = $"Command validation failed: {string.Join(", ", validation.Errors)}";
                logger.LogError(errorMessage);
                return CommandExecutionResult.Failed(errorMessage);
            }

            // Log warnings if any
            if (validation.Warnings.Count > 0)
            {
                logger.LogWarning("Command validation warnings: {Warnings}",
                    string.Join(", ", validation.Warnings));
            }

            // Execute the command
            var result = await ExecuteInternalAsync(context, cancellationToken);
            result.ExecutionDurationMs = stopwatch.ElapsedMilliseconds;

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Command {CommandType} completed successfully in {Duration}ms",
                    GetType().Name, result.ExecutionDurationMs);
            }
            else
            {
                logger.LogError(
                    "Command {CommandType} failed after {Duration}ms: {ErrorMessage}",
                    GetType().Name, result.ExecutionDurationMs, result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Command {CommandType} was cancelled after {Duration}ms",
                GetType().Name, stopwatch.ElapsedMilliseconds);

            return CommandExecutionResult.Failed(
                "Command execution was cancelled",
                "OperationCanceledException");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Command {CommandType} threw an exception after {Duration}ms",
                GetType().Name, stopwatch.ElapsedMilliseconds);

            return CommandExecutionResult.Failed(
                $"Command execution failed with exception: {ex.Message}",
                ex.ToString());
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Internal implementation of command execution.
    /// Override this method in derived classes to implement command logic.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command execution result</returns>
    protected abstract Task<CommandExecutionResult> ExecuteInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public virtual Task<bool> UndoAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Default implementation - most commands don't support undo
        context.Logger.LogWarning(
            "Undo operation is not supported for command {CommandType}",
            GetType().Name);

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public virtual Dictionary<string, object> GetMetadata()
    {
        return new Dictionary<string, object>
        {
            ["CommandId"] = CommandId,
            ["CommandType"] = GetType().Name,
            ["OperationId"] = OperationId,
            ["ChatId"] = ChatId,
            ["UserId"] = UserId,
            ["CreatedAt"] = CreatedAt
        };
    }
}

/// <summary>
/// Generic base implementation for typed operation commands.
/// </summary>
/// <typeparam name="TRequest">The request data type</typeparam>
/// <typeparam name="TResponse">The response data type</typeparam>
public abstract class OperationCommandBase<TRequest, TResponse> : OperationCommandBase, IOperationCommand<TRequest, TResponse>
{
    /// <inheritdoc />
    public TRequest Request { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the typed command base.
    /// </summary>
    /// <param name="operationId">The operation ID</param>
    /// <param name="chatId">The chat ID</param>
    /// <param name="userId">The user ID</param>
    /// <param name="request">The request data</param>
    protected OperationCommandBase(string operationId, string chatId, string userId, TRequest request)
        : base(operationId, chatId, userId)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <inheritdoc />
    public new async Task<CommandExecutionResult<TResponse>> ExecuteAsync(ICommandExecutionContext context, CancellationToken cancellationToken = default)
    {
        var baseResult = await base.ExecuteAsync(context, cancellationToken);

        // Convert base result to typed result
        if (!baseResult.IsSuccess)
        {
            return CommandExecutionResult<TResponse>.Failed(
                baseResult.ErrorMessage!,
                baseResult.ExceptionDetails);
        }

        // For typed commands, the ExecuteInternalAsync should populate the result data
        var typedResult = baseResult as CommandExecutionResult<TResponse>;
        if (typedResult != null)
        {
            return typedResult;
        }

        // If the base result doesn't contain typed data, execute the typed version
        return await ExecuteTypedInternalAsync(context, cancellationToken);
    }

    /// <summary>
    /// Internal implementation of typed command execution.
    /// Override this method in derived classes to implement typed command logic.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Typed command execution result</returns>
    protected abstract Task<CommandExecutionResult<TResponse>> ExecuteTypedInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Default implementation that calls the typed version.
    /// </summary>
    protected sealed override async Task<CommandExecutionResult> ExecuteInternalAsync(
        ICommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var typedResult = await ExecuteTypedInternalAsync(context, cancellationToken);
        return typedResult;
    }

    /// <inheritdoc />
    public override Dictionary<string, object> GetMetadata()
    {
        var metadata = base.GetMetadata();
        metadata["RequestType"] = typeof(TRequest).Name;
        metadata["ResponseType"] = typeof(TResponse).Name;

        // Add request data if it's serializable
        if (Request != null)
        {
            try
            {
                metadata["Request"] = Request;
            }
            catch (Exception)
            {
                // If request is not serializable, just add the type
                metadata["Request"] = typeof(TRequest).Name;
            }
        }

        return metadata;
    }
}

/// <summary>
/// Implementation of command execution context.
/// </summary>
public class CommandExecutionContext : ICommandExecutionContext
{
    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc />
    public ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of the execution context.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="logger">Logger for command execution</param>
    public CommandExecutionContext(IServiceProvider serviceProvider, ILogger logger)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public T GetService<T>() where T : class
    {
        return ServiceProvider.GetService<T>()!;
    }

    /// <inheritdoc />
    public T GetRequiredService<T>() where T : class
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <inheritdoc />
    public IServiceScope CreateScope()
    {
        return ServiceProvider.CreateScope();
    }
}