using System.ComponentModel.DataAnnotations;
using AIChat.Orleans.Contracts;
using AIChat.Server.Controllers;
using AIChat.Server.Services;
using AIChat.Server.Services.Routing;
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
    private readonly Mock<IModeRouter> _modeRouterMock;
    private readonly Mock<IModeService> _modeServiceMock;
    private readonly Mock<ILogger<ModeController>> _loggerMock;
    private readonly ModeController _controller;
    private static readonly string[] item = ["tool1", "tool2"];
    private static readonly string[] itemArray = ["tool3"];

    public ModeControllerTests()
    {
        _modeRouterMock = new Mock<IModeRouter>();
        _modeServiceMock = new Mock<IModeService>();
        _loggerMock = new Mock<ILogger<ModeController>>();

        // TODO: Update test mocks to properly test router pattern
        // For now, just create the controller with basic mocks
        _controller = new ModeController(_modeRouterMock.Object, _modeServiceMock.Object, _loggerMock.Object);
    }

    #region GetModes Tests

    [Fact]
    public async Task GetModesWithValidUserIdReturnsOkWithModes()
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

        var expectedResponse = new ModesResponse { Modes = modes, Count = modes.Count };
        _ = _modeRouterMock
            .Setup(r => r.ExecuteUserOperationAsync<ActionResult<ModesResponse>>(
                It.IsAny<string>(),
                It.IsAny<Func<IModeGrain, Task<ActionResult<ModesResponse>>>>(),
                It.IsAny<Func<IModeService, Task<ActionResult<ModesResponse>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OkObjectResult(expectedResponse));

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
    public async Task GetModesWithMissingUserIdReturnsBadRequest()
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
    public async Task GetModesWithEmptyUserIdReturnsBadRequest()
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
    public async Task GetModesWhenServiceFailsReturnsServerError()
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
    public async Task GetModeWithValidIdAndUserIdReturnsOkWithMode()
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
            Tools = ["tool1"],
            DefaultModel = "gpt-4",
            Category = "custom",
            IsSystem = false,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // Set up the router mock to return the expected ActionResult
        _ = _modeRouterMock
            .Setup(r => r.ExecuteModeOperationAsync<ActionResult<ModeDto>>(
                modeId,
                It.IsAny<Func<IModeGrain, Task<ActionResult<ModeDto>>>>(),
                It.IsAny<Func<IModeService, Task<ActionResult<ModeDto>>>>(),
                "GetMode",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OkObjectResult(mode));

        // Act
        var result = await _controller.GetMode(modeId, userId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedMode = okResult.Value.Should().BeOfType<ModeDto>().Subject;
        _ = returnedMode.Id.Should().Be(modeId);
        _ = returnedMode.Name.Should().Be("Test Mode");
    }

    [Fact]
    public async Task GetModeWithNonExistentIdReturnsNotFound()
    {
        // Arrange
        var modeId = "non-existent";
        var userId = "test-user-4";

        // Set up the router mock to return NotFound result
        _ = _modeRouterMock
            .Setup(r => r.ExecuteModeOperationAsync<ActionResult<ModeDto>>(
                modeId,
                It.IsAny<Func<IModeGrain, Task<ActionResult<ModeDto>>>>(),
                It.IsAny<Func<IModeService, Task<ActionResult<ModeDto>>>>(),
                "GetMode",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotFoundObjectResult(new { Error = "Mode not found" }));

        // Act
        var result = await _controller.GetMode(modeId, userId);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = notFoundResult.Value!.ToString().Should().Contain("Mode not found");
    }

    [Fact]
    public async Task GetModeWithMissingUserIdReturnsBadRequest()
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
    public async Task CreateModeWithValidRequestReturnsCreatedWithMode()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-5",
            Name = "New Mode",
            Description = "A new custom mode",
            Prompt = "You are a helpful assistant",
            Tools = ["tool1", "tool2"],
            DefaultModel = "gpt-4",
            Category = "custom",
        };

        var createdMode = new ModeDto
        {
            Id = "created-mode-id",
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = [.. request.Tools],
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
    public async Task CreateModeWithMissingUserIdReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = null!,
            Name = "New Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
    public async Task CreateModeWithHtmlSpecialCharactersInNameReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-6",
            Name = "Mode<script>alert('xss')</script>",
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
    public async Task CreateModeWithScriptTagInDescriptionReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-7",
            Name = "Valid Name",
            Description = "Description with <script>malicious code</script>",
            Prompt = "Normal prompt",
            Tools = ["tool1"],
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
    public async Task CreateModeWithEmptyToolNameReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-8",
            Name = "Valid Name",
            Description = "Valid description",
            Prompt = "Valid prompt",
            Tools = ["tool1", "", "tool3"], // Empty tool name
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
    public async Task CreateModeWithTooLongToolNameReturnsBadRequest()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-9",
            Name = "Valid Name",
            Description = "Valid description",
            Prompt = "Valid prompt",
            Tools = [new string('a', 101)], // 101 characters
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
    public async Task CreateModeWithDuplicateModeReturnsConflict()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-10",
            Name = "Duplicate Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
            .ReturnsAsync((false, "Mode with this name already exists", null));

        // Act
        var result = await _controller.CreateMode(request);

        // Assert
        var conflictResult = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        _ = conflictResult.Value!.ToString().Should().Contain("already exists");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateModeWithInvalidNameReturnsValidationError(string invalidName)
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user",
            Name = invalidName!,
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
    public async Task UpdateModeWithValidRequestReturnsOkWithUpdatedMode()
    {
        // Arrange
        var modeId = "mode-to-update";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-11",
            Name = "Updated Mode",
            Description = "Updated description",
            Prompt = "Updated prompt",
            Tools = ["tool3", "tool4"],
            DefaultModel = "gpt-4",
            Category = "role",
        };

        var updatedMode = new ModeDto
        {
            Id = modeId,
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            Tools = [.. request.Tools],
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
    public async Task UpdateModeWithNonExistentModeReturnsNotFound()
    {
        // Arrange
        var modeId = "non-existent";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-12",
            Name = "Updated Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
            .ReturnsAsync((false, "NotFound", null));

        // Act
        var result = await _controller.UpdateMode(modeId, request);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        _ = notFoundResult.Value!.ToString().Should().Contain("Mode not found or access denied");
    }

    [Fact]
    public async Task UpdateModeWithValidationErrorReturnsBadRequest()
    {
        // Arrange
        var modeId = "mode-to-update";
        var request = new UpdateModeApiRequest
        {
            UserId = "test-user-13",
            Name = "Name&<>", // Contains HTML special characters
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
    public async Task DeleteModeWithValidRequestReturnsNoContent()
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
    public async Task DeleteModeWithNonExistentModeReturnsNotFound()
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
    public async Task DeleteModeWithSystemModeReturnsBadRequest()
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
    public async Task DeleteModeWithMissingUserIdReturnsBadRequest()
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
    public async Task DeleteModeWhenServiceFailsReturnsServerError()
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
    public async Task GetModesWhenServiceFailsLogsError()
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
    public async Task CreateModeWhenServiceFailsLogsError()
    {
        // Arrange
        var request = new CreateModeApiRequest
        {
            UserId = "test-user-19",
            Name = "Mode",
            Description = "Description",
            Prompt = "Prompt",
            Tools = ["tool1"],
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
            .ReturnsAsync((false, "Creation failed", null));

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
