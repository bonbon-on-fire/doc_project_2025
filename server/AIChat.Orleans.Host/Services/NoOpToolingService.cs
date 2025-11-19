using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AIChat.Orleans.Services;

namespace AIChat.Orleans.Host.Services;

/// <summary>
/// No-op implementation of IToolingService used when MCP middleware is not available
/// This allows the application to compile and run without MCP functionality
/// </summary>
/// <remarks>
/// TODO: Replace with ToolingService when MCP middleware becomes available in LmDotnetTools
/// </remarks>
public class NoOpToolingService : IToolingService
{
    private readonly ILogger<NoOpToolingService> _logger;

    public NoOpToolingService(ILogger<NoOpToolingService> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "NoOpToolingService is active. MCP middleware is not available. Tool functionality is disabled."
        );
    }

    public Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddlewareAsync(
        string chatId,
        string? modeId = null,
        string? userId = null,
        IToolResultCallback? resultCallback = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug(
            "NoOpToolingService.CreateChatSpecificFunctionCallMiddlewareAsync called for chat {ChatId}. Returning null (no tools available).",
            chatId
        );
        return Task.FromResult<FunctionCallMiddleware?>(null);
    }
}
