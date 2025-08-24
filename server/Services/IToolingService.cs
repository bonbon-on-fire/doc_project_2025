using System.Threading;
using System.Threading.Tasks;
using AchieveAi.LmDotnetTools.LmCore.Middleware;

namespace AIChat.Server.Services;

/// <summary>
/// Service responsible for managing tool registrations and creating function call middleware
/// </summary>
public interface IToolingService
{
    /// <summary>
    /// Creates a chat-specific FunctionCallMiddleware with all registered tools
    /// </summary>
    /// <param name="chatId">The chat ID for which to create the middleware</param>
    /// <param name="modeId">Optional mode ID for tool filtering</param>
    /// <param name="userId">Optional user ID for mode-based tool filtering</param>
    /// <param name="resultCallback">Optional callback for tool execution results</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Configured FunctionCallMiddleware or null if no tools are available</returns>
    Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddlewareAsync(
        string chatId,
        string? modeId = null,
        string? userId = null,
        IToolResultCallback? resultCallback = null,
        CancellationToken cancellationToken = default);
}