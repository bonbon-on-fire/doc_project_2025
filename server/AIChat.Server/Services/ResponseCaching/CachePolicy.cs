namespace AIChat.Server.Services.ResponseCaching;

/// <summary>
/// Defines caching behavior and policy for response caching operations.
/// Provides comprehensive configuration for TTL, invalidation, and performance characteristics.
/// </summary>
public record CachePolicy
{
    /// <summary>
    /// Gets the cache operation type that determines default TTL and behavior.
    /// </summary>
    public required CacheOperationType OperationType { get; init; }

    /// <summary>
    /// Gets the absolute expiration time for the cache entry.
    /// If set, the entry will expire at this exact time regardless of access patterns.
    /// </summary>
    public TimeSpan? AbsoluteExpiration { get; init; }

    /// <summary>
    /// Gets the sliding expiration time for the cache entry.
    /// If set, the entry will expire after this duration of inactivity.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>
    /// Gets whether automatic invalidation triggers should be enabled.
    /// When true, related operations may trigger cache invalidation.
    /// </summary>
    public bool EnableInvalidationTriggers { get; init; } = true;

    /// <summary>
    /// Gets the cache priority for memory pressure scenarios.
    /// Higher priority entries are less likely to be evicted.
    /// </summary>
    public CachePriority Priority { get; init; } = CachePriority.Normal;

    /// <summary>
    /// Gets whether this cache entry should be included in cache warming operations.
    /// </summary>
    public bool IsWarmupCandidate { get; init; }

    /// <summary>
    /// Gets the maximum size in bytes for this cache entry.
    /// If the serialized entry exceeds this size, it will not be cached.
    /// </summary>
    public int? MaxSizeBytes { get; init; }

    /// <summary>
    /// Gets custom invalidation patterns that should trigger cache invalidation.
    /// </summary>
    public IReadOnlyList<string> InvalidationPatterns { get; init; } = [];

    /// <summary>
    /// Gets additional metadata about this cache policy.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Creates a cache policy for frequently accessed read operations (5 minutes).
    /// Suitable for user preferences, mode configurations, and similar data.
    /// </summary>
    /// <param name="invalidationPatterns">Optional patterns that trigger invalidation</param>
    /// <returns>Cache policy configured for frequent read operations</returns>
    public static CachePolicy CreateReadFrequent(params string[] invalidationPatterns)
    {
        return new CachePolicy
        {
            OperationType = CacheOperationType.ReadFrequent,
            SlidingExpiration = TimeSpan.FromMinutes(5),
            Priority = CachePriority.High,
            IsWarmupCandidate = true,
            InvalidationPatterns = invalidationPatterns
        };
    }

    /// <summary>
    /// Creates a cache policy for moderately accessed read operations (15 minutes).
    /// Suitable for chat history, monitoring data, and similar content.
    /// </summary>
    /// <param name="invalidationPatterns">Optional patterns that trigger invalidation</param>
    /// <returns>Cache policy configured for moderate read operations</returns>
    public static CachePolicy CreateReadModerate(params string[] invalidationPatterns)
    {
        return new CachePolicy
        {
            OperationType = CacheOperationType.ReadModerate,
            SlidingExpiration = TimeSpan.FromMinutes(15),
            Priority = CachePriority.Normal,
            InvalidationPatterns = invalidationPatterns
        };
    }

    /// <summary>
    /// Creates a cache policy for infrequently accessed read operations (1 hour).
    /// Suitable for system logs, archived data, and historical information.
    /// </summary>
    /// <param name="invalidationPatterns">Optional patterns that trigger invalidation</param>
    /// <returns>Cache policy configured for infrequent read operations</returns>
    public static CachePolicy CreateReadInfrequent(params string[] invalidationPatterns)
    {
        return new CachePolicy
        {
            OperationType = CacheOperationType.ReadInfrequent,
            SlidingExpiration = TimeSpan.FromHours(1),
            Priority = CachePriority.Low,
            InvalidationPatterns = invalidationPatterns
        };
    }

    /// <summary>
    /// Creates a cache policy for real-time read operations (30 seconds).
    /// Suitable for live metrics, active sessions, and real-time data.
    /// </summary>
    /// <param name="invalidationPatterns">Optional patterns that trigger invalidation</param>
    /// <returns>Cache policy configured for real-time read operations</returns>
    public static CachePolicy CreateReadRealTime(params string[] invalidationPatterns)
    {
        return new CachePolicy
        {
            OperationType = CacheOperationType.ReadRealTime,
            SlidingExpiration = TimeSpan.FromSeconds(30),
            Priority = CachePriority.High,
            EnableInvalidationTriggers = true,
            InvalidationPatterns = invalidationPatterns
        };
    }

