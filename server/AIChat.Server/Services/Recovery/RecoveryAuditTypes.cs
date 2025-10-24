namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Represents a request to start audit tracking for a recovery operation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryAuditRequest")]
public record RecoveryAuditRequest
{
    /// <summary>
    /// The unique identifier for the recovery operation.
    /// </summary>
    [Id(0)]
    public required string OperationId { get; init; }

    /// <summary>
    /// The grain being recovered.
    /// </summary>
    [Id(1)]
    public required string GrainId { get; init; }

    /// <summary>
    /// The type of grain being recovered.
    /// </summary>
    [Id(2)]
    public required string GrainType { get; init; }

    /// <summary>
    /// The target timestamp for recovery, if applicable.
    /// </summary>
    [Id(3)]
    public DateTimeOffset? TargetTimestamp { get; init; }

    /// <summary>
    /// The target version for recovery, if applicable.
    /// </summary>
    [Id(4)]
    public long? TargetVersion { get; init; }

    /// <summary>
    /// Who or what initiated the recovery operation.
    /// </summary>
    [Id(5)]
    public required string InitiatedBy { get; init; }

    /// <summary>
    /// The reason for the recovery operation.
    /// </summary>
    [Id(6)]
    public string? Reason { get; init; }

    /// <summary>
    /// The recovery strategy being used.
    /// </summary>
    [Id(7)]
    public RecoveryStrategy Strategy { get; init; }

    /// <summary>
    /// Additional metadata for the audit record.
    /// </summary>
    [Id(8)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Timestamp when the recovery operation was requested.
    /// </summary>
    [Id(9)]
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation ID for tracking related operations.
    /// </summary>
    [Id(10)]
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Source system or component that initiated the recovery.
    /// </summary>
    [Id(11)]
    public string? SourceSystem { get; init; }

    /// <summary>
    /// IP address of the requester, if applicable.
    /// </summary>
    [Id(12)]
    public string? SourceIpAddress { get; init; }

    /// <summary>
    /// Creates an audit request from a point-in-time recovery request.
    /// </summary>
    /// <param name="operationId">The operation identifier</param>
    /// <param name="recoveryRequest">The recovery request</param>
    /// <param name="sourceSystem">The source system</param>
    /// <param name="sourceIp">The source IP address</param>
    /// <returns>A recovery audit request</returns>
    public static RecoveryAuditRequest FromRecoveryRequest(
        string operationId,
        PointInTimeRecoveryRequest recoveryRequest,
        string? sourceSystem = null,
        string? sourceIp = null)
    {
        return new RecoveryAuditRequest
        {
            OperationId = operationId,
            GrainId = recoveryRequest.GrainId,
            GrainType = recoveryRequest.GrainType,
            TargetTimestamp = recoveryRequest.TargetTimestamp,
            TargetVersion = recoveryRequest.TargetVersion,
            InitiatedBy = recoveryRequest.InitiatedBy ?? "Unknown",
            Reason = recoveryRequest.Reason,
            Strategy = recoveryRequest.PreferredStrategy ?? RecoveryStrategy.HybridRecovery,
            Metadata = recoveryRequest.Metadata,
            CorrelationId = recoveryRequest.CorrelationId,
            SourceSystem = sourceSystem,
            SourceIpAddress = sourceIp
        };
    }
}

/// <summary>
/// Represents progress updates during a recovery operation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryProgressUpdate")]
public record RecoveryProgressUpdate
{
    /// <summary>
    /// The unique identifier for the recovery operation.
    /// </summary>
    [Id(0)]
    public required string OperationId { get; init; }

