using AIChat.Orleans.Contracts;
using AIChat.Server.Controllers;
using AIChat.Server.Hubs;
using AIChat.Server.Services;
using AIChat.Server.Services.Streaming;
using AIChat.Server.Storage;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace server.Tests.Controllers;

/// <summary>
/// Extension methods for testing.
/// </summary>
public static class TestExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> enumerable)
    {
        foreach (var item in enumerable)
        {
            yield return item;
            await Task.Yield();
        }
    }
}

/// <summary>
/// Unit tests for ChatController Orleans integration features.
/// </summary>
public class ChatControllerOrleansTests
{
    private readonly Mock<IChatService> _mockChatService;
    private readonly Mock<ILogger<ChatController>> _mockLogger;
    private readonly Mock<IServerSentEventsService> _mockSseService;
    private readonly Mock<ITaskStorage> _mockTaskStorage;
    private readonly Mock<IChatStorage> _mockChatStorage;
    private readonly Mock<IHubContext<ChatHub>> _mockHubContext;
    private readonly Mock<IHostEnvironment> _mockHostEnvironment;
    private readonly Mock<AIChat.Server.Services.Routing.IDualModeRouter> _mockRouter;
    private readonly Mock<IStreamingBridge> _mockStreamingBridge;
    private readonly ChatController _controller;

