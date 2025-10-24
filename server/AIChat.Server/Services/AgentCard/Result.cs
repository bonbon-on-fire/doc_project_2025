namespace AIChat.Server.Services.AgentCards;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T? value, string? error, bool isSuccess)
    {
        _value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>
    /// Gets whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value. Throws if the result is a failure.
    /// </summary>
    public T Value =>
        !IsSuccess
            ? throw new InvalidOperationException(
                $"Cannot access Value on a failed result. Error: {Error}"
            )
            : _value!;

    /// <summary>
    /// Gets the error message. Returns null if the result is a success.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types - This is a common pattern for Result types
    public static Result<T> Success(T value)
    {
        return EqualityComparer<T?>.Default.Equals(value, default(T?))
            ? throw new ArgumentNullException(nameof(value))
            : new Result<T>(value, null, true);
    }

    /// <summary>
    /// Creates a failed result with the given error message.
    /// </summary>
    public static Result<T> Failure(string error)
#pragma warning restore CA1000
    {
        return string.IsNullOrWhiteSpace(error)
            ? throw new ArgumentException("Error message cannot be empty", nameof(error))
            : new Result<T>(default, error, false);
    }

    /// <summary>
    /// Maps the success value to a new type using the provided function.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (mapper == null)
        {
            throw new ArgumentNullException(nameof(mapper));
        }

        if (IsSuccess)
        {
            return Result<TNew>.Success(mapper(_value!));
        }
        else
        {
            return Result<TNew>.Failure(Error!);
        }
    }

    /// <summary>
    /// Chains another result-producing operation if this result is successful.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> mapper)
    {
        if (mapper == null)
        {
            throw new ArgumentNullException(nameof(mapper));
        }

        if (IsSuccess)
        {
            return mapper(_value!);
        }
        else
        {
            return Result<TNew>.Failure(Error!);
        }
    }

    /// <summary>
    /// Provides a fallback value if the result is a failure.
    /// </summary>
    public T GetValueOrDefault(T defaultValue)
    {
        return IsSuccess ? _value! : defaultValue;
    }

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess && action != null)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (!IsSuccess && action != null)
        {
            action(Error!);
        }

        return this;
    }
}

/// <summary>
/// Non-generic Result for operations that don't return a value.
/// </summary>
public readonly struct Result
{
    private Result(string? error, bool isSuccess)
    {
        Error = error;
        IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Success()
    {
        return new Result(null, true);
    }

    public static Result Failure(string error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? throw new ArgumentException("Error message cannot be empty", nameof(error))
            : new Result(error, false);
    }
}
