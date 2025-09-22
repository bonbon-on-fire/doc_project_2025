using AIChat.Server.Services.StateManagement;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement;

/// <summary>
/// Unit tests for StateResult classes.
/// Tests the result pattern implementation for state management operations.
/// </summary>
public class StateResultTests
{
    #region StateResult<T> Tests

    [Fact]
    public void StateResult_FromSuccess_ShouldCreateSuccessfulResult()
    {
        // Arrange
        var data = "test data";
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = StateResult<string>.FromSuccess(data, metadata);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.IsFailure);
        Assert.Equal(data, result.Data);
        Assert.Null(result.Error);
        Assert.Equal(StateErrorCode.None, result.ErrorCode);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public void StateResult_FromError_ShouldCreateFailedResult()
    {
        // Arrange
        var error = "Test error";
        var errorCode = StateErrorCode.NotFound;
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = StateResult<string>.FromError(error, errorCode, metadata);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsFailure);
        Assert.Null(result.Data);
        Assert.Equal(error, result.Error);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public void StateResult_FromException_ShouldCreateFailedResultFromException()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var errorCode = StateErrorCode.ValidationError;

        // Act
        var result = StateResult<string>.FromException(exception, errorCode);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsFailure);
        Assert.Null(result.Data);
        Assert.Equal(exception.Message, result.Error);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public void StateResult_FromException_ShouldUseDefaultErrorCode()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var result = StateResult<string>.FromException(exception);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(StateErrorCode.InternalError, result.ErrorCode);
    }

    #endregion

    #region StateResult (non-generic) Tests

    [Fact]
    public void StateResult_FromSuccess_ShouldCreateSuccessfulNonGenericResult()
    {
        // Arrange
        var metadata = new Dictionary<string, object> { ["key"] = "value" };

        // Act
        var result = StateResult.FromSuccess(metadata);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
        Assert.Equal(StateErrorCode.None, result.ErrorCode);
        Assert.Equal(metadata, result.Metadata);
    }

    [Fact]
    public void StateResult_FromError_ShouldCreateFailedNonGenericResult()
    {
        // Arrange
        var error = "Test error";
        var errorCode = StateErrorCode.AccessDenied;

        // Act
        var result = StateResult.FromError(error, errorCode);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Equal(errorCode, result.ErrorCode);
    }

    [Fact]
    public void StateResult_FromException_ShouldCreateFailedNonGenericResultFromException()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");

        // Act
        var result = StateResult.FromException(exception, StateErrorCode.TimeoutError);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsFailure);
        Assert.Equal(exception.Message, result.Error);
        Assert.Equal(StateErrorCode.TimeoutError, result.ErrorCode);
    }

    #endregion

    #region StateErrorCode Tests

    [Theory]
    [InlineData(StateErrorCode.None)]
    [InlineData(StateErrorCode.NotFound)]
    [InlineData(StateErrorCode.ValidationError)]
    [InlineData(StateErrorCode.DuplicateKey)]
    [InlineData(StateErrorCode.ConcurrencyConflict)]
    [InlineData(StateErrorCode.InternalError)]
    [InlineData(StateErrorCode.NetworkError)]
    [InlineData(StateErrorCode.TimeoutError)]
    [InlineData(StateErrorCode.AccessDenied)]
    [InlineData(StateErrorCode.ServiceUnavailable)]
    public void StateErrorCode_ShouldHaveDefinedValues(StateErrorCode errorCode)
    {
        // Assert that all error codes are defined
        Assert.True(Enum.IsDefined(errorCode));
    }

    #endregion

    #region Member Access Tests

    [Fact]
    public void StateResult_IsFailure_ShouldReturnTrueWhenNotSuccessful()
    {
        // Arrange
        var result = StateResult<string>.FromError("error");

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(result.Success);
    }

    [Fact]
    public void StateResult_IsFailure_ShouldReturnFalseWhenSuccessful()
    {
        // Arrange
        var result = StateResult<string>.FromSuccess("data");

        // Assert
        Assert.False(result.IsFailure);
        Assert.True(result.Success);
    }

    [Fact]
    public void StateResult_SuccessfulResult_ShouldHaveDataNotNull()
    {
        // Arrange
        var data = "test";
        var result = StateResult<string>.FromSuccess(data);

        // Assert
        Assert.NotNull(result.Data);
        Assert.Equal(data, result.Data);
    }

    [Fact]
    public void StateResult_FailedResult_ShouldHaveErrorNotNull()
    {
        // Arrange
        var error = "test error";
        var result = StateResult<string>.FromError(error);

        // Assert
        Assert.NotNull(result.Error);
        Assert.Equal(error, result.Error);
    }

    #endregion
}