    public ChatControllerOrleansTests()
    {
        _mockChatService = new Mock<IChatService>();
        _mockLogger = new Mock<ILogger<ChatController>>();
        _mockSseService = new Mock<IServerSentEventsService>();
        _mockTaskStorage = new Mock<ITaskStorage>();
        _mockChatStorage = new Mock<IChatStorage>();
        _mockHubContext = new Mock<IHubContext<ChatHub>>();
        _mockHostEnvironment = new Mock<IHostEnvironment>();
        _mockRouter = new Mock<AIChat.Server.Services.Routing.IDualModeRouter>();
        _mockStreamingBridge = new Mock<IStreamingBridge>();

        _controller = new ChatController(
            _mockChatService.Object,
            _mockLogger.Object,
            _mockRouter.Object,
            _mockTaskStorage.Object,
            _mockChatStorage.Object,
            // Optional services
            _mockSseService.Object,
            _mockHubContext.Object,
            null, // backgroundChatService
            null, // operationTrackingService
            _mockStreamingBridge.Object,
            null // resilientStreamManager
        );

        // Setup HTTP context
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    /// <summary>
    /// Configures the environment for Orleans co-hosting (Test environment).
    /// </summary>
    private void SetupOrleansCoHostingEnvironment()
    {
        _ = _mockHostEnvironment
            .Setup(env => env.EnvironmentName)
            .Returns("Test");
    }

    /// <summary>
    /// Configures the environment for production (non-co-hosted).
    /// </summary>
    private void SetupProductionEnvironment()
    {
        _ = _mockHostEnvironment
            .Setup(env => env.EnvironmentName)
            .Returns("Production");
    }

    [Fact]
    public async Task StreamChatCompletionSseWithOrleansEnabledAddsOrleansHeaders()
    {
        // Arrange
        var request = new AIChat.Server.Controllers.CreateChatRequest(
            ChatId: null,
            UserId: "test-user",
            Message: "Test message",
            SystemPrompt: null,
            ModeId: null
        );

        var initResult = new StreamInitResult
        {
            ChatId = "test-chat",
            UserMessageId = "test-message",
            UserTimestamp = DateTime.UtcNow,
            UserSequenceNumber = 1,
        };

        // Setup Orleans as enabled
        SetupOrleansCoHostingEnvironment();

        // Setup all possible variations of IsOrleansEnabledAsync to return true
        _mockRouter
            .Setup(r => r.IsOrleansEnabledAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        // Setup cluster client and streaming bridge as available
        // Note: Test no longer uses IClusterClient - controller simplified to IGrainFactory only

        // Health checks now handled by router

        _ = _mockChatService
            .Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        // Act
        _ = await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;

        // Verify that the router method was called
        _mockRouter.Verify(r => r.IsOrleansEnabledAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("true", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("orleans", response.Headers["X-Processing-Mode"]);
    }

    [Fact]
    public async Task StreamChatCompletionSseWithOrleansDisabledAddsDirectHeaders()
    {
        // Arrange
        var request = new AIChat.Server.Controllers.CreateChatRequest(
            ChatId: null,
            UserId: "test-user",
            Message: "Test message",
            SystemPrompt: null,
            ModeId: null
        );

        var initResult = new StreamInitResult
        {
            ChatId = "test-chat",
            UserMessageId = "test-message",
            UserTimestamp = DateTime.UtcNow,
            UserSequenceNumber = 1,
        };

        // Setup Orleans as disabled
        SetupProductionEnvironment();
        _ = _mockRouter
            .Setup(r => r.IsOrleansEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _ = _mockChatService
            .Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _ = _mockChatService
            .Setup(cs =>
                cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .Returns(Task.CompletedTask);

        // Act
        _ = await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("false", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("direct", response.Headers["X-Processing-Mode"]);
    }

    [Fact]
    public async Task StreamChatCompletionSseOrleansFailureFallsBackToDirect()
    {
        // Arrange
        var request = new AIChat.Server.Controllers.CreateChatRequest(
            ChatId: null,
            UserId: "test-user",
            Message: "Test message",
            SystemPrompt: null,
            ModeId: null
        );

        var initResult = new StreamInitResult
        {
            ChatId = "test-chat",
            UserMessageId = "test-message",
            UserTimestamp = DateTime.UtcNow,
            UserSequenceNumber = 1,
        };

        // Setup Orleans as enabled but failing
        SetupProductionEnvironment();
        _ = _mockRouter
            .Setup(r => r.IsOrleansEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup grain factory to return the mock user grain
        // Grain access now handled by router

        // Make health check fail
        // Health checks now handled by router

        // Make Orleans streaming fail to test fallback
        // Streaming now handled by router

        _ = _mockChatService
            .Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _ = _mockChatService
            .Setup(cs =>
                cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .Returns(Task.CompletedTask);

        // Act
        _ = await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("false", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("direct", response.Headers["X-Processing-Mode"]);

        // Verify direct processing was called
        _mockChatService.Verify(
            cs =>
                cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task StreamChatCompletionSseWithOrleansStreamingRoutesToOrleans()
    {
        // Arrange
        var request = new AIChat.Server.Controllers.CreateChatRequest(
            ChatId: null,
            UserId: "test-user",
            Message: "Test message",
            SystemPrompt: null,
            ModeId: null
        );

        var initResult = new StreamInitResult
        {
            ChatId = "test-chat",
            UserMessageId = "test-message",
            UserTimestamp = DateTime.UtcNow,
            UserSequenceNumber = 1,
        };

        // Setup Orleans as enabled
        SetupOrleansCoHostingEnvironment();

        // Setup all possible variations of IsOrleansEnabledAsync to return true
        _mockRouter
            .Setup(r => r.IsOrleansEnabledAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(true));

        // Disable ResilientStreaming to use standard StreamingBridge
        // ResilientStreaming logic now handled by router

        // Setup grain factory to return the mock user grain
        // Grain access now handled by router

        // Health checks now handled by router

        // Setup the grain streaming
        var streamChunks = new List<StreamChunk>
        {
            new()
            {
                Content = "Test",
                IsComplete = false,
                ChunkIndex = 0,
            },
            new()
            {
                Content = " response",
                IsComplete = true,
                ChunkIndex = 1,
            },
        };

        // Streaming setup now handled by router

        _ = _mockChatService
            .Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _ = _mockStreamingBridge
            .Setup(sb =>
                sb.ConvertGrainToHttpStreamAsync(
                    It.IsAny<IAsyncEnumerable<StreamChunk>>(),
                    It.IsAny<HttpResponse>(),
                    It.IsAny<Func<StreamChunk, string>>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(Task.CompletedTask);

        // Act
        _ = await _controller.StreamChatCompletionSse(request);

        // Assert - Should have routed through Orleans
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("true", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("orleans", response.Headers["X-Processing-Mode"]);

        // Verify Orleans processing was used
        _mockStreamingBridge.Verify(
            sb =>
                sb.ConvertGrainToHttpStreamAsync(
                    It.IsAny<IAsyncEnumerable<StreamChunk>>(),
                    It.IsAny<HttpResponse>(),
                    It.IsAny<Func<StreamChunk, string>>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }
}
