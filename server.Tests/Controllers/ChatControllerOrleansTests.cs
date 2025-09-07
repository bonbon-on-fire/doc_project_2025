using System.Text;
using System.Text.Json;
using AIChat.Orleans.Contracts;
using AIChat.Server.Controllers;
using AIChat.Server.Hubs;
using AIChat.Server.Models;
using AIChat.Server.Services;
using AIChat.Server.Services.Streaming;
using AIChat.Server.Storage;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Moq;
using Orleans;
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
    private readonly Mock<IOptions<BackgroundProcessingOptions>> _mockBackgroundOptions;
    private readonly Mock<IClusterClient> _mockClusterClient;
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
        _mockFeatureManager = new Mock<IFeatureManager>();
        _mockBackgroundOptions = new Mock<IOptions<BackgroundProcessingOptions>>();
        _mockClusterClient = new Mock<IClusterClient>();
        _mockStreamingBridge = new Mock<IStreamingBridge>();

        _mockBackgroundOptions.Setup(o => o.Value)
            .Returns(new BackgroundProcessingOptions());

        _controller = new ChatController(
            _mockChatService.Object,
            _mockLogger.Object,
            _mockSseService.Object,
            _mockTaskStorage.Object,
            _mockChatStorage.Object,
            _mockHubContext.Object,
            _mockFeatureManager.Object,
            _mockBackgroundOptions.Object,
            _mockClusterClient.Object,
            null, // backgroundChatService
            null, // operationTrackingService
            _mockStreamingBridge.Object
        );

        // Setup HTTP context
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithOrleansEnabled_AddsOrleansHeaders()
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
            UserSequenceNumber = 1
        };

        // Setup Orleans as enabled
        _mockFeatureManager.Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        // Setup cluster client and streaming bridge as available
        var mockUserGrain = new Mock<IUserGrain>();
        _mockClusterClient.Setup(c => c.GetGrain<IUserGrain>("health-check-user", null))
            .Returns(mockUserGrain.Object);
        
        mockUserGrain.Setup(g => g.CheckHealth())
            .ReturnsAsync(new HealthCheckResult
            {
                IsHealthy = true,
                GrainId = "health-check-user",
                CheckedAt = DateTime.UtcNow
            });

        _mockChatService.Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        // Act
        await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("true", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("orleans", response.Headers["X-Processing-Mode"]);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithOrleansDisabled_AddsDirectHeaders()
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
            UserSequenceNumber = 1
        };

        // Setup Orleans as disabled
        _mockFeatureManager.Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(false);

        _mockChatService.Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _mockChatService.Setup(cs => cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("false", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("direct", response.Headers["X-Processing-Mode"]);
    }

    [Fact]
    public async Task StreamChatCompletionSse_OrleansFailure_FallsBackToDirect()
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
            UserSequenceNumber = 1
        };

        // Setup Orleans as enabled but failing
        _mockFeatureManager.Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        var mockUserGrain = new Mock<IUserGrain>();
        _mockClusterClient.Setup(c => c.GetGrain<IUserGrain>("health-check-user", null))
            .Returns(mockUserGrain.Object);
        
        // Make health check fail
        mockUserGrain.Setup(g => g.CheckHealth())
            .ThrowsAsync(new Exception("Orleans not available"));

        _mockChatService.Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _mockChatService.Setup(cs => cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _controller.StreamChatCompletionSse(request);

        // Assert
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("false", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("direct", response.Headers["X-Processing-Mode"]);
        
        // Verify direct processing was called
        _mockChatService.Verify(cs => cs.StreamAssistantResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamChatCompletionSse_WithOrleansStreaming_RoutesToOrleans()
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
            UserSequenceNumber = 1
        };

        // Setup Orleans as enabled
        _mockFeatureManager.Setup(fm => fm.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        var mockUserGrain = new Mock<IUserGrain>();
        _mockClusterClient.Setup(c => c.GetGrain<IUserGrain>("health-check-user", null))
            .Returns(mockUserGrain.Object);
        _mockClusterClient.Setup(c => c.GetGrain<IUserGrain>(request.UserId, null))
            .Returns(mockUserGrain.Object);
        
        mockUserGrain.Setup(g => g.CheckHealth())
            .ReturnsAsync(new HealthCheckResult
            {
                IsHealthy = true,
                GrainId = "health-check-user",
                CheckedAt = DateTime.UtcNow
            });

        // Setup the grain streaming
        var streamChunks = new List<StreamChunk>
        {
            new StreamChunk { Content = "Test", IsComplete = false, ChunkIndex = 0 },
            new StreamChunk { Content = " response", IsComplete = true, ChunkIndex = 1 }
        };
        
        mockUserGrain.Setup(g => g.ProcessChatStreamAsync(It.IsAny<ChatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(streamChunks.ToAsyncEnumerable());

        _mockChatService.Setup(cs => cs.PrepareUnifiedStreamChatAsync(It.IsAny<StreamChatRequest>()))
            .ReturnsAsync(initResult);

        _mockStreamingBridge.Setup(sb => sb.ConvertGrainToHttpStreamAsync(
            It.IsAny<IAsyncEnumerable<StreamChunk>>(),
            It.IsAny<HttpResponse>(),
            It.IsAny<Func<StreamChunk, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _controller.StreamChatCompletionSse(request);

        // Assert - Should have routed through Orleans
        var response = _controller.Response;
        Assert.True(response.Headers.ContainsKey("X-Orleans-Routed"));
        Assert.Equal("true", response.Headers["X-Orleans-Routed"]);
        Assert.True(response.Headers.ContainsKey("X-Processing-Mode"));
        Assert.Equal("orleans", response.Headers["X-Processing-Mode"]);
        
        // Verify Orleans processing was used
        _mockStreamingBridge.Verify(sb => sb.ConvertGrainToHttpStreamAsync(
            It.IsAny<IAsyncEnumerable<StreamChunk>>(),
            It.IsAny<HttpResponse>(),
            It.IsAny<Func<StreamChunk, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}