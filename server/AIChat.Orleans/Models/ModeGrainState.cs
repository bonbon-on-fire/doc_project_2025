using Orleans;

namespace AIChat.Orleans.Contracts;

/// <summary>
/// Persistent state for the ModeGrain.
/// Contains all mode-specific data that needs to survive grain deactivation/reactivation.
/// Implements comprehensive state management for mode configuration, transitions, and history.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeGrainState")]
public sealed class ModeGrainState
{
    /// <summary>
    /// Current mode state containing configuration and metadata.
    /// Null if the mode has not been initialized.
    /// </summary>
    [Id(0)]
    public ModeState? CurrentState { get; set; }

    /// <summary>
    /// Complete change history for this mode.
    /// Ordered chronologically with most recent changes last.
    /// Limited to configurable maximum number of entries (default 1000).
    /// </summary>
    [Id(1)]
    public List<ModeChangeEvent> ChangeHistory { get; set; } = [];

    /// <summary>
    /// Complete transition history for this mode.
    /// Tracks all mode transitions including successful and failed attempts.
    /// Limited to configurable maximum number of entries (default 500).
    /// </summary>
    [Id(2)]
    public List<ModeTransition> TransitionHistory { get; set; } = [];

    /// <summary>
    /// Scheduled transitions that are pending execution.
    /// Key: ScheduleId, Value: Scheduled transition request.
    /// </summary>
    [Id(3)]
    public Dictionary<string, ScheduledTransitionRequest> ScheduledTransitions { get; set; } = [];

    /// <summary>
    /// Cached configuration data for performance optimization.
    /// Key: Cache key (e.g., "effective_config_hash"), Value: Cached data.
    /// </summary>
    [Id(4)]
    public Dictionary<string, CachedItem> ConfigurationCache { get; set; } = [];

    /// <summary>
    /// Cached generated prompts for performance optimization.
    /// Key: Configuration hash, Value: Generated prompt with metadata.
    /// </summary>
    [Id(5)]
    public Dictionary<string, CachedPrompt> PromptCache { get; set; } = [];

    /// <summary>
    /// Cached validation results to avoid recomputation.
    /// Key: Validation context hash, Value: Validation result with expiry.
    /// </summary>
    [Id(6)]
    public Dictionary<string, CachedValidationResult> ValidationCache { get; set; } = [];

    /// <summary>
    /// Grain-level metadata and operational information.
    /// Used for monitoring, debugging, and performance tracking.
    /// </summary>
    [Id(7)]
    public ModeGrainMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Timestamp when this grain state was last modified.
    /// Used for optimistic concurrency control and cache invalidation.
    /// </summary>
    [Id(8)]
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// Incremented on every state modification.
    /// </summary>
    [Id(9)]
    public int Version { get; set; }

    /// <summary>
    /// Flag indicating whether this mode grain has been initialized.
    /// Used to enforce proper initialization flow.
    /// </summary>
    [Id(10)]
    public bool IsInitialized { get; set; }

    /// <summary>
    /// Timestamp when this grain was first activated.
    /// Used for grain lifecycle tracking and metrics.
    /// </summary>
    [Id(11)]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Performance metrics for this specific mode grain instance.
    /// Used for monitoring and optimization analysis.
    /// </summary>
    [Id(12)]
    public ModeGrainPerformanceMetrics Performance { get; set; } = new();
}

/// <summary>
/// Represents a cached item with expiration and metadata.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.CachedItem")]
public sealed class CachedItem
{
    /// <summary>
    /// The cached data (JSON serialized).
    /// </summary>
    [Id(0)]
    public required string Data { get; init; }

    /// <summary>
    /// Type of the cached data for deserialization.
    /// </summary>
    [Id(1)]
    public required string DataType { get; init; }

    /// <summary>
    /// When this item was cached.
    /// </summary>
    [Id(2)]
    public required DateTime CachedAtUtc { get; init; }

    /// <summary>
    /// When this item expires (null for no expiration).
    /// </summary>
    [Id(3)]
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Hash of the source data for cache invalidation.
    /// </summary>
    [Id(4)]
    public string? SourceHash { get; init; }

    /// <summary>
    /// Number of times this cached item has been accessed.
    /// </summary>
    [Id(5)]
    public int HitCount { get; set; }

    /// <summary>
    /// Last time this cached item was accessed.
    /// </summary>
    [Id(6)]
    public DateTime? LastAccessedUtc { get; set; }
}

/// <summary>
/// Represents a cached generated prompt with metadata.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.CachedPrompt")]
public sealed class CachedPrompt
{
    /// <summary>
    /// The generated prompt text.
    /// </summary>
    [Id(0)]
    public required string PromptText { get; init; }

    /// <summary>
    /// Configuration hash that generated this prompt.
    /// </summary>
    [Id(1)]
    public required string ConfigurationHash { get; init; }

    /// <summary>
    /// When this prompt was generated.
    /// </summary>
    [Id(2)]
    public required DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// When this cached prompt expires.
    /// </summary>
    [Id(3)]
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// Parameters used for prompt generation.
    /// </summary>
    [Id(4)]
    public Dictionary<string, string> GenerationParameters { get; init; } = [];

