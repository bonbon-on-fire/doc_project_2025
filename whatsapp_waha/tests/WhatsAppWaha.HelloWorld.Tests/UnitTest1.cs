using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Exceptions;
using WhatsAppWaha.Core.Interfaces;
using WhatsAppWaha.Core.Models;
using WhatsAppWaha.HelloWorld.Services;

namespace WhatsAppWaha.HelloWorld.Tests;

public class HelloWorldServiceTests
{
    private readonly Mock<IWahaService> _wahaServiceMock;
    private readonly Mock<INtfyService> _ntfyServiceMock;
    private readonly Mock<ILogger<HelloWorldService>> _loggerMock;
    private readonly Mock<IOptions<AppSettings>> _appSettingsMock;
    private readonly AppSettings _appSettings;
    private readonly HelloWorldService _service;

    public HelloWorldServiceTests()
    {
        _wahaServiceMock = new Mock<IWahaService>();
        _ntfyServiceMock = new Mock<INtfyService>();
        _loggerMock = new Mock<ILogger<HelloWorldService>>();
        _appSettingsMock = new Mock<IOptions<AppSettings>>();

        _appSettings = new AppSettings
        {
            Name = "Test App",
            Version = "1.0.0",
            Environment = "Test"
        };

        _appSettingsMock.Setup(x => x.Value).Returns(_appSettings);

        _service = new HelloWorldService(
            _wahaServiceMock.Object,
            _ntfyServiceMock.Object,
            _loggerMock.Object,
            _appSettingsMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var service = new HelloWorldService(
            _wahaServiceMock.Object,
            _ntfyServiceMock.Object,
            _loggerMock.Object,
            _appSettingsMock.Object);

        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullWahaService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HelloWorldService(null!, _ntfyServiceMock.Object, _loggerMock.Object, _appSettingsMock.Object));
    }

