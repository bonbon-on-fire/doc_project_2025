using AIChat.Server.Services.Recovery;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for recovery audit operations and queries.
/// Provides REST API endpoints for accessing recovery audit trail and analytics.
/// </summary>
[ApiController]
[Route("api/recovery/audit")]
[Produces("application/json")]
public class RecoveryAuditController : ControllerBase
{
    private readonly IRecoveryAuditService _auditService;
    private readonly ILogger<RecoveryAuditController> _logger;

    /// <summary>
    /// Initializes a new instance of the RecoveryAuditController.
    /// </summary>
    /// <param name="auditService">Recovery audit service</param>
    /// <param name="logger">Logger for diagnostics</param>
    public RecoveryAuditController(
        IRecoveryAuditService auditService,
        ILogger<RecoveryAuditController> logger)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Queries recovery audit history with flexible filtering and sorting.
    /// </summary>
    /// <param name="grainId">Filter by grain ID</param>
    /// <param name="grainType">Filter by grain type</param>
    /// <param name="initiatedBy">Filter by who initiated the recovery</param>
    /// <param name="success">Filter by recovery success status</param>
    /// <param name="strategyUsed">Filter by recovery strategy</param>
    /// <param name="startedAfter">Filter by operations started after this time</param>
    /// <param name="startedBefore">Filter by operations started before this time</param>
    /// <param name="completedAfter">Filter by operations completed after this time</param>
    /// <param name="completedBefore">Filter by operations completed before this time</param>
    /// <param name="correlationId">Filter by correlation ID</param>
    /// <param name="sourceSystem">Filter by source system</param>
    /// <param name="onlyWithWarnings">Include only operations with warnings</param>
    /// <param name="onlyWithSnapshots">Include only operations that used snapshots</param>
    /// <param name="sortOrder">Sort order for results</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    /// <param name="skip">Number of results to skip (for pagination)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching recovery audit entries</returns>
    /// <response code="200">Audit entries retrieved successfully</response>
    /// <response code="400">Invalid query parameters</response>
    /// <response code="500">Internal server error</response>
    [HttpGet]
    [ProducesResponseType(typeof(RecoveryAuditQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditQueryResult>> QueryRecoveryHistoryAsync(
        [FromQuery] string? grainId = null,
        [FromQuery] string? grainType = null,
        [FromQuery] string? initiatedBy = null,
        [FromQuery] bool? success = null,
        [FromQuery] RecoveryStrategy? strategyUsed = null,
        [FromQuery] DateTimeOffset? startedAfter = null,
        [FromQuery] DateTimeOffset? startedBefore = null,
        [FromQuery] DateTimeOffset? completedAfter = null,
        [FromQuery] DateTimeOffset? completedBefore = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? sourceSystem = null,
        [FromQuery] bool onlyWithWarnings = false,
        [FromQuery] bool onlyWithSnapshots = false,
        [FromQuery] AuditSortOrder sortOrder = AuditSortOrder.StartedAtDescending,
        [FromQuery] int? maxResults = 100,
        [FromQuery] int? skip = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate time range parameters
            if (startedAfter.HasValue && startedBefore.HasValue && startedAfter >= startedBefore)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Time Range",
                    Detail = "startedAfter must be earlier than startedBefore",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            if (completedAfter.HasValue && completedBefore.HasValue && completedAfter >= completedBefore)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Time Range",
                    Detail = "completedAfter must be earlier than completedBefore",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            // Validate pagination parameters
            if (maxResults.HasValue && maxResults <= 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Parameter",
                    Detail = "maxResults must be greater than 0",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            if (skip.HasValue && skip < 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Parameter",
                    Detail = "skip must be greater than or equal to 0",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            var query = new RecoveryAuditQuery
            {
                GrainId = grainId,
                GrainType = grainType,
                InitiatedBy = initiatedBy,
                Success = success,
                StrategyUsed = strategyUsed,
                StartedAfter = startedAfter,
                StartedBefore = startedBefore,
                CompletedAfter = completedAfter,
                CompletedBefore = completedBefore,
                CorrelationId = correlationId,
                SourceSystem = sourceSystem,
                OnlyWithWarnings = onlyWithWarnings,
                OnlyWithSnapshots = onlyWithSnapshots,
                SortOrder = sortOrder,
                MaxResults = maxResults,
                Skip = skip
            };

            var result = await _auditService.QueryRecoveryHistoryAsync(query, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query recovery audit history");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Query Failed",
                Detail = "An error occurred while querying recovery audit history",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets recent recovery operations (last 24 hours by default).
    /// </summary>
    /// <param name="hours">Number of hours back to look (default: 24)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recent recovery audit entries</returns>
    /// <response code="200">Recent entries retrieved successfully</response>
    /// <response code="400">Invalid hours parameter</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(RecoveryAuditQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditQueryResult>> GetRecentRecoveryOperationsAsync(
        [FromQuery] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        if (hours <= 0 || hours > 8760) // Max 1 year
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Parameter",
                Detail = "hours must be between 1 and 8760 (1 year)",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        try
        {
            var query = RecoveryAuditQuery.Recent(hours);
            var result = await _auditService.QueryRecoveryHistoryAsync(query, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent recovery operations");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Query Failed",
                Detail = "An error occurred while retrieving recent recovery operations",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets failed recovery operations.
    /// </summary>
    /// <param name="hours">Number of hours back to look (default: 168 - 1 week)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Failed recovery audit entries</returns>
    /// <response code="200">Failed entries retrieved successfully</response>
    /// <response code="400">Invalid hours parameter</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("failed")]
    [ProducesResponseType(typeof(RecoveryAuditQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditQueryResult>> GetFailedRecoveryOperationsAsync(
        [FromQuery] int hours = 168,
        CancellationToken cancellationToken = default)
    {
        if (hours <= 0 || hours > 8760) // Max 1 year
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Parameter",
                Detail = "hours must be between 1 and 8760 (1 year)",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        try
        {
            var query = RecoveryAuditQuery.Failed(hours);
            var result = await _auditService.QueryRecoveryHistoryAsync(query, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get failed recovery operations");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Query Failed",
                Detail = "An error occurred while retrieving failed recovery operations",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets recovery operations by a specific initiator.
    /// </summary>
    /// <param name="initiatedBy">Who initiated the operations</param>
    /// <param name="hours">Number of hours back to look (default: 168 - 1 week)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery operations by the specified initiator</returns>
    /// <response code="200">Entries retrieved successfully</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("by-initiator/{initiatedBy}")]
    [ProducesResponseType(typeof(RecoveryAuditQueryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditQueryResult>> GetRecoveryOperationsByInitiatorAsync(
        [FromRoute] string initiatedBy,
        [FromQuery] int hours = 168,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(initiatedBy))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Parameter",
                Detail = "initiatedBy cannot be null or empty",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        if (hours <= 0 || hours > 8760) // Max 1 year
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Parameter",
                Detail = "hours must be between 1 and 8760 (1 year)",
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }

        try
        {
            var query = RecoveryAuditQuery.ByInitiator(initiatedBy, hours);
            var result = await _auditService.QueryRecoveryHistoryAsync(query, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery operations by initiator {InitiatedBy}", initiatedBy);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Query Failed",
                Detail = "An error occurred while retrieving recovery operations by initiator",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets a specific recovery audit entry by audit ID.
    /// </summary>
    /// <param name="auditId">The audit ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The recovery audit entry</returns>
    /// <response code="200">Audit entry retrieved successfully</response>
    /// <response code="404">Audit entry not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("{auditId}")]
    [ProducesResponseType(typeof(RecoveryAuditEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditEntry>> GetRecoveryAuditEntryAsync(
        [FromRoute] string auditId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await _auditService.GetRecoveryAuditEntryAsync(auditId, cancellationToken);

            if (entry == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Audit Entry Not Found",
                    Detail = $"Recovery audit entry with ID '{auditId}' was not found",
                    Status = StatusCodes.Status404NotFound,
                    Instance = HttpContext.Request.Path
                });
            }

            return Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery audit entry {AuditId}", auditId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Audit Entry",
                Detail = "An error occurred while retrieving the recovery audit entry",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets a recovery audit entry by operation ID.
    /// </summary>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The recovery audit entry</returns>
    /// <response code="200">Audit entry retrieved successfully</response>
    /// <response code="404">Audit entry not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("by-operation/{operationId}")]
    [ProducesResponseType(typeof(RecoveryAuditEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditEntry>> GetRecoveryAuditEntryByOperationIdAsync(
        [FromRoute] string operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = await _auditService.GetRecoveryAuditEntryByOperationIdAsync(operationId, cancellationToken);

            if (entry == null)
            {
                return NotFound(new ProblemDetails
                {
                    Title = "Audit Entry Not Found",
                    Detail = $"Recovery audit entry for operation '{operationId}' was not found",
                    Status = StatusCodes.Status404NotFound,
                    Instance = HttpContext.Request.Path
                });
            }

            return Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recovery audit entry by operation ID {OperationId}", operationId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Audit Entry",
                Detail = "An error occurred while retrieving the recovery audit entry",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets audit metrics and statistics for analysis and reporting.
    /// </summary>
    /// <param name="fromTime">Start time for metrics calculation (optional)</param>
    /// <param name="toTime">End time for metrics calculation (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovery audit metrics</returns>
    /// <response code="200">Metrics retrieved successfully</response>
    /// <response code="400">Invalid time range</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(RecoveryAuditMetrics), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<RecoveryAuditMetrics>> GetAuditMetricsAsync(
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

            var metrics = await _auditService.GetAuditMetricsAsync(fromTime, toTime, cancellationToken);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audit metrics");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Failed to Get Metrics",
                Detail = "An error occurred while calculating audit metrics",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Exports audit data in various formats for compliance or analysis.
    /// </summary>
    /// <param name="format">Export format (CSV, JSON, XML)</param>
    /// <param name="grainId">Filter by grain ID (optional)</param>
    /// <param name="fromTime">Start time for export (optional)</param>
    /// <param name="toTime">End time for export (optional)</param>
    /// <param name="maxResults">Maximum number of records to export</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Exported audit data</returns>
    /// <response code="200">Data exported successfully</response>
    /// <response code="400">Invalid parameters</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> ExportAuditDataAsync(
        [FromQuery] AuditExportFormat format = AuditExportFormat.Csv,
        [FromQuery] string? grainId = null,
        [FromQuery] DateTimeOffset? fromTime = null,
        [FromQuery] DateTimeOffset? toTime = null,
        [FromQuery] int maxResults = 10000,
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

            if (maxResults <= 0 || maxResults > 100000)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Parameter",
                    Detail = "maxResults must be between 1 and 100,000",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = HttpContext.Request.Path
                });
            }

            var query = new RecoveryAuditQuery
            {
                GrainId = grainId,
                StartedAfter = fromTime,
                StartedBefore = toTime,
                MaxResults = maxResults,
                SortOrder = AuditSortOrder.StartedAtDescending
            };

            var exportData = await _auditService.ExportAuditDataAsync(query, format, cancellationToken);

            var contentType = format switch
            {
                AuditExportFormat.Csv => "text/csv",
                AuditExportFormat.Json => "application/json",
                AuditExportFormat.Xml => "application/xml",
                AuditExportFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };

            var fileName = $"recovery-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{format.ToString().ToLowerInvariant()}";

            return File(exportData, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export audit data");

            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Export Failed",
                Detail = "An error occurred while exporting audit data",
                Status = StatusCodes.Status500InternalServerError,
                Instance = HttpContext.Request.Path
            });
        }
    }

    /// <summary>
    /// Gets the health status of the recovery audit service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status information</returns>
    /// <response code="200">Health status retrieved successfully</response>
    /// <response code="503">Service is unhealthy</response>
    [HttpGet("health")]
    [ProducesResponseType(typeof(RecoveryAuditHealthStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RecoveryAuditHealthStatus), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RecoveryAuditHealthStatus>> GetHealthStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthStatus = await _auditService.GetHealthStatusAsync(cancellationToken);

            if (healthStatus.IsHealthy)
            {
                return Ok(healthStatus);
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable, healthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audit service health status");

            var unhealthyStatus = RecoveryAuditHealthStatus.Unhealthy(
                "RecoveryAuditService",
                [$"Health check failed: {ex.Message}"]);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, unhealthyStatus);
        }
    }
}
