using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Services.Routing;

/// <summary>
/// <para>
/// Router interface for dual-mode operation between Orleans logging coordination and direct logging services.
/// Provides seamless switching between Orleans-coordinated logging operations and direct file operations,
/// supporting the gradual migration strategy with fallback logic and feature flag integration.
/// </para>
/// <para>
/// Note: Logging operations typically benefit more from direct file access than Orleans coordination,
/// so this router primarily focuses on maintaining the existing direct operations while providing
/// Orleans integration points for future enhancements like distributed logging coordination.
/// </para>
/// </summary>
public interface ILogsRouter
{
    /// <summary>
    /// Executes a logging operation, routing through Orleans coordination or direct file operations based on configuration.
    /// </summary>
    /// <typeparam name="T">The type of result returned by the operation</typeparam>
    /// <param name="orleansOperation">The operation to execute using Orleans logging coordination (future enhancement)</param>
    /// <param name="directOperation">The operation to execute using direct file logging</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the executed operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<T> ExecuteAsync<T>(
        Func<Task<T>> orleansOperation,
        Func<Task<T>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a logging operation without a return value, routing through Orleans coordination or direct file operations.
    /// </summary>
    /// <param name="orleansOperation">The operation to execute using Orleans logging coordination (future enhancement)</param>
    /// <param name="directOperation">The operation to execute using direct file logging</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when operations or operationName are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task ExecuteAsync(
        Func<Task> orleansOperation,
        Func<Task> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a client log entry operation, the primary operation for the LogsController.
    /// </summary>
    /// <param name="logEntry">The log entry to process</param>
    /// <param name="orleansOperation">Optional Orleans-coordinated logging operation (future enhancement)</param>
    /// <param name="directOperation">Direct file logging operation</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>ActionResult representing the HTTP response</returns>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    /// <exception cref="RouterException">Thrown when both Orleans and direct operations fail</exception>
    Task<IActionResult> ExecuteLogEntryAsync(
        JsonElement logEntry,
        Func<JsonElement, Task<IActionResult>>? orleansOperation,
        Func<JsonElement, Task<IActionResult>> directOperation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if Orleans routing is currently enabled based on feature flags and system health.
    /// For logging operations, this may typically return false since direct file operations
    /// are usually more efficient for simple logging scenarios.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if Orleans routing is enabled and beneficial for logging, false otherwise</returns>
    Task<bool> IsOrleansEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a comprehensive health check of the logs router and its dependencies.
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