    /// <summary>
    /// The current status of the recovery operation.
    /// </summary>
    [Id(1)]
    public required RecoveryStatus Status { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    [Id(2)]
    public int ProgressPercentage { get; init; }

    /// <summary>
    /// Current step being performed.
    /// </summary>
    [Id(3)]
    public string? CurrentStep { get; init; }

    /// <summary>
    /// Number of events processed so far.
    /// </summary>
    [Id(4)]
    public int EventsProcessed { get; init; }

    /// <summary>
    /// Total number of events to process.
    /// </summary>
    [Id(5)]
    public int TotalEvents { get; init; }

    /// <summary>
    /// Time elapsed since recovery started.
    /// </summary>
    [Id(6)]
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Estimated time remaining.
    /// </summary>
    [Id(7)]
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// Additional progress details.
    /// </summary>
    [Id(8)]
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Timestamp of this progress update.
    /// </summary>
    [Id(9)]
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Any warnings encountered during this step.
    /// </summary>
    [Id(10)]
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Represents the final result of a recovery operation for auditing.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryAuditResult")]
public record RecoveryAuditResult
{
    /// <summary>
    /// The unique identifier for the recovery operation.
    /// </summary>
    [Id(0)]
    public required string OperationId { get; init; }

    /// <summary>
    /// Whether the recovery operation was successful.
    /// </summary>
    [Id(1)]
    public required bool Success { get; init; }

    /// <summary>
    /// The final status of the recovery operation.
    /// </summary>
    [Id(2)]
    public required RecoveryStatus FinalStatus { get; init; }

    /// <summary>
    /// The actual timestamp that was recovered to.
    /// </summary>
    [Id(3)]
    public DateTimeOffset? ActualTimestamp { get; init; }

    /// <summary>
    /// The actual version that was recovered to.
    /// </summary>
    [Id(4)]
    public long? ActualVersion { get; init; }

    /// <summary>
    /// The recovery strategy that was actually used.
    /// </summary>
    [Id(5)]
    public RecoveryStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Number of events that were replayed.
    /// </summary>
    [Id(6)]
    public int EventsReplayed { get; init; }

    /// <summary>
    /// Total time taken for the recovery operation.
    /// </summary>
    [Id(7)]
    public TimeSpan RecoveryDuration { get; init; }

    /// <summary>
    /// Whether a snapshot was used during recovery.
    /// </summary>
    [Id(8)]
    public bool SnapshotUsed { get; init; }

    /// <summary>
    /// The ID of the snapshot that was used, if any.
    /// </summary>
    [Id(9)]
    public string? SnapshotId { get; init; }

    /// <summary>
    /// The result of state validation.
    /// </summary>
    [Id(10)]
    public bool ValidationPassed { get; init; }

    /// <summary>
    /// Details of validation results.
    /// </summary>
    [Id(11)]
    public ConsistencyVerificationResult? ValidationDetails { get; init; }

    /// <summary>
    /// Any error message if the recovery failed.
    /// </summary>
    [Id(12)]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Stack trace if an exception occurred.
    /// </summary>
    [Id(13)]
    public string? ErrorStackTrace { get; init; }

    /// <summary>
    /// Warning messages from the recovery operation.
    /// </summary>
    [Id(14)]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Size of data that was processed during recovery.
    /// </summary>
    [Id(15)]
    public long DataProcessedBytes { get; init; }

    /// <summary>
    /// Performance metrics for the recovery operation.
    /// </summary>
    [Id(16)]
    public Dictionary<string, double>? PerformanceMetrics { get; init; }

    /// <summary>
    /// Additional metadata about the recovery result.
    /// </summary>
    [Id(17)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Timestamp when the recovery was completed.
    /// </summary>
    [Id(18)]
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an audit result from a point-in-time recovery result.
    /// </summary>
    /// <typeparam name="T">The type of recovered state</typeparam>
    /// <param name="result">The recovery result</param>
    /// <returns>A recovery audit result</returns>
    public static RecoveryAuditResult FromRecoveryResult<T>(PointInTimeRecoveryResult<T> result)
    {
        return new RecoveryAuditResult
        {
            OperationId = result.OperationId,
            Success = result.Status == RecoveryStatus.Completed,
            FinalStatus = result.Status,
            ActualTimestamp = result.ActualTimestamp,
            ActualVersion = result.ActualVersion,
            StrategyUsed = result.StrategyUsed,
            EventsReplayed = result.EventsReplayed,
            RecoveryDuration = result.RecoveryDuration,
            SnapshotUsed = !string.IsNullOrEmpty(result.SnapshotId),
            SnapshotId = result.SnapshotId,
            ValidationPassed = result.ValidationResult?.IsConsistent ?? true,
            ValidationDetails = result.ValidationResult,
            ErrorMessage = result.Error?.Message,
            ErrorStackTrace = result.Error?.StackTrace,
            Warnings = result.Warnings,
            Metadata = result.Metadata,
            CompletedAt = result.CompletedAt
        };
    }
}

/// <summary>
/// Represents a complete audit entry for a recovery operation.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryAuditEntry")]
public record RecoveryAuditEntry
{
    /// <summary>
    /// The unique identifier for this audit entry.
    /// </summary>
    [Id(0)]
    public required string AuditId { get; init; }

    /// <summary>
    /// The unique identifier for the recovery operation.
    /// </summary>
    [Id(1)]
    public required string OperationId { get; init; }

    /// <summary>
    /// The grain that was recovered.
    /// </summary>
    [Id(2)]
    public required string GrainId { get; init; }

    /// <summary>
    /// The type of grain that was recovered.
    /// </summary>
    [Id(3)]
    public required string GrainType { get; init; }

    /// <summary>
    /// Who or what initiated the recovery operation.
    /// </summary>
    [Id(4)]
    public required string InitiatedBy { get; init; }

    /// <summary>
    /// Timestamp when the recovery operation was started.
    /// </summary>
    [Id(5)]
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Timestamp when the recovery operation was completed.
    /// </summary>
    [Id(6)]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// The target timestamp for recovery.
    /// </summary>
    [Id(7)]
    public DateTimeOffset? TargetTimestamp { get; init; }

    /// <summary>
    /// The target version for recovery.
    /// </summary>
    [Id(8)]
    public long? TargetVersion { get; init; }

    /// <summary>
    /// The actual timestamp that was recovered to.
    /// </summary>
    [Id(9)]
    public DateTimeOffset? ActualTimestamp { get; init; }

    /// <summary>
    /// The actual version that was recovered to.
    /// </summary>
    [Id(10)]
    public long? ActualVersion { get; init; }

    /// <summary>
    /// The reason for the recovery operation.
    /// </summary>
    [Id(11)]
    public string? Reason { get; init; }

    /// <summary>
    /// The recovery strategy that was used.
    /// </summary>
    [Id(12)]
    public RecoveryStrategy StrategyUsed { get; init; }

    /// <summary>
    /// Whether the recovery operation was successful.
    /// </summary>
    [Id(13)]
    public bool Success { get; init; }

    /// <summary>
    /// The final status of the recovery operation.
    /// </summary>
    [Id(14)]
    public RecoveryStatus FinalStatus { get; init; }

    /// <summary>
    /// Number of events that were replayed.
    /// </summary>
    [Id(15)]
    public int EventsReplayed { get; init; }

    /// <summary>
    /// Total time taken for the recovery operation.
    /// </summary>
    [Id(16)]
    public TimeSpan RecoveryDuration { get; init; }

    /// <summary>
    /// Whether a snapshot was used during recovery.
    /// </summary>
    [Id(17)]
    public bool SnapshotUsed { get; init; }

    /// <summary>
    /// The ID of the snapshot that was used, if any.
    /// </summary>
    [Id(18)]
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Whether state validation passed.
    /// </summary>
    [Id(19)]
    public bool ValidationPassed { get; init; }

    /// <summary>
    /// Any error message if the recovery failed.
    /// </summary>
    [Id(20)]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Warning messages from the recovery operation.
    /// </summary>
    [Id(21)]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Source system that initiated the recovery.
    /// </summary>
    [Id(22)]
    public string? SourceSystem { get; init; }

    /// <summary>
    /// IP address of the requester.
    /// </summary>
    [Id(23)]
    public string? SourceIpAddress { get; init; }

    /// <summary>
    /// Correlation ID for tracking related operations.
    /// </summary>
    [Id(24)]
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Size of data processed during recovery.
    /// </summary>
    [Id(25)]
    public long DataProcessedBytes { get; init; }

    /// <summary>
    /// Performance metrics for the recovery operation.
    /// </summary>
    [Id(26)]
    public Dictionary<string, double>? PerformanceMetrics { get; init; }

    /// <summary>
    /// Additional metadata about the recovery operation.
    /// </summary>
    [Id(27)]
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates an audit entry from request and result.
    /// </summary>
    /// <param name="request">The original audit request</param>
    /// <param name="result">The final audit result</param>
    /// <returns>A complete audit entry</returns>
    public static RecoveryAuditEntry Create(
        RecoveryAuditRequest request,
        RecoveryAuditResult result)
    {
        return new RecoveryAuditEntry
        {
            AuditId = Guid.NewGuid().ToString(),
            OperationId = request.OperationId,
            GrainId = request.GrainId,
            GrainType = request.GrainType,
            InitiatedBy = request.InitiatedBy,
            StartedAt = request.RequestedAt,
            CompletedAt = result.CompletedAt,
            TargetTimestamp = request.TargetTimestamp,
            TargetVersion = request.TargetVersion,
            ActualTimestamp = result.ActualTimestamp,
            ActualVersion = result.ActualVersion,
            Reason = request.Reason,
            StrategyUsed = result.StrategyUsed,
            Success = result.Success,
            FinalStatus = result.FinalStatus,
            EventsReplayed = result.EventsReplayed,
            RecoveryDuration = result.RecoveryDuration,
            SnapshotUsed = result.SnapshotUsed,
            SnapshotId = result.SnapshotId,
            ValidationPassed = result.ValidationPassed,
            ErrorMessage = result.ErrorMessage,
            Warnings = result.Warnings,
            SourceSystem = request.SourceSystem,
            SourceIpAddress = request.SourceIpAddress,
            CorrelationId = request.CorrelationId,
            DataProcessedBytes = result.DataProcessedBytes,
            PerformanceMetrics = result.PerformanceMetrics,
            Metadata = MergeMetadata(request.Metadata, result.Metadata)
        };
    }

    /// <summary>
    /// Merges metadata from request and result.
    /// </summary>
    /// <param name="requestMetadata">Metadata from request</param>
    /// <param name="resultMetadata">Metadata from result</param>
    /// <returns>Merged metadata dictionary</returns>
    private static Dictionary<string, object>? MergeMetadata(
        Dictionary<string, object>? requestMetadata,
        Dictionary<string, object>? resultMetadata)
    {
        if (requestMetadata == null && resultMetadata == null)
        {
            return null;
        }

        var merged = new Dictionary<string, object>();

        if (requestMetadata != null)
        {
            foreach (var kvp in requestMetadata)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (resultMetadata != null)
        {
            foreach (var kvp in resultMetadata)
            {
                merged[kvp.Key] = kvp.Value; // Result metadata overwrites request metadata
            }
        }

        return merged.Count > 0 ? merged : null;
    }
}

/// <summary>
/// Query criteria for searching recovery audit history.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryAuditQuery")]
public record RecoveryAuditQuery
{
    /// <summary>
    /// Filter by grain ID.
    /// </summary>
    [Id(0)]
    public string? GrainId { get; init; }

    /// <summary>
    /// Filter by grain type.
    /// </summary>
    [Id(1)]
    public string? GrainType { get; init; }

    /// <summary>
    /// Filter by who initiated the recovery.
    /// </summary>
    [Id(2)]
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Filter by recovery success status.
    /// </summary>
    [Id(3)]
    public bool? Success { get; init; }

    /// <summary>
    /// Filter by final recovery status.
    /// </summary>
    [Id(4)]
    public RecoveryStatus? FinalStatus { get; init; }

    /// <summary>
    /// Filter by recovery strategy used.
    /// </summary>
    [Id(5)]
    public RecoveryStrategy? StrategyUsed { get; init; }

    /// <summary>
    /// Filter by operations started after this time.
    /// </summary>
    [Id(6)]
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>
    /// Filter by operations started before this time.
    /// </summary>
    [Id(7)]
    public DateTimeOffset? StartedBefore { get; init; }

    /// <summary>
    /// Filter by operations completed after this time.
    /// </summary>
    [Id(8)]
    public DateTimeOffset? CompletedAfter { get; init; }

    /// <summary>
    /// Filter by operations completed before this time.
    /// </summary>
    [Id(9)]
    public DateTimeOffset? CompletedBefore { get; init; }

    /// <summary>
    /// Filter by correlation ID.
    /// </summary>
    [Id(10)]
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Filter by source system.
    /// </summary>
    [Id(11)]
    public string? SourceSystem { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    [Id(12)]
    public int? MaxResults { get; init; } = 100;

    /// <summary>
    /// Number of results to skip (for pagination).
    /// </summary>
    [Id(13)]
    public int? Skip { get; init; }

    /// <summary>
    /// Sort order for results.
    /// </summary>
    [Id(14)]
    public AuditSortOrder SortOrder { get; init; } = AuditSortOrder.StartedAtDescending;

    /// <summary>
    /// Whether to include only operations with warnings.
    /// </summary>
    [Id(15)]
    public bool OnlyWithWarnings { get; init; }

    /// <summary>
    /// Whether to include only operations that used snapshots.
    /// </summary>
    [Id(16)]
    public bool OnlyWithSnapshots { get; init; }

    /// <summary>
    /// Creates a query for recent recovery operations.
    /// </summary>
    /// <param name="hours">Number of hours back to look</param>
    /// <returns>A query for recent operations</returns>
    public static RecoveryAuditQuery Recent(int hours = 24)
    {
        return new RecoveryAuditQuery
        {
            StartedAfter = DateTimeOffset.UtcNow.AddHours(-hours),
            SortOrder = AuditSortOrder.StartedAtDescending,
            MaxResults = 100
        };
    }

    /// <summary>
    /// Creates a query for failed recovery operations.
    /// </summary>
    /// <param name="hours">Number of hours back to look</param>
    /// <returns>A query for failed operations</returns>
    public static RecoveryAuditQuery Failed(int hours = 168) // 1 week default
    {
        return new RecoveryAuditQuery
        {
            Success = false,
            StartedAfter = DateTimeOffset.UtcNow.AddHours(-hours),
            SortOrder = AuditSortOrder.StartedAtDescending,
            MaxResults = 50
        };
    }

    /// <summary>
    /// Creates a query for operations by a specific user or system.
    /// </summary>
    /// <param name="initiatedBy">Who initiated the operations</param>
    /// <param name="hours">Number of hours back to look</param>
    /// <returns>A query for operations by the specified initiator</returns>
    public static RecoveryAuditQuery ByInitiator(string initiatedBy, int hours = 168)
    {
        return new RecoveryAuditQuery
        {
            InitiatedBy = initiatedBy,
            StartedAfter = DateTimeOffset.UtcNow.AddHours(-hours),
            SortOrder = AuditSortOrder.StartedAtDescending,
            MaxResults = 100
        };
    }
}

/// <summary>
/// Sort orders for audit query results.
/// </summary>
[Serializable]
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.AuditSortOrder")]
public enum AuditSortOrder
{
    /// <summary>
    /// Sort by started time, newest first.
    /// </summary>
    [Id(0)]
    StartedAtDescending = 0,

    /// <summary>
    /// Sort by started time, oldest first.
    /// </summary>
    [Id(1)]
    StartedAtAscending = 1,

    /// <summary>
    /// Sort by completed time, newest first.
    /// </summary>
    [Id(2)]
    CompletedAtDescending = 2,

    /// <summary>
    /// Sort by completed time, oldest first.
    /// </summary>
    [Id(3)]
    CompletedAtAscending = 3,

    /// <summary>
    /// Sort by recovery duration, longest first.
    /// </summary>
    [Id(4)]
    DurationDescending = 4,

    /// <summary>
    /// Sort by recovery duration, shortest first.
    /// </summary>
    [Id(5)]
    DurationAscending = 5,

    /// <summary>
    /// Sort by grain ID alphabetically.
    /// </summary>
    [Id(6)]
    GrainIdAscending = 6
}

/// <summary>
/// Result of a recovery audit query.
/// </summary>
[GenerateSerializer]
[Alias("AIChat.Server.Services.Recovery.RecoveryAuditQueryResult")]
public record RecoveryAuditQueryResult
{
    /// <summary>
    /// The recovery audit entries that match the query.
    /// </summary>
    [Id(0)]
    public required IReadOnlyList<RecoveryAuditEntry> Entries { get; init; }

    /// <summary>
    /// Total number of matching entries (before pagination).
    /// </summary>
    [Id(1)]
    public int TotalCount { get; init; }

    /// <summary>
    /// Number of entries returned in this result.
    /// </summary>
    [Id(2)]
    public int ReturnedCount { get; set; }

    /// <summary>
    /// Whether there are more results available.
    /// </summary>
    [Id(3)]
    public bool HasMoreResults { get; init; }

    /// <summary>
    /// The query that was executed.
    /// </summary>
    [Id(4)]
    public RecoveryAuditQuery? Query { get; init; }

    /// <summary>
    /// Time taken to execute the query.
    /// </summary>
    [Id(5)]
    public TimeSpan QueryDuration { get; init; }

    /// <summary>
    /// Timestamp when the query was executed.
    /// </summary>
    [Id(6)]
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates an empty result.
    /// </summary>
    /// <param name="query">The original query</param>
    /// <param name="queryDuration">Time taken for the query</param>
    /// <returns>An empty query result</returns>
    public static RecoveryAuditQueryResult Empty(
        RecoveryAuditQuery? query = null,
        TimeSpan queryDuration = default)
    {
        return new RecoveryAuditQueryResult
        {
            Entries = [],
            TotalCount = 0,
            HasMoreResults = false,
            Query = query,
            QueryDuration = queryDuration
        };
    }

    /// <summary>
    /// Creates a result with entries.
    /// </summary>
    /// <param name="entries">The matching audit entries</param>
    /// <param name="totalCount">Total count before pagination</param>
    /// <param name="hasMoreResults">Whether more results are available</param>
    /// <param name="query">The original query</param>
    /// <param name="queryDuration">Time taken for the query</param>
    /// <returns>A query result with entries</returns>
    public static RecoveryAuditQueryResult WithEntries(
        IReadOnlyList<RecoveryAuditEntry> entries,
        int totalCount,
        bool hasMoreResults,
        RecoveryAuditQuery? query = null,
        TimeSpan queryDuration = default)
    {
        return new RecoveryAuditQueryResult
        {
            Entries = entries,
            TotalCount = totalCount,
            HasMoreResults = hasMoreResults,
            Query = query,
            QueryDuration = queryDuration
        };
    }
}