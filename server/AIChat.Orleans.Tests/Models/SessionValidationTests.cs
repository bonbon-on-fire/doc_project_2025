using AIChat.Orleans.Contracts;
using Xunit;

namespace AIChat.Orleans.Tests.Models;

/// <summary>
/// Tests for SessionValidation helper methods.
/// </summary>
public class SessionValidationTests
{
    [Theory]
    [InlineData(SessionLifecycleState.Initialized, SessionLifecycleState.Connected, true)]
    [InlineData(SessionLifecycleState.Initialized, SessionLifecycleState.Archived, true)]
    [InlineData(SessionLifecycleState.Initialized, SessionLifecycleState.Error, true)]
    [InlineData(SessionLifecycleState.Initialized, SessionLifecycleState.Disconnected, false)]
    [InlineData(SessionLifecycleState.Connected, SessionLifecycleState.Disconnected, true)]
    [InlineData(SessionLifecycleState.Connected, SessionLifecycleState.Reconnecting, false)]
    [InlineData(SessionLifecycleState.Disconnected, SessionLifecycleState.Reconnecting, true)]
    [InlineData(SessionLifecycleState.Archived, SessionLifecycleState.Connected, false)]
    [InlineData(SessionLifecycleState.Error, SessionLifecycleState.Disconnected, true)]
    public void IsValidTransitionShouldValidateCorrectly(SessionLifecycleState from, SessionLifecycleState to, bool expected)
    {
        // Act
        var result = SessionValidation.IsValidTransition(from, to);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidTransitionShouldAllowSameState()
    {
        // Arrange
        var state = SessionLifecycleState.Connected;

        // Act
        var result = SessionValidation.IsValidTransition(state, state);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("session-12345678", true)]
    [InlineData("USER_SESSION_123", true)]
    [InlineData("test-session-with-long-id-1234567890", true)]
    [InlineData("short", false)] // Too short
    [InlineData("", false)] // Empty
    [InlineData(null, false)] // Null
    [InlineData("session@123", false)] // Invalid character
    [InlineData("session#123", false)] // Invalid character
    public void IsValidSessionIdShouldValidateCorrectly(string? sessionId, bool expected)
    {
        // Act
        var result = SessionValidation.IsValidSessionId(sessionId);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SignalR", true)]
    [InlineData("SSE", true)]
    [InlineData("WebSocket", true)]
    [InlineData("HTTP", true)]
    [InlineData("GRPC", true)]
    [InlineData("signalr", true)] // Case insensitive
    [InlineData("websocket", true)] // Case insensitive
    [InlineData("TCP", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidProtocolShouldValidateCorrectly(string? protocol, bool expected)
    {
        // Act
        var result = SessionValidation.IsValidProtocol(protocol);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SessionLifecycleState.Initialized, SessionStateTransition.Connect, SessionLifecycleState.Connected)]
    [InlineData(SessionLifecycleState.Connected, SessionStateTransition.Disconnect, SessionLifecycleState.Disconnected)]
    [InlineData(SessionLifecycleState.Disconnected, SessionStateTransition.StartReconnect, SessionLifecycleState.Reconnecting)]
    [InlineData(SessionLifecycleState.Reconnecting, SessionStateTransition.CompleteReconnect, SessionLifecycleState.Connected)]
    [InlineData(SessionLifecycleState.Error, SessionStateTransition.RecoverFromError, SessionLifecycleState.Disconnected)]
    [InlineData(SessionLifecycleState.Connected, SessionStateTransition.Archive, SessionLifecycleState.Archived)]
    public void GetNewStateShouldReturnCorrectState(SessionLifecycleState current, SessionStateTransition transition, SessionLifecycleState expected)
    {
        // Act
        var result = SessionValidation.GetNewState(current, transition);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetNewStateShouldReturnNullForInvalidTransition()
    {
        // Arrange
        var current = SessionLifecycleState.Archived;
        var transition = SessionStateTransition.Connect;

        // Act
        var result = SessionValidation.GetNewState(current, transition);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateConnectionRequestShouldPassForValidRequest()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            AuthToken = "valid-token",
            Endpoint = "https://example.com/connect"
        };

        // Act
        var isValid = SessionValidation.ValidateConnectionRequest(request, out var errors);

        // Assert
        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateConnectionRequestShouldFailForNullRequest()
    {
        // Act
        var isValid = SessionValidation.ValidateConnectionRequest(null!, out var errors);

        // Assert
        Assert.False(isValid);
        Assert.Contains("Connection request cannot be null.", errors);
    }

    [Fact]
    public void ValidateConnectionRequestShouldFailForMissingFields()
    {
        // Arrange
        var request = new ConnectionRequest
        {
            AuthToken = "",
            Endpoint = ""
        };

        // Act
        var isValid = SessionValidation.ValidateConnectionRequest(request, out var errors);

        // Assert
        Assert.False(isValid);
        Assert.Contains("Authentication token is required.", errors);
        Assert.Contains("Connection endpoint is required.", errors);
    }
}