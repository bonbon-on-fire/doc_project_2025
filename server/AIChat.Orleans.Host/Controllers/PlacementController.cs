using AIChat.Orleans.Placement;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Orleans.Host.Controllers;

/// <summary>
/// Controller for managing Orleans grain placement metrics and monitoring.
/// Provides API endpoints for placement effectiveness data and management operations.
/// </summary>
[ApiController]
[Route("api/orleans/placement")]
[Produces("application/json")]
public class PlacementController : ControllerBase
{
    private readonly IPlacementMetricsCollector _placementMetrics;
    private readonly ILogger<PlacementController> _logger;

    /// <summary>
    /// Initializes a new instance of the PlacementController.
    /// </summary>
    /// <param name="placementMetrics">Placement metrics collector service</param>
    /// <param name="logger">Logger instance</param>
    public PlacementController(
        IPlacementMetricsCollector placementMetrics,
        ILogger<PlacementController> logger)
    {
        _placementMetrics = placementMetrics ?? throw new ArgumentNullException(nameof(placementMetrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets comprehensive placement metrics including effectiveness data and silo distribution.
    /// </summary>
    /// <returns>Placement metrics summary with placement strategies, affinity success rates, and load distribution</returns>
    /// <response code="200">Returns placement metrics summary</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(PlacementMetricsSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PlacementMetricsSummary>> GetMetrics()
    {
        try
        {
            _logger.LogDebug("Retrieving placement metrics summary");
            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved placement metrics: {TotalPlacements} total placements, {Strategies} strategies",
                summary.TotalPlacements, summary.PlacementStrategyCounts.Count);

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving placement metrics");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving placement metrics",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }

    /// <summary>
    /// Resets all collected placement metrics data.
    /// This operation clears all placement counters and restarts metrics collection.
    /// </summary>
    /// <returns>Success confirmation</returns>
    /// <response code="200">Metrics reset successfully</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpPost("metrics/reset")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public ActionResult ResetMetrics()
    {
        try
        {
            _logger.LogInformation("Resetting placement metrics as requested");
            _placementMetrics.ResetMetrics();

            var result = new { Message = "Placement metrics reset successfully", Timestamp = DateTime.UtcNow };
            _logger.LogInformation("Placement metrics reset completed successfully");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting placement metrics");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while resetting placement metrics",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }

    /// <summary>
    /// Gets placement strategy information for all active grain types.
    /// </summary>
    /// <returns>Dictionary of grain types and their placement strategies</returns>
    /// <response code="200">Returns placement strategy information</response>
    [HttpGet("strategies")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, string>>> GetPlacementStrategies()
    {
        try
        {
            var summary = await _placementMetrics.GetMetricsSummaryAsync();
            var strategies = new Dictionary<string, string>();

            foreach (var kvp in summary.PlacementStrategyCounts)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2)
                {
                    strategies[parts[0]] = parts[1]; // GrainType -> PlacementStrategy
                }
            }

            return Ok(strategies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving placement strategies");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Gets current silo load distribution showing grain placement across silos.
    /// </summary>
    /// <returns>Dictionary of silo addresses and their grain counts</returns>
    /// <response code="200">Returns silo load distribution</response>
    [HttpGet("distribution")]
    [ProducesResponseType(typeof(Dictionary<string, long>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, long>>> GetSiloLoadDistribution()
    {
        try
        {
            var summary = await _placementMetrics.GetMetricsSummaryAsync();
            return Ok(summary.SiloLoadDistribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving silo load distribution");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}