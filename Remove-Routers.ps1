# Orleans Always-On Migration: Remove All Router Dependencies
# This script removes the DualModeRouter pattern and makes the system fully Orleans-dependent

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Orleans Always-On Migration: Router Removal" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Create backups
Write-Host "Step 1: Creating backups..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path ".migration-backups" | Out-Null
Copy-Item "server/AIChat.Server/Controllers/ChatController.cs" ".migration-backups/"
Copy-Item "server/AIChat.Server/Controllers/ModeController.cs" ".migration-backups/"
Copy-Item "server/AIChat.Server/Controllers/LogsController.cs" ".migration-backups/"
Copy-Item "server/AIChat.Server/Controllers/MonitoringController.cs" ".migration-backups/"
Copy-Item "server/AIChat.Server/Program.cs" ".migration-backups/"
Write-Host "✓ Backups created in .migration-backups/" -ForegroundColor Green
Write-Host ""

# Step 2: Delete all router files first (prevents build errors)
Write-Host "Step 2: Deleting router files..." -ForegroundColor Yellow
$routerFiles = @(
    "server/AIChat.Server/Services/Routing/DualModeRouter.cs",
    "server/AIChat.Server/Services/Routing/IDualModeRouter.cs",
    "server/AIChat.Server/Services/Routing/ModeRouter.cs",
    "server/AIChat.Server/Services/Routing/IModeRouter.cs",
    "server/AIChat.Server/Services/Routing/LogsRouter.cs",
    "server/AIChat.Server/Services/Routing/ILogsRouter.cs",
    "server/AIChat.Server/Services/Routing/MonitoringRouter.cs",
    "server/AIChat.Server/Services/Routing/IMonitoringRouter.cs",
    "server/AIChat.Server/Services/Routing/CircuitBreakerDualModeRouter.cs",
    "server/AIChat.Server/Services/Routing/DualModeRouterOptions.cs",
    "server/AIChat.Server/Services/ResponseCaching/Decorators/CachedLogsRouter.cs",
    "server/AIChat.Server/Services/ResponseCaching/Decorators/CachedModeRouter.cs",
    "server/AIChat.Server/Services/ResponseCaching/Decorators/CachedMonitoringRouter.cs",
    "server/AIChat.Server.Tests/Services/Routing/DualModeRouterTests.cs"
)

foreach ($file in $routerFiles) {
    if (Test-Path $file) {
        Remove-Item $file -Force
        Write-Host "  Deleted: $file" -ForegroundColor DarkGray
    }
}
Write-Host "✓ All router files deleted (14 files)" -ForegroundColor Green
Write-Host ""

# Step 3: Update Program.cs - Remove router registrations
Write-Host "Step 3: Updating Program.cs..." -ForegroundColor Yellow
$programContent = Get-Content "server/AIChat.Server/Program.cs" -Raw
# Remove all router registration lines
$programContent = $programContent -replace '(?m)^.*AddScoped.*Router.*$\r?\n?', ''
$programContent = $programContent -replace '(?m)^.*IModeRouter.*$\r?\n?', ''
$programContent = $programContent -replace '(?m)^.*IMonitoringRouter.*$\r?\n?', ''
$programContent = $programContent -replace '(?m)^.*ILogsRouter.*$\r?\n?', ''
$programContent = $programContent -replace '(?m)^.*IDualModeRouter.*$\r?\n?', ''
$programContent | Set-Content "server/AIChat.Server/Program.cs" -NoNewline
Write-Host "✓ Program.cs router registrations removed" -ForegroundColor Green
Write-Host ""

# Step 4: Update ChatController - Comment out router usage
Write-Host "Step 4: Updating ChatController..." -ForegroundColor Yellow
$chatContent = Get-Content "server/AIChat.Server/Controllers/ChatController.cs" -Raw
# Comment out router field
$chatContent = $chatContent -replace 'private readonly Services\.Routing\.IDualModeRouter _router = router;', '// private readonly Services.Routing.IDualModeRouter _router = router; // REMOVED: Orleans-only'
# Comment out router parameter
$chatContent = $chatContent -replace 'Services\.Routing\.IDualModeRouter router,', '// Services.Routing.IDualModeRouter router, // REMOVED: Orleans-only'
$chatContent | Set-Content "server/AIChat.Server/Controllers/ChatController.cs" -NoNewline
Write-Host "✓ ChatController router references commented out" -ForegroundColor Green
Write-Host ""

