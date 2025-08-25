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

    public ModeServiceTests()
    {
        _modeStorageMock = new Mock<IModeStorage>();
        _loggerMock = new Mock<ILogger<ModeService>>();
        _hostEnvironmentMock = new Mock<IHostEnvironment>();
        
        // Create a temporary directory for test system modes
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "modes"));
        
        _hostEnvironmentMock.Setup(x => x.ContentRootPath).Returns(_tempDir);
        
        _service = new ModeService(_modeStorageMock.Object, _loggerMock.Object, _hostEnvironmentMock.Object);
        
        // Create a test system mode file
        CreateTestSystemMode();
    }

    private void CreateTestSystemMode()
    {
        var systemMode = new
        {
            id = "test-system",
            name = "Test System Mode",
            description = "A test system mode",
            category = "test",
            prompt = "You are a test assistant",
            tools = new[] { "tool1", "tool2" },
            defaultModel = (string?)null
        };
        
        var json = JsonSerializer.Serialize(systemMode, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        
        File.WriteAllText(Path.Combine(_tempDir, "modes", "test-system.json"), json);
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
                UpdatedAtUtc = DateTime.UtcNow
            }
        };

        _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customModes));

        // Act
        var result = await _service.GetAllModesAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Modes.Should().HaveCount(2); // 1 system + 1 custom
        
        var systemMode = result.Modes.Should().ContainSingle(m => m.IsSystem).Subject;
        systemMode.Id.Should().Be("test-system");
        systemMode.Name.Should().Be("Test System Mode");
        systemMode.Tools.Should().Contain(new[] { "tool1", "tool2" });

        var customMode = result.Modes.Should().ContainSingle(m => !m.IsSystem).Subject;
        customMode.Id.Should().Be("custom-1");
        customMode.Name.Should().Be("Custom Mode");
        customMode.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task GetModeByIdAsync_ReturnsSystemMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";

        // Act
        var result = await _service.GetModeByIdAsync(modeId, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Mode.Should().NotBeNull();
        result.Mode!.Id.Should().Be("test-system");
        result.Mode.IsSystem.Should().BeTrue();
        result.Mode.Name.Should().Be("Test System Mode");
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
            UpdatedAtUtc = DateTime.UtcNow
        };

        _modeStorageMock.Setup(x => x.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customMode));

        // Act
        var result = await _service.GetModeByIdAsync(modeId, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Mode.Should().NotBeNull();
        result.Mode!.Id.Should().Be(modeId);
        result.Mode.IsSystem.Should().BeFalse();
        result.Mode.UserId.Should().Be(userId);
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
            Category = "custom"
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
            UpdatedAtUtc = DateTime.UtcNow
        };

        _modeStorageMock.Setup(x => x.CreateModeAsync(It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, createdMode));

        // Act
        var result = await _service.CreateCustomModeAsync(createRequest, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Mode.Should().NotBeNull();
        result.Mode!.Name.Should().Be(createRequest.Name);
        result.Mode.IsSystem.Should().BeFalse();
        result.Mode.UserId.Should().Be(userId);

        _modeStorageMock.Verify(x => x.CreateModeAsync(
            It.Is<ModeRecord>(m => m.UserId == userId && m.Name == createRequest.Name), 
            It.IsAny<CancellationToken>()), Times.Once);
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
            Category = "custom"
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
            UpdatedAtUtc = DateTime.UtcNow
        };

        _modeStorageMock.Setup(x => x.UpdateModeAsync(modeId, userId, It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, updatedMode));

        // Act
        var result = await _service.UpdateCustomModeAsync(modeId, updateRequest, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Mode.Should().NotBeNull();
        result.Mode!.Name.Should().Be(updateRequest.Name);
        result.Mode.Tools.Should().Contain(new[] { "tool3", "tool4" });

        _modeStorageMock.Verify(x => x.UpdateModeAsync(
            modeId, userId, It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomModeAsync_DeletesMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "custom-mode-1";

        _modeStorageMock.Setup(x => x.DeleteModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        // Act
        var result = await _service.DeleteCustomModeAsync(modeId, userId);

        // Assert
        result.Success.Should().BeTrue();

        _modeStorageMock.Verify(x => x.DeleteModeAsync(modeId, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCustomModeAsync_CannotDeleteSystemMode()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system"; // This is a system mode

        // Act
        var result = await _service.DeleteCustomModeAsync(modeId, userId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Cannot delete system mode");

        _modeStorageMock.Verify(x => x.DeleteModeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FilterToolsByModeAsync_FiltersCorrectly()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";
        var availableTools = new[] { "tool1", "tool2", "tool3", "tool4" };

        // Act (system mode has tool1, tool2)
        var result = await _service.FilterToolsByModeAsync(modeId, userId, availableTools);

        // Assert
        result.Success.Should().BeTrue();
        result.FilteredTools.Should().Contain(new[] { "tool1", "tool2" });
        result.FilteredTools.Should().NotContain(new[] { "tool3", "tool4" });
    }

    [Fact]
    public async Task FilterToolsByModeAsync_AllowsAllToolsWithWildcard()
    {
        // Arrange
        var userId = "test-user-1";
        var availableTools = new[] { "tool1", "tool2", "tool3", "tool4" };
        
        // Create system mode with wildcard
        var wildcardMode = new
        {
            id = "wildcard-mode",
            name = "Wildcard Mode",
            description = "Mode with all tools",
            category = "test",
            prompt = "You have all tools",
            tools = new[] { "*" },
            defaultModel = (string?)null
        };
        
        var json = JsonSerializer.Serialize(wildcardMode, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        
        File.WriteAllText(Path.Combine(_tempDir, "modes", "wildcard-mode.json"), json);

        // Act
        var result = await _service.FilterToolsByModeAsync("wildcard-mode", userId, availableTools);

        // Assert
        result.Success.Should().BeTrue();
        result.FilteredTools.Should().Contain(availableTools);
    }

    [Fact]
    public async Task GetModeSystemPromptAsync_ReturnsPrompt()
    {
        // Arrange
        var userId = "test-user-1";
        var modeId = "test-system";

        // Act
        var result = await _service.GetModeSystemPromptAsync(modeId, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.SystemPrompt.Should().Be("You are a test assistant");
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
            UpdatedAtUtc = DateTime.UtcNow
        };

        _modeStorageMock.Setup(x => x.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, customMode));

        // Act
        var result = await _service.GetModeDefaultModelAsync(modeId, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.DefaultModel.Should().Be("gpt-4");
    }

    #region Caching Behavior Tests

    [Fact]
    public async Task GetAllModesAsync_CachesSystemModes()
    {
        // Arrange
        var userId = "test-user-cache-1";
        
        // Setup mock to return empty user modes list
        _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));
        
        // First call
        await _service.GetAllModesAsync(userId);
        
        // Modify the system mode file
        var systemModeFile = Path.Combine(_tempDir, "modes", "test-system.json");
        var originalContent = File.ReadAllText(systemModeFile);
        var modifiedMode = new
        {
            id = "test-system",
            name = "Modified System Mode", // Changed name
            description = "A modified test system mode",
            category = "test",
            prompt = "You are a modified test assistant",
            tools = new[] { "tool1", "tool2" },
            defaultModel = (string?)null
        };
        
        var json = JsonSerializer.Serialize(modifiedMode, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(systemModeFile, json);

        // Act - Second call (should use cached version)
        var result = await _service.GetAllModesAsync(userId);

        // Assert - Name should still be the original, not modified
        if (!result.Success)
        {
            Console.WriteLine($"Error: {result.Error}");
        }
        result.Success.Should().BeTrue();
        var systemMode = result.Modes.First(m => m.Id == "test-system");
        systemMode.Name.Should().Be("Test System Mode"); // Original name, not "Modified System Mode"
        
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
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(Path.Combine(emptyDir, "modes")); // Empty modes directory
        
        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(x => x.ContentRootPath).Returns(emptyDir);
        
        var service = new ModeService(_modeStorageMock.Object, _loggerMock.Object, hostEnvMock.Object);

        _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var result = await service.GetAllModesAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
        result.Modes.Should().BeEmpty(); // No system modes, no custom modes
        
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
        
        // Create a service with a malformed JSON file
        var malformedDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(malformedDir);
        Directory.CreateDirectory(Path.Combine(malformedDir, "modes"));
        
        // Write malformed JSON
        File.WriteAllText(Path.Combine(malformedDir, "modes", "malformed.json"), "{ invalid json }");
        
        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(x => x.ContentRootPath).Returns(malformedDir);
        
        var service = new ModeService(_modeStorageMock.Object, _loggerMock.Object, hostEnvMock.Object);
        
        _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var result = await service.GetAllModesAsync(userId);

        // Assert
        result.Success.Should().BeTrue(); // Should handle error gracefully
        result.Modes.Should().BeEmpty(); // Malformed mode should be skipped
        
        // Verify error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to parse system mode JSON file")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
        
        // Cleanup
        Directory.Delete(malformedDir, true);
    }

    [Fact]
    public async Task GetAllModesAsync_HandlesIncompleteModeJson()
    {
        // Arrange
        var userId = "test-user-incomplete";
        
        // Create a service with an incomplete mode (missing required fields)
        var incompleteDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(incompleteDir);
        Directory.CreateDirectory(Path.Combine(incompleteDir, "modes"));
        
        // Write JSON missing required fields
        var incompleteMode = new
        {
            id = "incomplete-mode",
            name = "Incomplete Mode"
            // Missing description, prompt, tools, category
        };
        
        var json = JsonSerializer.Serialize(incompleteMode, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        File.WriteAllText(Path.Combine(incompleteDir, "modes", "incomplete.json"), json);
        
        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(x => x.ContentRootPath).Returns(incompleteDir);
        
        var service = new ModeService(_modeStorageMock.Object, _loggerMock.Object, hostEnvMock.Object);
        
        _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, new List<ModeRecord>()));

        // Act
        var result = await service.GetAllModesAsync(userId);

        // Assert
        result.Success.Should().BeTrue();
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
                UpdatedAtUtc = DateTime.UtcNow
            }
        };

        foreach (var userId in userIds)
        {
            _modeStorageMock.Setup(x => x.GetModesByUserAsync(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, null, userId == "concurrent-user-1" ? customModes : new List<ModeRecord>()));
        }

        // Act - Concurrent calls
        var tasks = userIds.Select(userId => _service.GetAllModesAsync(userId)).ToList();
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Should().OnlyContain(r => r.Success);
        
        // User 1 should have 2 modes (1 system + 1 custom)
        var user1Result = results[0];
        user1Result.Modes.Should().HaveCount(2);
        
        // Other users should have 1 mode (system only)
        for (int i = 1; i < results.Length; i++)
        {
            results[i].Modes.Should().HaveCount(1);
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
            Category = "custom"
        };

        var updateRequest = new UpdateModeRequest
        {
            Name = "Updated Concurrent Mode",
            Description = "Updated description",
            Prompt = "Updated prompt",
            Tools = new[] { "tool2" },
            DefaultModel = "gpt-4",
            Category = "custom"
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
            UpdatedAtUtc = DateTime.UtcNow
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
            UpdatedAtUtc = DateTime.UtcNow
        };

        _modeStorageMock.Setup(x => x.CreateModeAsync(It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, createdMode));

        _modeStorageMock.Setup(x => x.UpdateModeAsync(modeId, userId, It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, updatedMode));

        // Act - Concurrent create and update
        var createTask = _service.CreateCustomModeAsync(createRequest, userId);
        var updateTask = Task.Delay(10).ContinueWith(_ => 
            _service.UpdateCustomModeAsync(modeId, updateRequest, userId)).Unwrap();

        var results = await Task.WhenAll(createTask, updateTask);

        // Assert
        results[0].Success.Should().BeTrue();
        results[1].Success.Should().BeTrue();
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
            Category = "hacked"
        };

        // Act
        var result = await _service.UpdateCustomModeAsync(systemModeId, updateRequest, userId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Cannot update system mode");
        
        // Verify storage was not called
        _modeStorageMock.Verify(x => x.UpdateModeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModeRecord>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task FilterToolsByModeAsync_WithNonExistentMode_ReturnsAllTools()
    {
        // Arrange
        var userId = "test-user-edge-2";
        var nonExistentModeId = "non-existent-mode";
        var availableTools = new[] { "tool1", "tool2", "tool3" };

        _modeStorageMock.Setup(x => x.GetModeByIdAsync(nonExistentModeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "NotFound", null));

        // Act
        var result = await _service.FilterToolsByModeAsync(nonExistentModeId, userId, availableTools);

        // Assert
        result.Success.Should().BeTrue();
        result.FilteredTools.Should().BeEquivalentTo(availableTools); // Returns all tools when mode not found
    }

    [Fact]
    public async Task FilterToolsByModeAsync_WithEmptyToolsList_ReturnsNoTools()
    {
        // Arrange
        var userId = "test-user-edge-3";
        var modeId = "test-system";
        var emptyTools = Array.Empty<string>();

        // Act
        var result = await _service.FilterToolsByModeAsync(modeId, userId, emptyTools);

        // Assert
        result.Success.Should().BeTrue();
        result.FilteredTools.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModeSystemPromptAsync_WithNonExistentMode_ReturnsNull()
    {
        // Arrange
        var userId = "test-user-edge-4";
        var nonExistentModeId = "non-existent-mode";

        _modeStorageMock.Setup(x => x.GetModeByIdAsync(nonExistentModeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "NotFound", null));

        // Act
        var result = await _service.GetModeSystemPromptAsync(nonExistentModeId, userId);

        // Assert
        result.Success.Should().BeTrue();
        result.SystemPrompt.Should().BeNull();
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