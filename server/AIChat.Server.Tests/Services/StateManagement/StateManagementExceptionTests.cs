using AIChat.Server.Services.StateManagement;
using Xunit;

namespace AIChat.Server.Tests.Services.StateManagement;

/// <summary>
/// Unit tests for StateManagementException.
/// Tests structured error creation and context information.
/// </summary>
public class StateManagementExceptionTests
{
    [Fact]
    public void Constructor_WithAllParameters_ShouldSetAllProperties()
    {
        // Arrange
        const string message = "Test error message";
        const StateErrorCode errorCode = StateErrorCode.ValidationError;
        const string entityType = "TestEntity";
        const string entityId = "test-123";
        const string operation = "Create";
        var context = new Dictionary<string, object> { ["key"] = "value" };
        const string correlationId = "correlation-123";
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new StateManagementException(
            message, errorCode, entityType, entityId, operation, context, correlationId, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(context, exception.Context);
        Assert.Equal(correlationId, exception.CorrelationId);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void Constructor_WithMinimalParameters_ShouldSetDefaults()
    {
        // Arrange
        const string message = "Test error message";

        // Act
        var exception = new StateManagementException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(StateErrorCode.InternalError, exception.ErrorCode);
        Assert.Null(exception.EntityType);
        Assert.Null(exception.EntityId);
        Assert.Null(exception.Operation);
        Assert.Null(exception.Context);
        Assert.NotNull(exception.CorrelationId); // Should be auto-generated
        Assert.True(Guid.TryParse(exception.CorrelationId, out _)); // Should be a valid GUID
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void NotFound_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "User";
        const string entityId = "user-123";
        const string operation = "GetById";
        const string correlationId = "correlation-456";

        // Act
        var exception = StateManagementException.NotFound(entityType, entityId, operation, correlationId);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains(entityId, exception.Message);
        Assert.Contains("was not found", exception.Message);
        Assert.Equal(StateErrorCode.NotFound, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);
    }

    [Fact]
    public void ValidationError_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "User";
        var validationErrors = new[] { "Name is required", "Email is invalid" };
        const string operation = "Create";
        const string entityId = "user-123";
        const string correlationId = "correlation-789";

        // Act
        var exception = StateManagementException.ValidationError(
            entityType, validationErrors, operation, entityId, correlationId);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains("Validation failed", exception.Message);
        Assert.Contains("Name is required", exception.Message);
        Assert.Contains("Email is invalid", exception.Message);
        Assert.Equal(StateErrorCode.ValidationError, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);

        Assert.NotNull(exception.Context);
        Assert.True(exception.Context.ContainsKey("ValidationErrors"));
        var contextErrors = (List<string>)exception.Context["ValidationErrors"];
        Assert.Equal(validationErrors, contextErrors);
    }

    [Fact]
    public void DuplicateKey_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "User";
        const string entityId = "user-123";
        const string operation = "Create";
        const string conflictingField = "Email";
        const string correlationId = "correlation-abc";

        // Act
        var exception = StateManagementException.DuplicateKey(
            entityType, entityId, operation, conflictingField, correlationId);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains(conflictingField, exception.Message);
        Assert.Contains(entityId, exception.Message);
        Assert.Contains("already exists", exception.Message);
        Assert.Equal(StateErrorCode.DuplicateKey, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);

        Assert.NotNull(exception.Context);
        Assert.Equal(conflictingField, exception.Context["ConflictingField"]);
    }

    [Fact]
    public void DuplicateKey_WithoutConflictingField_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "User";
        const string entityId = "user-123";
        const string operation = "Create";

