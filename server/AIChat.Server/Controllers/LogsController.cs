using System.Text.Json;
using AIChat.Server.Services.Routing;
using Microsoft.AspNetCore.Mvc;

namespace AIChat.Server.Controllers;

/// <summary>
/// Controller for handling client-side logging operations.
/// Supports dual-mode routing between Orleans grains and direct file operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private static readonly JsonSerializerOptions S_JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogsRouter _router;
    private readonly ILogger<LogsController> _logger;

    /// <summary>
    /// Initializes a new instance of the LogsController.
    /// </summary>
    /// <param name="router">Logs router for Orleans/Direct service operations</param>
    /// <param name="logger">Logger for structured logging</param>
    public LogsController(
        ILogsRouter router,
        ILogger<LogsController> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Determine the log file path based on the current working directory
    // When running from the server directory (dotnet run), parent is project root
    // When running from bin directory (compiled), we need to go up more levels
    private static readonly string ClientLogFile = GetClientLogFilePath();

    // Static semaphore to ensure thread-safe writes to the client log file
    // Acts as a mutex (1,1) to prevent concurrent writes that could corrupt the file
    private static readonly SemaphoreSlim FileWriteLock = new(1, 1);

    private static string GetClientLogFilePath()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Check if we're in the server directory
        if (currentDir.EndsWith("server", StringComparison.OrdinalIgnoreCase))
        {
            // Running from server directory, parent is project root
            var projectRoot = Directory.GetParent(currentDir)?.FullName ?? currentDir;
            return Path.Combine(projectRoot, "logs", "client", "app.jsonl");
        }
        else if (currentDir.Contains("bin", StringComparison.OrdinalIgnoreCase))
        {
            // Running from bin directory, need to find project root
            var dir = new DirectoryInfo(currentDir);
            while (dir != null && !dir.Name.Equals("server", StringComparison.OrdinalIgnoreCase))
            {
                dir = dir.Parent;
            }
            if (dir?.Parent != null)
            {
                return Path.Combine(dir.Parent.FullName, "logs", "client", "app.jsonl");
            }
        }

        // Fallback: use current directory
        return Path.Combine(currentDir, "logs", "client", "app.jsonl");
    }

    /// <summary>
    /// Accept client log entries via POST /api/logs
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LogClientEntry([FromBody] JsonElement logEntry, CancellationToken cancellationToken = default)
    {
        return await _router.ExecuteLogEntryAsync(
            logEntry,
            // Orleans operation - TODO: Map to appropriate grain method for distributed logging coordination
            null, // For now, no Orleans operation (direct file logging is more efficient for simple logging)
            // Direct operation
            async entry =>
            {
                return await CreateLogEntryResponse(entry);
            },
            "LogClientEntry",
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a standardized log entry response from the direct operation.
    /// </summary>
    private async Task<IActionResult> CreateLogEntryResponse(JsonElement logEntry)
    {
        try
        {
            // Log the path being used (only once per app lifetime)
            if (!_pathLogged)
            {
                _logger.LogInformation("Client log file path: {LogPath}", ClientLogFile);
                _logger.LogInformation(
                    "Current directory: {CurrentDir}",
                    Directory.GetCurrentDirectory()
                );
                _pathLogged = true;
            }

            // Ensure the client logs directory exists
            var directory = Path.GetDirectoryName(ClientLogFile);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            // Serialize the log entry as JSONL (one JSON object per line)
            var jsonString = JsonSerializer.Serialize(logEntry, S_JsonSerializerOptions);

            // Use semaphore to ensure thread-safe writes to the file
            await FileWriteLock.WaitAsync();
            try
            {
                // Append to the JSONL file
                await System.IO.File.AppendAllTextAsync(
                    ClientLogFile,
                    jsonString + Environment.NewLine
                );
            }
            finally
            {
                _ = FileWriteLock.Release();
            }

            // Also log to server's structured logging system for correlation
            _logger.LogDebug("Client log entry written to file: {ClientLog}", jsonString);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write client log entry to {LogPath}", ClientLogFile);
            return StatusCode(500, $"Failed to write log entry: {ex.Message}");
        }
    }

    private static bool _pathLogged;
}
