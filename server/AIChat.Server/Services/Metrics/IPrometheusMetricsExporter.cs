namespace AIChat.Server.Services.Metrics;

/// <summary>
/// Health information for the metrics exporter.
/// </summary>
/// <param name="Status">Health status string (e.g., "Healthy", "Degraded")</param>
/// <param name="IsHealthy">True if the exporter is healthy and collecting metrics</param>
/// <param name="LastUpdateTime">Time of the last metrics update</param>
/// <param name="TotalMetricsExported">Total number of metrics exported</param>
/// <param name="TrackedGrainTypes">List of grain types being tracked</param>
public record MetricsHealthInfo(
    string Status,
    bool IsHealthy,
    DateTime? LastUpdateTime,
    int TotalMetricsExported,
    List<string> TrackedGrainTypes
);

/// <summary>
/// Interface for exporting Orleans metrics to Prometheus format.
/// Bridges between IOrleansMetricsCollector and Prometheus metric types.
/// </summary>
public interface IPrometheusMetricsExporter
{
    /// <summary>
    /// Updates all Prometheus metrics from the current Orleans metrics data.
    /// This method should be called periodically to refresh the metrics.
    /// </summary>
    /// <returns>Task that completes when metrics are updated</returns>
    Task UpdateMetricsAsync();

    /// <summary>
    /// Resets all Prometheus metrics. Use carefully in production.
    /// </summary>
    Task ResetMetricsAsync();

    /// <summary>
    /// Gets the health status of the metrics exporter.
    /// </summary>
    /// <returns>True if the exporter is healthy and collecting metrics</returns>
    Task<bool> IsHealthyAsync();

    /// <summary>
    /// Gets detailed health information about the metrics exporter.
    /// </summary>
    /// <returns>Detailed health information including metrics count and status</returns>
    Task<MetricsHealthInfo> GetHealthInfoAsync();
}