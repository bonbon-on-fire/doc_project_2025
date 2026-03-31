using System.Text.Json;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using AchieveAi.LmDotnetTools.Misc.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class ImprovedTaskManagerServiceTests
{
    private readonly Mock<ITaskStorage> _taskStorageMock;
    private readonly Mock<ILogger<ImprovedTaskManagerService>> _loggerMock;
    private readonly ImprovedTaskManagerService _service;

    public ImprovedTaskManagerServiceTests()
    {
        _taskStorageMock = new Mock<ITaskStorage>();
        _loggerMock = new Mock<ILogger<ImprovedTaskManagerService>>();
        _service = new ImprovedTaskManagerService(_taskStorageMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetTaskManagerAsync_WhenNoExistingTasks_CreatesNewTaskManager()
    {
        // Arrange
        var chatId = "test-chat-1";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager.Should().NotBeNull();
        taskManager.Should().BeOfType<TaskManager>();
        var markdown = taskManager.GetMarkdown();
        markdown.Should().Contain("No tasks");
    }

    [Fact]
    public async Task SaveTaskManagerStateAsync_SavesTasksToStorage()
    {
        // Arrange
        var chatId = "test-chat-2";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager and add some tasks
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Test Task 1");
        taskManager.AddTask("Test Task 2");

        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = new TaskManager(),
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.SaveTasksAsync(
                chatId,
                It.IsAny<TaskManager>(),
                0,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedTaskState);

        // Act
        await _service.SaveTaskManagerStateAsync(chatId);

        // Assert
        _taskStorageMock.Verify(x => x.SaveTasksAsync(
            chatId,
            It.IsAny<TaskManager>(),
            0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTaskManagerAsync_WithExistingTasks_RestoresTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-3";
        
        // Create a TaskManager with existing tasks
        var existingTaskManager = new TaskManager();
        existingTaskManager.AddTask("Restored Task 1");
        existingTaskManager.AddTask("Restored Task 2");
        var tasks = existingTaskManager.GetTasks();
        if (tasks.Count > 1)
        {
            existingTaskManager.AddTask("Subtask 1", parentId: tasks[1].Id);
            // Use the new AddNote method instead of ManageNotes
            existingTaskManager.AddNote(1, 2, "Note for task 2");
            existingTaskManager.UpdateTask(tasks[0].Id, "completed");
        }

        var existingTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = existingTaskManager,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTaskState);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager.Should().NotBeNull();
        var markdown = taskManager.GetMarkdown();
        
        // The restored TaskManager should have the tasks
        markdown.Should().Contain("Restored Task 1");
        markdown.Should().Contain("Restored Task 2");
        markdown.Should().Contain("Subtask 1");
        
        // Verify task completion status
        var restoredTasks = taskManager.GetTasks();
        restoredTasks.Should().HaveCountGreaterThan(0);
        restoredTasks[0].Status.Should().Be(TaskManager.TaskStatus.Completed);
    }

    [Fact]
    public async Task ClearTaskManagerAsync_RemovesFromCacheAndStorage()
    {
        // Arrange
        var chatId = "test-chat-4";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager to cache it
        await _service.GetTaskManagerAsync(chatId);

        // Act
        await _service.ClearTaskManagerAsync(chatId);

        // Assert
        _taskStorageMock.Verify(x => x.DeleteTasksAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTaskStateAsync_ReturnsCorrectTaskState()
    {
        // Arrange
        var chatId = "test-chat-5";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Task for JSON");

        // Act
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        taskState.Should().NotBeNull();
        var (markdown, tasks) = taskState.Value;
        markdown.Should().Contain("Task for JSON");
        tasks.Should().NotBeNull();
        tasks.Should().HaveCount(1);
        tasks[0].Title.Should().Be("Task for JSON");
    }

    [Fact]
    public async Task TaskManagersAreIsolatedPerChat()
    {
        // Arrange
        var chatId1 = "test-chat-6";
        var chatId2 = "test-chat-7";
        
        _taskStorageMock.Setup(x => x.GetTasksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager1 = await _service.GetTaskManagerAsync(chatId1);
        var taskManager2 = await _service.GetTaskManagerAsync(chatId2);
        
        taskManager1.AddTask("Task for Chat 1");
        taskManager2.AddTask("Task for Chat 2");

        // Assert
        var markdown1 = taskManager1.GetMarkdown();
        var markdown2 = taskManager2.GetMarkdown();
        
        markdown1.Should().Contain("Task for Chat 1");
        markdown1.Should().NotContain("Task for Chat 2");
        
        markdown2.Should().Contain("Task for Chat 2");
        markdown2.Should().NotContain("Task for Chat 1");
    }

    [Fact]
    public async Task ParseTasksFromMarkdown_HandlesInProgressTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-inprogress";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Create a task manager and add tasks with different statuses
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Not Started Task");
        taskManager.AddTask("In Progress Task");
        taskManager.AddTask("Completed Task");
        
        // Update task statuses to test all status symbols
        taskManager.UpdateTask("2", "in progress");  // Should become [-]
        taskManager.UpdateTask("3", "completed");    // Should become [x]

        // Simulate save and reload cycle to test parsing
        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = taskManager, // Use the actual TaskManager
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.SaveTasksAsync(
                chatId,
                It.IsAny<TaskManager>(),
                0,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedTaskState);

        // Act - Save and then get the task state
        await _service.SaveTaskManagerStateAsync(chatId);
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        taskState.Should().NotBeNull();
        var (markdown, tasks) = taskState.Value;
        
        // Check markdown contains all tasks 
        markdown.Should().Contain("Not Started Task");
        markdown.Should().Contain("In Progress Task");
        markdown.Should().Contain("Completed Task");
        
        // Most importantly, verify that [-] symbol is present (our fix working)
        markdown.Should().Contain("[-]", "InProgress symbol should be present in markdown");
        
        // Check tasks array contains all tasks with correct statuses
        var tasksArray = tasks;
        tasksArray.Should().HaveCount(3);
        
        // Find any task with InProgress status in the parsed array (the core fix)
        var inProgressTaskFound = false;
        var taskStatuses = new List<string>();
        
        foreach (var task in tasksArray)
        {
            var status = task.Status;
            var title = task.Title;
            taskStatuses.Add($"{title}: {status}");
            
            if (status == TaskManager.TaskStatus.InProgress)
            {
                inProgressTaskFound = true;
            }
        }
        
        // Debug info for troubleshooting
        var statusInfo = string.Join("; ", taskStatuses);
        
        inProgressTaskFound.Should().BeTrue($"At least one InProgress task should be correctly parsed and included in tasks array. Found statuses: {statusInfo}");
        
        // Check task count matches - tasks is now an IList, not JsonElement
        tasksArray.Count.Should().Be(3, "All three tasks should be counted");
    }
}