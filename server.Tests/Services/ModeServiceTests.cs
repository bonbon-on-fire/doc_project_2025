using System.Text.Json;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class ModeServiceTests : IDisposable
{
    private readonly Mock<IModeStorage> _modeStorageMock;
    private readonly Mock<ILogger<ModeService>> _loggerMock;
    private readonly Mock<IHostEnvironment> _hostEnvironmentMock;
    private readonly ModeService _service;
    private readonly string _tempDir;
    private static readonly string[] expected = new[] { "tool1", "tool2" };
    private static readonly string[] expectedArray = new[] { "tool3", "tool4" };
    private static readonly string[] unexpected = new[] { "tool3", "tool4" };
    private static readonly string[] stringArray = new[] { "tool1", "tool2" };
    private static readonly string[] stringArray0 = new[] { "*" };

    public ModeServiceTests()
    {
        _modeStorageMock = new Mock<IModeStorage>();
        _loggerMock = new Mock<ILogger<ModeService>>();
        _hostEnvironmentMock = new Mock<IHostEnvironment>();

        // Create a temporary directory structure that mimics the solution
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var serverDir = Path.Combine(_tempDir, "server");
        _ = Directory.CreateDirectory(serverDir);
        _ = Directory.CreateDirectory(Path.Combine(_tempDir, "agents"));

        // Set ContentRootPath to server directory so parent is solution root
        _ = _hostEnvironmentMock.Setup(x => x.ContentRootPath).Returns(serverDir);

        _service = new ModeService(
            _modeStorageMock.Object,
            _loggerMock.Object,
            _hostEnvironmentMock.Object
        );

        // Create a test system mode file
        CreateTestSystemMode();
    }

    private void CreateTestSystemMode()
    {
        CreateAgentCard(
            _tempDir,
            "test-system",
            "Test System Mode",
            "test",
            "You are a test assistant",
            stringArray
        );
    }

    private static void CreateAgentCard(
        string basePath,
        string agentId,
        string name,
        string category,
        string prompt,
        string[] tools,
        string? defaultModel = null
    )
    {
        var toolsList = tools.Length > 0 ? string.Join(", ", tools.Select(t => $"\"{t}\"")) : "";

        var modelLine = !string.IsNullOrEmpty(defaultModel)
            ? $"\nmodel_hints: [\"{defaultModel}\"]"
            : "";

        var agentCardContent =
            $@"---
agent: ""{agentId}""
name: ""{name}""
version: ""1.0.0""
category: ""{category}""{modelLine}
capabilities:
  tools: [{toolsList}]
---

# ROLE
{prompt}
";

        var agentsPath = Path.Combine(basePath, "agents");
        if (!Directory.Exists(agentsPath))
        {
            Directory.CreateDirectory(agentsPath);
        }

        File.WriteAllText(Path.Combine(agentsPath, $"{agentId}.agent.md"), agentCardContent);
    }

    [Fact]
    public async Task GetAllModesAsync_ReturnsSystemAndCustomModes()
    {
        // Arrange
        var userId = "test-user-1";
        var customModes = new List<ModeRecord>
        {
            new()
            {
                Id = "custom-1",
                UserId = userId,
                Name = "Custom Mode",
                Description = "A custom mode",
                Prompt = "Custom prompt",
                Tools = "[\"tool3\", \"tool4\"]",
                DefaultModel = "gpt-4",
                Category = "custom",
                CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
                UpdatedAtUtc = DateTime.UtcNow,
            },
        };

        _ = _modeStorageMock
            .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customModes));

        // Act
        var (Success, Error, Modes) = await _service.GetAllModesAsync(userId);

        // Assert
        _ = Success.Should().BeTrue();
        _ = Modes.Should().HaveCount(2); // 1 system + 1 custom

        var systemMode = Modes.Should().ContainSingle(m => m.IsSystem).Subject;
        _ = systemMode.Id.Should().Be("test-system");
        _ = systemMode.Name.Should().Be("Test System Mode");
        _ = systemMode.Tools.Should().Contain(expected);

        var customMode = Modes.Should().ContainSingle(m => !m.IsSystem).Subject;
        _ = customMode.Id.Should().Be("custom-1");
        _ = customMode.Name.Should().Be("Custom Mode");
        _ = customMode.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetModeByIdAsync_ReturnsSystemMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";

        // Act
        var (Success, Error, Mode) = await _service.GetModeByIdAsync(modeId, userId);

        // Assert
        _ = Success.Should().BeTrue();
        _ = Mode.Should().NotBeNull();
        _ = Mode!.Id.Should().Be("test-system");
        _ = Mode.IsSystem.Should().BeTrue();
        _ = Mode.Name.Should().Be("Test System Mode");
    }

    [Fact]
    public async Task GetModeByIdAsync_ReturnsCustomMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "custom-1";
        var customMode = new ModeRecord
        {
            Id = modeId,
            UserId = userId,
            Name = "Custom Mode",
            Description = "A custom mode",
            Prompt = "Custom prompt",
            Tools = "[\"tool1\", \"tool2\"]",
            DefaultModel = null,
            Category = "custom",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _ = _modeStorageMock
            .Setup(x => x.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customMode));

        // Act
        var (Success, Error, Mode) = await _service.GetModeByIdAsync(modeId, userId);

        // Assert
        _ = Success.Should().BeTrue();
        _ = Mode.Should().NotBeNull();
        _ = Mode!.Id.Should().Be(modeId);
        _ = Mode.IsSystem.Should().BeFalse();
        _ = Mode.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task CreateCustomModeAsync_CreatesMode()
    {
        // Arrange
        var userId = "test-user-1";
        var createRequest = new CreateModeRequest
        {
            Name = "New Custom Mode",
            Description = "A new custom mode",
            Prompt = "Custom prompt",
            Tools = new[] { "tool1", "tool2" },
            DefaultModel = "gpt-4",
            Category = "custom",
        };

        var createdMode = new ModeRecord
        {
            Id = "new-mode-id",
            UserId = userId,
            Name = createRequest.Name,
            Description = createRequest.Description,
            Prompt = createRequest.Prompt,
            Tools = "[\"tool1\", \"tool2\"]",
            DefaultModel = createRequest.DefaultModel,
            Category = createRequest.Category,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _ = _modeStorageMock
            .Setup(x => x.CreateModeAsync(It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, createdMode));

        // Act
        var (Success, Error, Mode) = await _service.CreateCustomModeAsync(createRequest, userId);

        // Assert
        _ = Success.Should().BeTrue();
        _ = Mode.Should().NotBeNull();
        _ = Mode!.Name.Should().Be(createRequest.Name);
        _ = Mode.IsSystem.Should().BeFalse();
        _ = Mode.UserId.Should().Be(userId);

        _modeStorageMock.Verify(
            x =>
                x.CreateModeAsync(
                    It.Is<ModeRecord>(m => m.UserId == userId && m.Name == createRequest.Name),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task UpdateCustomModeAsync_UpdatesMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "custom-mode-1";
        var updateRequest = new UpdateModeRequest
        {
            Name = "Updated Mode Name",
            Description = "Updated description",
            Prompt = "Updated prompt",
            Tools = new[] { "tool3", "tool4" },
            DefaultModel = "gpt-4",
            Category = "custom",
        };

        var updatedMode = new ModeRecord
        {
            Id = modeId,
            UserId = userId,
            Name = updateRequest.Name,
            Description = updateRequest.Description,
            Prompt = updateRequest.Prompt,
            Tools = "[\"tool3\", \"tool4\"]",
            DefaultModel = updateRequest.DefaultModel,
            Category = updateRequest.Category,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _ = _modeStorageMock
            .Setup(x =>
                x.UpdateModeAsync(
                    modeId,
                    userId,
                    It.IsAny<ModeRecord>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((true, null, updatedMode));

        // Act
        var (Success, Error, Mode) = await _service.UpdateCustomModeAsync(
            modeId,
            updateRequest,
            userId
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = Mode.Should().NotBeNull();
        _ = Mode!.Name.Should().Be(updateRequest.Name);
        _ = Mode.Tools.Should().Contain(expectedArray);

        _modeStorageMock.Verify(
            x =>
                x.UpdateModeAsync(
                    modeId,
                    userId,
                    It.IsAny<ModeRecord>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task DeleteCustomModeAsync_DeletesMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "custom-mode-1";

        _ = _modeStorageMock
            .Setup(x => x.DeleteModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        // Act
        var (Success, Error) = await _service.DeleteCustomModeAsync(modeId, userId);

        // Assert
        _ = Success.Should().BeTrue();

        _modeStorageMock.Verify(
            x => x.DeleteModeAsync(modeId, userId, It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task DeleteCustomModeAsync_CannotDeleteSystemMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system"; // This is a system mode

        // Act
        var (Success, Error) = await _service.DeleteCustomModeAsync(modeId, userId);

        // Assert
        _ = Success.Should().BeFalse();
        _ = Error.Should().Be("Cannot delete system mode");

        _modeStorageMock.Verify(
            x =>
                x.DeleteModeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task FilterToolsByModeAsync_FiltersCorrectly()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";
        var availableTools = new[] { "tool1", "tool2", "tool3", "tool4" };

        // Act (system mode has tool1, tool2)
        var (Success, Error, FilteredTools) = await _service.FilterToolsByModeAsync(
            modeId,
            userId,
            availableTools
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = FilteredTools.Should().Contain(expected);
        _ = FilteredTools.Should().NotContain(unexpected);
    }

    [Fact]
    public async Task FilterToolsByModeAsync_AllowsAllToolsWithWildcard()
    {
        // Arrange
        var userId = "test-user-1";
        var availableTools = new[] { "tool1", "tool2", "tool3", "tool4" };

        // Create system mode with wildcard
        CreateAgentCard(
            _tempDir,
            "wildcard-mode",
            "Wildcard Mode",
            "test",
            "You have all tools",
            stringArray0
        );

        // Act
        var (Success, Error, FilteredTools) = await _service.FilterToolsByModeAsync(
            "wildcard-mode",
            userId,
            availableTools
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = FilteredTools.Should().Contain(availableTools);
    }

    [Fact]
    public async Task GetModeSystemPromptAsync_ReturnsPrompt()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";

        // Act
        var (Success, Error, SystemPrompt) = await _service.GetModeSystemPromptAsync(
            modeId,
            userId
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = SystemPrompt.Should().Be("You are a test assistant");
    }

    [Fact]
    public async Task GetModeDefaultModelAsync_ReturnsModel()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "custom-1";
        var customMode = new ModeRecord
        {
            Id = modeId,
            UserId = userId,
            Name = "Custom Mode",
            Description = "A custom mode",
            Prompt = "Custom prompt",
            Tools = "[\"tool1\"]",
            DefaultModel = "gpt-4",
            Category = "custom",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _ = _modeStorageMock
            .Setup(x => x.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customMode));

        // Act
        var (Success, Error, DefaultModel) = await _service.GetModeDefaultModelAsync(
            modeId,
            userId
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = DefaultModel.Should().Be("gpt-4");
    }

    #region Caching Behavior Tests

    [Fact]
    public async Task GetAllModesAsync_CachesSystemModes()
    {
        // Arrange
        var userId = "test-user-cache-1";

        // Setup mock to return empty user modes list
        _ = _modeStorageMock
            .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // First call
        _ = await _service.GetAllModesAsync(userId);

        // Modify the system mode file
        var systemModeFile = Path.Combine(_tempDir, "agents", "test-system.agent.md");
        var originalContent = File.ReadAllText(systemModeFile);

        // Write modified agent card
        CreateAgentCard(
            _tempDir,
            "test-system",
            "Modified System Mode",
            "test",
            "You are a modified test assistant",
            stringArray
        );

        // Act - Second call (should use cached version)
        var (Success, Error, Modes) = await _service.GetAllModesAsync(userId);

        // Assert - Name should still be the original, not modified
        if (!Success)
        {
            Console.WriteLine($"Error: {Error}");
        }
        _ = Success.Should().BeTrue();
        var systemMode = Modes.First(m => m.Id == "test-system");
        _ = systemMode.Name.Should().Be("Test System Mode"); // Original name, not "Modified System Mode"

        // Cleanup - restore original file
        File.WriteAllText(systemModeFile, originalContent);
    }

    [Fact]
    public async Task GetAllModesAsync_HandlesEmptyModesDirectory()
    {
        // Arrange
        var userId = "test-user-empty-dir";

        // Create a service with an empty modes directory
        var emptyDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _ = Directory.CreateDirectory(emptyDir);
        _ = Directory.CreateDirectory(Path.Combine(emptyDir, "modes")); // Empty modes directory

        var hostEnvMock = new Mock<IHostEnvironment>();
        _ = hostEnvMock.Setup(x => x.ContentRootPath).Returns(emptyDir);

        var service = new ModeService(
            _modeStorageMock.Object,
            _loggerMock.Object,
            hostEnvMock.Object
        );

        _ = _modeStorageMock
            .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var (Success, Error, Modes) = await service.GetAllModesAsync(userId);

        // Assert
        _ = Success.Should().BeTrue();
        _ = Modes.Should().BeEmpty(); // No system modes, no custom modes

        // Cleanup
        Directory.Delete(emptyDir, true);
    }

    #endregion

    #region File System Error Tests

    [Fact]
    public async Task GetAllModesAsync_HandlesMalformedSystemModeJson()
    {
        // Arrange
        var userId = "test-user-malformed";

        // Create a service with a malformed Agent Card file
        var malformedDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var serverDir = Path.Combine(malformedDir, "server");
        _ = Directory.CreateDirectory(serverDir);
        _ = Directory.CreateDirectory(Path.Combine(malformedDir, "agents"));

        // Write malformed Agent Card YAML
        File.WriteAllText(
            Path.Combine(malformedDir, "agents", "malformed.agent.md"),
            @"---
invalid yaml: [ missing closing bracket
---

# Role
Test"
        );

        var hostEnvMock = new Mock<IHostEnvironment>();
        _ = hostEnvMock.Setup(x => x.ContentRootPath).Returns(serverDir);

        var service = new ModeService(
            _modeStorageMock.Object,
            _loggerMock.Object,
            hostEnvMock.Object
        );

        _ = _modeStorageMock
            .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var (Success, Error, Modes) = await service.GetAllModesAsync(userId);

        // Assert
        _ = Success.Should().BeTrue(); // Should handle error gracefully
        _ = Modes.Should().BeEmpty(); // Malformed mode should be skipped

        // Verify warning was logged for failed agent card
        _loggerMock.Verify(
            x =>
                x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (o, t) => o.ToString()!.Contains("Failed to load agent card")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.AtLeastOnce
        );

        // Cleanup
        Directory.Delete(malformedDir, true);
    }

    [Fact]
    public async Task GetAllModesAsync_HandlesIncompleteModeJson()
    {
        // Arrange
        var userId = "test-user-incomplete";

        // Create a service with an incomplete Agent Card (missing required fields)
        var incompleteDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var serverDir = Path.Combine(incompleteDir, "server");
        _ = Directory.CreateDirectory(serverDir);
        _ = Directory.CreateDirectory(Path.Combine(incompleteDir, "agents"));

        // Write Agent Card missing required agent field
        var incompleteAgentCard =
            @"---
name: ""Incomplete Mode""
category: ""test""
---

# Role
Incomplete";
        File.WriteAllText(
            Path.Combine(incompleteDir, "agents", "incomplete.agent.md"),
            incompleteAgentCard
        );

        var hostEnvMock = new Mock<IHostEnvironment>();
        _ = hostEnvMock.Setup(x => x.ContentRootPath).Returns(serverDir);

        var service = new ModeService(
            _modeStorageMock.Object,
            _loggerMock.Object,
            hostEnvMock.Object
        );

        _ = _modeStorageMock
            .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var (Success, Error, Modes) = await service.GetAllModesAsync(userId);

        // Assert
        _ = Success.Should().BeTrue();
        // The mode may still load with null values for missing fields, depending on implementation
        // What matters is that it doesn't crash

        // Cleanup
        Directory.Delete(incompleteDir, true);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task GetAllModesAsync_HandlesConcurrentAccess()
    {
        // Arrange
        var userIds = Enumerable.Range(1, 10).Select(i => $"concurrent-user-{i}").ToList();
        var customModes = new List<ModeRecord>
        {
            new()
            {
                Id = "custom-concurrent",
                UserId = "concurrent-user-1",
                Name = "Concurrent Mode",
                Description = "Test concurrent access",
                Prompt = "Concurrent prompt",
                Tools = "[\"tool1\"]",
                DefaultModel = null,
                Category = "custom",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            },
        };

        foreach (var userId in userIds)
        {
            _ = _modeStorageMock
                .Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    (
                        true,
                        null,
                        userId == "concurrent-user-1" ? customModes : new List<ModeRecord>()
                    )
                );
        }

        // Act - Concurrent calls
        var tasks = userIds.Select(userId => _service.GetAllModesAsync(userId)).ToList();
        var results = await Task.WhenAll(tasks);

        // Assert
        _ = results.Should().HaveCount(10);
        _ = results.Should().OnlyContain(r => r.Success);

        // User 1 should have 2 modes (1 system + 1 custom)
        var (Success, Error, Modes) = results[0];
        _ = Modes.Should().HaveCount(2);

        // Other users should have 1 mode (system only)
        for (int i = 1; i < results.Length; i++)
        {
            _ = results[i].Modes.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CreateAndUpdateMode_HandlesConcurrentOperations()
    {
        // Arrange
        var userId = "test-user-concurrent-2";
        var modeId = "mode-concurrent";

        var createRequest = new CreateModeRequest
        {
            Name = "Concurrent Mode",
            Description = "Test concurrent operations",
            Prompt = "Concurrent prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        var updateRequest = new UpdateModeRequest
        {
            Name = "Updated Concurrent Mode",
            Description = "Updated description",
            Prompt = "Updated prompt",
            Tools = new[] { "tool2" },
            DefaultModel = "gpt-4",
            Category = "custom",
        };

        var createdMode = new ModeRecord
        {
            Id = modeId,
            UserId = userId,
            Name = createRequest.Name,
            Description = createRequest.Description,
            Prompt = createRequest.Prompt,
            Tools = "[\"tool1\"]",
            DefaultModel = null,
            Category = createRequest.Category,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var updatedMode = new ModeRecord
        {
            Id = modeId,
            UserId = userId,
            Name = updateRequest.Name,
            Description = updateRequest.Description,
            Prompt = updateRequest.Prompt,
            Tools = "[\"tool2\"]",
            DefaultModel = updateRequest.DefaultModel,
            Category = updateRequest.Category,
            CreatedAtUtc = createdMode.CreatedAtUtc,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _ = _modeStorageMock
            .Setup(x => x.CreateModeAsync(It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, createdMode));

        _ = _modeStorageMock
            .Setup(x =>
                x.UpdateModeAsync(
                    modeId,
                    userId,
                    It.IsAny<ModeRecord>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((true, null, updatedMode));

        // Act - Concurrent create and update
        var createTask = _service.CreateCustomModeAsync(createRequest, userId);
        var updateTask = Task.Delay(10)
            .ContinueWith(_ => _service.UpdateCustomModeAsync(modeId, updateRequest, userId))
            .Unwrap();

        var results = await Task.WhenAll(createTask, updateTask);

        // Assert
        _ = results[0].Success.Should().BeTrue();
        _ = results[1].Success.Should().BeTrue();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task UpdateCustomModeAsync_CannotUpdateSystemMode()
    {
        // Arrange
        var userId = "test-user-edge-1";
        var systemModeId = "test-system";
        var updateRequest = new UpdateModeRequest
        {
            Name = "Hacked System Mode",
            Description = "Should not work",
            Prompt = "Should not update",
            Tools = new[] { "malicious-tool" },
            DefaultModel = null,
            Category = "hacked",
        };

        // Act
        var (Success, Error, Mode) = await _service.UpdateCustomModeAsync(
            systemModeId,
            updateRequest,
            userId
        );

        // Assert
        _ = Success.Should().BeFalse();
        _ = Error.Should().Be("Cannot update system mode");

        // Verify storage was not called
        _modeStorageMock.Verify(
            x =>
                x.UpdateModeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<ModeRecord>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task FilterToolsByModeAsync_WithNonExistentMode_ReturnsAllTools()
    {
        // Arrange
        var userId = "test-user-edge-2";
        var nonExistentModeId = "non-existent-mode";
        var availableTools = new[] { "tool1", "tool2", "tool3" };

        _ = _modeStorageMock
            .Setup(x =>
                x.GetModeByIdAsync(nonExistentModeId, userId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((false, "NotFound", null));

        // Act
        var (Success, Error, FilteredTools) = await _service.FilterToolsByModeAsync(
            nonExistentModeId,
            userId,
            availableTools
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = FilteredTools.Should().BeEquivalentTo(availableTools); // Returns all tools when mode not found
    }

    [Fact]
    public async Task FilterToolsByModeAsync_WithEmptyToolsList_ReturnsNoTools()
    {
        // Arrange
        var userId = "test-user-edge-3";
        var modeId = "test-system";
        var emptyTools = Array.Empty<string>();

        // Act
        var (Success, Error, FilteredTools) = await _service.FilterToolsByModeAsync(
            modeId,
            userId,
            emptyTools
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = FilteredTools.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModeSystemPromptAsync_WithNonExistentMode_ReturnsNull()
    {
        // Arrange
        var userId = "test-user-edge-4";
        var nonExistentModeId = "non-existent-mode";

        _ = _modeStorageMock
            .Setup(x =>
                x.GetModeByIdAsync(nonExistentModeId, userId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((false, "NotFound", null));

        // Act
        var (Success, Error, SystemPrompt) = await _service.GetModeSystemPromptAsync(
            nonExistentModeId,
            userId
        );

        // Assert
        _ = Success.Should().BeTrue();
        _ = SystemPrompt.Should().BeNull();
    }

    #endregion

    public void Dispose()
    {
        // Clean up temporary directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
