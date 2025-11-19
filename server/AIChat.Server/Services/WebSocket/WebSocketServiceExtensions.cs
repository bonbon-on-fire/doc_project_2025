using AIChat.Server.Handlers;

namespace AIChat.Server.Services.WebSocket;

/// <summary>
/// Extension methods for registering WebSocket services in the dependency injection container.
/// Provides comprehensive service registration for WebSocket handler functionality with Orleans integration.
/// </summary>
public static class WebSocketServiceExtensions
{
    /// <summary>
    /// Adds WebSocket services to the service collection with default configuration.
    /// This registers all necessary services for WebSocket functionality including handlers,
    /// session management, protocol negotiation, and message routing.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddWebSocketServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register WebSocket core services
        _ = services.AddScoped<IWebSocketHandler, WebSocketHandler>();
        _ = services.AddScoped<IWebSocketSessionManager, WebSocketSessionManager>();
        _ = services.AddScoped<IWebSocketProtocolNegotiator, WebSocketProtocolNegotiator>();
        _ = services.AddScoped<IWebSocketMessageRouter, WebSocketMessageRouter>();

        return services;
    }

    /// <summary>
    /// Adds WebSocket middleware to the application pipeline.
    /// This configures the WebSocket middleware with default settings for handling WebSocket connections.
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <param name="path">The path for WebSocket connections (default: "/ws")</param>
    /// <returns>The application builder for method chaining</returns>
    public static IApplicationBuilder UseWebSocketHandler(this IApplicationBuilder app, string path = "/ws")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = new WebSocketMiddlewareOptions { WebSocketPath = path };
        return app.UseMiddleware<WebSocketMiddleware>(options);
    }
}