    [Fact]
    public void Constructor_NullNtfyService_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HelloWorldService(_wahaServiceMock.Object, null!, _loggerMock.Object, _appSettingsMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HelloWorldService(_wahaServiceMock.Object, _ntfyServiceMock.Object, null!, _appSettingsMock.Object));
    }

    [Fact]
    public void Constructor_NullAppSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HelloWorldService(_wahaServiceMock.Object, _ntfyServiceMock.Object, _loggerMock.Object, null!));
    }

    #endregion

    #region ValidateArguments Tests

    [Fact]
    public void ValidateArguments_EmptyArgs_ShouldReturnFailure()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Phone number is required.");
        result.HelpText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateArguments_NullArgs_ShouldReturnFailure()
    {
        // Act
        var result = _service.ValidateArguments(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Phone number is required.");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("/?")]
    public void ValidateArguments_HelpFlags_ShouldReturnHelp(string helpFlag)
    {
        // Arrange
        var args = new[] { helpFlag };

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsHelp.Should().BeTrue();
        result.HelpText.Should().NotBeNullOrEmpty();
        result.HelpText.Should().Contain("Hello World WhatsApp Message Sender");
    }

    [Fact]
    public void ValidateArguments_EmptyPhoneNumber_ShouldReturnFailure()
    {
        // Arrange
        var args = new[] { "" };

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Phone number cannot be empty.");
    }

    [Fact]
    public void ValidateArguments_InvalidPhoneNumber_ShouldReturnFailure()
    {
        // Arrange
        var args = new[] { "invalid-phone" };
        _wahaServiceMock.Setup(x => x.ValidatePhoneNumber("invalid-phone")).Returns(false);

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid phone number format");
        result.ErrorMessage.Should().Contain("invalid-phone");
    }

    [Fact]
    public void ValidateArguments_ValidPhoneNumber_ShouldReturnSuccess()
    {
        // Arrange
        var args = new[] { "+1234567890" };
        _wahaServiceMock.Setup(x => x.ValidatePhoneNumber("+1234567890")).Returns(true);

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.PhoneNumber.Should().Be("+1234567890");
        result.ConfigOverrides.Should().NotBeNull();
    }

    [Fact]
    public void ValidateArguments_WithConfigOverrides_ShouldParseCorrectly()
    {
        // Arrange
        var args = new[] { "+1234567890", "--waha-url", "https://test.com", "--waha-session", "test-session" };
        _wahaServiceMock.Setup(x => x.ValidatePhoneNumber("+1234567890")).Returns(true);

        // Act
        var result = _service.ValidateArguments(args);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.PhoneNumber.Should().Be("+1234567890");
        result.ConfigOverrides.Should().ContainKey("Waha:BaseUrl");
        result.ConfigOverrides!["Waha:BaseUrl"].Should().Be("https://test.com");
        result.ConfigOverrides.Should().ContainKey("Waha:Session");
        result.ConfigOverrides["Waha:Session"].Should().Be("test-session");
    }

    #endregion

    #region SendHelloWorldMessageAsync Tests

    [Fact]
    public async Task SendHelloWorldMessageAsync_SessionInvalid_ShouldReturnSessionInvalidExitCode()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var exitCode = await _service.SendHelloWorldMessageAsync(phoneNumber);

        // Assert
        exitCode.Should().Be(ExitCodes.WahaSessionInvalid);
        _wahaServiceMock.Verify(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _ntfyServiceMock.Verify(x => x.SendErrorNotificationFireAndForget(
            "Hello World Failed",
            "WAHA session validation failed. Please check WAHA configuration."), Times.Once);
    }

    [Fact]
    public async Task SendHelloWorldMessageAsync_MessageSentSuccessfully_ShouldReturnSuccess()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _wahaServiceMock.Setup(x => x.SendTextMessageAsync(phoneNumber, It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WahaMessageResult { Id = "msg123", Status = "sent" });

        // Act
        var exitCode = await _service.SendHelloWorldMessageAsync(phoneNumber);

        // Assert
        exitCode.Should().Be(ExitCodes.Success);
        _wahaServiceMock.Verify(x => x.SendTextMessageAsync(phoneNumber, It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        _ntfyServiceMock.Verify(x => x.SendSuccessNotificationFireAndForget(
            "Hello World Sent",
            $"Message successfully sent to {phoneNumber}"), Times.Once);
    }

    [Fact]
    public async Task SendHelloWorldMessageAsync_MessageSendFailed_ShouldReturnFailureExitCode()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _wahaServiceMock.Setup(x => x.SendTextMessageAsync(phoneNumber, It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WahaMessageResult { Error = "send_failed", Message = "Network error" });

        // Act
        var exitCode = await _service.SendHelloWorldMessageAsync(phoneNumber);

        // Assert
        exitCode.Should().Be(ExitCodes.MessageSendFailed);
        _ntfyServiceMock.Verify(x => x.SendErrorNotificationFireAndForget(
            "Hello World Failed",
            $"Failed to send message to {phoneNumber}: send_failed"), Times.Once);
    }

    [Fact]
    public async Task SendHelloWorldMessageAsync_OperationCancelled_ShouldReturnCancelledExitCode()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        var cancellationToken = new CancellationToken(true); // Already cancelled
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var exitCode = await _service.SendHelloWorldMessageAsync(phoneNumber, cancellationToken: cancellationToken);

        // Assert
        exitCode.Should().Be(ExitCodes.OperationCancelled);
    }

    [Fact]
    public async Task SendHelloWorldMessageAsync_UnexpectedException_ShouldReturnUnexpectedErrorExitCode()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        // Act
        var exitCode = await _service.SendHelloWorldMessageAsync(phoneNumber);

        // Assert
        exitCode.Should().Be(ExitCodes.UnexpectedError);
        _ntfyServiceMock.Verify(x => x.SendErrorNotificationFireAndForget(
            "Hello World Error",
            $"Unexpected error sending to {phoneNumber}: Test exception"), Times.Once);
    }

    [Fact]
    public async Task SendHelloWorldMessageAsync_ShouldIncludeAppInfoInMessage()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        
        string capturedMessage = string.Empty;
        _wahaServiceMock.Setup(x => x.SendTextMessageAsync(phoneNumber, It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((phone, message, token) => capturedMessage = message)
            .ReturnsAsync(new WahaMessageResult { Id = "msg123", Status = "sent" });

        // Act
        await _service.SendHelloWorldMessageAsync(phoneNumber);

        // Assert
        capturedMessage.Should().Contain(_appSettings.Name);
        capturedMessage.Should().Contain(_appSettings.Version);
        capturedMessage.Should().Contain(_appSettings.Environment);
        capturedMessage.Should().Contain("Hello World");
        capturedMessage.Should().Contain("WAHA");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task EndToEndHappyPath_ShouldCompleteSuccessfully()
    {
        // Arrange
        var args = new[] { "+1234567890" };
        _wahaServiceMock.Setup(x => x.ValidatePhoneNumber("+1234567890")).Returns(true);
        _wahaServiceMock.Setup(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _wahaServiceMock.Setup(x => x.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WahaMessageResult { Id = "msg123", Status = "sent" });

        // Act
        var validationResult = _service.ValidateArguments(args);
        var exitCode = await _service.SendHelloWorldMessageAsync(validationResult.PhoneNumber!);

        // Assert
        validationResult.IsSuccess.Should().BeTrue();
        exitCode.Should().Be(ExitCodes.Success);
        
        _wahaServiceMock.Verify(x => x.ValidatePhoneNumber("+1234567890"), Times.Once);
        _wahaServiceMock.Verify(x => x.ValidateSessionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _wahaServiceMock.Verify(x => x.SendTextMessageAsync("+1234567890", It.IsAny<string>(), It.IsAny<SendTextOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        _ntfyServiceMock.Verify(x => x.SendSuccessNotificationFireAndForget(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion
}

public class CommandLineValidationResultTests
{
    [Fact]
    public void CreateSuccess_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        const string phoneNumber = "+1234567890";
        var configOverrides = new Dictionary<string, string> { ["key"] = "value" };

        // Act
        var result = CommandLineValidationResult.CreateSuccess(phoneNumber, configOverrides);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsHelp.Should().BeFalse();
        result.PhoneNumber.Should().Be(phoneNumber);
        result.ConfigOverrides.Should().BeSameAs(configOverrides);
        result.ErrorMessage.Should().BeNull();
        result.HelpText.Should().BeNull();
    }

    [Fact]
    public void CreateFailure_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        const string errorMessage = "Test error";
        const string helpText = "Test help";

        // Act
        var result = CommandLineValidationResult.CreateFailure(errorMessage, helpText);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsHelp.Should().BeFalse();
        result.ErrorMessage.Should().Be(errorMessage);
        result.HelpText.Should().Be(helpText);
        result.PhoneNumber.Should().BeNull();
        result.ConfigOverrides.Should().BeNull();
    }

    [Fact]
    public void CreateHelp_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        const string helpText = "Test help";

        // Act
        var result = CommandLineValidationResult.CreateHelp(helpText);

        // Assert
        result.IsHelp.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.HelpText.Should().Be(helpText);
        result.ErrorMessage.Should().BeNull();
        result.PhoneNumber.Should().BeNull();
        result.ConfigOverrides.Should().BeNull();
    }
}

public class ExitCodesTests
{
    [Fact]
    public void ExitCodes_ShouldHaveCorrectValues()
    {
        // Assert
        ExitCodes.Success.Should().Be(0);
        ExitCodes.InvalidArguments.Should().Be(1);
        ExitCodes.WahaSessionInvalid.Should().Be(2);
        ExitCodes.MessageSendFailed.Should().Be(3);
        ExitCodes.OperationCancelled.Should().Be(4);
        ExitCodes.UnexpectedError.Should().Be(99);
    }
}