    /// <summary>
    /// Estimated token count for this prompt.
    /// </summary>
    [Id(5)]
    public int? EstimatedTokenCount { get; init; }

    /// <summary>
    /// Number of times this cached prompt has been used.
    /// </summary>
    [Id(6)]
    public int UsageCount { get; set; }

    /// <summary>
    /// Last time this cached prompt was used.
    /// </summary>
    [Id(7)]
    public DateTime? LastUsedUtc { get; set; }
}

/// <summary>
/// Represents a cached validation result with expiration.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.CachedValidationResult")]
public sealed class CachedValidationResult
{
    /// <summary>
    /// The validation result (JSON serialized).
    /// </summary>
    [Id(0)]
    public required string ResultData { get; init; }

    /// <summary>
    /// Type of validation result for deserialization.
    /// </summary>
    [Id(1)]
    public required string ResultType { get; init; }

    /// <summary>
    /// Hash of the validation context/input.
    /// </summary>
    [Id(2)]
    public required string ContextHash { get; init; }

    /// <summary>
    /// When this validation was performed.
    /// </summary>
    [Id(3)]
    public required DateTime ValidatedAtUtc { get; init; }

    /// <summary>
    /// When this cached result expires.
    /// </summary>
    [Id(4)]
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    [Id(5)]
    public required bool IsValid { get; init; }

    /// <summary>
    /// Number of times this cached result has been used.
    /// </summary>
    [Id(6)]
    public int UsageCount { get; set; }
}

/// <summary>
/// Grain-level metadata for monitoring and debugging.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeGrainMetadata")]
public sealed class ModeGrainMetadata
{
    /// <summary>
    /// Number of method calls handled by this grain instance.
    /// </summary>
    [Id(0)]
    public long TotalMethodCalls { get; set; }

    /// <summary>
    /// Number of state persistence operations.
    /// </summary>
    [Id(1)]
    public long StatePersistenceCount { get; set; }

    /// <summary>
    /// Total time spent in method execution (milliseconds).
    /// </summary>
    [Id(2)]
    public long TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Number of cache hits across all cache types.
    /// </summary>
    [Id(3)]
    public long CacheHits { get; set; }

    /// <summary>
    /// Number of cache misses across all cache types.
    /// </summary>
    [Id(4)]
    public long CacheMisses { get; set; }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    [Id(5)]
    public long ErrorCount { get; set; }

    /// <summary>
    /// Last error message (for debugging).
    /// </summary>
    [Id(6)]
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// Last error timestamp.
    /// </summary>
    [Id(7)]
    public DateTime? LastErrorUtc { get; set; }

    /// <summary>
    /// Current health status of the grain.
    /// </summary>
    [Id(8)]
    public GrainHealthStatus HealthStatus { get; set; } = GrainHealthStatus.Healthy;

    /// <summary>
    /// Additional debug information.
    /// </summary>
    [Id(9)]
    public Dictionary<string, string> DebugInfo { get; set; } = [];
}

/// <summary>
/// Performance metrics specific to mode grain operations.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Orleans.Contracts.ModeGrainPerformanceMetrics")]
public sealed class ModeGrainPerformanceMetrics
{
    /// <summary>
    /// Average response time for configuration operations (milliseconds).
    /// </summary>
    [Id(0)]
    public double AverageConfigurationOpTimeMs { get; set; }

    /// <summary>
    /// Average response time for transition operations (milliseconds).
    /// </summary>
    [Id(1)]
    public double AverageTransitionOpTimeMs { get; set; }

    /// <summary>
    /// Average response time for validation operations (milliseconds).
    /// </summary>
    [Id(2)]
    public double AverageValidationOpTimeMs { get; set; }

    /// <summary>
    /// Cache hit rate as a percentage (0-100).
    /// </summary>
    [Id(3)]
    public double CacheHitRatePercent { get; set; }

    /// <summary>
    /// Total number of successful operations.
    /// </summary>
    [Id(4)]
    public long SuccessfulOperations { get; set; }

    /// <summary>
    /// Total number of failed operations.
    /// </summary>
    [Id(5)]
    public long FailedOperations { get; set; }

    /// <summary>
    /// Peak memory usage observed (bytes).
    /// </summary>
    [Id(6)]
    public long PeakMemoryUsageBytes { get; set; }

    /// <summary>
    /// Number of prompt generation operations.
    /// </summary>
    [Id(7)]
    public long PromptGenerationCount { get; set; }

    /// <summary>
    /// Average prompt generation time (milliseconds).
    /// </summary>
    [Id(8)]
    public double AveragePromptGenerationTimeMs { get; set; }
}

/// <summary>
/// Health status of a mode grain.
/// </summary>
public enum GrainHealthStatus
{
    /// <summary>Grain is operating normally</summary>
    Healthy,
    /// <summary>Grain has minor issues but is functional</summary>
    Degraded,
    /// <summary>Grain has significant issues</summary>
    Unhealthy,
    /// <summary>Grain is not responding</summary>
    Critical,
    /// <summary>Grain health is unknown</summary>
    Unknown
}