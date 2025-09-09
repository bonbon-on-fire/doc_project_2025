using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class ImprovedTaskManagerServiceTests : IDisposable
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

    public void Dispose()
    {
        _service?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetTaskManagerAsyncWhenNoExistingTasksCreatesNewTaskManager()
    {
        // Arrange
        var chatId = "test-chat-1";
        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        _ = taskManager.Should().NotBeNull();
        _ = taskManager.Should().BeOfType<TaskManager>();
        var markdown = taskManager.GetMarkdown();
        _ = markdown.Should().Contain("No tasks");
    }

    [Fact]
    public async Task SaveTaskManagerStateAsyncSavesTasksToStorage()
    {
        // Arrange
        var chatId = "test-chat-2";
        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager and add some tasks
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        _ = taskManager.AddTask("Test Task 1");
        _ = taskManager.AddTask("Test Task 2");

        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = new TaskManager(),
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow,
        };

        _ = _taskStorageMock
            .Setup(x =>
                x.SaveTasksAsync(chatId, It.IsAny<TaskManager>(), 0, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(savedTaskState);

        // Act
        await _service.SaveTaskManagerStateAsync(chatId);

        // Assert
        _taskStorageMock.Verify(
            x =>
                x.SaveTasksAsync(chatId, It.IsAny<TaskManager>(), 0, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetTaskManagerAsyncWithExistingTasksRestoresTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-3";

        // Create a TaskManager with existing tasks
        var existingTaskManager = new TaskManager();
        _ = existingTaskManager.AddTask("Restored Task 1");
        _ = existingTaskManager.AddTask("Restored Task 2");
        var tasks = existingTaskManager.GetTasks();
        if (tasks.Count > 1)
        {
            _ = existingTaskManager.AddTask("Subtask 1", parentId: tasks[1].Id);
            // Use the new AddNote method instead of ManageNotes
            _ = existingTaskManager.AddNote(1, 2, "Note for task 2");
            _ = existingTaskManager.UpdateTask(tasks[0].Id, "completed");
        }

        var existingTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = existingTaskManager,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow,
        };

        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTaskState);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        _ = taskManager.Should().NotBeNull();
        var markdown = taskManager.GetMarkdown();

        // The restored TaskManager should have the tasks
        _ = markdown.Should().Contain("Restored Task 1");
        _ = markdown.Should().Contain("Restored Task 2");
        _ = markdown.Should().Contain("Subtask 1");

        // Verify task completion status
        var restoredTasks = taskManager.GetTasks();
        _ = restoredTasks.Should().HaveCountGreaterThan(0);
        _ = restoredTasks[0].Status.Should().Be(TaskManager.TaskStatus.Completed);
    }

    [Fact]
    public async Task ClearTaskManagerAsyncRemovesFromCacheAndStorage()
    {
        // Arrange
        var chatId = "test-chat-4";
        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager to cache it
        _ = await _service.GetTaskManagerAsync(chatId);

        // Act
        await _service.ClearTaskManagerAsync(chatId);

        // Assert
        _taskStorageMock.Verify(
            x => x.DeleteTasksAsync(chatId, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetTaskStateAsyncReturnsCorrectTaskState()
    {
        // Arrange
        var chatId = "test-chat-5";
        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        var taskManager = await _service.GetTaskManagerAsync(chatId);
        _ = taskManager.AddTask("Task for JSON");

        // Act
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        _ = taskState.Should().NotBeNull();
        var (markdown, tasks) = taskState.Value;
        _ = markdown.Should().Contain("Task for JSON");
        _ = tasks.Should().NotBeNull();
        _ = tasks.Should().HaveCount(1);
        _ = tasks[0].Title.Should().Be("Task for JSON");
    }

    [Fact]
    public async Task TaskManagersAreIsolatedPerChat()
    {
        // Arrange
        var chatId1 = "test-chat-6";
        var chatId2 = "test-chat-7";

        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager1 = await _service.GetTaskManagerAsync(chatId1);
        var taskManager2 = await _service.GetTaskManagerAsync(chatId2);

        _ = taskManager1.AddTask("Task for Chat 1");
        _ = taskManager2.AddTask("Task for Chat 2");

        // Assert
        var markdown1 = taskManager1.GetMarkdown();
        var markdown2 = taskManager2.GetMarkdown();

        _ = markdown1.Should().Contain("Task for Chat 1");
        _ = markdown1.Should().NotContain("Task for Chat 2");

        _ = markdown2.Should().Contain("Task for Chat 2");
        _ = markdown2.Should().NotContain("Task for Chat 1");
    }

    [Fact]
    public async Task ParseTasksFromMarkdownHandlesInProgressTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-inprogress";
        _ = _taskStorageMock
            .Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Create a task manager and add tasks with different statuses
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        _ = taskManager.AddTask("Not Started Task");
        _ = taskManager.AddTask("In Progress Task");
        _ = taskManager.AddTask("Completed Task");

        // Update task statuses to test all status symbols
        _ = taskManager.UpdateTask("2", "in progress"); // Should become [-]
        _ = taskManager.UpdateTask("3", "completed"); // Should become [x]

        // Simulate save and reload cycle to test parsing
        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = taskManager, // Use the actual TaskManager
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow,
        };

        _ = _taskStorageMock
            .Setup(x =>
                x.SaveTasksAsync(chatId, It.IsAny<TaskManager>(), 0, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(savedTaskState);

        // Act - Save and then get the task state
        await _service.SaveTaskManagerStateAsync(chatId);
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        _ = taskState.Should().NotBeNull();
        var (markdown, tasks) = taskState.Value;

        // Check markdown contains all tasks
        _ = markdown.Should().Contain("Not Started Task");
        _ = markdown.Should().Contain("In Progress Task");
        _ = markdown.Should().Contain("Completed Task");

        // Most importantly, verify that [-] symbol is present (our fix working)
        _ = markdown.Should().Contain("[-]", "InProgress symbol should be present in markdown");

        // Check tasks array contains all tasks with correct statuses
        var tasksArray = tasks;
        _ = tasksArray.Should().HaveCount(3);

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

        _ = inProgressTaskFound
            .Should()
            .BeTrue(
                $"At least one InProgress task should be correctly parsed and included in tasks array. Found statuses: {statusInfo}"
            );

        // Check task count matches - tasks is now an IList, not JsonElement
        _ = tasksArray.Count.Should().Be(3, "All three tasks should be counted");
    }
}
