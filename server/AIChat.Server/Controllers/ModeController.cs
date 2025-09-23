using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services;
using AIChat.Server.Services.Routing;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for managing chat modes including system modes and user-created custom modes.
/// Provides CRUD operations for modes with proper validation and authorization.
/// Supports dual-mode routing between Orleans grains and direct services.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ModeController : ControllerBase
{
    private readonly IModeRouter _router;
    private readonly IModeService _modeService;
    private readonly ILogger<ModeController> _logger;

    /// <summary>
    /// Initializes a new instance of the ModeController.
    /// </summary>
    /// <param name="router">Mode router for Orleans/Direct service operations</param>
    /// <param name="modeService">Direct mode service for fallback operations</param>
    /// <param name="logger">Logger for structured logging</param>
    public ModeController(
        IModeRouter router,
        IModeService modeService,
        ILogger<ModeController> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _modeService = modeService ?? throw new ArgumentNullException(nameof(modeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    /// <summary>
    /// Get all available modes for a user, including system modes and user's custom modes.
    /// </summary>
    /// <param name="userId">User ID to get modes for</param>
    /// <returns>List of all available modes</returns>
    [HttpGet]
    public async Task<ActionResult<ModesResponse>> GetModes([FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        return await _router.ExecuteUserOperationAsync<ActionResult<ModesResponse>>(
            userId,
            // Orleans operation - TODO: Map to appropriate grain method
            async modeGrain =>
            {
                // For now, use pass-through to direct service
                // Future enhancement: Use grain.GetAllModesAsync(userId) when implemented
                var result = await _modeService.GetAllModesAsync(userId, cancellationToken);
                return CreateModesResponse(result, userId);
            },
            // Direct service operation
            async service =>
            {
                var result = await service.GetAllModesAsync(userId, cancellationToken);
                return CreateModesResponse(result, userId);
            },
            "GetModes",
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a standardized modes response from service result.
    /// </summary>
    private ActionResult<ModesResponse> CreateModesResponse(
        (bool Success, string? Error, IReadOnlyList<ModeDto> Modes) result,
        string userId)
    {
        if (!result.Success)
        {
            _logger.LogError("Error retrieving modes for user {UserId}: {Error}", userId, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve modes" });
        }

        var response = new ModesResponse { Modes = [.. result.Modes], Count = result.Modes.Count };
        return Ok(response);
    }

    /// <summary>
    /// Get a specific mode by ID.
    /// </summary>
    /// <param name="id">Mode ID</param>
    /// <param name="userId">User ID for permission checking on custom modes</param>
    /// <returns>Mode details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ModeDto>> GetMode(string id, [FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        return await _router.ExecuteModeOperationAsync<ActionResult<ModeDto>>(
            id,
            // Orleans operation - TODO: Map to appropriate grain method
            async modeGrain =>
            {
                // For now, use pass-through to direct service
                // Future enhancement: Use grain.GetStateAsync() when implemented
                var result = await _modeService.GetModeByIdAsync(id, userId, cancellationToken);
                return CreateModeResponse(result, id, userId);
            },
            // Direct service operation
            async service =>
            {
                var result = await service.GetModeByIdAsync(id, userId, cancellationToken);
                return CreateModeResponse(result, id, userId);
            },
            "GetMode",
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a standardized mode response from service result.
    /// </summary>
    private ActionResult<ModeDto> CreateModeResponse(
        (bool Success, string? Error, ModeDto? Mode) result,
        string modeId,
        string userId)
    {
        if (!result.Success)
        {
            if (result.Error == "NotFound")
            {
                return NotFound(new { Error = "Mode not found" });
            }

            _logger.LogError(
                "Error retrieving mode {ModeId} for user {UserId}: {Error}",
                modeId,
                userId,
                result.Error
            );
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve mode" });
        }

        return Ok(result.Mode);
    }

    /// <summary>
    /// Creates a standardized create mode response from service result.
    /// </summary>
    private ActionResult<ModeDto> CreateCreateModeResponse(
        (bool Success, string? Error, ModeDto? Mode) result,
        string userId)
    {
        if (!result.Success)
        {
            if (result.Error?.Contains("already exists") == true)
            {
                return Conflict(new { Error = result.Error });
            }

            _logger.LogError(
                "Error creating mode for user {UserId}: {Error}",
                userId,
                result.Error
            );
            return StatusCode(500, new { Error = result.Error ?? "Failed to create mode" });
        }

        return CreatedAtAction(
            nameof(GetMode),
            new { id = result.Mode!.Id, userId = userId },
            result.Mode
        );
    }

    /// <summary>
    /// Create a new custom mode for a user.
    /// </summary>
    /// <param name="request">Mode creation request</param>
    /// <returns>Created mode</returns>
    [HttpPost]
    public async Task<ActionResult<ModeDto>> CreateMode([FromBody] CreateModeApiRequest request)
    {
        // Validate request
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        // Additional business logic validation
        var validationResult = ValidateModeRequest(
            request.Name,
            request.Description,
            request.Prompt,
            request.Tools
        );
        if (validationResult != null)
        {
            return BadRequest(new { Error = validationResult });
        }

        // Map API request to service request
        var serviceRequest = new CreateModeRequest
        {
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools,
            DefaultModel = request.DefaultModel,
            Category = request.Category,
        };

        return await _router.ExecuteUserOperationAsync<ActionResult<ModeDto>>(
            request.UserId,
            // Orleans operation - TODO: Map to appropriate grain method
            async modeGrain =>
            {
                // For now, use pass-through to direct service
                // Future enhancement: Use grain.InitializeAsync() when implemented
                var result = await _modeService.CreateCustomModeAsync(serviceRequest, request.UserId);
                return CreateCreateModeResponse(result, request.UserId);
            },
            // Direct service operation
            async service =>
            {
                var result = await service.CreateCustomModeAsync(serviceRequest, request.UserId);
                return CreateCreateModeResponse(result, request.UserId);
            },
            "CreateMode"
        );
    }

    /// <summary>
    /// Update an existing custom mode. Only the mode owner can update.
    /// </summary>
    /// <param name="id">Mode ID to update</param>
    /// <param name="request">Mode update request</param>
    /// <returns>Updated mode</returns>
    [HttpPut("{id}")]
    public async Task<ActionResult<ModeDto>> UpdateMode(
        string id,
        [FromBody] UpdateModeApiRequest request
    )
    {
        // Validate request
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        // Additional business logic validation
        var validationResult = ValidateModeRequest(
            request.Name,
            request.Description,
            request.Prompt,
            request.Tools
        );
        if (validationResult != null)
        {
            return BadRequest(new { Error = validationResult });
        }

        // Map API request to service request
        var serviceRequest = new UpdateModeRequest
        {
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools,
            DefaultModel = request.DefaultModel,
            Category = request.Category,
        };

        return await _router.ExecuteModeOperationAsync<ActionResult<ModeDto>>(
            id,
            // Orleans operation - TODO: Map to appropriate grain method
            async modeGrain =>
            {
                // For now, use pass-through to direct service
                // Future enhancement: Use grain.UpdateConfigurationAsync() when implemented
                var result = await _modeService.UpdateCustomModeAsync(id, serviceRequest, request.UserId);
                return CreateUpdateModeResponse(result, id, request.UserId);
            },
            // Direct service operation
            async service =>
            {
                var result = await service.UpdateCustomModeAsync(id, serviceRequest, request.UserId);
                return CreateUpdateModeResponse(result, id, request.UserId);
            },
            "UpdateMode"
        );
    }

    /// <summary>
    /// Creates a standardized update mode response from service result.
    /// </summary>
    private ActionResult<ModeDto> CreateUpdateModeResponse(
        (bool Success, string? Error, ModeDto? Mode) result,
        string modeId,
        string userId)
    {
        if (!result.Success)
        {
            if (result.Error == "NotFound")
            {
                return NotFound(new { Error = "Mode not found or access denied" });
            }

            _logger.LogError(
                "Error updating mode {ModeId} for user {UserId}: {Error}",
                modeId,
                userId,
                result.Error
            );
            return StatusCode(500, new { Error = result.Error ?? "Failed to update mode" });
        }

        return Ok(result.Mode);
    }

    /// <summary>
    /// Delete a custom mode. Only the mode owner can delete.
    /// </summary>
    /// <param name="id">Mode ID to delete</param>
    /// <param name="userId">User ID for ownership verification</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMode(string id, [FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        return await _router.ExecuteModeOperationAsync<ActionResult>(
            id,
            // Orleans operation - TODO: Map to appropriate grain method
            async modeGrain =>
            {
                // For now, use pass-through to direct service
                // Future enhancement: Use grain.ArchiveAsync() when implemented
                var result = await _modeService.DeleteCustomModeAsync(id, userId, cancellationToken);
                return CreateDeleteModeResponse(result, id, userId);
            },
            // Direct service operation
            async service =>
            {
                var result = await service.DeleteCustomModeAsync(id, userId, cancellationToken);
                return CreateDeleteModeResponse(result, id, userId);
            },
            "DeleteMode",
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a standardized delete mode response from service result.
    /// </summary>
    private ActionResult CreateDeleteModeResponse(
        (bool Success, string? Error) result,
        string modeId,
        string userId)
    {
        if (!result.Success)
        {
            if (result.Error == "NotFound")
            {
                return NotFound(new { Error = "Mode not found or access denied" });
            }

            if (result.Error == "Cannot delete system mode")
            {
                return BadRequest(new { Error = "Cannot delete system modes" });
            }

            _logger.LogError(
                "Error deleting mode {ModeId} for user {UserId}: {Error}",
                modeId,
                userId,
                result.Error
            );
            return StatusCode(500, new { Error = result.Error ?? "Failed to delete mode" });
        }

        return NoContent();
    }

    /// <summary>
    /// Validates mode request data according to business rules.
    /// Following Single Responsibility Principle by separating validation logic.
    /// </summary>
    /// <param name="name">Mode name</param>
    /// <param name="description">Mode description</param>
    /// <param name="prompt">System prompt</param>
    /// <param name="tools">Tool list</param>
    /// <returns>Error message if validation fails, null if valid</returns>
    private static string? ValidateModeRequest(
        string name,
        string description,
        string prompt,
        IReadOnlyList<string> tools
    )
    {
        // Business rule: Mode names should not contain special characters that could cause issues
        if (name.Contains('<') || name.Contains('>') || name.Contains('&'))
        {
            return "Mode name cannot contain HTML special characters";
        }

        // Business rule: Tools list must be valid
        if (tools.Any(string.IsNullOrWhiteSpace))
        {
            return "Tool names cannot be empty or whitespace";
        }

        // Business rule: Prevent potential XSS in descriptions and prompts
        if (
            description.Contains("<script>", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("<script>", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "Mode content cannot contain script tags";
        }

        // Business rule: Tool names should follow naming convention
        foreach (var tool in tools)
        {
            if (tool.Length > 100)
            {
                return "Tool names must be 100 characters or less";
            }
        }

        return null; // Valid
    }
}

/// <summary>
/// Response model for the GET /api/modes endpoint
/// </summary>
public sealed class ModesResponse
{
    [JsonPropertyName("modes")]
    public required List<ModeDto> Modes { get; init; }

    [JsonPropertyName("count")]
    public required int Count { get; init; }
}

/// <summary>
/// API request model for creating a new custom mode
/// </summary>
public sealed class CreateModeApiRequest
{
    [JsonPropertyName("userId")]
    [Required]
    public required string UserId { get; init; }

    [JsonPropertyName("name")]
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Description { get; init; }

    [JsonPropertyName("prompt")]
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public required string Prompt { get; init; }

    [JsonPropertyName("tools")]
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<string> Tools { get; init; }

    [JsonPropertyName("defaultModel")]
    [StringLength(100)]
    public string? DefaultModel { get; init; }

    [JsonPropertyName("category")]
    [StringLength(50)]
    public string? Category { get; init; }
}

/// <summary>
/// API request model for updating an existing custom mode
/// </summary>
public sealed class UpdateModeApiRequest
{
    [JsonPropertyName("userId")]
    [Required]
    public required string UserId { get; init; }

    [JsonPropertyName("name")]
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public required string Description { get; init; }

    [JsonPropertyName("prompt")]
    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public required string Prompt { get; init; }

    [JsonPropertyName("tools")]
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<string> Tools { get; init; }

    [JsonPropertyName("defaultModel")]
    [StringLength(100)]
    public string? DefaultModel { get; init; }

    [JsonPropertyName("category")]
    [StringLength(50)]
    public string? Category { get; init; }
}
