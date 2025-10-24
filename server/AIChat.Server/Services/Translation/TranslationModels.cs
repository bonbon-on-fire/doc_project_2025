using System.ComponentModel.DataAnnotations;
using AIChat.Orleans.Contracts;

namespace AIChat.Server.Services.Translation;

/// <summary>
/// Context information for protocol translation operations.
/// Provides the necessary context data to perform accurate translations between different protocol formats.
/// </summary>
public sealed class TranslationContext
{
    /// <summary>
    /// User identifier associated with the translation request.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session identifier for WebSocket or other session-based protocols.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Connection identifier for SignalR or other connection-based protocols.
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Additional properties that may be needed for specific translations.
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = [];

    /// <summary>
    /// Timestamp when the translation context was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation identifier for tracking requests across services.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Source protocol that initiated the translation request.
    /// </summary>
    public string? SourceProtocol { get; set; }

    /// <summary>
    /// Target protocol for the translation result.
    /// </summary>
    public string? TargetProtocol { get; set; }

    /// <summary>
    /// Creates a new translation context with the specified user and correlation IDs.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="correlationId">Correlation identifier</param>
    /// <returns>New translation context instance</returns>
    public static TranslationContext Create(string? userId = null, string? correlationId = null)
    {
        return new TranslationContext
        {
            UserId = userId,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a translation context for SignalR operations.
    /// </summary>
    /// <param name="connectionId">SignalR connection identifier</param>
    /// <param name="userId">User identifier</param>
    /// <returns>SignalR-specific translation context</returns>
    public static TranslationContext ForSignalR(string connectionId, string? userId = null)
    {
        return new TranslationContext
        {
            ConnectionId = connectionId,
            UserId = userId,
            SourceProtocol = "SignalR",
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a translation context for WebSocket operations.
    /// </summary>
    /// <param name="sessionId">WebSocket session identifier</param>
    /// <param name="userId">User identifier</param>
    /// <returns>WebSocket-specific translation context</returns>
    public static TranslationContext ForWebSocket(string sessionId, string? userId = null)
    {
        return new TranslationContext
        {
            SessionId = sessionId,
            UserId = userId,
            SourceProtocol = "WebSocket",
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a translation context for REST API operations.
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <returns>REST-specific translation context</returns>
    public static TranslationContext ForRest(string? userId = null)
    {
        return new TranslationContext
        {
            UserId = userId,
            SourceProtocol = "REST",
            CorrelationId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Result of a protocol translation operation.
/// </summary>
/// <typeparam name="T">Type of the translated data</typeparam>
public sealed class TranslationResult<T>
{
    /// <summary>
    /// Indicates whether the translation operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The translated data if the operation was successful.
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Error message if the translation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for programmatic error handling.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Time taken to perform the translation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Additional metadata about the translation operation.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>
    /// Source type that was translated from.
    /// </summary>
    public Type? SourceType { get; set; }

    /// <summary>
    /// Target type that was translated to.
    /// </summary>
    public Type? TargetType { get; set; }
}

/// <summary>
/// Statistics and metrics for protocol translation operations.
/// </summary>
public sealed class TranslationMetrics
{
    /// <summary>
    /// Total number of translation operations performed.
    /// </summary>
    public long TotalTranslations { get; set; }

    /// <summary>
    /// Number of successful translations.
    /// </summary>
    public long SuccessfulTranslations { get; set; }

    /// <summary>
    /// Number of failed translations.
    /// </summary>
    public long FailedTranslations { get; set; }

    /// <summary>
    /// Average translation duration in milliseconds.
    /// </summary>
    public double AverageTranslationTimeMs { get; set; }

    /// <summary>
    /// Maximum translation duration recorded in milliseconds.
    /// </summary>
    public double MaxTranslationTimeMs { get; set; }

    /// <summary>
    /// Minimum translation duration recorded in milliseconds.
    /// </summary>
    public double MinTranslationTimeMs { get; set; }

    /// <summary>
    /// Translation success rate as a percentage (0-100).
    /// </summary>
    public double SuccessRate => TotalTranslations > 0 ? (SuccessfulTranslations * 100.0) / TotalTranslations : 0;

    /// <summary>
    /// Breakdown of translations by source protocol.
    /// </summary>
    public Dictionary<string, long> TranslationsBySourceProtocol { get; set; } = [];

    /// <summary>
    /// Breakdown of translations by target protocol.
    /// </summary>
    public Dictionary<string, long> TranslationsByTargetProtocol { get; set; } = [];

    /// <summary>
    /// Last reset time for metrics.
    /// </summary>
    public DateTime LastResetTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Resets all metrics to zero.
    /// </summary>
    public void Reset()
    {
        TotalTranslations = 0;
        SuccessfulTranslations = 0;
        FailedTranslations = 0;
        AverageTranslationTimeMs = 0;
        MaxTranslationTimeMs = 0;
        MinTranslationTimeMs = 0;
        TranslationsBySourceProtocol.Clear();
        TranslationsByTargetProtocol.Clear();
        LastResetTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Configuration options for the protocol translation service.
/// </summary>
public sealed class TranslationOptions
{
    /// <summary>
    /// Maximum time to wait for a translation operation to complete.
    /// </summary>
    [Range(100, 30000)]
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Whether to enable translation result caching.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache expiration time in minutes.
    /// </summary>
    [Range(1, 1440)]
    public int CacheExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum number of cached translation results.
    /// </summary>
    [Range(100, 10000)]
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Whether to enable detailed metrics collection.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Whether to enable batch translation support.
    /// </summary>
    public bool EnableBatchTranslation { get; set; } = true;

    /// <summary>
    /// Maximum number of items in a batch translation operation.
    /// </summary>
    [Range(1, 100)]
    public int MaxBatchSize { get; set; } = 10;

    /// <summary>
    /// Default options for development environments.
    /// </summary>
    public static TranslationOptions Development => new()
    {
        TimeoutMs = 10000,
        EnableCaching = false,
        EnableMetrics = true,
        EnableBatchTranslation = true,
        MaxBatchSize = 5
    };

    /// <summary>
    /// Default options for production environments.
    /// </summary>
    public static TranslationOptions Production => new()
    {
        TimeoutMs = 5000,
        EnableCaching = true,
        CacheExpirationMinutes = 15,
        MaxCacheSize = 5000,
        EnableMetrics = true,
        EnableBatchTranslation = true,
        MaxBatchSize = 10
    };
}

/// <summary>
/// Simple DTO representing the result of an Orleans operation for translation purposes.
/// This avoids tight coupling to Orleans internal types.
/// </summary>
public sealed class MessageResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The processed message.
    /// </summary>
    public ChatMessage? Message { get; set; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Operation timestamp.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional result metadata.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static MessageResult CreateSuccess(ChatMessage message)
        => new() { Success = true, Message = message };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static MessageResult CreateFailure(string errorMessage, string? errorCode = null)
        => new() { Success = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
}

/// <summary>
/// Non-generic factory class for creating TranslationResult instances.
/// This pattern eliminates CA1000 warnings about static members on generic types.
/// </summary>
public static class TranslationResult
{
    /// <summary>
    /// Creates a successful translation result with basic type information.
    /// </summary>
    /// <typeparam name="T">The type of the translated data</typeparam>
    /// <param name="data">The translated data</param>
    /// <param name="duration">Time taken for translation</param>
    /// <returns>Successful translation result</returns>
    public static TranslationResult<T> CreateSuccess<T>(T data, TimeSpan duration = default)
    {
        return new TranslationResult<T>
        {
            Success = true,
            Data = data,
            Duration = duration,
            SourceType = typeof(T),
            TargetType = typeof(T)
        };
    }

    /// <summary>
    /// Creates a failed translation result.
    /// </summary>
    /// <typeparam name="T">The type of the translated data</typeparam>
    /// <param name="errorMessage">Error description</param>
    /// <param name="errorCode">Error code</param>
    /// <param name="duration">Time taken for translation attempt</param>
    /// <returns>Failed translation result</returns>
    public static TranslationResult<T> Failure<T>(string errorMessage, string? errorCode = null, TimeSpan duration = default)
    {
        return new TranslationResult<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates a typed successful translation result with source and target type information.
    /// </summary>
    /// <typeparam name="TSource">Source type</typeparam>
    /// <typeparam name="TTarget">Target type</typeparam>
    /// <param name="data">The translated data</param>
    /// <param name="duration">Time taken for translation</param>
    /// <returns>Successful translation result with type information</returns>
    public static TranslationResult<TTarget> SuccessWithTypes<TSource, TTarget>(TTarget data, TimeSpan duration = default)
    {
        return new TranslationResult<TTarget>
        {
            Success = true,
            Data = data,
            Duration = duration,
            SourceType = typeof(TSource),
            TargetType = typeof(TTarget)
        };
    }
}