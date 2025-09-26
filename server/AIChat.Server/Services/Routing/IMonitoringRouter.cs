using AIChat.Orleans.Contracts;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// Router interface for dual-mode operation between Orleans monitoring grains and direct monitoring services.
/// Provides seamless switching between Orleans-based monitoring operations and direct service calls,
/// supporting the gradual migration strategy with fallback logic and feature flag integration.
/// </summary>
public interface IMonitoringRouter
{
    /// <summary>
    /// Executes a monitoring operation that returns a result, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans monitoring grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteAsync<T>(
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a monitoring operation without a return value, routing to Orleans grain or direct service based on configuration.
    /// </summary>
    /// <param name="orleansOperation">The operation to execute using Orleans monitoring grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task ExecuteAsync(
        Func<ISessionMonitoringGrain, Task> orleansOperation,
        Func<ProductionMonitoringService, Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a system-wide monitoring operation using Orleans system monitoring capabilities.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans system monitoring</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteSystemOperationAsync<T>(
        Func<IHealthCheckGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a session-specific monitoring operation, routing to Orleans grain with session context.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="sessionId">Session ID for grain key resolution</param>
    /// <param name="orleansOperation">The operation to execute using Orleans session monitoring grain</param>
    /// <param name="directOperation">The fallback operation to execute using direct service</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when sessionId, operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteSessionOperationAsync<T>(
        string sessionId,
        Func<ISessionMonitoringGrain, Task<T>> orleansOperation,
        Func<ProductionMonitoringService, Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Orleans routing is currently enabled based on feature flags and system health.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans routing is enabled and healthy, false otherwise</returns>
    Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive health check of the monitoring router and its dependencies.
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