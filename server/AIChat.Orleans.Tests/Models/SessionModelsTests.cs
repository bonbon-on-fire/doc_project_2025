using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AIChat.Orleans.Contracts;
using Orleans;
using Xunit;

namespace AIChat.Orleans.Tests.Models;

/// <summary>
/// Unit tests for Session grain data models and DTOs.
/// </summary>
public class SessionModelsTests
{
    #region SessionInitRequest Tests

    [Fact]
    public void SessionInitRequestShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var request = new SessionInitRequest
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ProtocolType = "SignalR"
        };

        // Assert
        Assert.Equal("session-123", request.SessionId);
        Assert.Equal("user-456", request.UserId);
        Assert.Equal("client-789", request.ClientId);
        Assert.Equal("SignalR", request.ProtocolType);
    }

    [Fact]
    public void SessionInitRequestShouldHaveDefaultValues()
    {
        // Arrange & Act
        var request = new SessionInitRequest
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ProtocolType = "SignalR"
        };

        // Assert
        Assert.Equal(3600, request.TimeoutSeconds);
        Assert.NotNull(request.Metadata);
        Assert.Empty(request.Metadata);
        Assert.NotEqual(default, request.CreatedAt);
    }

    [Fact]
    public void SessionInitRequestShouldValidateRequiredFields()
    {
        // Arrange
        var request = new SessionInitRequest
        {
            SessionId = null!,
            UserId = null!,
            ClientId = null!,
            ProtocolType = null!
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("SessionId"));
        Assert.Contains(results, r => r.MemberNames.Contains("UserId"));
        Assert.Contains(results, r => r.MemberNames.Contains("ClientId"));
        Assert.Contains(results, r => r.MemberNames.Contains("ProtocolType"));
    }

    [Fact]
    public void SessionInitRequestShouldHaveGenerateSerializerAttribute()
    {
        // Assert
        Assert.NotNull(typeof(SessionInitRequest).GetCustomAttributes(typeof(GenerateSerializerAttribute), false).FirstOrDefault());
    }

    [Fact]
    public void SessionInitRequestShouldHaveAliasAttribute()
    {
        // Arrange
        var aliasAttribute = typeof(SessionInitRequest).GetCustomAttributes(typeof(AliasAttribute), false)
            .FirstOrDefault() as AliasAttribute;

        // Assert
        Assert.NotNull(aliasAttribute);
        // AliasAttribute doesn't expose Value property directly in newer versions
    }

    #endregion

    #region SessionState Tests

    [Fact]
    public void SessionStateShouldHaveRequiredProperties()
    {
        // Arrange & Act
        var state = new SessionState
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ConnectionStatus = new ConnectionStatus { State = "Connected", IsConnected = true },
            ProtocolInfo = new SessionProtocolInfo
            {
                ProtocolType = "SignalR",
                Version = "1.0",
                State = new ProtocolState { StateName = "Ready", IsReady = true },
                Configuration = new ProtocolConfiguration { ProtocolType = "SignalR" }
            }
        };

        // Assert
        Assert.Equal("session-123", state.SessionId);
        Assert.Equal("user-456", state.UserId);
        Assert.Equal("client-789", state.ClientId);
        Assert.NotNull(state.ConnectionStatus);
        Assert.NotNull(state.ProtocolInfo);
    }

    [Fact]
    public void SessionStateShouldBeSerializable()
    {
        // Arrange
        var state = new SessionState
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ConnectionStatus = new ConnectionStatus { State = "Connected", IsConnected = true },
            ProtocolInfo = new SessionProtocolInfo
            {
                ProtocolType = "SignalR",
                Version = "1.0",
                State = new ProtocolState { StateName = "Ready", IsReady = true },
                Configuration = new ProtocolConfiguration { ProtocolType = "SignalR" }
            },
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IsArchived = false,
            TotalConnectionTimeSeconds = 3600,
            ReconnectionCount = 2
        };

        // Act
        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<SessionState>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(state.SessionId, deserialized.SessionId);
        Assert.Equal(state.UserId, deserialized.UserId);
        Assert.Equal(state.TotalConnectionTimeSeconds, deserialized.TotalConnectionTimeSeconds);
        Assert.Equal(state.ReconnectionCount, deserialized.ReconnectionCount);
    }

    #endregion

    #region ConnectionRequest Tests

    [Fact]
    public void ConnectionRequestShouldValidateRequiredFields()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            AuthToken = null!,
            Endpoint = null!
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("AuthToken"));
        Assert.Contains(results, r => r.MemberNames.Contains("Endpoint"));
    }

    [Fact]
    public void ConnectionRequestShouldHaveDefaultValues()
    {
        // Arrange & Act
        var request = new ConnectionRequest
        {
            AuthToken = "token",
            Endpoint = "https://example.com"
        };

        // Assert
        Assert.Equal(30, request.KeepAliveIntervalSeconds);
        Assert.NotNull(request.ProtocolOptions);
        Assert.NotNull(request.ClientCapabilities);
        Assert.NotEqual(default, request.RequestTime);
    }

    #endregion

    #region ProtocolConfiguration Tests

    [Fact]
    public void ProtocolConfigurationShouldValidateRequiredFields()
    {
        // Arrange
        var config = new ProtocolConfiguration
        {
            ProtocolType = null!
        };
        var context = new ValidationContext(config);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(config, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("ProtocolType"));
    }

    [Fact]
    public void ProtocolConfigurationShouldHaveDefaultVersion()
    {
        // Arrange & Act
        var config = new ProtocolConfiguration
        {
            ProtocolType = "SignalR"
        };

        // Assert
        Assert.Equal("1.0", config.Version);
        Assert.NotNull(config.Settings);
        Assert.NotNull(config.SupportedFeatures);
    }

    #endregion

    #region SessionMetrics Tests

    [Fact]
    public void SessionMetricsShouldInitializeWithValidData()
    {
        // Arrange & Act
        var metrics = new SessionMetrics
        {
            SessionId = "session-123",
            ConnectionMetrics = new ConnectionMetrics
            {
                BytesSent = 1000,
                BytesReceived = 2000,
                MessagesSent = 10,
                MessagesReceived = 20,
                AverageLatencyMs = 15.5
            },
            ResourceUsage = new ResourceUsage
            {
                MemoryBytes = 1048576,
                CpuPercent = 25.5,
                NetworkBandwidthBytesPerSecond = 10000
            },
            PerformanceStats = new PerformanceStats
            {
                AverageResponseTimeMs = 100,
                P95ResponseTimeMs = 200,
                P99ResponseTimeMs = 500,
                ThroughputPerSecond = 1000,
                ErrorRatePercent = 0.5
            }
        };

        // Assert
        Assert.Equal("session-123", metrics.SessionId);
        Assert.NotNull(metrics.ConnectionMetrics);
        Assert.NotNull(metrics.ResourceUsage);
        Assert.NotNull(metrics.PerformanceStats);
        Assert.NotNull(metrics.CustomMetrics);
    }

    #endregion

    #region DiagnosticLevel Enum Tests

    [Fact]
    public void DiagnosticLevelShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal(0, (int)DiagnosticLevel.Basic);
        Assert.Equal(1, (int)DiagnosticLevel.Standard);
        Assert.Equal(2, (int)DiagnosticLevel.Detailed);
        Assert.Equal(3, (int)DiagnosticLevel.Full);
    }

    #endregion

    #region AlertSeverity Enum Tests

    [Fact]
    public void AlertSeverityShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal(0, (int)AlertSeverity.Info);
        Assert.Equal(1, (int)AlertSeverity.Warning);
        Assert.Equal(2, (int)AlertSeverity.Error);
        Assert.Equal(3, (int)AlertSeverity.Critical);
    }

    #endregion

    #region Range Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(86400)]
    [InlineData(3600)]
    public void SessionInitRequestTimeoutSecondsShouldAcceptValidRange(int timeout)
    {
        // Arrange
        var request = new SessionInitRequest
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ProtocolType = "SignalR",
            TimeoutSeconds = timeout
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.True(isValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86401)]
    public void SessionInitRequestTimeoutSecondsShouldRejectInvalidRange(int timeout)
    {
        // Arrange
        var request = new SessionInitRequest
        {
            SessionId = "session-123",
            UserId = "user-456",
            ClientId = "client-789",
            ProtocolType = "SignalR",
            TimeoutSeconds = timeout
        };
        var context = new ValidationContext(request);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(request, context, results, true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("TimeoutSeconds"));
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void AllModelsShouldBeSerializable()
    {
        // Arrange
        var modelTypes = new[]
        {
            typeof(SessionInitRequest),
            typeof(SessionState),
            typeof(SessionEvent),
            typeof(ConnectionRequest),
            typeof(SessionConnection),
            typeof(ConnectionQuality),
            typeof(ReconnectionRequest),
            typeof(ConnectionAttempt),
            typeof(ConnectionStatus),
            typeof(ConnectionConfiguration),
            typeof(HeartbeatResponse),
            typeof(ConnectionMetrics),
            typeof(ProtocolConfiguration),
            typeof(SessionProtocolInfo),
            typeof(ProtocolState),
            typeof(ProtocolSwitchRequest),
            typeof(ProtocolValidationResult),
            typeof(ProtocolCapabilities),
            typeof(ProtocolInfo),
            typeof(ProtocolMessage),
            typeof(ProtocolResponse),
            typeof(SessionHealthCheck),
            typeof(SessionMetrics),
            typeof(PerformanceStats),
            typeof(DiagnosticReport),
            typeof(AlertConfiguration),
            typeof(SessionAlert),
            typeof(TraceData),
            typeof(ResourceUsage)
        };

        // Act & Assert
        foreach (var type in modelTypes)
        {
            var hasSerializable = type.GetCustomAttributes(typeof(SerializableAttribute), false).Length > 0;
            var hasGenerateSerializer = type.GetCustomAttributes(typeof(GenerateSerializerAttribute), false).Length > 0;

            Assert.True(hasSerializable, $"{type.Name} should have [Serializable] attribute");
            Assert.True(hasGenerateSerializer, $"{type.Name} should have [GenerateSerializer] attribute");
        }
    }

    #endregion
}