using AIChat.Orleans.Contracts;
using AIChat.Orleans.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Orleans.Host.Controllers;

/// <summary>
/// Controller for accessing Orleans cluster metrics and performance data.
/// Provides API endpoints for grain lifecycle monitoring and operational metrics.
/// Uses the Grain Facade pattern to access metrics through grain interfaces.
/// </summary>
/// <remarks>
/// This controller demonstrates the architectural separation between WebHost and Orleans Silo.
/// Instead of directly injecting internal services (IOrleansMetricsCollector), it uses
/// IGrainFactory to call IClusterMetricsGrain, maintaining a clean client-server boundary.
/// </remarks>
[ApiController]
[Route("api/orleans/metrics")]
[Produces("application/json")]
public class MetricsController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<MetricsController> _logger;

    /// <summary>
    /// Initializes a new instance of the MetricsController.
    /// </summary>
    /// <param name="grainFactory">Orleans grain factory for accessing monitoring grains</param>
    /// <param name="logger">Logger instance</param>
    public MetricsController(
        IGrainFactory grainFactory,
        ILogger<MetricsController> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets comprehensive Orleans metrics summary including grain lifecycle and performance data.
    /// </summary>
    /// <returns>Aggregated metrics data for all grains in the cluster</returns>
    /// <response code="200">Returns Orleans metrics summary</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet]
    [ProducesResponseType(typeof(OrleansMetricsSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OrleansMetricsSummary>> GetMetricsSummary()
    {
        try
        {
            _logger.LogDebug("Retrieving Orleans metrics summary via ClusterMetricsGrain");

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var metricsGrain = _grainFactory.GetGrain<IClusterMetricsGrain>(0);
            var summary = await metricsGrain.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved metrics: {TotalGrains} active grains, {GrainTypes} types",
                summary.TotalActiveGrains,
                summary.GrainTypeMetrics.Count);

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Orleans metrics summary");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving Orleans metrics",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }

    /// <summary>
    /// Gets detailed metrics for a specific grain type.
    /// </summary>
    /// <param name="grainType">Type of grain to get metrics for (e.g., "UserGrain", "ChatGrain")</param>
    /// <returns>Detailed metrics for the specified grain type</returns>
    /// <response code="200">Returns grain type metrics</response>
    /// <response code="400">Invalid grain type provided</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("{grainType}")]
    [ProducesResponseType(typeof(GrainTypeMetrics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GrainTypeMetrics>> GetGrainTypeMetrics(string grainType)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(grainType))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = "Grain type is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            _logger.LogDebug("Retrieving metrics for grain type: {GrainType} via ClusterMetricsGrain", grainType);

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var metricsGrain = _grainFactory.GetGrain<IClusterMetricsGrain>(0);
            var metrics = await metricsGrain.GetGrainTypeMetricsAsync(grainType);

            _logger.LogDebug(
                "Successfully retrieved metrics for {GrainType}: {ActiveInstances} active instances",
                grainType,
                metrics.ActiveInstances);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metrics for grain type: {GrainType}", grainType);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = $"An error occurred while retrieving metrics for grain type '{grainType}'",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }

    /// <summary>
    /// Resets all collected Orleans metrics. Use carefully in production environments.
    /// </summary>
    /// <returns>Success confirmation</returns>
    /// <response code="200">Metrics reset successfully</response>
    /// <response code="500">Internal server error occurred</response>
    /// <remarks>
    /// This operation clears all metric counters and restarts collection.
    /// Should be used primarily for testing or after maintenance windows.
    /// </remarks>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ResetMetrics()
    {
        try
        {
            _logger.LogInformation("Resetting Orleans metrics as requested");

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var metricsGrain = _grainFactory.GetGrain<IClusterMetricsGrain>(0);
            await metricsGrain.ResetMetricsAsync();

            var result = new { Message = "Orleans metrics reset successfully", Timestamp = DateTime.UtcNow };
            _logger.LogInformation("Orleans metrics reset completed successfully");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting Orleans metrics");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while resetting Orleans metrics",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }
}
