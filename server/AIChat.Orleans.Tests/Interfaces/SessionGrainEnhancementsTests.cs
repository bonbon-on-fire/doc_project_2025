using AIChat.Orleans.Contracts;
using Orleans.Concurrency;
using Xunit;

namespace AIChat.Orleans.Tests.Interfaces;

/// <summary>
/// Tests for enhanced session grain interface features.
/// </summary>
public class SessionGrainEnhancementsTests
{
    [Fact]
    public void ISessionStateGrainShouldHaveNewConvenienceMethods()
    {
        // Arrange
        var type = typeof(ISessionStateGrain);

        // Act & Assert
        Assert.NotNull(type.GetMethod("CanTransitionAsync"));
        Assert.NotNull(type.GetMethod("TransitionStateAsync"));
    }

    [Fact]
    public void ISessionConnectionGrainShouldHaveNewConvenienceMethods()
    {
        // Arrange
        var type = typeof(ISessionConnectionGrain);

        // Act & Assert
        Assert.NotNull(type.GetMethod("QuickConnectAsync"));
        Assert.NotNull(type.GetMethod("ForceDisconnectAsync"));
        Assert.NotNull(type.GetMethod("IsReadyForConnectionAsync"));
    }

    [Fact]
    public void CanTransitionAsyncShouldHaveReadOnlyAttribute()
    {
        // Arrange
        var type = typeof(ISessionStateGrain);
        var method = type.GetMethod("CanTransitionAsync");

        // Act
        var hasReadOnly = method!.GetCustomAttributes(typeof(ReadOnlyAttribute), false).Length > 0;

        // Assert
        Assert.True(hasReadOnly);
    }

    [Fact]
    public void IsReadyForConnectionAsyncShouldHaveReadOnlyAttribute()
    {
        // Arrange
        var type = typeof(ISessionConnectionGrain);
        var method = type.GetMethod("IsReadyForConnectionAsync");

        // Act
        var hasReadOnly = method!.GetCustomAttributes(typeof(ReadOnlyAttribute), false).Length > 0;

        // Assert
        Assert.True(hasReadOnly);
    }

    [Fact]
    public void TransitionStateAsyncShouldReturnSessionOperationResult()
    {
        // Arrange
        var type = typeof(ISessionStateGrain);
        var method = type.GetMethod("TransitionStateAsync");

        // Act
        var returnType = method!.ReturnType;

        // Assert
        Assert.True(returnType.IsGenericType);
        Assert.Equal(typeof(Task<>), returnType.GetGenericTypeDefinition());

        var innerType = returnType.GetGenericArguments()[0];
        Assert.True(innerType.IsGenericType);
        Assert.Equal(typeof(SessionOperationResult<>), innerType.GetGenericTypeDefinition());
    }

    [Fact]
    public void QuickConnectAsyncShouldHaveCorrectParameters()
    {
        // Arrange
        var type = typeof(ISessionConnectionGrain);
        var method = type.GetMethod("QuickConnectAsync");

        // Act
        var parameters = method!.GetParameters();

        // Assert
        Assert.Equal(4, parameters.Length);
        Assert.Equal("sessionId", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("userId", parameters[1].Name);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal("protocolType", parameters[2].Name);
        Assert.Equal(typeof(string), parameters[2].ParameterType);
        Assert.Equal("cancellationToken", parameters[3].Name);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
        Assert.True(parameters[3].HasDefaultValue);
    }

    [Fact]
    public void SessionStateTransitionRequestShouldBeSerializable()
    {
        // Arrange
        var request = new SessionStateTransitionRequest
        {
            Transition = SessionStateTransition.Connect,
            Reason = "User initiated",
            Metadata = new Dictionary<string, string> { ["key"] = "value" }
        };

        // Act & Assert
        Assert.NotNull(request);
        Assert.Equal(SessionStateTransition.Connect, request.Transition);
        Assert.Equal("User initiated", request.Reason);
        _ = Assert.Single(request.Metadata);
    }

    [Fact]
    public void SessionOperationResultShouldHaveAllProperties()
    {
        // Arrange
        var result = new SessionOperationResult<string>
        {
            Success = true,
            Data = "test-data",
            ErrorMessage = null,
            ErrorCode = null,
            Duration = TimeSpan.FromMilliseconds(100),
            Context = new Dictionary<string, string> { ["operation"] = "test" }
        };

        // Act & Assert
        Assert.True(result.Success);
        Assert.Equal("test-data", result.Data);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorCode);
        Assert.Equal(TimeSpan.FromMilliseconds(100), result.Duration);
        _ = Assert.Single(result.Context);
        Assert.NotEmpty(result.CorrelationId);
        Assert.NotEqual(default, result.Timestamp);
    }

    [Fact]
    public void ISessionStateGrainShouldHaveExpectedTotalMethods()
    {
        // Arrange
        var type = typeof(ISessionStateGrain);

        // Act
        var methods = type.GetMethods()
            .Where(m => !m.IsSpecialName && m.DeclaringType == type)
            .ToList();

        // Assert
        Assert.Equal(10, methods.Count); // 8 original + 2 new convenience methods
    }

    [Fact]
    public void ISessionConnectionGrainShouldHaveExpectedTotalMethods()
    {
        // Arrange
        var type = typeof(ISessionConnectionGrain);

        // Act
        var methods = type.GetMethods()
            .Where(m => !m.IsSpecialName && m.DeclaringType == type)
            .ToList();

        // Assert
        Assert.Equal(12, methods.Count); // 9 original + 3 new convenience methods
    }
}
