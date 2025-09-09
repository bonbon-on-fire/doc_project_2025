using System.Security.Claims;
using AIChat.Server.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Middleware;

public class ProtocolNegotiationMiddlewareTests
{
    private readonly Mock<IFeatureManager> _featureManager;
    private readonly Mock<ILogger<ProtocolNegotiationMiddleware>> _logger;
    private readonly ProtocolNegotiationMiddleware _middleware;
    private bool _nextCalled;

    public ProtocolNegotiationMiddlewareTests()
    {
        _featureManager = new Mock<IFeatureManager>();
        _logger = new Mock<ILogger<ProtocolNegotiationMiddleware>>();
        _nextCalled = false;

        _middleware = new ProtocolNegotiationMiddleware(
            next: (httpContext) =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            },
            _featureManager.Object,
            _logger.Object);
    }

    [Fact]
    public async Task InvokeAsyncWithChatEndpointNegotiatesProtocol()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
        Assert.Equal("1.0", context.Items["ProtocolVersion"]);
        Assert.Equal("SignalR", context.Response.Headers["X-Selected-Protocol"].ToString());
        Assert.Equal("1.0", context.Response.Headers["X-Protocol-Version"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithNonChatEndpointSkipsNegotiation()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/health";
        context.Request.Headers.UserAgent = "Mozilla/5.0";

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.False(context.Items.ContainsKey("PreferredProtocol"));
        Assert.False(context.Response.Headers.ContainsKey("X-Selected-Protocol"));
    }

    [Fact]
    public async Task InvokeAsyncWithSignalRDisabledSelectsSSE()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(false);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SSE", context.Items["PreferredProtocol"]);
        Assert.Equal("SSE", context.Response.Headers["X-Selected-Protocol"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithLegacyBrowserSelectsSSE()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.1; Trident/6.0)";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SSE", context.Items["PreferredProtocol"]);
        Assert.Equal("SSE", context.Response.Headers["X-Selected-Protocol"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithExplicitSSERequestSelectsSSE()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        context.Request.Headers["X-Requested-Protocol"] = "SSE";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SSE", context.Items["PreferredProtocol"]);
        Assert.Equal("SSE", context.Response.Headers["X-Selected-Protocol"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithExplicitSignalRRequestSelectsSignalR()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        context.Request.Headers["X-Requested-Protocol"] = "SignalR";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
        Assert.Equal("SignalR", context.Response.Headers["X-Selected-Protocol"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithWebSocketUpgradeSelectsSignalR()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        context.Request.Headers.Upgrade = "websocket";
        context.Request.Headers.Connection = "Upgrade";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
        Assert.Equal("SignalR", context.Response.Headers["X-Selected-Protocol"].ToString());
    }

    [Fact]
    public async Task InvokeAsyncWithOldEdgeBrowserSelectsSSE()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.140 Safari/537.36 Edge/18.17763";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SSE", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithNewEdgeBrowserSelectsSignalR()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithMessageEndpointNegotiatesProtocol()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/messages/list";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithChatHubEndpointNegotiatesProtocol()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/chatHub";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithSSEEndpointNegotiatesProtocol()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat-sse";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithAuthenticatedUserChecksUserPreference()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user123")
        ]));
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        // Currently returns SignalR as user preferences are not implemented
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithEmptyUserAgentSelectsSignalR()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        // No User-Agent header
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }

    [Fact]
    public async Task InvokeAsyncWithInvalidRequestedProtocolDefaultsToSignalR()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat/send";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/120.0";
        context.Request.Headers["X-Requested-Protocol"] = "InvalidProtocol";
        _ = _featureManager.Setup(x => x.IsEnabledAsync("SignalRMessaging"))
            .ReturnsAsync(true);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("SignalR", context.Items["PreferredProtocol"]);
    }
}
