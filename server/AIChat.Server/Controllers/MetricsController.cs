using AIChat.Server.Services.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for Prometheus metrics health and status.
/// The /metrics endpoint is handled by Prometheus middleware.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IPrometheusMetricsExporter _metricsExporter;
    private readonly ILogger<MetricsController> _logger;

    /// <summary>
    /// Initializes a new instance of the MetricsController.
    /// </summary>
    /// <param name="metricsExporter">Metrics exporter for health checks</param>
    /// <param name="logger">Logger instance for diagnostics</param>
    public MetricsController(
        IPrometheusMetricsExporter metricsExporter,
        ILogger<MetricsController> logger)
    {
        _metricsExporter = metricsExporter ?? throw new ArgumentNullException(nameof(metricsExporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Health check endpoint for metrics exporter.
    /// </summary>
    /// <returns>Health status of metrics collection</returns>
    [HttpGet("health")]
    [Produces("application/json")]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var healthInfo = await _metricsExporter.GetHealthInfoAsync();

            var response = new
            {
                healthInfo.Status,
                healthInfo.IsHealthy,
                Timestamp = DateTime.UtcNow,
                Service = "PrometheusMetricsEndpoint",
                healthInfo.LastUpdateTime,
                healthInfo.TotalMetricsExported,
                healthInfo.TrackedGrainTypes,
                Message = healthInfo.IsHealthy
                    ? "Prometheus metrics endpoint is healthy and available at /metrics"
                    : "Metrics exporter is degraded - check recent updates"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metrics health check failed");
            return StatusCode(500, new
            {
                Status = "Error",
                Timestamp = DateTime.UtcNow,
                Service = "PrometheusMetricsEndpoint",
                Error = ex.Message
            });
        }
    }
}