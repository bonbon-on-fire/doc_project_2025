using Orleans;
using Orleans.Concurrency;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Grain interface for session monitoring and diagnostics.
/// Handles health checks, metrics collection, performance monitoring, and alerting.
/// This interface follows the Interface Segregation Principle by focusing on monitoring-related operations.
/// </summary>
[Alias("AIChat.Orleans.Contracts.ISessionMonitoringGrain")]
public interface ISessionMonitoringGrain : IGrainWithStringKey
{
    /// <summary>
    /// Gets comprehensive session metrics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Session metrics including performance, resource usage, and error rates</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetMetricsAsync")]
    [ReadOnly]
    Task<SessionMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a custom metric for the session.
    /// </summary>
    /// <param name="metricName">Name of the metric</param>
    /// <param name="value">Metric value</param>
    /// <param name="metadata">Optional metadata for the metric</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("RecordMetricAsync")]
    Task RecordMetricAsync(string metricName, double value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets session performance statistics over a time period.
    /// </summary>
    /// <param name="startTime">Start of the time period</param>
    /// <param name="endTime">End of the time period</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Performance statistics for the specified period</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetPerformanceStatsAsync")]
    [ReadOnly]
    Task<PerformanceStats> GetPerformanceStatsAsync(DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a diagnostic collection for troubleshooting.
    /// </summary>
    /// <param name="diagnosticLevel">Level of diagnostic detail to collect</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Diagnostic report with detailed session information</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("CollectDiagnosticsAsync")]
    [ReadOnly]
    Task<DiagnosticReport> CollectDiagnosticsAsync(DiagnosticLevel diagnosticLevel = DiagnosticLevel.Standard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an alert condition for the session.
    /// </summary>
    /// <param name="alert">Alert configuration with conditions and actions</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Registered alert identifier</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    /// <exception cref="SessionValidationException">Thrown when alert configuration is invalid</exception>
    [Alias("RegisterAlertAsync")]
    Task<string> RegisterAlertAsync(AlertConfiguration alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active alerts for the session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>List of active alerts</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetActiveAlertsAsync")]
    [ReadOnly]
    Task<List<SessionAlert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges an alert to stop further notifications.
    /// </summary>
    /// <param name="alertId">Identifier of the alert to acknowledge</param>
    /// <param name="acknowledgedBy">User or system acknowledging the alert</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session or alert does not exist</exception>
    [Alias("AcknowledgeAlertAsync")]
    Task AcknowledgeAlertAsync(string alertId, string acknowledgedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a performance trace for detailed analysis.
    /// </summary>
    /// <param name="traceName">Name of the trace</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Trace identifier for stopping the trace later</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("StartTraceAsync")]
    Task<string> StartTraceAsync(string traceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a performance trace and returns collected data.
    /// </summary>
    /// <param name="traceId">Identifier of the trace to stop</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Collected trace data</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session or trace does not exist</exception>
    [Alias("StopTraceAsync")]
    Task<TraceData> StopTraceAsync(string traceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets resource usage information for the session.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Resource usage including memory, CPU, and bandwidth</returns>
    /// <exception cref="SessionNotFoundException">Thrown when the session does not exist</exception>
    [Alias("GetResourceUsageAsync")]
    [ReadOnly]
    Task<ResourceUsage> GetResourceUsageAsync(CancellationToken cancellationToken = default);
}
