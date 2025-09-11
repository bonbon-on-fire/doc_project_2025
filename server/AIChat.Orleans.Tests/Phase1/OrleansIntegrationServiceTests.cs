using AIChat.Orleans.Client.Configuration;
using AIChat.Orleans.Client.Services;
using AIChat.Orleans.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Moq;
using NUnit.Framework;

namespace AIChat.Orleans.Tests.Phase1;

/// <summary>
/// Tests for OrleansIntegrationService to verify shadow mode behavior.
/// </summary>
[TestFixture]
public class OrleansIntegrationServiceTests
{
    private Mock<IGrainFactory>? _mockGrainFactory;
    private Mock<IFeatureManager>? _mockFeatureManager;
    private Mock<ILogger<OrleansIntegrationService>>? _mockLogger;
    private Mock<IUserGrain>? _mockUserGrain;
    private Mock<IOptions<OrleansResilienceConfiguration>>? _mockResilienceOptions;
    private OrleansIntegrationService? _service;

    [SetUp]
    public void Setup()
    {
        _mockGrainFactory = new Mock<IGrainFactory>();
        _mockFeatureManager = new Mock<IFeatureManager>();
        _mockLogger = new Mock<ILogger<OrleansIntegrationService>>();
        _mockUserGrain = new Mock<IUserGrain>();
        _mockResilienceOptions = new Mock<IOptions<OrleansResilienceConfiguration>>();

        // Setup default resilience configuration
        _ = _mockResilienceOptions
            .Setup(x => x.Value)
            .Returns(new OrleansResilienceConfiguration());

        _ = _mockGrainFactory
            .Setup(x => x.GetGrain<IUserGrain>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(_mockUserGrain.Object);

        _service = new OrleansIntegrationService(
            _mockGrainFactory.Object,
            _mockFeatureManager.Object,
            _mockLogger.Object,
            _mockResilienceOptions.Object
        );
    }

    [Test]
    public async Task RecordUserActivityAsyncWithFeatureFlagEnabledShouldCallGrain()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        _ = _mockUserGrain!
            .Setup(x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service!.RecordUserActivityAsync(
            "test-user",
            ActivityType.MessageSent,
            new { test = "data" }
        );

        // Assert
        _mockUserGrain.Verify(
            x =>
                x.RecordActivity(
                    ActivityType.MessageSent,
                    It.Is<string>(json => json.Contains("test"))
                ),
            Times.Once
        );
    }

    [Test]
    public async Task RecordUserActivityAsyncWithFeatureFlagDisabledShouldNotCallGrain()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(false);

        // Act
        await _service!.RecordUserActivityAsync(
            "test-user",
            ActivityType.MessageSent,
            new { test = "data" }
        );

        // Assert
        _mockUserGrain!.Verify(
            x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Test]
    public async Task RecordUserActivityAsyncWithEmptyUserIdShouldNotCallGrain()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        // Act
        await _service!.RecordUserActivityAsync(
            "",
            ActivityType.MessageSent,
            new { test = "data" }
        );

        // Assert
        _mockUserGrain!.Verify(
            x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Test]
    public Task RecordUserActivityAsyncWithGrainExceptionShouldNotThrow()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        _ = _mockUserGrain!
            .Setup(x => x.RecordActivity(It.IsAny<ActivityType>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Grain failure"));

        // Act & Assert - Should not throw in shadow mode
        Assert.DoesNotThrowAsync(
            async () =>
                await _service!.RecordUserActivityAsync(
                    "test-user",
                    ActivityType.MessageSent,
                    new { test = "data" }
                )
        );

        // Verify warning was logged
        _mockLogger!.Verify(
            x =>
                x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>(
                        (v, t) => v.ToString()!.Contains("Failed to record Orleans activity")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            Times.Once
        );
        return Task.CompletedTask;
    }

    [Test]
    public async Task GetUserStateAsyncWithValidUserShouldReturnState()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        var expectedState = new UserGrainState
        {
            UserId = "test-user",
            LastActivity = DateTime.UtcNow,
        };

        _ = _mockUserGrain!.Setup(x => x.GetState()).ReturnsAsync(expectedState);

        // Act
        var result = await _service!.GetUserStateAsync("test-user");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.UserId, Is.EqualTo("test-user"));
            Assert.That(result.LastActivity, Is.EqualTo(expectedState.LastActivity));
        });
    }

    [Test]
    public async Task IsOrleansHealthyAsyncWithHealthyGrainShouldReturnTrue()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        _ = _mockUserGrain!
            .Setup(x => x.CheckHealth())
            .ReturnsAsync(new HealthCheckResult { IsHealthy = true });

        // Act
        var result = await _service!.IsOrleansHealthyAsync();

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsOrleansHealthyAsyncWithFeatureFlagDisabledShouldReturnFalse()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(false);

        // Act
        var result = await _service!.IsOrleansHealthyAsync();

        // Assert
        Assert.That(result, Is.False);
        _mockUserGrain!.Verify(x => x.CheckHealth(), Times.Never);
    }

    [Test]
    public async Task CheckUserHealthAsyncWithValidUserShouldReturnHealthResult()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        var expectedHealth = new HealthCheckResult
        {
            IsHealthy = true,
            GrainId = "test-user",
            CheckedAt = DateTime.UtcNow,
            Warnings = [],
        };

        _ = _mockUserGrain!.Setup(x => x.CheckHealth()).ReturnsAsync(expectedHealth);

        // Act
        var result = await _service!.CheckUserHealthAsync("test-user");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.IsHealthy, Is.True);
            Assert.That(result.GrainId, Is.EqualTo("test-user"));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task GetConnectionStatusAsyncShouldReturnStatusInfo()
    {
        // Arrange
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ReturnsAsync(true);

        // Act
        var result = await _service!.GetConnectionStatusAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.ConnectionState, Is.Not.Null);
            Assert.That(result.Warnings, Is.Not.Null);
        });
    }

    [Test]
    public Task Phase2MethodsShouldBeStubbed()
    {
        // Act & Assert - These should not throw but also not do anything in Phase 1
        Assert.DoesNotThrowAsync(async () =>
        {
            await _service!.RegisterConnectionAsync("user", "conn-1", "client-1");
            await _service.UnregisterConnectionAsync("user", "conn-1");
            await _service.SubscribeToChatAsync("user", "conn-1", "chat-1");
        });
        return Task.CompletedTask;
    }

    [Test]
    public async Task Phase3MethodsShouldReturnDummyValues()
    {
        // Arrange
        var message = new ChatMessage
        {
            Id = "msg-1",
            ChatId = "chat-1",
            UserId = "user",
            Content = "test",
        };

        // Act
        var operationId = await _service!.ProcessMessageAsync("user", message);

        Assert.DoesNotThrowAsync(
            async () => await _service.CancelOperationAsync("user", operationId)
        );

        // Assert
        Assert.That(operationId, Is.Not.Null);
        Assert.That(operationId, Is.Not.Empty);
    }

    [Test]
    public Task ShadowModeShouldNeverThrowExceptions()
    {
        // Arrange - Setup various failure scenarios
        _ = _mockFeatureManager!
            .Setup(x => x.IsEnabledAsync("OrleansIntegration"))
            .ThrowsAsync(new InvalidOperationException("Feature manager failure"));

        // Act & Assert - None of these should throw
        Assert.DoesNotThrowAsync(async () =>
        {
            await _service!.RecordUserActivityAsync(
                "user",
                ActivityType.MessageSent,
                new { test = "data" }
            );
            _ = await _service.GetUserStateAsync("user");
            _ = await _service.IsOrleansHealthyAsync();
            _ = await _service.CheckUserHealthAsync("user");
            var status = await _service.GetConnectionStatusAsync();
            Assert.That(status, Is.Not.Null);
        });
        return Task.CompletedTask;
    }
}
