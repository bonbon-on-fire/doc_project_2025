using System.ComponentModel.DataAnnotations;
using AIChat.Server.Controllers;
using AIChat.Server.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Controllers;

/// <summary>
/// Unit tests for ModeController API endpoints.
/// Tests all CRUD operations, validation, authorization, and error handling.
/// </summary>
public class ModeControllerTests
{
    private readonly Mock<IModeService> _modeServiceMock;
    private readonly Mock<ILogger<ModeController>> _loggerMock;
    private readonly ModeController _controller;
    private static readonly string[] item = new[] { "tool1", "tool2" };
    private static readonly string[] itemArray = new[] { "tool3" };

    public ModeControllerTests()
    {
        _modeServiceMock = new Mock<IModeService>();
        _loggerMock = new Mock<ILogger<ModeController>>();
        _controller = new ModeController(_modeServiceMock.Object, _loggerMock.Object);
    }

    #region GetModes Tests

    [Fact]
    public async Task GetModes_WithValidUserId_ReturnsOkWithModes()
    {
        // Arrange
        var userId = "test-user-1";
        var modes = new List<ModeDto>
        {
            new()
            {
                Id = "mode-1",
                Name = "Mode 1",
                Description = "Test mode 1",
                Prompt = "Prompt 1",
                Tools = item,
                DefaultModel = null,
                Category = "task",
                IsSystem = true,
                UserId = null,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
            },
            new()
            {
                Id = "mode-2",
                Name = "Mode 2",
                Description = "Test mode 2",
                Prompt = "Prompt 2",
                Tools = itemArray,
                DefaultModel = "gpt-4",
                Category = "custom",
                IsSystem = false,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
        };

        _ = _modeServiceMock
            .Setup(s => s.GetAllModesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, modes));

        // Act
        var result = await _controller.GetModes(userId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ModesResponse>().Subject;
        _ = response.Modes.Should().HaveCount(2);
        _ = response.Count.Should().Be(2);
        _ = response.Modes[0].Name.Should().Be("Mode 1");
        _ = response.Modes[1].Name.Should().Be("Mode 2");
    }

    [Fact]
    public async Task GetModes_WithMissingUserId_ReturnsBadRequest()
    {
        // Arrange
        string userId = null!;

        // Act
        var result = await _controller.GetModes(userId);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequestResult.Value!.ToString();
        _ = error.Should().Contain("UserId is required");
    }

    [Fact]
    public async Task GetModes_WithEmptyUserId_ReturnsBadRequest()
    {
        // Arrange
        var userId = "   "; // Whitespace

        // Act
        var result = await _controller.GetModes(userId);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequestResult.Value!.ToString();
        _ = error.Should().Contain("UserId is required");
    }

    [Fact]
    public async Task GetModes_WhenServiceFails_ReturnsServerError()
    {
        // Arrange
        var userId = "test-user-2";
        _ = _modeServiceMock
            .Setup(s => s.GetAllModesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Database connection failed", new List<ModeDto>()));

        // Act
        var result = await _controller.GetModes(userId);

        // Assert
        var statusResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        _ = statusResult.StatusCode.Should().Be(500);
        _ = statusResult.Value!.ToString().Should().Contain("Database connection failed");
    }

    #endregion

    #region GetMode Tests

    [Fact]
    public async Task GetMode_WithValidIdAndUserId_ReturnsOkWithMode()
    {
        // Arrange
        var modeId = "test-mode-1";
        var userId = "test-user-3";
        var mode = new ModeDto
        {
            Id = modeId,
            Name = "Test Mode",
            Description = "A test mode",
            Prompt = "Test prompt",
            Tools = new[] { "tool1" },
            DefaultModel = "gpt-4",
            Category = "custom",
            IsSystem = false,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _ = _modeServiceMock
            .Setup(s => s.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, mode));

        // Act
        var result = await _controller.GetMode(modeId, userId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedMode = okResult.Value.Should().BeOfType<ModeDto>().Subject;
        _ = returnedMode.Id.Should().Be(modeId);
        _ = returnedMode.Name.Should().Be("Test Mode");
    }

    [Fact]
    public async Task GetMode_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var modeId = "non-existent";
        var userId = "test-user-4";

        _ = _modeServiceMock
            .Setup(s => s.GetModeByIdAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "NotFound", (ModeDto?)null));

        // Act
        var result = await _controller.GetMode(modeId, userId);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = notFoundResult.Value!.ToString().Should().Contain("Mode not found");
    }

    [Fact]
    public async Task GetMode_WithMissingUserId_ReturnsBadRequest()
    {
        // Arrange
        var modeId = "test-mode-2";
        string userId = null!;

        // Act
        var result = await _controller.GetMode(modeId, userId);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult.Value!.ToString().Should().Contain("UserId is required");
    }

    #endregion

    #region CreateMode Tests

    [Fact]
    public async Task CreateMode_WithValidRequest_ReturnsCreatedWithMode()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-5",
            Name = "New Mode",
            Description = "A new custom mode",
            Prompt = "You are a helpful assistant",
            Tools = new[] { "tool1", "tool2" },
            DefaultModel = "gpt-4",
            Category = "custom",
        };

