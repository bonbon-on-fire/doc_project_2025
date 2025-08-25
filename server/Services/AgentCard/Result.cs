using System;

namespace AIChat.Server.Services.AgentCards
{
    /// <summary>
    /// Represents the result of an operation that can either succeed with a value or fail with an error.
    /// </summary>
    /// <typeparam name="T">The type of the success value</typeparam>
    public readonly struct Result<T>
    {
        private readonly T? _value;
        private readonly string? _error;
        private readonly bool _isSuccess;

        private Result(T? value, string? error, bool isSuccess)
        {
            _value = value;
            _error = error;
            _isSuccess = isSuccess;
        }

        /// <summary>
        /// Gets whether the operation succeeded.
        /// </summary>
        public bool IsSuccess => _isSuccess;

        /// <summary>
        /// Gets whether the operation failed.
        /// </summary>
        public bool IsFailure => !_isSuccess;

        /// <summary>
        /// Gets the success value. Throws if the result is a failure.
        /// </summary>
        public T Value
        {
            get
            {
                if (!_isSuccess)
                    throw new InvalidOperationException($"Cannot access Value on a failed result. Error: {_error}");
                return _value!;
            }
        }

        /// <summary>
        /// Gets the error message. Returns null if the result is a success.
        /// </summary>
        public string? Error => _error;

        /// <summary>
        /// Creates a successful result with the given value.
        /// </summary>
        public static Result<T> Success(T value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return new Result<T>(value, null, true);
        }

        /// <summary>
        /// Creates a failed result with the given error message.
        /// </summary>
        public static Result<T> Failure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message cannot be empty", nameof(error));
            return new Result<T>(default, error, false);
        }

        /// <summary>
        /// Maps the success value to a new type using the provided function.
        /// </summary>
        public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));
            
            return _isSuccess 
                ? Result<TNew>.Success(mapper(_value!)) 
                : Result<TNew>.Failure(_error!);
        }

        /// <summary>
        /// Chains another result-producing operation if this result is successful.
        /// </summary>
        public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> mapper)
        {
            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));
            
            return _isSuccess 
                ? mapper(_value!) 
                : Result<TNew>.Failure(_error!);
        }

        /// <summary>
        /// Provides a fallback value if the result is a failure.
        /// </summary>
        public T GetValueOrDefault(T defaultValue)
        {
            return _isSuccess ? _value! : defaultValue;
        }

        /// <summary>
        /// Executes an action if the result is successful.
        /// </summary>
        public Result<T> OnSuccess(Action<T> action)
        {
            if (_isSuccess && action != null)
                action(_value!);
            return this;
        }

        /// <summary>
        /// Executes an action if the result is a failure.
        /// </summary>
        public Result<T> OnFailure(Action<string> action)
        {
            if (!_isSuccess && action != null)
                action(_error!);
            return this;
        }
    }

    /// <summary>
    /// Non-generic Result for operations that don't return a value.
    /// </summary>
    public readonly struct Result
    {
        private readonly string? _error;
        private readonly bool _isSuccess;

        private Result(string? error, bool isSuccess)
        {
            _error = error;
            _isSuccess = isSuccess;
        }

        public bool IsSuccess => _isSuccess;
        public bool IsFailure => !_isSuccess;
        public string? Error => _error;

        public static Result Success() => new Result(null, true);
        public static Result Failure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                throw new ArgumentException("Error message cannot be empty", nameof(error));
            return new Result(error, false);
        }
    }
}