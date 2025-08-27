using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AchieveAi.LmDotnetTools.McpMiddleware;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Functions;
using AIChat.Server.Models;
using AIChat.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

/// <summary>
/// Tests for ToolingService function filtering integration
/// </summary>
public class ToolingServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IMcpClientManager> _mockMcpClientManager;
    private readonly Mock<ITaskManagerService> _mockTaskManagerService;
    private readonly Mock<IModeService> _mockModeService;
    private readonly Mock<ILogger<ToolingService>> _mockLogger;
    private readonly Mock<IOptions<McpConfiguration>> _mockOptions;
    private readonly Mock<IMcpClient> _mockMcpClient;

    public ToolingServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockMcpClientManager = new Mock<IMcpClientManager>();
        _mockTaskManagerService = new Mock<ITaskManagerService>();
        _mockModeService = new Mock<IModeService>();
        _mockLogger = new Mock<ILogger<ToolingService>>();
        _mockOptions = new Mock<IOptions<McpConfiguration>>();
        _mockMcpClient = new Mock<IMcpClient>();

        // Setup default service provider behavior
        var mockFunctionCallLogger = new Mock<ILogger<FunctionCallMiddleware>>();
        var mockWeatherLogger = new Mock<ILogger<WeatherFunction>>();
        var mockMcpClientLogger = new Mock<ILogger<McpClientFunctionProvider>>();

        // Mock GetService to return appropriate loggers (GetRequiredService uses GetService internally)
        _ = _mockServiceProvider
            .Setup(x => x.GetService(typeof(ILogger<FunctionCallMiddleware>)))
            .Returns(mockFunctionCallLogger.Object);
        _ = _mockServiceProvider
            .Setup(x => x.GetService(typeof(ILogger<WeatherFunction>)))
            .Returns(mockWeatherLogger.Object);
        _ = _mockServiceProvider
            .Setup(x => x.GetService(typeof(ILogger<McpClientFunctionProvider>)))
            .Returns(mockMcpClientLogger.Object);

        // Setup TaskManagerService to return a valid TaskManager
        var taskManager = new AchieveAi.LmDotnetTools.Misc.Utils.TaskManager();
        _ = _mockTaskManagerService
            .Setup(x => x.GetTaskManagerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskManager);
    }

    #region FunctionFiltering Tests

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithFunctionFiltering_AppliesConfiguration()
    {
        // Arrange
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = new List<string> { "search*", "read*" },
                GlobalBlockedFunctions = new List<string> { "delete*" },
                UsePrefixOnlyForCollisions = true,
                ProviderConfigs = new Dictionary<string, ProviderFilterConfig>
                {
                    ["MCP_github"] = new ProviderFilterConfig
                    {
                        AllowedFunctions = new List<string> { "create_issue", "list_*" },
                        BlockedFunctions = new List<string> { "delete_repo" },
                        CustomPrefix = "gh_",
                    },
                },
            },
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["github"] = new McpServerConfig { Type = "stdio", Command = "mcp-server-github" },
            },
        };

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        // Setup MCP client manager to return our mocked clients
        var mcpClients = new Dictionary<string, IMcpClient> { ["github"] = _mockMcpClient.Object };
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpClients);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null, // modeId
            null, // userId
            null, // resultCallback
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
        // Verify that filtering configuration was applied through logging
        _mockLogger.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (v, t) => v.ToString()!.Contains("Function filtering enabled")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithLegacyToolFiltering_MapsToFunctionFiltering()
    {
        // Arrange
#pragma warning disable CS0618 // Type or member is obsolete
        var config = new McpConfiguration
        {
            ToolFiltering = new AIChat.Server.Models.McpToolFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedTools = new List<string> { "search*" },
                GlobalBlockedTools = new List<string> { "delete*" },
                UsePrefixOnlyForCollisions = true,
            },
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["github"] = new McpServerConfig
                {
                    Type = "stdio",
                    Command = "mcp-server-github",
                    AllowedTools = new List<string> { "create_issue" },
                },
            },
        };
