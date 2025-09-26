using System.ComponentModel.DataAnnotations;
using AIChat.Server.Services.Recovery;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for point-in-time recovery operations and management.
/// Provides REST API endpoints for time-travel debugging capabilities.
/// </summary>
[ApiController]
[Route("api/recovery/point-in-time")]
[Produces("application/json")]
public class PointInTimeRecoveryController : ControllerBase
{
    private readonly IPointInTimeRecoveryService _recoveryService;
    private readonly IRecoveryAuditService _auditService;
    private readonly IEventProjectionFactory _projectionFactory;
    private readonly ILogger<PointInTimeRecoveryController> _logger;

    /// <summary>
    /// Initializes a new instance of the PointInTimeRecoveryController.
    /// </summary>
    /// <param name="recoveryService">Point-in-time recovery service</param>
    /// <param name="auditService">Recovery audit service</param>
    /// <param name="projectionFactory">Event projection factory</param>
    /// <param name="logger">Logger for diagnostics</param>
    public PointInTimeRecoveryController(
        IPointInTimeRecoveryService recoveryService,
        IRecoveryAuditService auditService,
        IEventProjectionFactory projectionFactory,
        ILogger<PointInTimeRecoveryController> logger)
    {
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _projectionFactory = projectionFactory ?? throw new ArgumentNullException(nameof(projectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initiates a point-in-time recovery operation.
    /// </summary>
    /// <param name="request">The recovery request details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The recovery operation response</returns>
    /// <response code="202">Recovery operation started successfully</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="404">Grain type not supported</response>
    /// <response code="409">Recovery not possible at requested point</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("recover")]
    [ProducesResponseType(typeof(RecoveryOperationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryOperationResponse>> StartRecoveryAsync(
        [FromBody] PointInTimeRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            _logger.LogInformation(
                "Received point-in-time recovery request for grain {GrainId} by {InitiatedBy}",
                request.GrainId, request.InitiatedBy);

            // Validate grain type is supported
            if (!_projectionFactory.IsGrainTypeSupported(request.GrainType))
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Grain Type Not Supported",
                    Detail = $"Grain type '{request.GrainType}' is not supported for point-in-time recovery",
                    Status = StatusCodes.Status404NotFound,
                    Instance = HttpContext.Request.Path
                });
            }

            // Validate recovery is possible
            if (request.TargetTimestamp.HasValue)
            {
                var validationResult = await _recoveryService.ValidateRecoveryPossibilityAsync(
                    request.GrainId,
                    request.TargetTimestamp.Value,
                    request.ValidationLevel,
                    cancellationToken);

                if (!validationResult.IsPossible)
                {
                    return Conflict(new ProblemDetails
                    {
                        Title = "Recovery Not Possible",
                        Detail = string.Join("; ", validationResult.Issues),
                        Status = StatusCodes.Status409Conflict,
                        Instance = HttpContext.Request.Path
                    });
                }
            }

            // Start background recovery if requested
            if (request.UseBackgroundRecovery)
            {
                var operationId = await _recoveryService.StartBackgroundRecoveryAsync(
                    request,
                    _projectionFactory.CreateProjection<object>,
                    cancellationToken);

                // Start audit tracking
                var auditRequest = RecoveryAuditRequest.FromRecoveryRequest(
                    operationId,
                    request,
                    "RestAPI",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                var auditId = await _auditService.StartRecoveryAuditAsync(auditRequest, cancellationToken);

                return Accepted(new RecoveryOperationResponse
                {
                    OperationId = operationId,
                    AuditId = auditId,
                    Status = "Started",
                    Message = "Background recovery operation started successfully",
                    EstimatedCompletionTime = DateTime.UtcNow.AddMinutes(5) // Rough estimate
                });
            }
            else
            {
                // Perform synchronous recovery (not recommended for large operations)
                return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
                {
                    Title = "Synchronous Recovery Not Implemented",
                    Detail = "Synchronous recovery is not implemented. Please use background recovery (UseBackgroundRecovery = true)",
                    Status = StatusCodes.Status501NotImplemented,
                    Instance = HttpContext.Request.Path
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recovery for grain {GrainId}", request.GrainId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Recovery Failed",
                Detail = "An error occurred while starting the recovery operation",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets the status of a recovery operation.
    /// </summary>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The current status of the recovery operation</returns>
    /// <response code="200">Recovery status retrieved successfully</response>
    /// <response code="404">Recovery operation not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("operations/{operationId}")]
    [ProducesResponseType(typeof(PointInTimeRecoveryResult<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PointInTimeRecoveryResult<object>>> GetRecoveryStatusAsync(
        [FromRoute] string operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _recoveryService.GetRecoveryStatusAsync(operationId, cancellationToken);

            if (result == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Recovery Operation Not Found",
                    Detail = $"Recovery operation with ID '{operationId}' was not found",
                    Status = StatusCodes.Status404NotFound,
                    Instance = HttpContext.Request.Path
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery status for operation {OperationId}", operationId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Recovery Status",
                Detail = "An error occurred while retrieving the recovery operation status",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Cancels a recovery operation.
    /// </summary>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the cancellation attempt</returns>
    /// <response code="200">Recovery operation cancelled successfully</response>
    /// <response code="404">Recovery operation not found</response>
    /// <response code="409">Recovery operation cannot be cancelled (already completed)</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("operations/{operationId}/cancel")]
    [ProducesResponseType(typeof(CancellationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CancellationResponse>> CancelRecoveryAsync(
        [FromRoute] string operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cancelled = await _recoveryService.CancelRecoveryAsync(operationId, cancellationToken);

            if (!cancelled)
            {
                // Check if operation exists
                var status = await _recoveryService.GetRecoveryStatusAsync(operationId, cancellationToken);
                if (status == null)
                {
                    return NotFound(new ProblemDetails
                    {
                        Title = "Recovery Operation Not Found",
                        Detail = $"Recovery operation with ID '{operationId}' was not found",
                        Status = StatusCodes.Status404NotFound,
                        Instance = HttpContext.Request.Path
                    });
                }

                return Conflict(new ProblemDetails
                {
                    Title = "Cannot Cancel Recovery",
                    Detail = "The recovery operation cannot be cancelled because it is already completed or in a non-cancellable state",
                    Status = StatusCodes.Status409Conflict,
                    Instance = HttpContext.Request.Path
                });
            }

            return Ok(new CancellationResponse
            {
                OperationId = operationId,
                Cancelled = true,
                Message = "Recovery operation cancelled successfully",
                CancelledAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel recovery operation {OperationId}", operationId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Cancel Recovery",
                Detail = "An error occurred while cancelling the recovery operation",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets available recovery points for a grain within a time range.
    /// </summary>
    /// <param name="grainId">The grain ID</param>
    /// <param name="fromTime">Start time for recovery points (optional)</param>
    /// <param name="toTime">End time for recovery points (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Available recovery points for the grain</returns>
    /// <response code="200">Recovery points retrieved successfully</response>
    /// <response code="400">Invalid time range parameters</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("grains/{grainId}/recovery-points")]
    [ProducesResponseType(typeof(IReadOnlyList<RecoveryPoint>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<RecoveryPoint>>> GetAvailableRecoveryPointsAsync(
        [FromRoute] string grainId,
        [FromQuery] DateTimeOffset? fromTime = null,
        [FromQuery] DateTimeOffset? toTime = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (fromTime.HasValue && toTime.HasValue && fromTime >= toTime)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Time Range",
                    Detail = "fromTime must be earlier than toTime",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            var recoveryPoints = await _recoveryService.GetAvailableRecoveryPointsAsync(
                grainId,
                fromTime,
                toTime,
                cancellationToken);

            return Ok(recoveryPoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery points for grain {GrainId}", grainId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Recovery Points",
                Detail = "An error occurred while retrieving recovery points",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Validates if recovery to a specific point in time is possible.
    /// </summary>
    /// <param name="request">The validation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with feasibility and recommendations</returns>
    /// <response code="200">Validation completed successfully</response>
    /// <response code="400">Invalid validation request</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(PointInTimeRecoveryValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PointInTimeRecoveryValidationResult>> ValidateRecoveryAsync(
        [FromBody] RecoveryValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            PointInTimeRecoveryValidationResult result;

            if (request.TargetTimestamp.HasValue)
            {
                result = await _recoveryService.ValidateRecoveryPossibilityAsync(
                    request.GrainId,
                    request.TargetTimestamp.Value,
                    request.ValidationLevel,
                    cancellationToken);
            }
            else if (request.TargetVersion.HasValue)
            {
                result = await _recoveryService.ValidateRecoveryPossibilityAsync(
                    request.GrainId,
                    request.TargetVersion.Value,
                    request.ValidationLevel,
                    cancellationToken);
            }
            else
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Validation Request",
                    Detail = "Either TargetTimestamp or TargetVersion must be specified",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate recovery for grain {GrainId}", request.GrainId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Validation Failed",
                Detail = "An error occurred while validating the recovery request",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets recovery metrics and statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery metrics</returns>
    /// <response code="200">Metrics retrieved successfully</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(RecoveryMetrics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryMetrics>> GetRecoveryMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _recoveryService.GetRecoveryMetricsAsync(cancellationToken);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery metrics");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Metrics",
                Detail = "An error occurred while retrieving recovery metrics",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets the health status of the point-in-time recovery service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status information</returns>
    /// <response code="200">Health status retrieved successfully</response>
    /// <response code="503">Service is unhealthy</response>
    [HttpGet("health")]
    [ProducesResponseType(typeof(PointInTimeRecoveryHealthStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PointInTimeRecoveryHealthStatus), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PointInTimeRecoveryHealthStatus>> GetHealthStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthStatus = await _recoveryService.GetHealthStatusAsync(cancellationToken);

            if (healthStatus.IsHealthy)
            {
                return Ok(healthStatus);
            }
            else
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, healthStatus);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health status");

            var unhealthyStatus = PointInTimeRecoveryHealthStatus.Unhealthy(
                "PointInTimeRecoveryService",
                [$"Health check failed: {ex.Message}"]);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, unhealthyStatus);
        }
    }
}

/// <summary>
/// Response for recovery operation start.
/// </summary>
public record RecoveryOperationResponse
{
    /// <summary>
    /// The unique identifier for the recovery operation.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// The audit ID for tracking the operation.
    /// </summary>
    public string? AuditId { get; init; }

    /// <summary>
    /// Current status of the operation.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Estimated completion time.
    /// </summary>
    public DateTime? EstimatedCompletionTime { get; init; }

    /// <summary>
    /// Links to track the operation.
    /// </summary>
    public Dictionary<string, string>? Links { get; init; }
}

/// <summary>
/// Response for recovery cancellation.
/// </summary>
public record CancellationResponse
{
    /// <summary>
    /// The recovery operation ID that was cancelled.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Whether the cancellation was successful.
    /// </summary>
    public required bool Cancelled { get; init; }

    /// <summary>
    /// Human-readable message about the cancellation.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// When the cancellation occurred.
    /// </summary>
    public DateTimeOffset CancelledAt { get; init; }
}

/// <summary>
/// Request for recovery validation.
/// </summary>
public record RecoveryValidationRequest
{
    /// <summary>
    /// The grain ID to validate recovery for.
    /// </summary>
    [Required]
    public required string GrainId { get; init; }

    /// <summary>
    /// The target timestamp to validate recovery to.
    /// </summary>
    public DateTimeOffset? TargetTimestamp { get; init; }

    /// <summary>
    /// The target version to validate recovery to.
    /// </summary>
    public long? TargetVersion { get; init; }

    /// <summary>
    /// The level of validation to perform.
    /// </summary>
    public RecoveryValidationLevel ValidationLevel { get; init; } = RecoveryValidationLevel.Standard;
}