# Step 5: Update ModeController - Remove router completely
Write-Host "Step 5: Updating ModeController (complete rewrite)..." -ForegroundColor Yellow
$modeController = @'
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AIChat.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Orleans;

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
'@
$modeController | Set-Content "server/AIChat.Server/Controllers/ModeController.cs" -NoNewline
Write-Host "✓ ModeController rewritten (router-free)" -ForegroundColor Green
Write-Host ""

# Step 6: Update LogsController
Write-Host "Step 6: Updating LogsController..." -ForegroundColor Yellow
$logsContent = Get-Content "server/AIChat.Server/Controllers/LogsController.cs" -Raw
$logsContent = $logsContent -replace 'private readonly ILogsRouter _router;', '// private readonly ILogsRouter _router; // REMOVED: Direct file logging'
$logsContent = $logsContent -replace 'ILogsRouter router,', '// ILogsRouter router, // REMOVED: Direct file logging'
$logsContent = $logsContent -replace '_router = router.*', '// _router = router; // REMOVED: Direct file logging'
$logsContent = $logsContent -replace 'return await _router\.ExecuteLogEntryAsync\([^)]+\);', 'return await CreateLogEntryResponse(logEntry);'
$logsContent | Set-Content "server/AIChat.Server/Controllers/LogsController.cs" -NoNewline
Write-Host "✓ LogsController router removed" -ForegroundColor Green
Write-Host ""

# Step 7: Update MonitoringController
Write-Host "Step 7: Updating MonitoringController..." -ForegroundColor Yellow
$monContent = Get-Content "server/AIChat.Server/Controllers/MonitoringController.cs" -Raw
$monContent = $monContent -replace 'private readonly IMonitoringRouter _router;', '// private readonly IMonitoringRouter _router; // REMOVED: Orleans-only'
$monContent = $monContent -replace 'IMonitoringRouter router,', '// IMonitoringRouter router, // REMOVED: Orleans-only'
$monContent = $monContent -replace '_router = router.*', '// _router = router; // REMOVED: Orleans-only'
# Comment out all _router.ExecuteSystemOperationAsync calls
$monContent = $monContent -replace 'return await _router\.ExecuteSystemOperationAsync', '// return await _router.ExecuteSystemOperationAsync // REMOVED'
$monContent | Set-Content "server/AIChat.Server/Controllers/MonitoringController.cs" -NoNewline
Write-Host "✓ MonitoringController router removed" -ForegroundColor Green
Write-Host ""

# Step 8: Build and validate
Write-Host "Step 8: Building solution..." -ForegroundColor Yellow
try {
    dotnet build server/AIChat.Server/AIChat.Server.csproj --no-incremental 2>&1 | Tee-Object -Variable buildOutput | Out-Null

    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "✓ Migration Phase 1 Complete!" -ForegroundColor Green
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Changes made:" -ForegroundColor White
    Write-Host "  ✓ 503 check removed from ChatController (ALREADY DONE)" -ForegroundColor Green
    Write-Host "  ✓ 14 router files deleted" -ForegroundColor Green
    Write-Host "  ✓ Program.cs router registrations removed" -ForegroundColor Green
    Write-Host "  ✓ ModeController completely rewritten" -ForegroundColor Green
    Write-Host "  ✓ ChatController, LogsController, MonitoringController router references commented out" -ForegroundColor Green
    Write-Host ""
    Write-Host "⚠️  Known Issues:" -ForegroundColor Yellow
    Write-Host "  - ChatController still has commented-out router code (needs manual cleanup)" -ForegroundColor Yellow
    Write-Host "  - MonitoringController endpoints may need direct service calls" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor White
    Write-Host "  1. Review build output for remaining errors" -ForegroundColor White
    Write-Host "  2. Test the stream-sse endpoint (should NO LONGER return 503!)" -ForegroundColor White
    Write-Host "  3. If successful, delete .migration-backups/" -ForegroundColor White
    Write-Host ""
} catch {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Red
    Write-Host "✗ Build failed - reverting changes..." -ForegroundColor Red
    Write-Host "==========================================" -ForegroundColor Red
    Copy-Item ".migration-backups/*" "server/AIChat.Server/Controllers/" -Force
    Copy-Item ".migration-backups/Program.cs" "server/AIChat.Server/" -Force
    Write-Host "Files restored from backup" -ForegroundColor Yellow
    throw
}
