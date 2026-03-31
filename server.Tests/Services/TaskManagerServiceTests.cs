using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using System.Text.Json;
using FluentAssertions;

namespace AIChat.Server.Tests.Services;

public class TaskManagerServiceTests
{
    private readonly Mock<ITaskStorage> _mockTaskStorage;
    private readonly Mock<ILogger<ImprovedTaskManagerService>> _mockLogger;
    private readonly ImprovedTaskManagerService _service;

    public TaskManagerServiceTests()
    {
        _mockTaskStorage = new Mock<ITaskStorage>();
        _mockLogger = new Mock<ILogger<ImprovedTaskManagerService>>();
        _service = new ImprovedTaskManagerService(_mockTaskStorage.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetTaskManagerAsync_CreatesNewInstance_WhenNoneExists()
    {
        // Arrange
        var chatId = "test-chat-1";
        _mockTaskStorage.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager1 = await _service.GetTaskManagerAsync(chatId);
        var taskManager2 = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager1.Should().NotBeNull();
        taskManager2.Should().NotBeNull();
        taskManager1.Should().BeSameAs(taskManager2, "should return cached instance");
    }

    [Fact]
    public async Task GetTaskManagerAsync_LoadsExistingState_WhenExists()
    {
        // Arrange
        var chatId = "test-chat-2";
        // Create a TaskManager with a test task
        var savedTaskManager = new TaskManager();
        savedTaskManager.AddTask("Test Task");

        var taskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = savedTaskManager,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _mockTaskStorage.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskState);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager.Should().NotBeNull();
        _mockTaskStorage.Verify(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveTaskManagerStateAsync_PersistsState_Successfully()
    {
        // Arrange
        var chatId = "test-chat-3";
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        
        var newState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = taskManager,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _mockTaskStorage.Setup(x => x.SaveTasksAsync(
                chatId,
                It.IsAny<TaskManager>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newState);

        // Act
        await _service.SaveTaskManagerStateAsync(chatId);

        // Assert
        _mockTaskStorage.Verify(x => x.SaveTasksAsync(
            chatId,
            It.IsAny<TaskManager>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearTaskManagerAsync_RemovesFromCacheAndStorage()
    {
        // Arrange
        var chatId = "test-chat-4";
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Act
        await _service.ClearTaskManagerAsync(chatId);

        // Assert
        _mockTaskStorage.Verify(x => x.DeleteTasksAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
        
        // Getting TaskManager again should create a new instance
        var newTaskManager = await _service.GetTaskManagerAsync(chatId);
        newTaskManager.Should().NotBeSameAs(taskManager);
    }

    [Fact]
    public async Task GetTaskStateAsync_ReturnsProperJsonStructure()
    {
        // Arrange
        var chatId = "test-chat-5";
        await _service.GetTaskManagerAsync(chatId);

        // Act
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        taskState.Should().NotBeNull();
        var (markdown, tasks) = taskState.Value;
        markdown.Should().NotBeNullOrEmpty();
        tasks.Should().NotBeNull();
    }

    [Fact]
    public async Task MultipleChats_MaintainSeparateTaskManagers()
    {
        // Arrange
        var chatId1 = "test-chat-6";
        var chatId2 = "test-chat-7";

        // Act
        var taskManager1 = await _service.GetTaskManagerAsync(chatId1);
        var taskManager2 = await _service.GetTaskManagerAsync(chatId2);

        // Assert
        taskManager1.Should().NotBeNull();
        taskManager2.Should().NotBeNull();
        taskManager1.Should().NotBeSameAs(taskManager2, "different chats should have different TaskManager instances");
    }
}