using System.Collections.Concurrent;
using System.Text.Json;
using AIChat.Server.Storage;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services;

/// <summary>
/// Service for managing chat modes including system modes and user-created custom modes.
/// Loads system modes from JSON files on startup and provides caching for performance.
/// </summary>
public sealed class ModeService : IModeService
{
    private readonly IModeStorage _modeStorage;
    private readonly ILogger<ModeService> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ConcurrentDictionary<string, SystemModeConfig> _systemModes = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _systemModesLoaded = false;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);

    public ModeService(
        IModeStorage modeStorage,
        ILogger<ModeService> logger,
        IHostEnvironment hostEnvironment)
    {
        _modeStorage = modeStorage;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    public async Task<(bool Success, string? Error, IReadOnlyList<ModeDto> Modes)> GetAllModesAsync(
        string userId, 
        CancellationToken ct = default)
    {
        try
        {
            // Ensure system modes are loaded
            await EnsureSystemModesLoadedAsync(ct);

            // Get user's custom modes
            var userModesResult = await _modeStorage.GetModesByUserAsync(userId, ct);
            if (!userModesResult.Success)
            {
                _logger.LogError("Failed to get user modes for user {UserId}: {Error}", userId, userModesResult.Error);
                return (false, userModesResult.Error, new List<ModeDto>());
            }

            var allModes = new List<ModeDto>();

            // Add system modes
            foreach (var systemMode in _systemModes.Values)
            {
                allModes.Add(new ModeDto
                {
                    Id = systemMode.Id,
                    Name = systemMode.Name,
                    Description = systemMode.Description,
                    Prompt = systemMode.Prompt,
                    Tools = systemMode.Tools ?? new List<string>(),
                    DefaultModel = systemMode.DefaultModel,
                    IsSystem = true,
                    Category = systemMode.Category
                });
            }

            // Add user's custom modes
            foreach (var userMode in userModesResult.Modes)
            {
                var tools = string.IsNullOrEmpty(userMode.Tools) 
                    ? new List<string>() 
                    : JsonSerializer.Deserialize<List<string>>(userMode.Tools, _jsonOptions) ?? new List<string>();

                allModes.Add(new ModeDto
                {
                    Id = userMode.Id,
                    Name = userMode.Name,
                    Description = userMode.Description,
                    Prompt = userMode.Prompt,
                    Tools = tools,
                    DefaultModel = userMode.DefaultModel,
                    IsSystem = false,
                    UserId = userMode.UserId,
                    Category = userMode.Category,
                    CreatedAt = userMode.CreatedAtUtc,
                    UpdatedAt = userMode.UpdatedAtUtc
                });
            }

            // Sort modes: system modes first, then custom modes by creation date
            var sortedModes = allModes
                .OrderBy(m => m.IsSystem ? 0 : 1)
                .ThenBy(m => m.Name)
                .ThenByDescending(m => m.CreatedAt)
                .ToList();

            return (true, null, sortedModes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all modes for user {UserId}", userId);
            return (false, ex.Message, new List<ModeDto>());
        }
    }

    public async Task<(bool Success, string? Error, ModeDto? Mode)> GetModeByIdAsync(
        string modeId,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            // Ensure system modes are loaded
            await EnsureSystemModesLoadedAsync(ct);

            // Check if it's a system mode first
            if (_systemModes.TryGetValue(modeId, out var systemMode))
            {
                var dto = new ModeDto
                {
                    Id = systemMode.Id,
                    Name = systemMode.Name,
                    Description = systemMode.Description,
                    Prompt = systemMode.Prompt,
                    Tools = systemMode.Tools ?? new List<string>(),
                    DefaultModel = systemMode.DefaultModel,
                    IsSystem = true,
                    Category = systemMode.Category
                };
                return (true, null, dto);
            }

            // Check user's custom modes
            var userModeResult = await _modeStorage.GetModeByIdAsync(modeId, userId, ct);
            if (!userModeResult.Success)
            {
                return (false, userModeResult.Error, null);
            }

            if (userModeResult.Mode != null)
            {
                var tools = string.IsNullOrEmpty(userModeResult.Mode.Tools) 
                    ? new List<string>() 
                    : JsonSerializer.Deserialize<List<string>>(userModeResult.Mode.Tools, _jsonOptions) ?? new List<string>();

                var dto = new ModeDto
                {
                    Id = userModeResult.Mode.Id,
                    Name = userModeResult.Mode.Name,
                    Description = userModeResult.Mode.Description,
                    Prompt = userModeResult.Mode.Prompt,
                    Tools = tools,
                    DefaultModel = userModeResult.Mode.DefaultModel,
                    IsSystem = false,
                    UserId = userModeResult.Mode.UserId,
                    Category = userModeResult.Mode.Category,
                    CreatedAt = userModeResult.Mode.CreatedAtUtc,
                    UpdatedAt = userModeResult.Mode.UpdatedAtUtc
                };
                return (true, null, dto);
            }

            return (false, "NotFound", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mode {ModeId} for user {UserId}", modeId, userId);
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error, ModeDto? Mode)> CreateCustomModeAsync(
        CreateModeRequest mode,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var modeId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var toolsJson = JsonSerializer.Serialize(mode.Tools.ToList(), _jsonOptions);

            var record = new ModeRecord
            {
                Id = modeId,
                UserId = userId,
                Name = mode.Name,
                Description = mode.Description,
                Prompt = mode.Prompt,
                Tools = toolsJson,
                DefaultModel = mode.DefaultModel,
                Category = mode.Category ?? "custom",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var result = await _modeStorage.CreateModeAsync(record, ct);
            if (!result.Success || result.Mode == null)
            {
                return (false, result.Error, null);
            }

            var dto = new ModeDto
            {
                Id = result.Mode.Id,
                Name = result.Mode.Name,
                Description = result.Mode.Description,
                Prompt = result.Mode.Prompt,
                Tools = mode.Tools,
                DefaultModel = result.Mode.DefaultModel,
                IsSystem = false,
                UserId = result.Mode.UserId,
                Category = result.Mode.Category,
                CreatedAt = result.Mode.CreatedAtUtc,
                UpdatedAt = result.Mode.UpdatedAtUtc
            };

            _logger.LogInformation("Created custom mode {ModeId} for user {UserId}", modeId, userId);
            return (true, null, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating custom mode for user {UserId}", userId);
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error, ModeDto? Mode)> UpdateCustomModeAsync(
        string modeId,
        UpdateModeRequest mode,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            // Ensure system modes are loaded
            await EnsureSystemModesLoadedAsync(ct);
            
            // Check if this is a system mode
            if (_systemModes.ContainsKey(modeId))
            {
                return (false, "Cannot update system mode", null);
            }
            
            var toolsJson = JsonSerializer.Serialize(mode.Tools.ToList(), _jsonOptions);

            var updatedRecord = new ModeRecord
            {
                Id = modeId,
                UserId = userId,
                Name = mode.Name,
                Description = mode.Description,
                Prompt = mode.Prompt,
                Tools = toolsJson,
                DefaultModel = mode.DefaultModel,
                Category = mode.Category ?? "custom",
                CreatedAtUtc = DateTime.UtcNow, // This will be ignored in update
                UpdatedAtUtc = DateTime.UtcNow
            };

            var result = await _modeStorage.UpdateModeAsync(modeId, userId, updatedRecord, ct);
            if (!result.Success || result.Mode == null)
            {
                return (false, result.Error, null);
            }

            var dto = new ModeDto
            {
                Id = result.Mode.Id,
                Name = result.Mode.Name,
                Description = result.Mode.Description,
                Prompt = result.Mode.Prompt,
                Tools = mode.Tools,
                DefaultModel = result.Mode.DefaultModel,
                IsSystem = false,
                UserId = result.Mode.UserId,
                Category = result.Mode.Category,
                CreatedAt = result.Mode.CreatedAtUtc,
                UpdatedAt = result.Mode.UpdatedAtUtc
            };

            _logger.LogInformation("Updated custom mode {ModeId} for user {UserId}", modeId, userId);
            return (true, null, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating custom mode {ModeId} for user {UserId}", modeId, userId);
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error)> DeleteCustomModeAsync(
        string modeId,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            // Ensure it's not a system mode
            await EnsureSystemModesLoadedAsync(ct);
            if (_systemModes.ContainsKey(modeId))
            {
                return (false, "Cannot delete system mode");
            }

            var result = await _modeStorage.DeleteModeAsync(modeId, userId, ct);
            if (result.Success)
            {
                _logger.LogInformation("Deleted custom mode {ModeId} for user {UserId}", modeId, userId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting custom mode {ModeId} for user {UserId}", modeId, userId);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error, IReadOnlyList<string> FilteredTools)> FilterToolsByModeAsync(
        string modeId,
        string userId,
        IReadOnlyList<string> availableTools,
        CancellationToken ct = default)
    {
        try
        {
            var modeResult = await GetModeByIdAsync(modeId, userId, ct);
            if (!modeResult.Success || modeResult.Mode == null)
            {
                // If mode not found, fall back to all tools
                _logger.LogWarning("Mode {ModeId} not found for user {UserId}, using all available tools", modeId, userId);
                return (true, null, availableTools);
            }

            var mode = modeResult.Mode;
            
            // If tools contains "*", allow all tools
            if (mode.Tools.Contains("*"))
            {
                return (true, null, availableTools);
            }

            // Filter tools based on mode configuration
            var filteredTools = availableTools
                .Where(tool => mode.Tools.Contains(tool))
                .ToList();

            return (true, null, filteredTools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering tools for mode {ModeId} and user {UserId}", modeId, userId);
            return (false, ex.Message, new List<string>());
        }
    }

    public async Task<(bool Success, string? Error, string? SystemPrompt)> GetModeSystemPromptAsync(
        string modeId,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var modeResult = await GetModeByIdAsync(modeId, userId, ct);
            if (!modeResult.Success || modeResult.Mode == null)
            {
                // Return success with null prompt for non-existent modes
                return (true, null, null);
            }

            var prompt = string.IsNullOrWhiteSpace(modeResult.Mode.Prompt) ? null : modeResult.Mode.Prompt;
            return (true, null, prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system prompt for mode {ModeId} and user {UserId}", modeId, userId);
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string? Error, string? DefaultModel)> GetModeDefaultModelAsync(
        string modeId,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var modeResult = await GetModeByIdAsync(modeId, userId, ct);
            if (!modeResult.Success || modeResult.Mode == null)
            {
                // Return success with null model for non-existent modes
                return (true, null, null);
            }

            return (true, null, modeResult.Mode.DefaultModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default model for mode {ModeId} and user {UserId}", modeId, userId);
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Ensures system modes are loaded from JSON files. Thread-safe and idempotent.
    /// </summary>
    private async Task EnsureSystemModesLoadedAsync(CancellationToken ct = default)
    {
        if (_systemModesLoaded)
            return;

        await _loadSemaphore.WaitAsync(ct);
        try
        {
            if (_systemModesLoaded)
                return;

            await LoadSystemModesAsync(ct);
            _systemModesLoaded = true;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    /// <summary>
    /// Loads system modes from JSON files in the modes directory.
    /// </summary>
    private async Task LoadSystemModesAsync(CancellationToken ct = default)
    {
        try
        {
            var modesPath = Path.Combine(_hostEnvironment.ContentRootPath, "modes");
            
            if (!Directory.Exists(modesPath))
            {
                _logger.LogWarning("Modes directory not found at {ModesPath}, no system modes will be available", modesPath);
                return;
            }

            var jsonFiles = Directory.GetFiles(modesPath, "*.json", SearchOption.TopDirectoryOnly);
            _logger.LogInformation("Loading {Count} system mode files from {ModesPath}", jsonFiles.Length, modesPath);

            foreach (var filePath in jsonFiles)
            {
                try
                {
                    var jsonContent = await File.ReadAllTextAsync(filePath, ct);
                    var systemMode = JsonSerializer.Deserialize<SystemModeConfig>(jsonContent, _jsonOptions);
                    
                    if (systemMode?.Id != null)
                    {
                        _systemModes.TryAdd(systemMode.Id, systemMode);
                        _logger.LogDebug("Loaded system mode: {ModeId} from {FilePath}", systemMode.Id, filePath);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid system mode configuration in file {FilePath}: missing or null Id", filePath);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse system mode JSON file {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load system mode from file {FilePath}", filePath);
                }
            }

            _logger.LogInformation("Successfully loaded {Count} system modes", _systemModes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading system modes from directory");
        }
    }
}

/// <summary>
/// Configuration model for system modes loaded from JSON files.
/// </summary>
internal sealed class SystemModeConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string>? Tools { get; set; }
    public string? DefaultModel { get; set; }
}