    /// <summary>
    /// Creates a cache policy for write-through operations (no caching).
    /// Used for create, update, and delete operations that should not be cached.
    /// </summary>
    /// <returns>Cache policy configured for write-through operations</returns>
    public static CachePolicy CreateWriteThrough()
    {
        return new CachePolicy
        {
            OperationType = CacheOperationType.WriteThrough,
            EnableInvalidationTriggers = false,
            Priority = CachePriority.None
        };
    }

    /// <summary>
    /// Creates a custom cache policy with specific expiration settings.
    /// </summary>
    /// <param name="operationType">The cache operation type</param>
    /// <param name="absoluteExpiration">Optional absolute expiration</param>
    /// <param name="slidingExpiration">Optional sliding expiration</param>
    /// <param name="priority">Cache priority level</param>
    /// <param name="invalidationPatterns">Optional invalidation patterns</param>
    /// <returns>Custom cache policy with specified settings</returns>
    public static CachePolicy CreateCustom(
        CacheOperationType operationType,
        TimeSpan? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        CachePriority priority = CachePriority.Normal,
        params string[] invalidationPatterns)
    {
        return new CachePolicy
        {
            OperationType = operationType,
            AbsoluteExpiration = absoluteExpiration,
            SlidingExpiration = slidingExpiration,
            Priority = priority,
            InvalidationPatterns = invalidationPatterns
        };
    }
}

/// <summary>
/// Enumeration of cache operation types that determine default caching behavior.
/// </summary>
public enum CacheOperationType
{
    /// <summary>
    /// Frequently accessed read operations (5 minutes TTL).
    /// Used for user preferences, mode configurations, and similar data.
    /// </summary>
    ReadFrequent = 0,

    /// <summary>
    /// Moderately accessed read operations (15 minutes TTL).
    /// Used for chat history, monitoring data, and similar content.
    /// </summary>
    ReadModerate = 1,

    /// <summary>
    /// Infrequently accessed read operations (1 hour TTL).
    /// Used for system logs, archived data, and historical information.
    /// </summary>
    ReadInfrequent = 2,

    /// <summary>
    /// Real-time read operations (30 seconds TTL).
    /// Used for live metrics, active sessions, and real-time data.
    /// </summary>
    ReadRealTime = 3,

    /// <summary>
    /// Write-through operations (no caching).
    /// Used for create, update, and delete operations that should not be cached.
    /// </summary>
    WriteThrough = 4
}

/// <summary>
/// Enumeration of cache priority levels for memory pressure scenarios.
/// </summary>
public enum CachePriority
{
    /// <summary>
    /// No caching priority - entries with this priority are not cached.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low priority - first to be evicted under memory pressure.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Normal priority - default caching priority level.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// High priority - last to be evicted under memory pressure.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical priority - never evicted under memory pressure.
    /// </summary>
    Critical = 4
}

/// <summary>
/// Provides cache policies for specific router operations.
/// This class encapsulates the business logic for determining appropriate cache policies.
/// </summary>
public static class RouterCachePolicies
{
    /// <summary>
    /// Gets cache policies for Mode router operations.
    /// </summary>
    public static class Mode
    {
        /// <summary>
        /// Cache policy for GetModes operation - frequently accessed, 5 minute TTL.
        /// </summary>
        public static CachePolicy GetModes => CachePolicy.CreateReadFrequent("Mode:*:*:*:*");

        /// <summary>
        /// Cache policy for GetMode operation - frequently accessed, 5 minute TTL.
        /// </summary>
        public static CachePolicy GetMode => CachePolicy.CreateReadFrequent("Mode:GetMode:*:*:*");

        /// <summary>
        /// Cache policy for CreateMode operation - write-through, no caching.
        /// </summary>
        public static CachePolicy CreateMode => CachePolicy.CreateWriteThrough();

        /// <summary>
        /// Cache policy for UpdateMode operation - write-through, no caching.
        /// </summary>
        public static CachePolicy UpdateMode => CachePolicy.CreateWriteThrough();

        /// <summary>
        /// Cache policy for DeleteMode operation - write-through, no caching.
        /// </summary>
        public static CachePolicy DeleteMode => CachePolicy.CreateWriteThrough();
    }

