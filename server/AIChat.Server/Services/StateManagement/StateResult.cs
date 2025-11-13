using System.Diagnostics.CodeAnalysis;

namespace AIChat.Server.Services.StateManagement;

/// <summary>
/// Represents the result of a state management operation that returns data.
/// </summary>
/// <typeparam name="T">The type of data returned by the operation</typeparam>
public record StateResult<T>
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the data returned by the operation if successful.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the specific error code for categorization.
    /// </summary>
    public StateErrorCode ErrorCode { get; init; } = StateErrorCode.None;

    /// <summary>
    /// Gets additional metadata about the operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(false, nameof(Data))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !Success;

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The data to return</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful state result</returns>
    public static StateResult<T> FromSuccess(T data, Dictionary<string, object>? metadata = null)
    {
        return new StateResult<T>
        {
            Success = true,
            Data = data,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result with error information.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state result</returns>
    public static StateResult<T> FromError(string error, StateErrorCode errorCode = StateErrorCode.InternalError, Dictionary<string, object>? metadata = null)
    {
        return new StateResult<T>
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state result</returns>
    public static StateResult<T> FromException(Exception exception, StateErrorCode errorCode = StateErrorCode.InternalError, Dictionary<string, object>? metadata = null)
    {
        return new StateResult<T>
        {
            Success = false,
            Error = exception.Message,
            ErrorCode = errorCode,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the result of a state management operation that does not return data.
/// </summary>
public record StateResult
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the specific error code for categorization.
    /// </summary>
    public StateErrorCode ErrorCode { get; init; } = StateErrorCode.None;

    /// <summary>
    /// Gets additional metadata about the operation.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !Success;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A successful state result</returns>
    public static StateResult FromSuccess(Dictionary<string, object>? metadata = null)
    {
        return new StateResult
        {
            Success = true,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result with error information.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state result</returns>
    public static StateResult FromError(string error, StateErrorCode errorCode = StateErrorCode.InternalError, Dictionary<string, object>? metadata = null)
    {
        return new StateResult
        {
            Success = false,
            Error = error,
            ErrorCode = errorCode,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result from an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="errorCode">The error code</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A failed state result</returns>
    public static StateResult FromException(Exception exception, StateErrorCode errorCode = StateErrorCode.InternalError, Dictionary<string, object>? metadata = null)
    {
        return new StateResult
        {
            Success = false,
            Error = exception.Message,
            ErrorCode = errorCode,
            Metadata = metadata
        };
    }
}

/// <summary>
/// Represents the specific error codes for state management operations.
/// </summary>
public enum StateErrorCode
{
    /// <summary>
    /// No error occurred.
    /// </summary>
    None = 0,

    /// <summary>
    /// The requested entity was not found.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The provided data failed validation.
    /// </summary>
    ValidationError = 2,

    /// <summary>
    /// A duplicate key constraint was violated.
    /// </summary>
    DuplicateKey = 3,

    /// <summary>
    /// A concurrency conflict occurred during update.
    /// </summary>
    ConcurrencyConflict = 4,

    /// <summary>
    /// An internal error occurred.
    /// </summary>
    InternalError = 5,

    /// <summary>
    /// A network error occurred.
    /// </summary>
    NetworkError = 6,

    /// <summary>
    /// The operation timed out.
    /// </summary>
    TimeoutError = 7,

    /// <summary>
    /// Access was denied to the resource.
    /// </summary>
    AccessDenied = 8,

    /// <summary>
    /// The service is temporarily unavailable.
    /// </summary>
    ServiceUnavailable = 9
}
