using AIChat.Orleans.Contracts;
using Xunit;

namespace AIChat.Orleans.Tests.Exceptions;

/// <summary>
/// Tests for enhanced exception features including severity and retry hints.
/// </summary>
public class SessionGrainExceptionEnhancementsTests
{
    [Fact]
    public void SessionNotFoundExceptionShouldHaveCorrectDefaultSettings()
    {
        // Arrange & Act
        var exception = new SessionNotFoundException("test-session");

        // Assert
        Assert.Equal(ExceptionSeverity.Low, exception.Severity);
        Assert.False(exception.CanRetry);
        Assert.True(exception.IsRecoverable);
        Assert.Equal("SESSION_NOT_FOUND", exception.ErrorCode);
    }

    [Fact]
    public void SessionDisconnectedExceptionShouldBeRetryable()
    {
        // Arrange & Act
        var exception = new SessionDisconnectedException("test-session", "Network failure");

        // Assert
        Assert.Equal(ExceptionSeverity.Medium, exception.Severity);
        Assert.True(exception.CanRetry);
        Assert.Equal(TimeSpan.FromSeconds(5), exception.RetryAfter);
        Assert.Equal(3, exception.MaxRetryAttempts);
        Assert.True(exception.IsRecoverable);
        Assert.Equal("SESSION_DISCONNECTED", exception.ErrorCode);
    }

    [Fact]
    public void ExceptionShouldSupportFluentConfiguration()
    {
        // Arrange
        var exception = new SessionNotFoundException("test-session");

        // Act
        _ = exception
            .WithSeverity(ExceptionSeverity.Critical)
            .WithRetryPolicy(true, TimeSpan.FromSeconds(10), 5)
            .WithContext("Operation", "Connect");

        // Assert
        Assert.Equal(ExceptionSeverity.Critical, exception.Severity);
        Assert.True(exception.CanRetry);
        Assert.Equal(TimeSpan.FromSeconds(10), exception.RetryAfter);
        Assert.Equal(5, exception.MaxRetryAttempts);
        Assert.Equal("Connect", exception.Context["Operation"]);
    }

    [Fact]
    public void ExceptionSeverityShouldHaveAllLevels()
    {
        // Assert
        Assert.Equal(0, (int)ExceptionSeverity.Low);
        Assert.Equal(1, (int)ExceptionSeverity.Medium);
        Assert.Equal(2, (int)ExceptionSeverity.High);
        Assert.Equal(3, (int)ExceptionSeverity.Critical);
    }

    private sealed class TestSessionException : SessionGrainException
    {
        public TestSessionException(string message) : base(message, "test-session")
        {
        }
    }

    [Fact]
    public void SessionGrainExceptionShouldHaveDefaultSettings()
    {
        // Arrange & Act
        var exception = new TestSessionException("Test message");

        // Assert
        Assert.Equal(ExceptionSeverity.Medium, exception.Severity);
        Assert.False(exception.CanRetry);
        Assert.Null(exception.RetryAfter);
        Assert.Null(exception.MaxRetryAttempts);
        Assert.True(exception.IsRecoverable);
        Assert.Null(exception.ErrorCode);
        Assert.NotNull(exception.CorrelationId);
        Assert.Equal("test-session", exception.SessionId);
    }

    [Fact]
    public void SessionProtocolExceptionShouldHaveNewErrorCodeProperty()
    {
        // Arrange & Act
        var exception = new SessionProtocolException("WebSocket", "Connection failed", "test-session");
        _ = exception.WithErrorCode("PROTOCOL_ERROR_001");

        // Assert
        Assert.Equal("PROTOCOL_ERROR_001", exception.ErrorCode);
    }
}