    /// <summary>
    /// Gets cache policies for Chat router operations.
    /// </summary>
    public static class Chat
    {
        /// <summary>
        /// Cache policy for GetChatHistory operation - moderate access, 15 minute TTL.
        /// </summary>
        public static CachePolicy GetChatHistory => CachePolicy.CreateReadModerate("Chat:GetChatHistory:*:*:*");

        /// <summary>
        /// Cache policy for GetChat operation - moderate access, 15 minute TTL.
        /// </summary>
        public static CachePolicy GetChat => CachePolicy.CreateReadModerate("Chat:GetChat:*:*:*");

        /// <summary>
        /// Cache policy for CreateChat operation - write-through, no caching.
        /// </summary>
        public static CachePolicy CreateChat => CachePolicy.CreateWriteThrough();

        /// <summary>
        /// Cache policy for SendMessage operation - write-through, no caching.
        /// </summary>
        public static CachePolicy SendMessage => CachePolicy.CreateWriteThrough();
    }

    /// <summary>
    /// Gets cache policies for Monitoring router operations.
    /// </summary>
    public static class Monitoring
    {
        /// <summary>
        /// Cache policy for GetMetrics operation - real-time data, 30 second TTL.
        /// </summary>
        public static CachePolicy GetMetrics => CachePolicy.CreateReadRealTime("Monitoring:GetMetrics:*:*:*");

        /// <summary>
        /// Cache policy for GetSystemHealth operation - moderate access, 15 minute TTL.
        /// </summary>
        public static CachePolicy GetSystemHealth => CachePolicy.CreateReadModerate("Monitoring:GetSystemHealth:*:*:*");

        /// <summary>
        /// Cache policy for GetCapacityMetrics operation - moderate access, 15 minute TTL.
        /// </summary>
        public static CachePolicy GetCapacityMetrics => CachePolicy.CreateReadModerate("Monitoring:GetCapacityMetrics:*:*:*");
    }

    /// <summary>
    /// Gets cache policies for Logs router operations.
    /// </summary>
    public static class Logs
    {
        /// <summary>
        /// Cache policy for GetLogs operation - infrequent access, 1 hour TTL.
        /// </summary>
        public static CachePolicy GetLogs => CachePolicy.CreateReadInfrequent("Logs:GetLogs:*:*:*");

        /// <summary>
        /// Cache policy for SearchLogs operation - infrequent access, 1 hour TTL.
        /// </summary>
        public static CachePolicy SearchLogs => CachePolicy.CreateReadInfrequent("Logs:SearchLogs:*:*:*");

        /// <summary>
        /// Cache policy for ExportLogs operation - infrequent access, 1 hour TTL.
        /// </summary>
        public static CachePolicy ExportLogs => CachePolicy.CreateReadInfrequent("Logs:ExportLogs:*:*:*");
    }

    /// <summary>
    /// Gets the appropriate cache policy for a router operation.
    /// </summary>
    /// <param name="routerType">The router type (e.g., "Mode", "Chat")</param>
    /// <param name="operationName">The operation name (e.g., "GetModes", "CreateChat")</param>
    /// <returns>The appropriate cache policy, or null if no caching should be used</returns>
    public static CachePolicy? GetPolicy(string routerType, string operationName)
    {
        return routerType.ToLowerInvariant() switch
        {
            "mode" => operationName.ToLowerInvariant() switch
            {
                "getmodes" => Mode.GetModes,
                "getmode" => Mode.GetMode,
                "createmode" => Mode.CreateMode,
                "updatemode" => Mode.UpdateMode,
                "deletemode" => Mode.DeleteMode,
                _ => null
            },
            "chat" => operationName.ToLowerInvariant() switch
            {
                "getchathistory" => Chat.GetChatHistory,
                "getchat" => Chat.GetChat,
                "createchat" => Chat.CreateChat,
                "sendmessage" => Chat.SendMessage,
                _ => null
            },
            "monitoring" => operationName.ToLowerInvariant() switch
            {
                "getmetrics" => Monitoring.GetMetrics,
                "getsystemhealth" => Monitoring.GetSystemHealth,
                "getcapacitymetrics" => Monitoring.GetCapacityMetrics,
                _ => null
            },
            "logs" => operationName.ToLowerInvariant() switch
            {
                "getlogs" => Logs.GetLogs,
                "searchlogs" => Logs.SearchLogs,
                "exportlogs" => Logs.ExportLogs,
                _ => null
            },
            _ => null
        };
    }
}
