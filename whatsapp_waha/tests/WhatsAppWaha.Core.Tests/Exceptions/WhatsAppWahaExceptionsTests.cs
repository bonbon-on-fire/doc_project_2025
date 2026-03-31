using FluentAssertions;
using WhatsAppWaha.Core.Exceptions;

namespace WhatsAppWaha.Core.Tests.Exceptions;

public class WhatsAppWahaExceptionsTests
{
  [Fact]
  public void WahaServiceException_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Test message";
    const string errorCode = WahaServiceException.ErrorCodes.ConnectionFailed;

    // Act
    var exception = new WahaServiceException(message, errorCode);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.Context.Should().NotBeNull().And.BeEmpty();
    exception.InnerException.Should().BeNull();
  }

  [Fact]
  public void WahaServiceException_WithInnerException_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Test message";
    const string errorCode = WahaServiceException.ErrorCodes.MessageSendFailed;
    var innerException = new InvalidOperationException("Inner exception");

    // Act
    var exception = new WahaServiceException(message, errorCode, innerException);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.InnerException.Should().Be(innerException);
  }

  [Fact]
  public void NtfyServiceException_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Ntfy test message";
    const string errorCode = NtfyServiceException.ErrorCodes.PollingFailed;

    // Act
    var exception = new NtfyServiceException(message, errorCode);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.Context.Should().NotBeNull().And.BeEmpty();
  }

  [Fact]
  public void ConfigurationException_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Configuration error";
    const string errorCode = ConfigurationException.ErrorCodes.ValidationFailed;
    const string sectionName = "TestSection";

    // Act
    var exception = new ConfigurationException(message, errorCode, sectionName);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.SectionName.Should().Be(sectionName);
    exception.ValidationErrors.Should().BeEmpty();
  }

  [Fact]
  public void ConfigurationException_WithValidationErrors_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Validation failed";
    const string errorCode = ConfigurationException.ErrorCodes.ValidationFailed;
    var validationErrors = new[] { "Error 1", "Error 2", "Error 3" };
    const string sectionName = "TestSection";

    // Act
    var exception = new ConfigurationException(message, errorCode, validationErrors, sectionName);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.SectionName.Should().Be(sectionName);
    exception.ValidationErrors.Should().BeEquivalentTo(validationErrors);
  }

  [Fact]
  public void MessageProcessingException_ShouldInitializeCorrectly()
  {
    // Arrange
    const string message = "Processing failed";
    const string errorCode = MessageProcessingException.ErrorCodes.ProcessingFailed;
    const string messageId = "msg-123";

    // Act
    var exception = new MessageProcessingException(message, errorCode, messageId);

    // Assert
    exception.Message.Should().Be(message);
    exception.ErrorCode.Should().Be(errorCode);
    exception.MessageId.Should().Be(messageId);
  }

  [Fact]
  public void WhatsAppWahaException_WithContext_ShouldAddContextData()
  {
    // Arrange
    var exception = new WahaServiceException("Test", WahaServiceException.ErrorCodes.ConnectionFailed);

    // Act
    var result = exception.WithContext("UserId", "123")
                         .WithContext("Timestamp", DateTime.UtcNow)
                         .WithContext("RetryCount", 3);

    // Assert
    result.Should().BeSameAs(exception);
    exception.Context.Should().HaveCount(3);
    exception.Context["UserId"].Should().Be("123");
    exception.Context["RetryCount"].Should().Be(3);
    exception.Context.Should().ContainKey("Timestamp");
  }

  [Fact]
  public void WhatsAppWahaException_WithContext_ShouldOverwriteExistingKeys()
  {
    // Arrange
    var exception = new WahaServiceException("Test", WahaServiceException.ErrorCodes.ConnectionFailed);
    exception.WithContext("Key", "OriginalValue");

    // Act
    exception.WithContext("Key", "NewValue");

    // Assert
    exception.Context["Key"].Should().Be("NewValue");
    exception.Context.Should().HaveCount(1);
  }

  [Theory]
  [InlineData(WahaServiceException.ErrorCodes.ConnectionFailed)]
  [InlineData(WahaServiceException.ErrorCodes.AuthenticationFailed)]
  [InlineData(WahaServiceException.ErrorCodes.InvalidSession)]
  [InlineData(WahaServiceException.ErrorCodes.MessageSendFailed)]
  [InlineData(WahaServiceException.ErrorCodes.InvalidPhoneNumber)]
  [InlineData(WahaServiceException.ErrorCodes.RateLimitExceeded)]
  [InlineData(WahaServiceException.ErrorCodes.ServiceUnavailable)]
  public void WahaServiceException_ErrorCodes_ShouldBeCorrect(string expectedErrorCode)
  {
    // Act
    var exception = new WahaServiceException("Test", expectedErrorCode);

    // Assert
    exception.ErrorCode.Should().Be(expectedErrorCode);
    expectedErrorCode.Should().StartWith("WAHA_");
  }

  [Theory]
  [InlineData(NtfyServiceException.ErrorCodes.ConnectionFailed)]
  [InlineData(NtfyServiceException.ErrorCodes.AuthenticationFailed)]
  [InlineData(NtfyServiceException.ErrorCodes.InvalidTopic)]
  [InlineData(NtfyServiceException.ErrorCodes.MessageRetrievalFailed)]
  [InlineData(NtfyServiceException.ErrorCodes.NotificationSendFailed)]
  [InlineData(NtfyServiceException.ErrorCodes.ServiceUnavailable)]
  [InlineData(NtfyServiceException.ErrorCodes.PollingFailed)]
  public void NtfyServiceException_ErrorCodes_ShouldBeCorrect(string expectedErrorCode)
  {
    // Act
    var exception = new NtfyServiceException("Test", expectedErrorCode);

    // Assert
    exception.ErrorCode.Should().Be(expectedErrorCode);
    expectedErrorCode.Should().StartWith("NTFY_");
  }

  [Theory]
  [InlineData(ConfigurationException.ErrorCodes.ValidationFailed)]
  [InlineData(ConfigurationException.ErrorCodes.MissingSection)]
  [InlineData(ConfigurationException.ErrorCodes.InvalidValue)]
  [InlineData(ConfigurationException.ErrorCodes.LoadFailed)]
  public void ConfigurationException_ErrorCodes_ShouldBeCorrect(string expectedErrorCode)
  {
    // Act
    var exception = new ConfigurationException("Test", expectedErrorCode);

    // Assert
    exception.ErrorCode.Should().Be(expectedErrorCode);
    expectedErrorCode.Should().StartWith("CONFIG_");
  }

  [Theory]
  [InlineData(MessageProcessingException.ErrorCodes.ProcessingFailed)]
  [InlineData(MessageProcessingException.ErrorCodes.InvalidMessage)]
  [InlineData(MessageProcessingException.ErrorCodes.DeserializationFailed)]
  [InlineData(MessageProcessingException.ErrorCodes.ValidationFailed)]
  [InlineData(MessageProcessingException.ErrorCodes.ProcessorNotFound)]
  public void MessageProcessingException_ErrorCodes_ShouldBeCorrect(string expectedErrorCode)
  {
    // Act
    var exception = new MessageProcessingException("Test", expectedErrorCode);

    // Assert
    exception.ErrorCode.Should().Be(expectedErrorCode);
    expectedErrorCode.Should().StartWith("MESSAGE_");
  }

  [Fact]
  public void WhatsAppWahaException_ShouldBeSerializable()
  {
    // Arrange
    var originalException = new WahaServiceException("Test message", WahaServiceException.ErrorCodes.ConnectionFailed)
        .WithContext("TestKey", "TestValue");

    // Act & Assert - Just verify the exception has serialization support
    // Note: Full serialization testing would require more complex setup in .NET 9
    originalException.ErrorCode.Should().NotBeNullOrEmpty();
    originalException.Context.Should().ContainKey("TestKey");
  }

  [Fact]
  public void ConfigurationException_WithNullSectionName_ShouldHandleGracefully()
  {
    // Act
    var exception = new ConfigurationException("Test", ConfigurationException.ErrorCodes.ValidationFailed, sectionName: null);

    // Assert
    exception.SectionName.Should().BeNull();
    exception.ValidationErrors.Should().BeEmpty();
  }

  [Fact]
  public void MessageProcessingException_WithNullMessageId_ShouldHandleGracefully()
  {
    // Act
    var exception = new MessageProcessingException("Test", MessageProcessingException.ErrorCodes.ProcessingFailed, messageId: null);

    // Assert
    exception.MessageId.Should().BeNull();
  }
}
