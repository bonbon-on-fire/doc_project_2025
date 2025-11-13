using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AIChat.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ModeController : ControllerBase
{
    private readonly IModeService _modeService;
    private readonly ILogger<ModeController> _logger;

    public ModeController(IModeService modeService, ILogger<ModeController> logger)
    {
        _modeService = modeService ?? throw new ArgumentNullException(nameof(modeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<ActionResult<ModesResponse>> GetModes([FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        try
        {
            var result = await _modeService.GetAllModesAsync(userId, cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? "Failed to retrieve modes");
            }
            var response = new ModesResponse { Modes = [.. result.Modes], Count = result.Modes.Count };
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving modes for user {UserId}", userId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ModeDto>> GetMode(string id, [FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        try
        {
            var result = await _modeService.GetModeByIdAsync(id, userId, cancellationToken);
            if (!result.Success)
            {
                if (result.Error == "NotFound")
                {
                    return NotFound(new { Error = "Mode not found" });
                }
                throw new InvalidOperationException(result.Error ?? "Failed to retrieve mode");
            }
            return Ok(result.Mode);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Error = "Mode not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving mode {ModeId} for user {UserId}", id, userId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ModeDto>> CreateMode([FromBody] CreateModeApiRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        var validationResult = ValidateModeRequest(request.Name, request.Description, request.Prompt, request.Tools);
        if (validationResult != null)
        {
            return BadRequest(new { Error = validationResult });
        }

        var serviceRequest = new CreateModeRequest
        {
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools,
            DefaultModel = request.DefaultModel,
            Category = request.Category,
        };

        try
        {
            var result = await _modeService.CreateCustomModeAsync(serviceRequest, request.UserId);
            if (!result.Success)
            {
                if (result.Error?.Contains("already exists") == true)
                {
                    return Conflict(new { Error = result.Error });
                }
                throw new InvalidOperationException(result.Error ?? "Failed to create mode");
            }
            return CreatedAtAction(nameof(GetMode), new { id = result.Mode!.Id, userId = request.UserId }, result.Mode);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating mode for user {UserId}", request.UserId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ModeDto>> UpdateMode(string id, [FromBody] UpdateModeApiRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        var validationResult = ValidateModeRequest(request.Name, request.Description, request.Prompt, request.Tools);
        if (validationResult != null)
        {
            return BadRequest(new { Error = validationResult });
        }

        var serviceRequest = new UpdateModeRequest
        {
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools,
            DefaultModel = request.DefaultModel,
            Category = request.Category,
        };

        try
        {
            var result = await _modeService.UpdateCustomModeAsync(id, serviceRequest, request.UserId);
            if (!result.Success)
            {
                if (result.Error == "NotFound")
                {
                    return NotFound(new { Error = "Mode not found or access denied" });
                }
                throw new InvalidOperationException(result.Error ?? "Failed to update mode");
            }
            return Ok(result.Mode);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Error = "Mode not found or access denied" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mode {ModeId} for user {UserId}", id, request.UserId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteMode(string id, [FromQuery] string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(new { Error = "UserId is required" });
        }

        try
        {
            var result = await _modeService.DeleteCustomModeAsync(id, userId, cancellationToken);
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
                throw new InvalidOperationException(result.Error ?? "Failed to delete mode");
            }
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Error = "Mode not found or access denied" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot delete system mode"))
        {
            return BadRequest(new { Error = "Cannot delete system modes" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting mode {ModeId} for user {UserId}", id, userId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    private static string? ValidateModeRequest(string name, string description, string prompt, IReadOnlyList<string> tools)
    {
        if (name.Contains('<') || name.Contains('>') || name.Contains('&'))
        {
            return "Mode name cannot contain HTML special characters";
        }
        if (tools.Any(string.IsNullOrWhiteSpace))
        {
            return "Tool names cannot be empty or whitespace";
        }
        if (description.Contains("<script>", StringComparison.OrdinalIgnoreCase) || prompt.Contains("<script>", StringComparison.OrdinalIgnoreCase))
        {
            return "Mode content cannot contain script tags";
        }
        foreach (var tool in tools)
        {
            if (tool.Length > 100)
            {
                return "Tool names must be 100 characters or less";
            }
        }
        return null;
    }
}

public sealed class ModesResponse
{
    [JsonPropertyName("modes")]
    public required List<ModeDto> Modes { get; init; }

    [JsonPropertyName("count")]
    public required int Count { get; init; }
}

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
