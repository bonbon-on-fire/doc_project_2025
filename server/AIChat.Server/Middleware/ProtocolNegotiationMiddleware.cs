using Microsoft.FeatureManagement;

namespace AIChat.Server.Middleware;

/// <summary>
/// Middleware that negotiates the communication protocol (SignalR vs SSE) based on client capabilities,
/// feature flags, and user preferences.
/// </summary>
public partial class ProtocolNegotiationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<ProtocolNegotiationMiddleware> _logger;

    public ProtocolNegotiationMiddleware(
        RequestDelegate next,
        IFeatureManager featureManager,
        ILogger<ProtocolNegotiationMiddleware> logger
    )
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _featureManager = featureManager ?? throw new ArgumentNullException(nameof(featureManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only negotiate protocol for chat-related endpoints
        if (IsProtocolNegotiationRequired(context))
        {
            var selectedProtocol = await DetermineProtocolAsync(context);

            // Store selected protocol in HttpContext for downstream components
            context.Items["PreferredProtocol"] = selectedProtocol;
            context.Items["ProtocolVersion"] = "1.0";

            // Add response headers to indicate selected protocol
            context.Response.Headers["X-Selected-Protocol"] = selectedProtocol;
            context.Response.Headers["X-Protocol-Version"] = "1.0";

            _logger.LogDebug(
                "Protocol negotiation completed. Selected: {Protocol} for path: {Path}, User-Agent: {UserAgent}",
                selectedProtocol,
                context.Request.Path,
                context.Request.Headers.UserAgent.ToString()
            );
        }

        await _next(context);
    }

    /// <summary>
    /// Determines if protocol negotiation is required for the current request.
    /// </summary>
    private static bool IsProtocolNegotiationRequired(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Negotiate for chat-related endpoints
        return path.StartsWith("/api/chat", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/messages", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/chatHub", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/chat-sse", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines the appropriate protocol based on client capabilities, feature flags, and user preferences.
    /// </summary>
    private async Task<string> DetermineProtocolAsync(HttpContext context)
    {
        // Step 1: Check if SignalR is enabled via feature flag
        var signalREnabled = await _featureManager.IsEnabledAsync("SignalRMessaging");
        if (!signalREnabled)
        {
            _logger.LogDebug("SignalR disabled by feature flag, using SSE");
            return "SSE";
        }

        // Step 2: Check client capabilities (browser support)
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (IsLegacyBrowser(userAgent))
        {
            _logger.LogDebug(
                "Legacy browser detected, falling back to SSE. User-Agent: {UserAgent}",
                userAgent
            );
            return "SSE";
        }

        // Step 3: Check for explicit protocol request (optional header)
        var requestedProtocol = context.Request.Headers["X-Requested-Protocol"].ToString();
        if (!string.IsNullOrEmpty(requestedProtocol))
        {
            if (requestedProtocol.Equals("SSE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("SSE explicitly requested via header");
                return "SSE";
            }
            if (requestedProtocol.Equals("SignalR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("SignalR explicitly requested via header");
                return "SignalR";
            }
        }

        // Step 4: Check user preferences (future enhancement - currently returns null)
        var userId = context.User?.FindFirst("sub")?.Value ?? context.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(userId))
        {
            var userPreference = await GetUserProtocolPreferenceAsync(userId);
            if (!string.IsNullOrEmpty(userPreference))
            {
                _logger.LogDebug(
                    "Using user preference: {Protocol} for user: {UserId}",
                    userPreference,
                    userId
                );
                return userPreference;
            }
        }

        // Step 5: Check for WebSocket support indication
        var upgradeHeader = context.Request.Headers.Upgrade.ToString();
        var connectionHeader = context.Request.Headers.Connection.ToString();

        // If client explicitly requests WebSocket upgrade, prefer SignalR
        if (
            upgradeHeader.Contains("websocket", StringComparison.OrdinalIgnoreCase)
            && connectionHeader.Contains("Upgrade", StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogDebug("WebSocket upgrade requested, using SignalR");
            return "SignalR";
        }

        // Default to SignalR for modern clients
        _logger.LogDebug("Using default protocol: SignalR");
        return "SignalR";
    }

    /// <summary>
    /// Checks if the user agent represents a legacy browser that doesn't support WebSockets well.
    /// </summary>
    private static bool IsLegacyBrowser(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return false;
        }

        // Check for Internet Explorer
        if (userAgent.Contains("MSIE") || userAgent.Contains("Trident"))
        {
            return true;
        }

        // Check for old Edge (pre-Chromium)
        if (userAgent.Contains("Edge/") && !userAgent.Contains("Edg/"))
        {
            // Extract Edge version and check if it's pre-Chromium (< 79)
            var edgeMatch = MyRegex().Match(userAgent);
            if (edgeMatch.Success && int.TryParse(edgeMatch.Groups[1].Value, out var version))
            {
                return version < 79;
            }
        }

        // Check for very old versions of other browsers
        // Safari < 7, Chrome < 23, Firefox < 11
        if (userAgent.Contains("Safari/") && !userAgent.Contains("Chrome"))
        {
            var safariMatch = MyRegex1().Match(userAgent);
            if (safariMatch.Success && int.TryParse(safariMatch.Groups[1].Value, out var version))
            {
                return version < 7;
            }
        }

        return false;
    }

    /// <summary>
    /// Retrieves user's protocol preference from storage (future enhancement).
    /// </summary>
    private static Task<string?> GetUserProtocolPreferenceAsync(string userId)
    {
        // TODO: Implement user preference storage and retrieval
        // This would typically query a database or cache for user preferences
        // For now, return null to indicate no preference
        return Task.FromResult<string?>(null);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Edge/(\d+)")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"Version/(\d+)")]
    private static partial System.Text.RegularExpressions.Regex MyRegex1();
}

/// <summary>
/// Extension methods for adding ProtocolNegotiationMiddleware to the pipeline.
/// </summary>
public static class ProtocolNegotiationMiddlewareExtensions
{
    /// <summary>
    /// Adds the ProtocolNegotiationMiddleware to the application pipeline.
    /// </summary>
    public static IApplicationBuilder UseProtocolNegotiation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ProtocolNegotiationMiddleware>();
    }
}