#pragma warning restore CS0618 // Type or member is obsolete

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        var mcpClients = new Dictionary<string, IMcpClient> { ["github"] = _mockMcpClient.Object };
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpClients);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null,
            null,
            null,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
        // Verify that legacy configuration was mapped and applied
        _mockLogger.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (v, t) => v.ToString()!.Contains("Function filtering enabled")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithoutFiltering_DoesNotApplyConfiguration()
    {
        // Arrange
        var config = new McpConfiguration
        {
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["github"] = new McpServerConfig { Type = "stdio", Command = "mcp-server-github" },
            },
        };

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        var mcpClients = new Dictionary<string, IMcpClient> { ["github"] = _mockMcpClient.Object };
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpClients);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null,
            null,
            null,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
        // Verify that no filtering was applied
        _mockLogger.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("MCP tool filtering")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithMultipleProviders_AppliesProviderSpecificConfig()
    {
        // Arrange
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = new List<string> { "*" },
                UsePrefixOnlyForCollisions = false,
                ProviderConfigs = new Dictionary<string, ProviderFilterConfig>
                {
                    ["MCP_github"] = new ProviderFilterConfig
                    {
                        AllowedFunctions = new List<string> { "create_*", "list_*" },
                        CustomPrefix = "gh_",
                    },
                    ["MCP_gitlab"] = new ProviderFilterConfig
                    {
                        AllowedFunctions = new List<string> { "merge_*", "push_*" },
                        CustomPrefix = "gl_",
                    },
                },
            },
            McpServers = new Dictionary<string, McpServerConfig>
            {
                ["github"] = new McpServerConfig { Type = "stdio", Command = "mcp-server-github" },
                ["gitlab"] = new McpServerConfig { Type = "stdio", Command = "mcp-server-gitlab" },
            },
        };

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        var mockGitLabClient = new Mock<IMcpClient>();
        var mcpClients = new Dictionary<string, IMcpClient>
        {
            ["github"] = _mockMcpClient.Object,
            ["gitlab"] = mockGitLabClient.Object,
        };
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpClients);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null,
            null,
            null,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
        _mockLogger.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (v, t) => v.ToString()!.Contains("Function filtering enabled")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithNullMcpClients_HandlesGracefully()
    {
        // Arrange
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = new List<string> { "*" },
            },
        };

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        // Return null from MCP client manager
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, IMcpClient>?)null);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null,
            null,
            null,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
    }

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithEmptyMcpClients_HandlesGracefully()
    {
        // Arrange
        var config = new McpConfiguration
        {
            FunctionFiltering = new FunctionFilterConfig
            {
                EnableFiltering = true,
                GlobalAllowedFunctions = new List<string> { "*" },
            },
        };

        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        var mcpClients = new Dictionary<string, IMcpClient>();
        _ = _mockMcpClientManager
            .Setup(x => x.GetActiveClientsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mcpClients);

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act
        var middleware = await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            "test-chat-id",
            null,
            null,
            null,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(middleware);
    }

    [Fact]
    public async Task CreateChatSpecificFunctionCallMiddleware_WithCancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var config = new McpConfiguration();
        _ = _mockOptions.Setup(x => x.Value).Returns(config);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Setup TaskManagerService to throw when cancelled
        _ = _mockTaskManagerService
            .Setup(x => x.GetTaskManagerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var toolingService = new ToolingService(
            _mockServiceProvider.Object,
            _mockMcpClientManager.Object,
            _mockTaskManagerService.Object,
            _mockModeService.Object,
            _mockOptions.Object,
            _mockLogger.Object
        );

        // Act & Assert
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () =>
                toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
                    "test-chat-id",
                    null,
                    null,
                    null,
                    cts.Token
                )
        );
    }

    #endregion
}
