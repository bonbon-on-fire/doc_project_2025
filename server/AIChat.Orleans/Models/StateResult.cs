namespace AIChat.Orleans.Models;

/// <summary>
/// Represents the result of a state operation without data.
/// This provides a consistent pattern for grain operations that might succeed or fail.
/// </summary>
public class StateResult
{
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool Success { get; protected init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; protected init; }

    /// <summary>
    /// Initializes a new instance of the StateResult class.
    /// </summary>
    /// <param name="success">Whether the operation was successful</param>
    /// <param name="error">Error message if operation failed</param>
    protected StateResult(bool success, string? error = null)
    {
        Success = success;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful StateResult</returns>
    public static StateResult FromSuccess() => new(true);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <returns>A failed StateResult</returns>
    public static StateResult FromError(string error) => new(false, error);

    /// <summary>
    /// Implicitly converts a StateResult to a boolean indicating success.
    /// </summary>
    /// <param name="result">The StateResult to convert</param>
    public static implicit operator bool(StateResult result) => result.Success;
}

/// <summary>
/// Represents the result of a state operation with data.
/// This provides a consistent pattern for grain operations that return data and might succeed or fail.
/// </summary>
/// <typeparam name="T">The type of data returned on success</typeparam>
public class StateResult<T> : StateResult
{
    /// <summary>
    /// Gets the data returned by the operation if successful.
    /// </summary>
    public T? Data { get; private init; }

    /// <summary>
    /// Initializes a new instance of the StateResult{T} class.
    /// </summary>
    /// <param name="success">Whether the operation was successful</param>
    /// <param name="data">The data returned on success</param>
    /// <param name="error">Error message if operation failed</param>
    protected StateResult(bool success, T? data = default, string? error = null)
        : base(success, error)
    {
        Data = data;
    }

    /// <summary>
    /// Creates a successful result with data.
    /// </summary>
    /// <param name="data">The data to return</param>
    /// <returns>A successful StateResult with data</returns>
    public static StateResult<T> FromSuccess(T data) => new(true, data);

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    /// <param name="error">The error message</param>
    /// <returns>A failed StateResult</returns>
    public static new StateResult<T> FromError(string error) => new(false, default, error);

    /// <summary>
    /// Implicitly converts a StateResult{T} to its data type.
    /// Throws an InvalidOperationException if the operation was not successful.
    /// </summary>
    /// <param name="result">The StateResult to convert</param>
    /// <exception cref="InvalidOperationException">Thrown when accessing data from a failed result</exception>
    public static implicit operator T?(StateResult<T> result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException($"Cannot access data from failed operation: {result.Error}");
        }
        return result.Data;
    }
}