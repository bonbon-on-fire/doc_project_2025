using System.Text.Json;
using AIChat.Orleans.Contracts;
using Xunit;

namespace AIChat.Orleans.Tests.Exceptions;

/// <summary>
/// Unit tests for Session grain exceptions.
/// </summary>
public class SessionGrainExceptionTests
{
    #region SessionNotFoundException Tests

    [Fact]
    public void SessionNotFoundExceptionShouldInitializeWithSessionId()
    {
        // Arrange & Act
        var exception = new SessionNotFoundException("session-123");

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("session-123", exception.RequestedSessionId);
        Assert.Contains("session-123", exception.Message);
        Assert.NotNull(exception.CorrelationId);
        Assert.NotEqual(default, exception.OccurredAt);
    }

    [Fact]
    public void SessionNotFoundExceptionShouldInitializeWithCustomMessage()
    {
        // Arrange & Act
        var exception = new SessionNotFoundException("session-123", "Custom error message");

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("Custom error message", exception.Message);
        Assert.Contains("session-123", exception.Context["RequestedSessionId"].ToString());
    }

    [Fact]
    public void SessionNotFoundExceptionShouldBeSerializable()
    {
        // Arrange
        var original = new SessionNotFoundException("session-123");

        // Act
        var json = JsonSerializer.Serialize(new
        {
            original.Message,
            original.SessionId,
            original.RequestedSessionId,
            original.CorrelationId
        });
        var deserialized = JsonSerializer.Deserialize<dynamic>(json);

        // Assert
        Assert.NotNull(deserialized);
    }

    #endregion

    #region SessionAlreadyExistsException Tests

    [Fact]
    public void SessionAlreadyExistsExceptionShouldInitializeWithSessionId()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;

        // Act
        var exception = new SessionAlreadyExistsException("session-123", createdAt);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("session-123", exception.ExistingSessionId);
        Assert.Equal(createdAt, exception.ExistingSessionCreatedAt);
        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public void SessionAlreadyExistsExceptionShouldHandleNullCreatedAt()
    {
        // Arrange & Act
        var exception = new SessionAlreadyExistsException("session-123", null);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Null(exception.ExistingSessionCreatedAt);
        Assert.False(exception.Context.ContainsKey("ExistingSessionCreatedAt"));
    }

    #endregion

    #region SessionDisconnectedException Tests

