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
using Microsoft.FeatureManagement;
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
    private readonly Mock<IFeatureManager> _mockFeatureManager;
    private readonly Mock<IHostEnvironment> _mockHostEnvironment;
    private readonly Mock<IStreamingBridge> _mockStreamingBridge;
    private readonly Mock<IGrainFactory> _mockGrainFactory;
    private readonly ChatController _controller;

    public ChatControllerOrleansTests()
    {
        _mockChatService = new Mock<IChatService>();
        _mockLogger = new Mock<ILogger<ChatController>>();
        _mockSseService = new Mock<IServerSentEventsService>();
        _mockTaskStorage = new Mock<ITaskStorage>();
        _mockChatStorage = new Mock<IChatStorage>();
        _mockHubContext = new Mock<IHubContext<ChatHub>>();
        _mockFeatureManager = new Mock<IFeatureManager>();
        _mockHostEnvironment = new Mock<IHostEnvironment>();
        _mockStreamingBridge = new Mock<IStreamingBridge>();
        _mockGrainFactory = new Mock<IGrainFactory>();

        _controller = new ChatController(
            _mockChatService.Object,
            _mockLogger.Object,
            _mockSseService.Object,
            _mockTaskStorage.Object,
            _mockChatStorage.Object,
            _mockHubContext.Object,
            _mockFeatureManager.Object,
            _mockHostEnvironment.Object,
            _mockGrainFactory.Object,
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
        _ = _mockFeatureManager
            .Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        // Setup cluster client and streaming bridge as available
        var mockUserGrain = new Mock<IUserGrain>();
        // Note: Test no longer uses IClusterClient - controller simplified to IGrainFactory only

        _ = mockUserGrain
            .Setup(g => g.CheckHealth())
            .ReturnsAsync(
                new HealthCheckResult
                {
                    IsHealthy = true,
                    GrainId = "health-check-user",
                    CheckedAt = DateTime.UtcNow,
                }
            );

        _ = _mockChatService
            .Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        // Act
        _ = await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
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
        _ = _mockFeatureManager
            .Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
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
        _ = _mockFeatureManager
            .Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        var mockUserGrain = new Mock<IUserGrain>();

        // Setup grain factory to return the mock user grain
        _ = _mockGrainFactory
            .Setup(gf => gf.GetGrain<IUserGrain>("test-user", null))
            .Returns(mockUserGrain.Object);

        // Make health check fail
        _ = mockUserGrain
            .Setup(g => g.CheckHealth())
            .ThrowsAsync(new InvalidOperationException("Orleans not available"));

        // Make Orleans streaming fail to test fallback
        _ = mockUserGrain
            .Setup(g => g.ProcessChatStreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Orleans streaming failed"));

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
        _ = _mockFeatureManager
            .Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        // Disable ResilientStreaming to use standard StreamingBridge
        _ = _mockFeatureManager
            .Setup(fm => fm.IsEnabledAsync("ResilientStreaming"))
            .ReturnsAsync(false);

        var mockUserGrain = new Mock<IUserGrain>();

        // Setup grain factory to return the mock user grain
        _ = _mockGrainFactory
            .Setup(gf => gf.GetGrain<IUserGrain>("test-user", null))
            .Returns(mockUserGrain.Object);

        _ = mockUserGrain
            .Setup(g => g.CheckHealth())
            .ReturnsAsync(
                new HealthCheckResult
                {
                    IsHealthy = true,
                    GrainId = "health-check-user",
                    CheckedAt = DateTime.UtcNow,
                }
            );

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

        _ = mockUserGrain
            .Setup(g =>
                g.ProcessChatStreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>())
            )
            .Returns(streamChunks.ToAsyncEnumerable());

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