        var createdMode = new ModeDto
        {
            Id = "created-mode-id",
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools.ToArray(),
            DefaultModel = request.DefaultModel,
            Category = request.Category,
            IsSystem = false,
            UserId = request.UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _ = _modeServiceMock
            .Setup(s =>
                s.CreateCustomModeAsync(
                    It.IsAny<CreateModeRequest>(),
                    request.UserId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((true, null, createdMode));

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        _ = createdResult.ActionName.Should().Be(nameof(ModeController.GetMode));
        _ = createdResult.RouteValues!["id"].Should().Be("created-mode-id");
        _ = createdResult.RouteValues["userId"].Should().Be("test-user-5");

        var returnedMode = createdResult.Value.Should().BeOfType<ModeDto>().Subject;
        _ = returnedMode.Name.Should().Be("New Mode");
    }

    [Fact]
    public async Task CreateMode_WithMissingUserId_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = null!,
            Name = "New Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult.Value!.ToString().Should().Contain("UserId is required");
    }

    [Fact]
    public async Task CreateMode_WithHtmlSpecialCharactersInName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-6",
            Name = "Mode<script>alert('xss')</script>",
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult
            .Value!.ToString()
            .Should()
            .Contain("Mode name cannot contain HTML special characters");
    }

    [Fact]
    public async Task CreateMode_WithScriptTagInDescription_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-7",
            Name = "Valid Name",
            Description = "Description with <script>malicious code</script>",
            Prompt = "Normal prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult
            .Value!.ToString()
            .Should()
            .Contain("Mode content cannot contain script tags");
    }

    [Fact]
    public async Task CreateMode_WithEmptyToolName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-8",
            Name = "Valid Name",
            Description = "Valid description",
            Prompt = "Valid prompt",
            Tools = new[] { "tool1", "", "tool3" }, // Empty tool name
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult
            .Value!.ToString()
            .Should()
            .Contain("Tool names cannot be empty or whitespace");
    }

    [Fact]
    public async Task CreateMode_WithTooLongToolName_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-9",
            Name = "Valid Name",
            Description = "Valid description",
            Prompt = "Valid prompt",
            Tools = new[] { new string('a', 101) }, // 101 characters
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult
            .Value!.ToString()
            .Should()
            .Contain("Tool names must be 100 characters or less");
    }

    [Fact]
    public async Task CreateMode_WithDuplicateMode_ReturnsConflict()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-10",
            Name = "Duplicate Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        _ = _modeServiceMock
            .Setup(s =>
                s.CreateCustomModeAsync(
                    It.IsAny<CreateModeRequest>(),
                    request.UserId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((false, "Mode with this name already exists", (ModeDto?)null));

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var conflictResult = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        _ = conflictResult.Value!.ToString().Should().Contain("already exists");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMode_WithInvalidName_ReturnsValidationError(string invalidName)
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user",
            Name = invalidName!,
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        // Manually trigger validation
        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            request,
            validationContext,
            validationResults,
            true
        );

        // Assert
        _ = isValid.Should().BeFalse();
        _ = validationResults.Should().Contain(r => r.MemberNames.Contains("Name"));
    }

    #endregion

    #region UpdateMode Tests

    [Fact]
    public async Task UpdateMode_WithValidRequest_ReturnsOkWithUpdatedMode()
    {
        // Arrange
        var modeId = "mode-to-update";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-11",
            Name = "Updated Mode",
            Description = "Updated description",
            Prompt = "Updated prompt",
            Tools = new[] { "tool3", "tool4" },
            DefaultModel = "gpt-4",
            Category = "role",
        };

        var updatedMode = new ModeDto
        {
            Id = modeId,
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = request.Tools.ToArray(),
            DefaultModel = request.DefaultModel,
            Category = request.Category,
            IsSystem = false,
            UserId = request.UserId,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
        };

        _ = _modeServiceMock
            .Setup(s =>
                s.UpdateCustomModeAsync(
                    modeId,
                    It.IsAny<UpdateModeRequest>(),
                    request.UserId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((true, null, updatedMode));

        // Act
        var result = await _controller.UpdateMode(modeId, request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedMode = okResult.Value.Should().BeOfType<ModeDto>().Subject;
        _ = returnedMode.Name.Should().Be("Updated Mode");
        _ = returnedMode.DefaultModel.Should().Be("gpt-4");
    }

    [Fact]
    public async Task UpdateMode_WithNonExistentMode_ReturnsNotFound()
    {
        // Arrange
        var modeId = "non-existent";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-12",
            Name = "Updated Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        _ = _modeServiceMock
            .Setup(s =>
                s.UpdateCustomModeAsync(
                    modeId,
                    It.IsAny<UpdateModeRequest>(),
                    request.UserId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((false, "NotFound", (ModeDto?)null));

        // Act
        var result = await _controller.UpdateMode(modeId, request);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = notFoundResult.Value!.ToString().Should().Contain("Mode not found or access denied");
    }

    [Fact]
    public async Task UpdateMode_WithValidationError_ReturnsBadRequest()
    {
        // Arrange
        var modeId = "mode-to-update";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-13",
            Name = "Name&<>", // Contains HTML special characters
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        // Act
        var result = await _controller.UpdateMode(modeId, request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult
            .Value!.ToString()
            .Should()
            .Contain("Mode name cannot contain HTML special characters");
    }

    #endregion

    #region DeleteMode Tests

    [Fact]
    public async Task DeleteMode_WithValidRequest_ReturnsNoContent()
    {
        // Arrange
        var modeId = "mode-to-delete";
        var userId = "test-user-14";

        _ = _modeServiceMock
            .Setup(s => s.DeleteCustomModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null));

        // Act
        var result = await _controller.DeleteMode(modeId, userId);

        // Assert
        _ = result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteMode_WithNonExistentMode_ReturnsNotFound()
    {
        // Arrange
        var modeId = "non-existent";
        var userId = "test-user-15";

        _ = _modeServiceMock
            .Setup(s => s.DeleteCustomModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "NotFound"));

        // Act
        var result = await _controller.DeleteMode(modeId, userId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = notFoundResult.Value!.ToString().Should().Contain("Mode not found or access denied");
    }

    [Fact]
    public async Task DeleteMode_WithSystemMode_ReturnsBadRequest()
    {
        // Arrange
        var modeId = "system-mode";
        var userId = "test-user-16";

        _ = _modeServiceMock
            .Setup(s => s.DeleteCustomModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Cannot delete system mode"));

        // Act
        var result = await _controller.DeleteMode(modeId, userId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult.Value!.ToString().Should().Contain("Cannot delete system modes");
    }

    [Fact]
    public async Task DeleteMode_WithMissingUserId_ReturnsBadRequest()
    {
        // Arrange
        var modeId = "mode-to-delete";
        string userId = null!;

        // Act
        var result = await _controller.DeleteMode(modeId, userId);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        _ = badRequestResult.Value!.ToString().Should().Contain("UserId is required");
    }

    [Fact]
    public async Task DeleteMode_WhenServiceFails_ReturnsServerError()
    {
        // Arrange
        var modeId = "mode-to-delete";
        var userId = "test-user-17";

        _ = _modeServiceMock
            .Setup(s => s.DeleteCustomModeAsync(modeId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Database error occurred"));

        // Act
        var result = await _controller.DeleteMode(modeId, userId);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        _ = statusResult.StatusCode.Should().Be(500);
        _ = statusResult.Value!.ToString().Should().Contain("Database error occurred");
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task GetModes_WhenServiceFails_LogsError()
    {
        // Arrange
        var userId = "test-user-18";
        var errorMessage = "Service failure";

        _ = _modeServiceMock
            .Setup(s => s.GetAllModesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, errorMessage, new List<ModeDto>()));

        // Act
        _ = await _controller.GetModes(userId);

        // Assert
        _loggerMock.Verify(
            x =>
                x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error retrieving modes")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task CreateMode_WhenServiceFails_LogsError()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-19",
            Name = "Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = new[] { "tool1" },
            DefaultModel = null,
            Category = "custom",
        };

        _ = _modeServiceMock
            .Setup(s =>
                s.CreateCustomModeAsync(
                    It.IsAny<CreateModeRequest>(),
                    request.UserId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((false, "Creation failed", (ModeDto?)null));

        // Act
        _ = await _controller.CreateMode(request);

        // Assert
        _loggerMock.Verify(
            x =>
                x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error creating mode")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
    }

    #endregion
}
