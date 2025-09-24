using AIChat.Orleans.Contracts;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Router interface for dual-mode operation between Orleans grains and direct services.
/// Provides seamless switching between Orleans-based chat operations and direct service calls,
/// supporting the gradual migration strategy with fallback logic and feature flag integration.
/// </summary>
public interface IDualModeRouter
{
    /// <summary>
    /// Executes a chat operation that returns a result, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteAsync<T>(
        Func<IChatGrain, Task<T>> orleansOperation,
        Func<IChatService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a chat operation without a return value, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <param name="orleansOperation">The operation to execute using Orleans grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task ExecuteAsync(
        Func<IChatGrain, Task> orleansOperation,
        Func<IChatService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a chat-specific operation that returns a result, routing to Orleans grain or direct service based on configuration.
    /// Uses the chat ID to route to the appropriate Orleans grain.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="chatId">Chat ID to use for Orleans grain routing</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteAsync<T>(
        Func<IChatGrain, Task<T>> orleansOperation,
        Func<IChatService, Task<T>> directOperation,
        string operationName,
        string chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a chat-specific operation without a return value, routing to Orleans grain or direct service based on configuration.
    /// Uses the chat ID to route to the appropriate Orleans grain.
    /// </summary>
    /// <param name="orleansOperation">The operation to execute using Orleans grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="chatId">Chat ID to use for Orleans grain routing</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task ExecuteAsync(
        Func<IChatGrain, Task> orleansOperation,
        Func<IChatService, Task> directOperation,
        string operationName,
        string chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Orleans routing is currently enabled based on feature flags and system health.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans routing is enabled and healthy, false otherwise</returns>
    Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive health check of the router and its dependencies.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status including Orleans connectivity and feature flag status</returns>
    Task<RouterHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current routing metrics for monitoring and observability.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Current router performance and usage metrics</returns>
    Task<RouterMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the health status of the dual-mode router.
/// </summary>
public record RouterHealthStatus
{
    /// <summary>
    /// Gets whether the router is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets whether Orleans routing is available and healthy.
    /// </summary>
    public required bool IsOrleansHealthy { get; init; }

    /// <summary>
    /// Gets whether direct service routing is available and healthy.
    /// </summary>
    public required bool IsDirectServiceHealthy { get; init; }

    /// <summary>
    /// Gets whether Orleans feature flag is enabled.
    /// </summary>
    public required bool IsOrleansEnabled { get; init; }

    /// <summary>
    /// Gets the current routing mode being used.
    /// </summary>
    public required RouterMode CurrentMode { get; init; }

    /// <summary>
    /// Gets any error messages or status details.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the timestamp of this health check.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents performance and usage metrics for the dual-mode router.
/// </summary>
public record RouterMetrics
{
    /// <summary>
    /// Gets the total number of operations executed.
    /// </summary>
    public long TotalOperations { get; init; }

    /// <summary>
    /// Gets the number of operations routed to Orleans.
    /// </summary>
    public long OrleansOperations { get; init; }

    /// <summary>
    /// Gets the number of operations routed to direct services.
    /// </summary>
    public long DirectServiceOperations { get; init; }

    /// <summary>
    /// Gets the number of times fallback to direct service was used.
    /// </summary>
    public long FallbackOperations { get; init; }

    /// <summary>
    /// Gets the number of failed operations.
    /// </summary>
    public long FailedOperations { get; init; }

    /// <summary>
    /// Gets the average execution time for Orleans operations in milliseconds.
    /// </summary>
    public double OrleansAverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Gets the average execution time for direct service operations in milliseconds.
    /// </summary>
    public double DirectServiceAverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Gets the timestamp when metrics were collected.
    /// </summary>
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the current routing mode of the dual-mode router.
/// </summary>
public enum RouterMode
{
    /// <summary>
    /// Orleans routing is primary with fallback to direct services.
    /// </summary>
    Orleans,

    /// <summary>
    /// Direct service routing only (Orleans disabled or unhealthy).
    /// </summary>
    DirectService,

    /// <summary>
    /// Router is degraded - some operations may fail.
    /// </summary>
    Degraded
}

/// <summary>
/// Exception thrown when router operations fail.
/// </summary>
public class RouterException : Exception
{
    /// <summary>
    /// Initializes a new instance of the RouterException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public RouterException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the RouterException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public RouterException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Gets the operation name that failed.
    /// </summary>
    public string? OperationName { get; init; }

    /// <summary>
    /// Gets whether the Orleans operation was attempted.
    /// </summary>
    public bool OrleansAttempted { get; init; }

    /// <summary>
    /// Gets whether the direct service operation was attempted.
    /// </summary>
    public bool DirectServiceAttempted { get; init; }
}