        // Act
        var exception = StateManagementException.DuplicateKey(entityType, entityId, operation);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains(entityId, exception.Message);
        Assert.Contains("already exists", exception.Message);
        Assert.DoesNotContain("Email", exception.Message); // Should not contain specific field
        Assert.Null(exception.Context); // No conflicting field context
    }

    [Fact]
    public void ConcurrencyConflict_WithVersions_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "Document";
        const string entityId = "doc-123";
        const string operation = "Update";
        const string expectedVersion = "v1.0";
        const string actualVersion = "v1.1";
        const string correlationId = "correlation-def";

        // Act
        var exception = StateManagementException.ConcurrencyConflict(
            entityType, entityId, operation, expectedVersion, actualVersion, correlationId);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains(entityId, exception.Message);
        Assert.Contains("Concurrency conflict", exception.Message);
        Assert.Contains(expectedVersion, exception.Message);
        Assert.Contains(actualVersion, exception.Message);
        Assert.Equal(StateErrorCode.ConcurrencyConflict, exception.ErrorCode);

        Assert.NotNull(exception.Context);
        Assert.Equal(expectedVersion, exception.Context["ExpectedVersion"]);
        Assert.Equal(actualVersion, exception.Context["ActualVersion"]);
    }

    [Fact]
    public void ConcurrencyConflict_WithoutVersions_ShouldCreateCorrectException()
    {
        // Arrange
        const string entityType = "Document";
        const string entityId = "doc-123";
        const string operation = "Update";

        // Act
        var exception = StateManagementException.ConcurrencyConflict(entityType, entityId, operation);

        // Assert
        Assert.Contains(entityType, exception.Message);
        Assert.Contains(entityId, exception.Message);
        Assert.Contains("Concurrency conflict", exception.Message);
        Assert.Contains("modified by another operation", exception.Message);
        Assert.Equal(StateErrorCode.ConcurrencyConflict, exception.ErrorCode);
    }

    [Fact]
    public void NetworkError_ShouldCreateCorrectException()
    {
        // Arrange
        const string operation = "GetFromDatabase";
        var networkError = new TimeoutException("Connection timeout");
        const string entityType = "User";
        const string entityId = "user-123";
        const string correlationId = "correlation-ghi";

        // Act
        var exception = StateManagementException.NetworkError(
            operation, networkError, entityType, entityId, correlationId);

        // Assert
        Assert.Contains(operation, exception.Message);
        Assert.Contains("Network error", exception.Message);
        Assert.Contains(networkError.Message, exception.Message);
        Assert.Equal(StateErrorCode.NetworkError, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);
        Assert.Equal(networkError, exception.InnerException);
    }

    [Fact]
    public void TimeoutError_ShouldCreateCorrectException()
    {
        // Arrange
        const string operation = "DatabaseQuery";
        const int timeoutMs = 5000;
        const string entityType = "Chat";
        const string entityId = "chat-123";
        const string correlationId = "correlation-jkl";

        // Act
        var exception = StateManagementException.TimeoutError(
            operation, timeoutMs, entityType, entityId, correlationId);

        // Assert
        Assert.Contains(operation, exception.Message);
        Assert.Contains("timed out", exception.Message);
        Assert.Contains(timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message);
        Assert.Equal(StateErrorCode.TimeoutError, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);

        Assert.NotNull(exception.Context);
        Assert.Equal(timeoutMs, exception.Context["TimeoutMs"]);
    }

    [Fact]
    public void AccessDenied_ShouldCreateCorrectException()
    {
        // Arrange
        const string operation = "DeleteChat";
        const string entityType = "Chat";
        const string entityId = "chat-123";
        const string reason = "User does not own this chat";
        const string correlationId = "correlation-mno";

        // Act
        var exception = StateManagementException.AccessDenied(
            operation, entityType, entityId, reason, correlationId);

        // Assert
        Assert.Contains(operation, exception.Message);
        Assert.Contains(entityType, exception.Message);
        Assert.Contains("Access denied", exception.Message);
        Assert.Contains(reason, exception.Message);
        Assert.Equal(StateErrorCode.AccessDenied, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);

        Assert.NotNull(exception.Context);
        Assert.Equal(reason, exception.Context["Reason"]);
    }

    [Fact]
    public void ServiceUnavailable_ShouldCreateCorrectException()
    {
        // Arrange
        const string operation = "StoreInDatabase";
        const string serviceName = "SQLiteDatabase";
        const string entityType = "Message";
        const string entityId = "msg-123";
        const string correlationId = "correlation-pqr";

        // Act
        var exception = StateManagementException.ServiceUnavailable(
            operation, serviceName, entityType, entityId, correlationId);

        // Assert
        Assert.Contains(operation, exception.Message);
        Assert.Contains(serviceName, exception.Message);
        Assert.Contains("is unavailable", exception.Message);
        Assert.Equal(StateErrorCode.ServiceUnavailable, exception.ErrorCode);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityId);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(correlationId, exception.CorrelationId);

        Assert.NotNull(exception.Context);
        Assert.Equal(serviceName, exception.Context["ServiceName"]);
    }

    [Fact]
    public void ToString_ShouldIncludeAllContextInformation()
    {
        // Arrange
        const string message = "Test error";
        const StateErrorCode errorCode = StateErrorCode.ValidationError;
        const string entityType = "User";
        const string entityId = "user-123";
        const string operation = "Create";
        var context = new Dictionary<string, object> { ["field"] = "email", ["value"] = "invalid" };
        const string correlationId = "correlation-stu";

        var exception = new StateManagementException(
            message, errorCode, entityType, entityId, operation, context, correlationId);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains(message, result);
        Assert.Contains(correlationId, result);
        Assert.Contains(errorCode.ToString(), result);
        Assert.Contains(entityType, result);
        Assert.Contains(entityId, result);
        Assert.Contains(operation, result);
        Assert.Contains("field: email", result);
        Assert.Contains("value: invalid", result);
    }

    [Fact]
    public void ToString_WithMinimalInfo_ShouldNotContainNullValues()
    {
        // Arrange
        var exception = new StateManagementException("Simple error");

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains("Simple error", result);
        Assert.Contains("CorrelationId:", result);
        Assert.Contains("ErrorCode:", result);

        // Should not contain null-related entries
        Assert.DoesNotContain("EntityType: ", result);
        Assert.DoesNotContain("EntityId: ", result);
        Assert.DoesNotContain("Operation: ", result);
        Assert.DoesNotContain("Context: ", result);
    }
}