using AIChat.Orleans.Contracts;
using AIChat.Server.Services;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Router interface for dual-mode operation between Orleans mode grains and direct mode services.
/// Provides seamless switching between Orleans-based mode operations and direct service calls,
/// supporting the gradual migration strategy with fallback logic and feature flag integration.
/// </summary>
public interface IModeRouter
{
    /// <summary>
    /// Executes a mode operation that returns a result, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans mode grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteAsync<T>(
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a mode operation without a return value, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <param name="orleansOperation">The operation to execute using Orleans mode grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task ExecuteAsync(
        Func<IModeGrain, Task> orleansOperation,
        Func<IModeService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a user-specific mode operation, routing to Orleans grain with user context or direct service.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="userId">User ID for grain key resolution and permission context</param>
    /// <param name="orleansOperation">The operation to execute using Orleans mode grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId, operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteUserOperationAsync<T>(
        string userId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a mode-specific operation, routing to Orleans grain with mode context or direct service.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="modeId">Mode ID for grain key resolution</param>
    /// <param name="orleansOperation">The operation to execute using Orleans mode grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when modeId, operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteModeOperationAsync<T>(
        string modeId,
        Func<IModeGrain, Task<T>> orleansOperation,
        Func<IModeService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Orleans routing is currently enabled based on feature flags and system health.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans routing is enabled and healthy, false otherwise</returns>
    Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive health check of the mode router and its dependencies.
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