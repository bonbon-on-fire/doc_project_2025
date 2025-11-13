using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;

namespace AIChat.Server.Handlers;

/// <summary>
/// <para>
/// ASP.NET Core middleware for handling WebSocket connections.
/// Integrates WebSocket protocol handling into the request pipeline and delegates
/// to the WebSocketHandler for comprehensive connection management.
/// </para>
/// <para>
/// Features:
/// - WebSocket upgrade request detection and handling
/// - Authentication and authorization integration
/// - Request validation and security checks
/// - Integration with existing ASP.NET Core pipeline
/// - Comprehensive logging and error handling
/// </para>
/// </summary>
public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebSocketMiddleware> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketMiddleware");

    /// <summary>
    /// Configuration
    /// </summary>
    private readonly WebSocketMiddlewareOptions _options;

    /// <summary>
    /// Initializes a new instance of the WebSocketMiddleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="options">Middleware configuration options</param>
    public WebSocketMiddleware(
        RequestDelegate next,
        ILogger<WebSocketMiddleware> logger,
        WebSocketMiddlewareOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new WebSocketMiddlewareOptions();
    }

    /// <summary>
    /// Processes the HTTP request and handles WebSocket upgrade requests.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <returns>Task representing the async operation</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        using var activity = ActivitySource.StartActivity("WebSocketMiddleware");

        try
        {
            // Check if this is a WebSocket request for our endpoint
            if (!IsWebSocketRequest(context))
            {
                await _next(context);
                return;
            }

            _logger.LogInformation(
                "WebSocket upgrade request received from {RemoteIP} for path {Path}",
                context.Connection.RemoteIpAddress, context.Request.Path);

            activity?.SetTag("request.path", context.Request.Path.Value);
            activity?.SetTag("request.method", context.Request.Method);
            activity?.SetTag("remote.ip", context.Connection.RemoteIpAddress?.ToString());

            // Validate the request
            var validationResult = await ValidateWebSocketRequestAsync(context);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "WebSocket request validation failed: {ErrorMessage}",
                    validationResult.ErrorMessage);

                context.Response.StatusCode = validationResult.StatusCode;
                await context.Response.WriteAsync(validationResult.ErrorMessage ?? "Invalid WebSocket request");

                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "validation_failed");
                return;
            }

            // Perform authentication if required
            if (_options.RequireAuthentication && context.User.Identity?.IsAuthenticated == false)
            {
                _logger.LogWarning("Unauthenticated WebSocket request rejected");

                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Authentication required");

                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "authentication_required");
                return;
            }

            // Check authorization if configured
            if (!await CheckAuthorizationAsync(context))
            {
                _logger.LogWarning("Unauthorized WebSocket request rejected for user {UserId}",
                    context.User.Identity?.Name ?? "unknown");

                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Insufficient permissions");

                activity?.SetTag("operation.success", false);
                activity?.SetTag("error.reason", "authorization_failed");
                return;
            }

            // Accept the WebSocket connection
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();

            _logger.LogDebug("WebSocket connection accepted for {ConnectionId}", context.Connection.Id);

            activity?.SetTag("websocket.accepted", true);
            activity?.SetTag("operation.success", true);

            // Delegate to the WebSocket handler
            var webSocketHandler = context.RequestServices.GetRequiredService<IWebSocketHandler>();
            await webSocketHandler.HandleWebSocketAsync(context, webSocket, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket middleware error for request {Path}", context.Request.Path);

            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);

            // Try to return an error response if headers haven't been sent
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error");
            }
        }
    }

    #region Private Helper Methods

    private bool IsWebSocketRequest(HttpContext context)
    {
        // Check if this is a WebSocket upgrade request for our configured path
        return context.WebSockets.IsWebSocketRequest &&
               context.Request.Path.StartsWithSegments(_options.WebSocketPath, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<WebSocketRequestValidationResult> ValidateWebSocketRequestAsync(HttpContext context)
    {
        try
        {
            // Basic WebSocket request validation
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return WebSocketRequestValidationResult.Failure(
                    "Not a WebSocket request", 400);
            }

            // Check for required headers if configured
            if (_options.RequiredHeaders.Count > 0)
            {
                foreach (var requiredHeader in _options.RequiredHeaders)
                {
                    if (!context.Request.Headers.TryGetValue(requiredHeader.Key, out var value))
                    {
                        return WebSocketRequestValidationResult.Failure(
                            $"Missing required header: {requiredHeader.Key}", 400);
                    }

                    if (requiredHeader.Value != null &&
                        !string.Equals(value, requiredHeader.Value,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return WebSocketRequestValidationResult.Failure(
                            $"Invalid header value for {requiredHeader.Key}", 400);
                    }
                }
            }

            // Validate origin if configured
            if (_options.AllowedOrigins.Count > 0)
            {
                var origin = context.Request.Headers.Origin.FirstOrDefault();
                if (string.IsNullOrEmpty(origin) ||
                    !_options.AllowedOrigins.Any(allowed =>
                        string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase)))
                {
                    return WebSocketRequestValidationResult.Failure(
                        "Origin not allowed", 403);
                }
            }

            // Rate limiting check if configured
            if (_options.RateLimitingEnabled)
            {
                var isAllowed = await CheckRateLimitAsync(context);
                if (!isAllowed)
                {
                    return WebSocketRequestValidationResult.Failure(
                        "Rate limit exceeded", 429);
                }
            }

            return WebSocketRequestValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket request validation");
            return WebSocketRequestValidationResult.Failure(
                "Validation error", 500);
        }
    }

    private async Task<bool> CheckAuthorizationAsync(HttpContext context)
    {
        try
        {
            // If no specific authorization requirements, allow
            if (string.IsNullOrEmpty(_options.RequiredRole) && string.IsNullOrEmpty(_options.RequiredPolicy))
            {
                return true;
            }

            // Check required role
            if (!string.IsNullOrEmpty(_options.RequiredRole) &&
                !context.User.IsInRole(_options.RequiredRole))
            {
                return false;
            }

            // Check required policy (if authorization service is available)
            if (!string.IsNullOrEmpty(_options.RequiredPolicy))
            {
                var authorizationService = context.RequestServices.GetService<IAuthorizationService>();
                if (authorizationService != null)
                {
                    var result = await authorizationService.AuthorizeAsync(
                        context.User, null, _options.RequiredPolicy);
                    return result.Succeeded;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authorization check");
            return false;
        }
    }

    private async Task<bool> CheckRateLimitAsync(HttpContext context)
    {
        // Basic rate limiting implementation
        // In a production system, this would integrate with a proper rate limiting service
        try
        {
            var clientId = GetClientIdentifier(context);
            var cacheKey = $"websocket_rate_limit_{clientId}";

            // This is a simplified rate limiting check
            // In production, use a proper rate limiting library or service
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit check");
            return true; // Allow on error to avoid blocking legitimate requests
        }
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // Use user ID if authenticated, otherwise fall back to IP address
        return context.User.Identity?.Name ??
               context.Connection.RemoteIpAddress?.ToString() ??
               "unknown";
    }

    #endregion Private Helper Methods
}

/// <summary>
/// Configuration options for the WebSocket middleware.
/// </summary>
public class WebSocketMiddlewareOptions
{
    /// <summary>
    /// Gets or sets the path that WebSocket requests should be handled on.
    /// Default is "/ws".
    /// </summary>
    public PathString WebSocketPath { get; set; } = "/ws";

    /// <summary>
    /// Gets or sets whether authentication is required for WebSocket connections.
    /// Default is false.
    /// </summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>
    /// Gets or sets the required role for WebSocket connections.
    /// If null or empty, no role check is performed.
    /// </summary>
    public string? RequiredRole { get; set; }

    /// <summary>
    /// Gets or sets the required authorization policy for WebSocket connections.
    /// If null or empty, no policy check is performed.
    /// </summary>
    public string? RequiredPolicy { get; set; }

    /// <summary>
    /// Gets or sets the allowed origins for WebSocket connections.
    /// If empty, all origins are allowed.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>
    /// Gets or sets required headers for WebSocket requests.
    /// Key is header name, value is expected value (null means any value is acceptable).
    /// </summary>
    public Dictionary<string, string?> RequiredHeaders { get; set; } = [];

    /// <summary>
    /// Gets or sets whether rate limiting is enabled.
    /// Default is false.
    /// </summary>
    public bool RateLimitingEnabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of requests per time window for rate limiting.
    /// Default is 100.
    /// </summary>
    public int RateLimitMaxRequests { get; set; } = 100;

    /// <summary>
    /// Gets or sets the time window for rate limiting.
    /// Default is 1 minute.
    /// </summary>
    public TimeSpan RateLimitTimeWindow { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Represents the result of WebSocket request validation.
/// </summary>
public record WebSocketRequestValidationResult
{
    /// <summary>
    /// Gets whether the validation was successful.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the HTTP status code to return if validation failed.
    /// </summary>
    public int StatusCode { get; init; } = 400;

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A successful validation result</returns>
    public static WebSocketRequestValidationResult Success()
    {
        return new WebSocketRequestValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="statusCode">The HTTP status code</param>
    /// <returns>A failed validation result</returns>
    public static WebSocketRequestValidationResult Failure(string errorMessage, int statusCode = 400)
    {
        return new WebSocketRequestValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            StatusCode = statusCode
        };
    }
}

/// <summary>
/// Extension methods for configuring WebSocket middleware.
/// </summary>
public static class WebSocketMiddlewareExtensions
{
    /// <summary>
    /// Adds WebSocket middleware to the application pipeline.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="options">Optional middleware configuration</param>
    /// <returns>The application builder</returns>
    public static IApplicationBuilder UseWebSocketHandler(
        this IApplicationBuilder app,
        WebSocketMiddlewareOptions? options = null)
    {
        return app.UseMiddleware<WebSocketMiddleware>(options ?? new WebSocketMiddlewareOptions());
    }

    /// <summary>
    /// Adds WebSocket middleware to the application pipeline with configuration.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>The application builder</returns>
    public static IApplicationBuilder UseWebSocketHandler(
        this IApplicationBuilder app,
        Action<WebSocketMiddlewareOptions> configure)
    {
        var options = new WebSocketMiddlewareOptions();
        configure(options);
        return app.UseMiddleware<WebSocketMiddleware>(options);
    }
}
