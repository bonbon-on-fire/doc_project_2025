using AIChat.Server.Models;
using AIChat.Server.Services.StateManagement.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement.Validation;

/// <summary>
/// Unit tests for ChatValidator to demonstrate validation framework functionality.
/// </summary>
public class ChatValidatorTests
{
    private readonly ChatValidator _validator;
    private readonly ILogger<ChatValidator> _logger;

    public ChatValidatorTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ChatValidator>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        _validator = new ChatValidator(_logger, serviceProvider);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithValidChat_ShouldReturnSuccess()
    {
        // Arrange
        var validChat = new Chat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Title = "Valid Chat Title",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _validator.ValidateCreateAsync(validChat);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        // Should have one warning about referential integrity check not implemented
        Assert.Single(result.Warnings);
        Assert.Contains("Referential integrity check", result.Warnings[0].Message);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithInvalidId_ShouldReturnError()
    {
        // Arrange
        var invalidChat = new Chat
        {
            Id = "invalid-guid-format",
            UserId = Guid.NewGuid().ToString(),
            Title = "Valid Title",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _validator.ValidateCreateAsync(invalidChat);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(ValidationErrorCode.InvalidFormat, result.Errors[0].ErrorCode);
        Assert.Contains("GUID format", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithUnsafeTitle_ShouldReturnError()
    {
        // Arrange
        var unsafeChat = new Chat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Title = "Unsafe <script>alert('xss')</script> Title",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _validator.ValidateCreateAsync(unsafeChat);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(ValidationErrorCode.BusinessRuleViolation, result.Errors[0].ErrorCode);
        Assert.Contains("unsafe content", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithFutureCreatedDate_ShouldReturnError()
    {
        // Arrange
        var futureTime = DateTime.UtcNow.AddHours(1);
        var futureChat = new Chat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Title = "Future Chat",
            CreatedAt = futureTime, // Future date
            UpdatedAt = futureTime.AddMinutes(1) // Also in future but after created
        };

        // Act
        var result = await _validator.ValidateCreateAsync(futureChat);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == ValidationErrorCode.OutOfRange &&
                                          e.ErrorMessage.Contains("cannot be in the future"));
    }

    [Fact]
    public async Task ValidateUpdateAsync_WithUserIdChange_ShouldReturnError()
    {
        // Arrange
        var originalUserId = Guid.NewGuid().ToString();
        var newUserId = Guid.NewGuid().ToString();

        var existingChat = new Chat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = originalUserId,
            Title = "Original Title",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        var updatedChat = new Chat
        {
            Id = existingChat.Id,
            UserId = newUserId, // Changed user ID
            Title = "Updated Title",
            CreatedAt = existingChat.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _validator.ValidateUpdateAsync(existingChat.Id, updatedChat, existingChat);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == ValidationErrorCode.BusinessRuleViolation &&
                                          e.ErrorMessage.Contains("User ID cannot be changed"));
    }

    [Fact]
    public async Task ValidateDeleteAsync_WithManyMessages_ShouldReturnError()
    {
        // Arrange
        var chatWithManyMessages = new Chat
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            Title = "Chat with Many Messages",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };

        // Add many messages (simulating 15 messages)
        for (int i = 0; i < 15; i++)
        {
            chatWithManyMessages.Messages.Add(new Message
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Message {i}",
                ChatId = chatWithManyMessages.Id
            });
        }

        // Act
        var result = await _validator.ValidateDeleteAsync(chatWithManyMessages.Id, chatWithManyMessages);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(ValidationErrorCode.BusinessRuleViolation, result.Errors[0].ErrorCode);
        Assert.Contains("Archive the chat instead", result.Errors[0].ErrorMessage);
    }

    [Fact]
    public void GetValidationRules_ShouldReturnExpectedRules()
    {
        // Act
        var createRules = _validator.GetValidationRules(ValidationOperation.Create);
        var updateRules = _validator.GetValidationRules(ValidationOperation.Update);
        var deleteRules = _validator.GetValidationRules(ValidationOperation.Delete);

        // Assert
        Assert.NotEmpty(createRules);
        Assert.NotEmpty(updateRules);
        Assert.NotEmpty(deleteRules);

        // Check specific rules exist
        Assert.Contains(createRules, r => r.RuleId == "ChatId_GuidFormat");
        Assert.Contains(createRules, r => r.RuleId == "UserId_ReferentialIntegrity");
        Assert.Contains(updateRules, r => r.RuleId == "UserId_Immutable");
        Assert.Contains(deleteRules, r => r.RuleId == "Messages_DeleteRestriction");
    }

    [Fact]
    public void GetMetrics_ShouldReturnEmptyMetrics()
    {
        // Act
        var metrics = _validator.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.TotalValidations);
        Assert.Equal(0, metrics.SuccessfulValidations);
        Assert.Equal(0, metrics.FailedValidations);
    }

    [Fact]
    public void Name_ShouldReturnChatValidator()
    {
        // Act & Assert
        Assert.Equal("ChatValidator", _validator.Name);
    }
}
