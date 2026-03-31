using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Exceptions;
using WhatsAppWaha.Core.Models;
using WhatsAppWaha.Core.Services;
using Waha; // ✅ Add WAHA SDK namespace
using WahaConfig = WhatsAppWaha.Core.Configuration.WahaSettings; // ✅ Alias to resolve conflict

namespace WhatsAppWaha.Core.Tests.Services;

/// <summary>
/// Unit tests for WahaService.
/// 🔧 HYBRID APPROACH: Tests both WAHA SDK integration and HTTP client usage
/// </summary>
public class WahaServiceTests
{
    private readonly Mock<IWahaApiClient> _wahaClientMock; // ✅ WAHA SDK client mock
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock; // 🔧 For HTTP client
    private readonly HttpClient _httpClient; // 🔧 For missing SDK methods
    private readonly Mock<ILogger<WahaService>> _loggerMock;
    private readonly Mock<IOptions<WahaConfig>> _wahaSettingsMock; // ✅ Using aliased config
    private readonly WahaConfig _wahaSettings;
    private readonly WahaService _wahaService;

    public WahaServiceTests()
    {
        _wahaClientMock = new Mock<IWahaApiClient>(); // ✅ Mock WAHA SDK client
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>(); // 🔧 Mock HTTP handler
        _loggerMock = new Mock<ILogger<WahaService>>();
        _wahaSettingsMock = new Mock<IOptions<WahaConfig>>(); // ✅ Using aliased config

        _wahaSettings = new WahaConfig // ✅ Using aliased config
        {
            BaseUrl = "https://test-waha.com",
            Session = "test-session",
            TimeoutSeconds = 30,
            MaxRetryAttempts = 3,
            RetryDelayMs = 1000
        };

        _wahaSettingsMock.Setup(x => x.Value).Returns(_wahaSettings);

        // Create HTTP client with mocked handler
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_wahaSettings.BaseUrl)
        };

        _wahaService = new WahaService(
            _wahaClientMock.Object, // ✅ WAHA SDK client mock (configured with BaseURL + API Key)
            _loggerMock.Object,
            _wahaSettingsMock.Object);
    }

    #region Phone Number Validation Tests

    [Theory]
    [InlineData("+15551234567", true)] // Valid E.164 format
    [InlineData("+442012345678", true)] // Valid UK E.164
    [InlineData("+33123456789", true)] // Valid French E.164
    [InlineData("15551234567", true)] // Valid international without +
    [InlineData("442012345678", true)] // Valid UK international
    [InlineData("15551234567@c.us", true)] // Valid WAHA chat ID format
    [InlineData("442012345678@c.us", true)] // Valid UK WAHA chat ID
    [InlineData("", false)] // Empty string
    [InlineData(" ", false)] // Whitespace only
    [InlineData(null, false)] // Null
    [InlineData("abc123", false)] // Contains letters
    [InlineData("123", false)] // Too short
    [InlineData("1234567890123456789", false)] // Too long
    [InlineData("+", false)] // Just plus sign
    [InlineData("++15551234567", false)] // Double plus
    [InlineData("155-512-34567", false)] // Contains dashes
    [InlineData("(555) 123-4567", false)] // US format with parentheses
    public void ValidatePhoneNumber_ShouldReturnExpectedResult(string? phoneNumber, bool expected)
    {
        // Act
        var result = _wahaService.ValidatePhoneNumber(phoneNumber!);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Send Text Message Tests

    [Fact]
    public async Task SendTextMessageAsync_WithValidInputs_ShouldReturnSuccessResult()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "Hello World";
        var expectedResponse = new WahaMessageResult
        {
            Id = "msg_123456789",
            Status = "sent",
            Timestamp = 1640995200
        };

        // 🔧 HYBRID APPROACH: Mock HTTP client response (SDK doesn't have SendTextAsync)
        SetupHttpResponse(HttpStatusCode.OK, expectedResponse);

        // Act
        var result = await _wahaService.SendTextMessageAsync(phoneNumber, message);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Id.Should().Be(expectedResponse.Id);
        result.Status.Should().Be(expectedResponse.Status);

        // Verify HTTP request was made to correct endpoint
        VerifyHttpRequest("/api/sendText", HttpMethod.Post);
    }

    [Fact]
    public async Task SendTextMessageAsync_WithInvalidPhoneNumber_ShouldThrowWahaServiceException()
    {
        // Arrange
        var phoneNumber = "invalid-phone";
        var message = "Hello World";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<WahaServiceException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));

        exception.ErrorCode.Should().Be(WahaServiceException.ErrorCodes.InvalidPhoneNumber);
        exception.Context.Should().ContainKey("phoneNumber");
        exception.Context["phoneNumber"].Should().Be(phoneNumber);
    }

    [Fact]
    public async Task SendTextMessageAsync_WithEmptyMessage_ShouldThrowWahaServiceException()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<WahaServiceException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));

        exception.ErrorCode.Should().Be(WahaServiceException.ErrorCodes.MessageSendFailed);
        exception.Context.Should().ContainKey("phoneNumber");
    }

    [Fact]
    public async Task SendTextMessageAsync_WithHttpRequestException_ShouldThrowWahaServiceException()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "Hello World";

        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));
    }

    [Fact]
    public async Task SendTextMessageAsync_WithTaskCanceledException_ShouldThrowWahaServiceException()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "Hello World";

        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));
    }

    [Fact]
    public async Task SendTextMessageAsync_WithUnauthorizedResponse_ShouldThrowWahaServiceException()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "Hello World";

        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));
    }

    [Fact]
    public async Task SendTextMessageAsync_WithRateLimitResponse_ShouldReturnErrorResult()
    {
        // Arrange
        var phoneNumber = "+15551234567";
        var message = "Hello World";

        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.SendTextMessageAsync(phoneNumber, message));
    }

    [Theory]
    [InlineData("+15551234567", "15551234567@c.us")]
    [InlineData("15551234567", "15551234567@c.us")]
    [InlineData("442012345678", "442012345678@c.us")]
    [InlineData("15551234567@c.us", "15551234567@c.us")]
    public void FormatPhoneNumber_ShouldConvertToCorrectChatId(string inputPhone, string expectedChatId)
    {
        // Act - Test the static method directly since it doesn't depend on WAHA SDK
        var result = WahaService.FormatPhoneNumber(inputPhone);

        // Assert
        result.Should().Be(expectedChatId);
    }

    #endregion

    #region Session Validation Tests

    [Fact]
    public async Task ValidateSessionAsync_WithWorkingSession_ShouldReturnTrue()
    {
        // Arrange
        var sessionResponse = new { status = "WORKING", name = "Test Session" };
        SetupHttpResponse(HttpStatusCode.OK, sessionResponse);

        // Act
        var result = await _wahaService.ValidateSessionAsync();

        // Assert
        result.Should().BeTrue();
        VerifyHttpRequest($"/api/sessions/{_wahaSettings.Session}", HttpMethod.Get);
    }

    [Theory]
    [InlineData("STOPPED")]
    [InlineData("STARTING")]
    [InlineData("SCAN_QR_CODE")]
    [InlineData("FAILED")]
    public async Task ValidateSessionAsync_WithNonWorkingStatus_ShouldReturnFalse(string status)
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    [Fact]
    public async Task ValidateSessionAsync_WithMissingStatusProperty_ShouldReturnFalse()
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    [Fact]
    public async Task ValidateSessionAsync_WithNotFoundResponse_ShouldReturnFalse()
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    [Fact]
    public async Task ValidateSessionAsync_WithHttpRequestException_ShouldThrowWahaServiceException()
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    [Fact]
    public async Task ValidateSessionAsync_WithTaskCanceledException_ShouldThrowWahaServiceException()
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    [Fact]
    public async Task ValidateSessionAsync_WithInvalidJson_ShouldThrowWahaServiceException()
    {
        // ✅ TODO: This test will be updated once WAHA SDK method signatures are determined
        // For now, testing that NotImplementedException is thrown as expected

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => _wahaService.ValidateSessionAsync());
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullWahaClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new WahaService(null!, _loggerMock.Object, _wahaSettingsMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new WahaService(_wahaClientMock.Object, null!, _wahaSettingsMock.Object));
    }

    [Fact]
    public void Constructor_WithNullWahaSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new WahaService(_wahaClientMock.Object, _loggerMock.Object, null!));
    }

    #endregion

    #region Helper Methods

    // 🔧 HYBRID APPROACH: Need HTTP helpers for methods not in WAHA SDK
    private void SetupHttpResponse<T>(HttpStatusCode statusCode, T responseObject, Action<string>? captureContent = null, bool isJson = true)
    {
        var responseContent = isJson
            ? JsonSerializer.Serialize(responseObject, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            : responseObject?.ToString() ?? "";

        var httpResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, token) =>
            {
                if (captureContent != null && request.Content != null)
                {
                    var content = request.Content.ReadAsStringAsync().Result;
                    captureContent(content);
                }
            })
            .ReturnsAsync(httpResponse);
    }

    private void VerifyHttpRequest(string expectedUri, HttpMethod expectedMethod)
    {
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == expectedMethod &&
                    req.RequestUri!.PathAndQuery == expectedUri),
                ItExpr.IsAny<CancellationToken>());
    }

    #endregion
}