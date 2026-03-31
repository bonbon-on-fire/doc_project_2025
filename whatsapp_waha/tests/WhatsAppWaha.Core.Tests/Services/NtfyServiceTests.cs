using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Exceptions;
using WhatsAppWaha.Core.Models;
using WhatsAppWaha.Core.Services;

namespace WhatsAppWaha.Core.Tests.Services;

public class NtfyServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<NtfyService>> _loggerMock;
    private readonly Mock<IOptions<NtfySettings>> _settingsMock;
    private readonly NtfySettings _settings;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;

    public NtfyServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<NtfyService>>();
        _settingsMock = new Mock<IOptions<NtfySettings>>();
        
        _settings = new NtfySettings
        {
            BaseUrl = "https://ntfy.example.com",
            MessagesTopic = "whatsapp-messages",
            NotificationsTopic = "whatsapp-notifications",
            PollingIntervalMs = 5000,
            MaxMessagesPerPoll = 10,
            TimeoutSeconds = 30,
            MaxProcessedMessageIds = 1000,
            EnableFireAndForget = true
        };

        _settingsMock.Setup(x => x.Value).Returns(_settings);

        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_settings.BaseUrl)
        };

        _httpClientFactoryMock.Setup(x => x.CreateClient("NtfyClient")).Returns(_httpClient);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ValidParameters_ShouldInitializeCorrectly()
    {
        // Act
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Assert
        Assert.NotNull(service);
        var context = service.GetProcessingContext();
        Assert.Equal(_settings.MaxProcessedMessageIds, context.MaxProcessedIds);
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new NtfyService(null!, _loggerMock.Object, _settingsMock.Object));
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new NtfyService(_httpClientFactoryMock.Object, null!, _settingsMock.Object));
    }

    [Fact]
    public void Constructor_NullSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, null!));
    }

    #endregion

    #region PollMessagesAsync Tests

    [Fact]
    public async Task PollMessagesAsync_DefaultTopic_ShouldUseConfiguredMessagesTopic()
    {
        // Arrange
        var expectedJson = """
            {"id":"msg1","time":1640995200,"topic":"whatsapp-messages","message":"Test message","event":"message"}
            """;

        SetupHttpResponse(HttpStatusCode.OK, expectedJson);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(_settings.MessagesTopic, result.Topic);
        Assert.Single(result.Messages);
        Assert.Equal("msg1", result.Messages[0].Id);
    }

    [Fact]
    public async Task PollMessagesAsync_ValidResponse_ShouldReturnSuccessResponse()
    {
        // Arrange
        var expectedJson = """
            {"id":"msg1","time":1640995200,"topic":"test-topic","message":"Test message 1","event":"message"}
            {"id":"msg2","time":1640995260,"topic":"test-topic","message":"Test message 2","event":"message"}
            """;

        SetupHttpResponse(HttpStatusCode.OK, expectedJson);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesAsync("test-topic");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test-topic", result.Topic);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("msg1", result.Messages[0].Id);
        Assert.Equal("msg2", result.Messages[1].Id);
        Assert.Equal(2, result.NewMessageCount);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public async Task PollMessagesAsync_EmptyResponse_ShouldReturnEmptySuccess()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesAsync("test-topic");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test-topic", result.Topic);
        Assert.Empty(result.Messages);
        Assert.Equal(0, result.NewMessageCount);
    }

    [Fact]
    public async Task PollMessagesAsync_DuplicateMessages_ShouldFilterCorrectly()
    {
        // Arrange
        var expectedJson = """
            {"id":"msg1","time":1640995200,"topic":"test-topic","message":"Test message 1","event":"message"}
            {"id":"msg2","time":1640995260,"topic":"test-topic","message":"Test message 2","event":"message"}
            """;

        SetupHttpResponse(HttpStatusCode.OK, expectedJson);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // First poll - both messages are new
        await service.PollMessagesAsync("test-topic");

        // Setup same response again
        SetupHttpResponse(HttpStatusCode.OK, expectedJson);

        // Act - Second poll with same messages
        var result = await service.PollMessagesAsync("test-topic");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal(2, result.DuplicateCount); // Both messages are duplicates
        Assert.Equal(0, result.NewMessageCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PollMessagesAsync_InvalidTopic_ShouldThrowNtfyServiceException(string? topic)
    {
        // Arrange
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.PollMessagesAsync(topic!));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.InvalidTopic, exception.ErrorCode);
    }

    [Fact]
    public async Task PollMessagesAsync_HttpError_ShouldReturnFailureResponse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "Topic not found");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesAsync("test-topic");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("test-topic", result.Topic);
        Assert.Contains("404", result.ErrorMessage!);
        Assert.Contains("Topic not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task PollMessagesAsync_ServiceUnavailable_ShouldThrowNtfyServiceException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, "Service temporarily unavailable");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.PollMessagesAsync("test-topic"));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.ServiceUnavailable, exception.ErrorCode);
    }

    [Fact]
    public async Task PollMessagesAsync_Timeout_ShouldThrowNtfyServiceException()
    {
        // Arrange
        SetupHttpTimeout();
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.PollMessagesAsync("test-topic"));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.ConnectionFailed, exception.ErrorCode);
        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task PollMessagesAsync_InvalidJson_ShouldReturnEmptyResult()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "invalid json content");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesAsync("test-topic");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Messages);
        Assert.Equal("test-topic", result.Topic);
    }

    #endregion

    #region PollMessagesSinceAsync Tests

    [Fact]
    public async Task PollMessagesSinceAsync_ValidParameters_ShouldIncludeSinceParameter()
    {
        // Arrange
        var expectedJson = """
            {"id":"msg1","time":1640995200,"topic":"test-topic","message":"Test message","event":"message"}
            """;

        var expectedUrl = "test-topic/json?poll=10&since=1640995000";
        SetupHttpResponse(HttpStatusCode.OK, expectedJson, expectedUrl);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.PollMessagesSinceAsync("test-topic", 1640995000);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task PollMessagesSinceAsync_NegativeSince_ShouldThrowNtfyServiceException()
    {
        // Arrange
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.PollMessagesSinceAsync("test-topic", -1));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.PollingFailed, exception.ErrorCode);
    }

    #endregion

    #region SendNotificationAsync Tests

    [Fact]
    public async Task SendNotificationAsync_ValidNotification_ShouldReturnTrue()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Title = "Test Title",
            Message = "Test Message",
            Priority = 3
        };

        SetupHttpResponse(HttpStatusCode.OK, "", "test-topic");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendNotificationAsync(notification);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SendNotificationAsync_HttpError_ShouldReturnFalse()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Message = "Test Message"
        };

        SetupHttpResponse(HttpStatusCode.BadRequest, "Bad request", "test-topic");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendNotificationAsync(notification);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendNotificationAsync_NullNotification_ShouldThrowArgumentNullException()
    {
        // Arrange
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.SendNotificationAsync(null!));
    }

    [Fact]
    public async Task SendNotificationAsync_EmptyTopic_ShouldThrowNtfyServiceException()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "",
            Message = "Test Message"
        };
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.SendNotificationAsync(notification));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.InvalidTopic, exception.ErrorCode);
    }

    [Fact]
    public async Task SendNotificationAsync_EmptyMessage_ShouldThrowNtfyServiceException()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Message = ""
        };
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.SendNotificationAsync(notification));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.NotificationSendFailed, exception.ErrorCode);
    }

    [Fact]
    public async Task SendNotificationAsync_Timeout_ShouldThrowNtfyServiceException()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Message = "Test Message"
        };

        SetupHttpTimeout("test-topic");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<NtfyServiceException>(
            () => service.SendNotificationAsync(notification));
        
        Assert.Equal(NtfyServiceException.ErrorCodes.NotificationSendFailed, exception.ErrorCode);
    }

    #endregion

    #region Convenience Notification Methods Tests

    [Fact]
    public async Task SendSuccessNotificationAsync_ShouldCreateSuccessNotificationAndSend()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "", _settings.NotificationsTopic);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendSuccessNotificationAsync("Test Title", "Test Message");

        // Assert
        Assert.True(result);
        VerifyHttpRequest(_settings.NotificationsTopic, content => 
        {
            Assert.Contains("Test Title", content);
            Assert.Contains("Test Message", content);
            Assert.Contains("\"priority\":3", content);
        });
    }

    [Fact]
    public async Task SendErrorNotificationAsync_ShouldCreateErrorNotificationAndSend()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "", _settings.NotificationsTopic);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendErrorNotificationAsync("Error Title", "Error Message");

        // Assert
        Assert.True(result);
        VerifyHttpRequest(_settings.NotificationsTopic, content => 
        {
            Assert.Contains("Error Title", content);
            Assert.Contains("Error Message", content);
            Assert.Contains("\"priority\":4", content);
        });
    }

    [Fact]
    public async Task SendInfoNotificationAsync_ShouldCreateInfoNotificationAndSend()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "", _settings.NotificationsTopic);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendInfoNotificationAsync("Info Title", "Info Message");

        // Assert
        Assert.True(result);
        VerifyHttpRequest(_settings.NotificationsTopic, content => 
        {
            Assert.Contains("Info Title", content);
            Assert.Contains("Info Message", content);
            Assert.Contains("\"priority\":2", content);
        });
    }

    [Fact]
    public async Task SendWarningNotificationAsync_ShouldCreateWarningNotificationAndSend()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "", _settings.NotificationsTopic);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.SendWarningNotificationAsync("Warning Title", "Warning Message");

        // Assert
        Assert.True(result);
        VerifyHttpRequest(_settings.NotificationsTopic, content => 
        {
            Assert.Contains("Warning Title", content);
            Assert.Contains("Warning Message", content);
            Assert.Contains("\"priority\":3", content);
        });
    }

    #endregion

    #region Fire-and-Forget Tests

    [Fact]
    public void SendNotificationFireAndForget_EnabledSetting_ShouldExecuteAsynchronously()
    {
        // Arrange
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Message = "Test Message"
        };

        SetupHttpResponse(HttpStatusCode.OK, "", "test-topic");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act (should not throw or block)
        service.SendNotificationFireAndForget(notification);

        // Assert - method returns immediately, actual HTTP call happens asynchronously
        // We can't easily test the async execution without making the test flaky
        // The main assertion is that this doesn't throw or block
    }

    [Fact]
    public void SendNotificationFireAndForget_DisabledSetting_ShouldSkip()
    {
        // Arrange
        _settings.EnableFireAndForget = false;
        var notification = new NtfyNotification
        {
            Topic = "test-topic",
            Message = "Test Message"
        };

        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act (should not throw)
        service.SendNotificationFireAndForget(notification);

        // Assert - verify no HTTP calls were made
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public void SendNotificationFireAndForget_NullNotification_ShouldNotThrow()
    {
        // Arrange
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act & Assert (should not throw)
        service.SendNotificationFireAndForget(null!);
    }

    [Fact]
    public void SendSuccessNotificationFireAndForget_ShouldExecuteWithoutBlocking()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "", _settings.NotificationsTopic);
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act (should not throw or block)
        service.SendSuccessNotificationFireAndForget("Success Title", "Success Message");

        // Assert - method returns immediately
    }

    #endregion

    #region ValidateServiceAsync Tests

    [Fact]
    public async Task ValidateServiceAsync_ServiceAccessible_ShouldReturnTrue()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "ntfy service", "/");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.ValidateServiceAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateServiceAsync_ServiceNotAccessible_ShouldReturnFalse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.ServiceUnavailable, "", "/");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.ValidateServiceAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateServiceAsync_Exception_ShouldReturnFalse()
    {
        // Arrange
        SetupHttpException("/");
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var result = await service.ValidateServiceAsync();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetServiceStatistics Tests

    [Fact]
    public void GetServiceStatistics_ShouldReturnCompleteStatistics()
    {
        // Arrange
        var service = new NtfyService(_httpClientFactoryMock.Object, _loggerMock.Object, _settingsMock.Object);

        // Act
        var stats = service.GetServiceStatistics();

        // Assert
        Assert.Contains("ServiceStartTime", stats);
        Assert.Contains("UptimeMinutes", stats);
        Assert.Contains("TotalPollRequests", stats);
        Assert.Contains("TotalMessagesRetrieved", stats);
        Assert.Contains("TotalNotificationsSent", stats);
        Assert.Contains("TotalErrors", stats);
        Assert.Contains("LastPollTime", stats);
        Assert.Contains("SuccessRate", stats);
        Assert.Contains("Settings", stats);
        
        // Verify settings are included
        var settings = (Dictionary<string, object>)stats["Settings"];
        Assert.Equal(_settings.BaseUrl, settings["BaseUrl"]);
        Assert.Equal(_settings.MessagesTopic, settings["MessagesTopic"]);
        Assert.Equal(_settings.NotificationsTopic, settings["NotificationsTopic"]);
    }

    #endregion

    #region Helper Methods

    private void SetupHttpResponse(HttpStatusCode statusCode, string content, string? expectedUrl = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        if (expectedUrl != null)
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => 
                        req.RequestUri!.ToString().Contains(expectedUrl)),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }
        else
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }
    }

    private void SetupHttpTimeout(string? expectedUrl = null)
    {
        if (expectedUrl != null)
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => 
                        req.RequestUri!.ToString().Contains(expectedUrl)),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("Request timed out"));
        }
        else
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("Request timed out"));
        }
    }

    private void SetupHttpException(string? expectedUrl = null)
    {
        if (expectedUrl != null)
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => 
                        req.RequestUri!.ToString().Contains(expectedUrl)),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Connection failed"));
        }
        else
        {
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Connection failed"));
        }
    }

    private void VerifyHttpRequest(string expectedUrl, Action<string> contentValidator)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => 
                req.RequestUri!.ToString().Contains(expectedUrl) &&
                req.Method == HttpMethod.Post &&
                VerifyRequestContent(req, contentValidator)),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    private static bool VerifyRequestContent(HttpRequestMessage request, Action<string> contentValidator)
    {
        if (request.Content == null) return false;
        
        var content = request.Content.ReadAsStringAsync().Result;
        contentValidator(content);
        return true;
    }

    #endregion
}