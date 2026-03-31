using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(ILogger<LogsController> logger) : ControllerBase
{
    private readonly static JsonSerializerOptions S_JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<LogsController> _logger = logger;
    
    // Determine the log file path based on the current working directory
    // When running from the server directory (dotnet run), parent is project root
    // When running from bin directory (compiled), we need to go up more levels
    private static readonly string ClientLogFile = GetClientLogFilePath();
    
    // Static semaphore to ensure thread-safe writes to the client log file
    // Acts as a mutex (1,1) to prevent concurrent writes that could corrupt the file
    private static readonly SemaphoreSlim FileWriteLock = new SemaphoreSlim(1, 1);
    
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

    // Accept POST /api/logs
    [HttpPost]
    public async Task<IActionResult> LogClientEntry([FromBody] JsonElement logEntry)
    {
        try
        {
            // Log the path being used (only once per app lifetime)
            if (!_pathLogged)
            {
                _logger.LogInformation("Client log file path: {LogPath}", ClientLogFile);
                _logger.LogInformation("Current directory: {CurrentDir}", Directory.GetCurrentDirectory());
                _pathLogged = true;
            }
            
            // Ensure the client logs directory exists
            var directory = Path.GetDirectoryName(ClientLogFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
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
                    jsonString + Environment.NewLine);
            }
            finally
            {
                FileWriteLock.Release();
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
    
    private static bool _pathLogged = false;
}
