using System.Text.Json;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class ModeServiceTests
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

    public void Dispose()
    {
        // Clean up temporary directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}