    [Fact]
    public void SessionDisconnectedExceptionShouldInitializeWithDetails()
    {
        // Arrange
        var disconnectedAt = DateTime.UtcNow;

        // Act
        var exception = new SessionDisconnectedException("session-123", "Network failure", disconnectedAt);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("Network failure", exception.DisconnectionReason);
        Assert.Equal(disconnectedAt, exception.DisconnectedAt);
        Assert.Contains("disconnected", exception.Message.ToLower(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SessionDisconnectedExceptionShouldHandleNullValues()
    {
        // Arrange & Act
        var exception = new SessionDisconnectedException("session-123", null, null);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Null(exception.DisconnectionReason);
        Assert.Null(exception.DisconnectedAt);
        Assert.Equal("Unknown", exception.Context["DisconnectionReason"]);
    }

    #endregion

    #region SessionProtocolException Tests

    [Fact]
    public void SessionProtocolExceptionShouldInitializeWithProtocol()
    {
        // Arrange & Act
        var exception = new SessionProtocolException("SignalR", "Protocol handshake failed", "session-123");

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("SignalR", exception.ProtocolType);
        Assert.Contains("Protocol handshake failed", exception.Message);
    }

    [Fact]
    public void SessionProtocolExceptionShouldInitializeWithOperation()
    {
        // Arrange & Act
        var exception = new SessionProtocolException("SignalR", "Handshake", "Failed to negotiate", "session-123");

        // Assert
        Assert.Equal("SignalR", exception.ProtocolType);
        Assert.Equal("Handshake", exception.FailedOperation);
        Assert.Contains("Handshake", exception.Message);
        Assert.Contains("Failed to negotiate", exception.Message);
    }

    [Fact]
    public void SessionProtocolExceptionShouldSupportErrorCode()
    {
        // Arrange
        var exception = new SessionProtocolException("SignalR", "Connection failed", "session-123");

        // Act
        _ = exception.WithErrorCode("ERR_CONN_001");

        // Assert
        Assert.Equal("ERR_CONN_001", exception.ErrorCode);
        Assert.Equal("ERR_CONN_001", exception.Context["ErrorCode"]);
    }

    [Fact]
    public void SessionProtocolExceptionShouldSupportInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new SessionProtocolException("SignalR", "Protocol error", innerException, "session-123");

        // Assert
        Assert.Equal(innerException, exception.InnerException);
        Assert.Contains("Protocol error", exception.Message);
    }

    #endregion

    #region SessionReconnectionException Tests

    [Fact]
    public void SessionReconnectionExceptionShouldInitializeWithAttempts()
    {
        // Arrange & Act
        var exception = new SessionReconnectionException("session-123", 5, 3);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal(5, exception.AttemptCount);
        Assert.Equal(3, exception.MaxAttempts);
        Assert.Contains("5 attempts", exception.Message);
        Assert.Contains("max: 3", exception.Message);
    }

    [Fact]
    public void SessionReconnectionExceptionShouldInitializeWithLastError()
    {
        // Arrange & Act
        var exception = new SessionReconnectionException("session-123", 3, "Connection timeout");

        // Assert
        Assert.Equal(3, exception.AttemptCount);
        Assert.Equal("Connection timeout", exception.LastError);
        Assert.Contains("Connection timeout", exception.Message);
    }

    [Fact]
    public void SessionReconnectionExceptionShouldSupportInnerException()
    {
        // Arrange
        var innerException = new TimeoutException("Timeout occurred");

        // Act
        var exception = new SessionReconnectionException("session-123", "Reconnection failed", innerException);

        // Assert
        Assert.Equal(innerException, exception.InnerException);
        Assert.Equal("Timeout occurred", exception.LastError);
    }

    #endregion

    #region SessionTimeoutException Tests

    [Fact]
    public void SessionTimeoutExceptionShouldInitializeWithDetails()
    {
        // Arrange & Act
        var exception = new SessionTimeoutException("session-123", "Connect", 30);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("Connect", exception.Operation);
        Assert.Equal(30, exception.TimeoutSeconds);
        Assert.Contains("30 seconds", exception.Message);
        Assert.Contains("Connect", exception.Message);
    }

    [Fact]
    public void SessionTimeoutExceptionShouldSupportCustomMessage()
    {
        // Arrange & Act
        var exception = new SessionTimeoutException("Custom timeout message", "session-123", "Read", 60);

        // Assert
        Assert.Equal("Custom timeout message", exception.Message);
        Assert.Equal("Read", exception.Operation);
        Assert.Equal(60, exception.TimeoutSeconds);
    }

    #endregion

    #region SessionValidationException Tests

    [Fact]
    public void SessionValidationExceptionShouldInitializeWithMessage()
    {
        // Arrange & Act
        var exception = new SessionValidationException("Validation failed", "session-123");

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("Validation failed", exception.Message);
        Assert.NotNull(exception.ValidationErrors);
        Assert.Empty(exception.ValidationErrors);
    }

    [Fact]
    public void SessionValidationExceptionShouldInitializeWithErrors()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

        // Act
        var exception = new SessionValidationException(errors, "session-123");

        // Assert
        Assert.Equal(3, exception.ValidationErrors.Count);
        Assert.Contains("3 error(s)", exception.Message);
        Assert.Contains("Error 1", exception.Message);
    }

    [Fact]
    public void SessionValidationExceptionShouldInitializeWithField()
    {
        // Arrange & Act
        var exception = new SessionValidationException("Username", "Username is required", "session-123");

        // Assert
        Assert.Equal("Username", exception.FailedField);
        _ = Assert.Single(exception.ValidationErrors);
        Assert.Contains("Username", exception.Message);
        Assert.Contains("Username is required", exception.Message);
    }

    [Fact]
    public void SessionValidationExceptionShouldSupportAddingErrors()
    {
        // Arrange
        var exception = new SessionValidationException("Initial error", "session-123");

        // Act
        _ = exception.AddError("Additional error 1");
        _ = exception.AddError("Additional error 2");

        // Assert
        Assert.Equal(2, exception.ValidationErrors.Count);
        Assert.Contains("Additional error 1", exception.ValidationErrors);
        Assert.Contains("Additional error 2", exception.ValidationErrors);
    }

    #endregion

    #region SessionLimitExceededException Tests

    [Fact]
    public void SessionLimitExceededExceptionShouldInitializeWithLimits()
    {
        // Arrange & Act
        var exception = new SessionLimitExceededException("Connections", 1005, 1000, "session-123");

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("Connections", exception.LimitType);
        Assert.Equal(1005, exception.CurrentValue);
        Assert.Equal(1000, exception.MaxLimit);
        Assert.Contains("current=1005", exception.Message);
        Assert.Contains("max=1000", exception.Message);
    }

    [Fact]
    public void SessionLimitExceededExceptionShouldSupportCustomMessage()
    {
        // Arrange & Act
        var exception = new SessionLimitExceededException(
            "Too many connections",
            "Connections",
            500,
            100,
            "session-123");

        // Assert
        Assert.Equal("Too many connections", exception.Message);
        Assert.Equal(500, exception.CurrentValue);
        Assert.Equal(100, exception.MaxLimit);
    }

    #endregion

    #region SessionAlreadyConnectedException Tests

    [Fact]
    public void SessionAlreadyConnectedExceptionShouldInitializeWithConnectionId()
    {
        // Arrange
        var connectedAt = DateTime.UtcNow;

        // Act
        var exception = new SessionAlreadyConnectedException("session-123", "conn-456", connectedAt);

        // Assert
        Assert.Equal("session-123", exception.SessionId);
        Assert.Equal("conn-456", exception.ExistingConnectionId);
        Assert.Equal(connectedAt, exception.ConnectedAt);
        Assert.Contains("already connected", exception.Message);
        Assert.Contains("conn-456", exception.Message);
    }

    [Fact]
    public void SessionAlreadyConnectedExceptionShouldHandleNullConnectedAt()
    {
        // Arrange & Act
        var exception = new SessionAlreadyConnectedException("session-123", "conn-456", null);

        // Assert
        Assert.Equal("conn-456", exception.ExistingConnectionId);
        Assert.Null(exception.ConnectedAt);
        Assert.False(exception.Context.ContainsKey("ConnectedAt"));
    }

    #endregion

    #region Base Exception Tests

    [Fact]
    public void SessionGrainExceptionShouldHaveCorrelationId()
    {
        // Arrange & Act
        var exception1 = new SessionNotFoundException("session-123");
        var exception2 = new SessionNotFoundException("session-456");

        // Assert
        Assert.NotNull(exception1.CorrelationId);
        Assert.NotNull(exception2.CorrelationId);
        Assert.NotEqual(exception1.CorrelationId, exception2.CorrelationId);
    }

    [Fact]
    public void SessionGrainExceptionShouldSupportContext()
    {
        // Arrange
        var exception = new SessionNotFoundException("session-123");

        // Act
        _ = exception.WithContext("Key1", "Value1")
                .WithContext("Key2", 123)
                .WithContext("Key3", true);

        // Assert
        Assert.Equal("Value1", exception.Context["Key1"]);
        Assert.Equal(123, exception.Context["Key2"]);
        Assert.Equal(true, exception.Context["Key3"]);
    }

    [Fact]
    public void SessionGrainExceptionShouldTrackOccurredAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var exception = new SessionNotFoundException("session-123");
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(exception.OccurredAt >= beforeCreation);
        Assert.True(exception.OccurredAt <= afterCreation);
    }

    #endregion
}
