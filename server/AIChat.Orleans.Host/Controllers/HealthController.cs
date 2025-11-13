using AIChat.Orleans.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Orleans.Host.Controllers;

/// <summary>
/// Controller for Orleans cluster health monitoring and diagnostics.
/// Provides API endpoints for cluster health status and silo information.
/// Uses the Grain Facade pattern to access health data through grain interfaces.
/// </summary>
/// <remarks>
/// This controller demonstrates the architectural separation between WebHost and Orleans Silo.
/// Instead of directly accessing management APIs, it uses IGrainFactory to call
/// IClusterHealthGrain, maintaining a clean client-server boundary.
/// </remarks>
[ApiController]
[Route("api/orleans/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    /// Initializes a new instance of the HealthController.
    /// </summary>
    /// <param name="grainFactory">Orleans grain factory for accessing monitoring grains</param>
    /// <param name="logger">Logger instance</param>
    public HealthController(
        IGrainFactory grainFactory,
        ILogger<HealthController> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets comprehensive cluster health status including silo count and connectivity.
    /// </summary>
    /// <returns>Cluster health status with diagnostics</returns>
    /// <response code="200">Returns cluster health status</response>
    /// <response code="503">Cluster is unhealthy</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ClusterHealthStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ClusterHealthStatus), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ClusterHealthStatus>> GetClusterHealth()
    {
        try
        {
            _logger.LogDebug("Retrieving cluster health status via ClusterHealthGrain");

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var healthGrain = _grainFactory.GetGrain<IClusterHealthGrain>(0);
            var health = await healthGrain.GetClusterHealthAsync();

            _logger.LogDebug(
                "Cluster health: {Status} with {SiloCount} active silos",
                health.Status,
                health.ActiveSiloCount);

            // Return appropriate HTTP status code based on health
            if (health.IsHealthy)
            {
                return Ok(health);
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cluster health status");

            var errorHealth = new ClusterHealthStatus
            {
                IsHealthy = false,
                Status = "Error",
                CheckedAt = DateTime.UtcNow,
                Warnings = [$"Failed to query cluster health: {ex.Message}"]
            };

            return StatusCode(StatusCodes.Status503ServiceUnavailable, errorHealth);
        }
    }

    /// <summary>
    /// Gets detailed information about all active silos in the cluster.
    /// </summary>
    /// <returns>List of silo information with status and activation counts</returns>
    /// <response code="200">Returns silo status information</response>
    /// <response code="500">Internal server error occurred</response>
    [HttpGet("silos")]
    [ProducesResponseType(typeof(List<SiloInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<SiloInfo>>> GetSiloStatus()
    {
        try
        {
            _logger.LogDebug("Retrieving silo status information via ClusterHealthGrain");

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var healthGrain = _grainFactory.GetGrain<IClusterHealthGrain>(0);
            var silos = await healthGrain.GetSiloStatusAsync();

            _logger.LogDebug("Successfully retrieved status for {Count} silos", silos.Count);

            return Ok(silos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving silo status information");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "An error occurred while retrieving silo status",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }

    /// <summary>
    /// Performs a quick ping test to verify cluster responsiveness.
    /// </summary>
    /// <returns>Ping response with timestamp</returns>
    /// <response code="200">Ping successful</response>
    /// <response code="500">Ping failed</response>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Ping()
    {
        try
        {
            _logger.LogTrace("Ping request received");

            // Use grain facade pattern - call monitoring grain instead of direct service injection
            var healthGrain = _grainFactory.GetGrain<IClusterHealthGrain>(0);
            var timestamp = await healthGrain.PingAsync();

            var response = new
            {
                Message = "Pong",
                Timestamp = timestamp,
                ResponseTime = DateTime.UtcNow
            };

            _logger.LogTrace("Ping successful at {Timestamp}", timestamp);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Detail = "Ping failed",
                    Status = StatusCodes.Status500InternalServerError
                });
        }
    }
}
