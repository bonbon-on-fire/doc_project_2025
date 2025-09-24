using AIChat.Orleans.Placement;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for managing Orleans grain placement metrics and monitoring in AIChat Server.
/// Provides API endpoints for placement effectiveness data and management operations.
/// Works through Orleans client connection to collect metrics from Orleans Host.
/// </summary>
[ApiController]
[Route("api/orleans/placement")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "Orleans")]
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
            _logger.LogDebug("Retrieving placement metrics summary from Orleans cluster");
            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Successfully retrieved placement metrics: {TotalPlacements} total placements, {Strategies} strategies",
                summary.TotalPlacements, summary.PlacementStrategyCounts.Count);

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving placement metrics from Orleans cluster");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving placement metrics from Orleans cluster",
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://httpstatuses.com/500"
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
            _logger.LogInformation("Resetting placement metrics in Orleans cluster as requested");
            _placementMetrics.ResetMetrics();

            var result = new
            {
                Message = "Placement metrics reset successfully",
                Timestamp = DateTime.UtcNow,
                Source = "AIChat.Server"
            };
            _logger.LogInformation("Placement metrics reset completed successfully");

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting placement metrics in Orleans cluster");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while resetting placement metrics in Orleans cluster",
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://httpstatuses.com/500"
                });
        }
    }

    /// <summary>
    /// Gets placement strategy information for all active grain types.
    /// </summary>
    /// <returns>Dictionary of grain types and their placement strategies</returns>
    /// <response code="200">Returns placement strategy information</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("strategies")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Dictionary<string, string>>> GetPlacementStrategies()
    {
        try
        {
            _logger.LogDebug("Retrieving placement strategies from Orleans cluster");
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

            _logger.LogDebug(
                "Retrieved {StrategyCount} placement strategies from Orleans cluster",
                strategies.Count);

            return Ok(strategies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving placement strategies from Orleans cluster");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving placement strategies from Orleans cluster",
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://httpstatuses.com/500"
                });
        }
    }

    /// <summary>
    /// Gets current silo load distribution showing grain placement across silos.
    /// </summary>
    /// <returns>Dictionary of silo addresses and their grain counts</returns>
    /// <response code="200">Returns silo load distribution</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("distribution")]
    [ProducesResponseType(typeof(Dictionary<string, long>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Dictionary<string, long>>> GetSiloLoadDistribution()
    {
        try
        {
            _logger.LogDebug("Retrieving silo load distribution from Orleans cluster");
            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Retrieved silo load distribution: {SiloCount} silos with total {GrainCount} grains",
                summary.SiloLoadDistribution.Count, summary.SiloLoadDistribution.Values.Sum());

            return Ok(summary.SiloLoadDistribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving silo load distribution from Orleans cluster");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving silo load distribution from Orleans cluster",
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://httpstatuses.com/500"
                });
        }
    }

    /// <summary>
    /// Gets placement effectiveness metrics showing affinity success rates.
    /// </summary>
    /// <returns>Dictionary of grain type pairs and their affinity success counts</returns>
    /// <response code="200">Returns affinity success metrics</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("affinity")]
    [ProducesResponseType(typeof(Dictionary<string, long>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Dictionary<string, long>>> GetAffinityMetrics()
    {
        try
        {
            _logger.LogDebug("Retrieving affinity metrics from Orleans cluster");
            var summary = await _placementMetrics.GetMetricsSummaryAsync();

            _logger.LogDebug(
                "Retrieved affinity metrics: {AffinityPairCount} grain type pairs with affinity tracking",
                summary.AffinitySuccessCounts.Count);

            return Ok(summary.AffinitySuccessCounts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving affinity metrics from Orleans cluster");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving affinity metrics from Orleans cluster",
                    Status = StatusCodes.Status500InternalServerError,
                    Type = "https://httpstatuses.com/500"
                });
        }
    }
}