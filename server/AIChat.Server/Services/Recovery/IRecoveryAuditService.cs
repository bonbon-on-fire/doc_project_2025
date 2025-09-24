namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Service for tracking and auditing all point-in-time recovery operations.
/// Provides comprehensive audit trail for compliance, troubleshooting, and analysis.
/// </summary>
public interface IRecoveryAuditService
{
    /// <summary>
    /// Records the start of a recovery operation and returns an audit ID for tracking.
    /// This should be called before any recovery operation begins.
    /// </summary>
    /// <param name="request">The recovery audit request with operation details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The unique audit ID for tracking this recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when request is null</exception>
    /// <exception cref="RecoveryException">Thrown when audit initialization fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<string> StartRecoveryAuditAsync(
        RecoveryAuditRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the progress of an ongoing recovery operation.
    /// This can be called multiple times during a recovery to track progress.
    /// </summary>
    /// <param name="auditId">The audit ID returned from StartRecoveryAuditAsync</param>
    /// <param name="progress">The current progress information</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the update operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when auditId or progress is null</exception>
    /// <exception cref="ArgumentException">Thrown when auditId is not found</exception>
    /// <exception cref="RecoveryException">Thrown when audit update fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task UpdateRecoveryProgressAsync(
        string auditId,
        RecoveryProgressUpdate progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the completion of a recovery operation with final results.
    /// This should be called when a recovery operation completes (successfully or not).
    /// </summary>
    /// <param name="auditId">The audit ID returned from StartRecoveryAuditAsync</param>
    /// <param name="result">The final result of the recovery operation</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the completion operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when auditId or result is null</exception>
    /// <exception cref="ArgumentException">Thrown when auditId is not found</exception>
    /// <exception cref="RecoveryException">Thrown when audit completion fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task CompleteRecoveryAuditAsync(
        string auditId,
        RecoveryAuditResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries recovery audit history with flexible filtering and sorting.
    /// Supports pagination for large result sets.
    /// </summary>
    /// <param name="query">The query criteria for filtering and sorting results</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The audit entries matching the query criteria</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="RecoveryException">Thrown when query execution fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryAuditQueryResult> QueryRecoveryHistoryAsync(
        RecoveryAuditQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific recovery audit entry by its audit ID.
    /// </summary>
    /// <param name="auditId">The audit ID to retrieve</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The audit entry, or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when auditId is null</exception>
    /// <exception cref="RecoveryException">Thrown when retrieval fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryAuditEntry?> GetRecoveryAuditEntryAsync(
        string auditId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific recovery audit entry by the recovery operation ID.
    /// </summary>
    /// <param name="operationId">The recovery operation ID</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The audit entry, or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when operationId is null</exception>
    /// <exception cref="RecoveryException">Thrown when retrieval fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryAuditEntry?> GetRecoveryAuditEntryByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets audit statistics and metrics for analysis and reporting.
    /// </summary>
    /// <param name="fromTime">Start time for metrics calculation, or null for all history</param>
    /// <param name="toTime">End time for metrics calculation, or null for current time</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Comprehensive audit metrics</returns>
    /// <exception cref="ArgumentException">Thrown when time range is invalid</exception>
    /// <exception cref="RecoveryException">Thrown when metrics calculation fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryAuditMetrics> GetAuditMetricsAsync(
        DateTimeOffset? fromTime = null,
        DateTimeOffset? toTime = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports audit data to external format (CSV, JSON, etc.) for compliance or analysis.
    /// </summary>
    /// <param name="query">Query criteria for data to export</param>
    /// <param name="format">Export format (CSV, JSON, XML)</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The exported data as a byte array</returns>
    /// <exception cref="ArgumentNullException">Thrown when query is null</exception>
    /// <exception cref="ArgumentException">Thrown when format is not supported</exception>
    /// <exception cref="RecoveryException">Thrown when export fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<byte[]> ExportAuditDataAsync(
        RecoveryAuditQuery query,
        AuditExportFormat format = AuditExportFormat.Csv,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old audit entries based on retention policies.
    /// This is typically called by a background service.
    /// </summary>
    /// <param name="retentionPolicy">The retention policy to apply</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The number of audit entries that were cleaned up</returns>
    /// <exception cref="ArgumentNullException">Thrown when retentionPolicy is null</exception>
    /// <exception cref="RecoveryException">Thrown when cleanup fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<int> CleanupAuditEntriesAsync(
        AuditRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of the audit service.
    /// Checks storage availability and performance metrics.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryAuditHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Export formats supported by the audit service.
/// </summary>
public enum AuditExportFormat
{
    /// <summary>
    /// Comma-separated values format.
    /// </summary>
    Csv,

    /// <summary>
    /// JavaScript Object Notation format.
    /// </summary>
    Json,

    /// <summary>
    /// Extensible Markup Language format.
    /// </summary>
    Xml,

    /// <summary>
    /// Microsoft Excel format.
    /// </summary>
    Excel
}

/// <summary>
/// Retention policy for audit data cleanup.
/// </summary>
public record AuditRetentionPolicy
{
    /// <summary>
    /// Maximum age of audit entries to retain.
    /// </summary>
    public required TimeSpan MaxAge { get; init; }

    /// <summary>
    /// Maximum number of audit entries to retain per grain.
    /// </summary>
    public int? MaxEntriesPerGrain { get; init; }

    /// <summary>
    /// Whether to keep failed recovery entries longer.
    /// </summary>
    public bool RetainFailedRecoveries { get; init; } = true;

    /// <summary>
    /// Maximum age for failed recovery entries if RetainFailedRecoveries is true.
    /// </summary>
    public TimeSpan? FailedRecoveryMaxAge { get; init; }

    /// <summary>
    /// Whether to archive entries before deletion.
    /// </summary>
    public bool ArchiveBeforeDelete { get; init; } = false;

    /// <summary>
    /// Archive storage location if archiving is enabled.
    /// </summary>
    public string? ArchiveLocation { get; init; }

    /// <summary>
    /// Creates a standard retention policy (30 days).
    /// </summary>
    /// <returns>A standard retention policy</returns>
    public static AuditRetentionPolicy Standard()
    {
        return new AuditRetentionPolicy
        {
            MaxAge = TimeSpan.FromDays(30),
            RetainFailedRecoveries = true,
            FailedRecoveryMaxAge = TimeSpan.FromDays(90)
        };
    }

    /// <summary>
    /// Creates a strict retention policy (7 days).
    /// </summary>
    /// <returns>A strict retention policy</returns>
    public static AuditRetentionPolicy Strict()
    {
        return new AuditRetentionPolicy
        {
            MaxAge = TimeSpan.FromDays(7),
            MaxEntriesPerGrain = 100,
            RetainFailedRecoveries = true,
            FailedRecoveryMaxAge = TimeSpan.FromDays(14)
        };
    }

    /// <summary>
    /// Creates a long-term retention policy (1 year).
    /// </summary>
    /// <returns>A long-term retention policy</returns>
    public static AuditRetentionPolicy LongTerm()
    {
        return new AuditRetentionPolicy
        {
            MaxAge = TimeSpan.FromDays(365),
            RetainFailedRecoveries = true,
            FailedRecoveryMaxAge = TimeSpan.FromDays(730), // 2 years
            ArchiveBeforeDelete = true
        };
    }
}

/// <summary>
/// Audit metrics and statistics for analysis and reporting.
/// </summary>
public record RecoveryAuditMetrics
{
    /// <summary>
    /// Total number of recovery operations tracked.
    /// </summary>
    public long TotalRecoveryOperations { get; init; }

    /// <summary>
    /// Number of successful recovery operations.
    /// </summary>
    public long SuccessfulRecoveries { get; init; }

    /// <summary>
    /// Number of failed recovery operations.
    /// </summary>
    public long FailedRecoveries { get; init; }

    /// <summary>
    /// Recovery success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalRecoveryOperations > 0
        ? (double)SuccessfulRecoveries / TotalRecoveryOperations * 100
        : 0;

    /// <summary>
    /// Average recovery time across all operations.
    /// </summary>
    public TimeSpan AverageRecoveryTime { get; init; }

    /// <summary>
    /// Median recovery time across all operations.
    /// </summary>
    public TimeSpan MedianRecoveryTime { get; init; }

    /// <summary>
    /// 95th percentile recovery time.
    /// </summary>
    public TimeSpan P95RecoveryTime { get; init; }

    /// <summary>
    /// Distribution of recovery strategies used.
    /// </summary>
    public Dictionary<RecoveryStrategy, long> StrategyUsageCount { get; init; } = new();

    /// <summary>
    /// Distribution of who initiated recoveries.
    /// </summary>
    public Dictionary<string, long> InitiatorUsageCount { get; init; } = new();

    /// <summary>
    /// Distribution of recovery reasons.
    /// </summary>
    public Dictionary<string, long> ReasonUsageCount { get; init; } = new();

    /// <summary>
    /// Distribution of grain types recovered.
    /// </summary>
    public Dictionary<string, long> GrainTypeUsageCount { get; init; } = new();

    /// <summary>
    /// Number of recoveries that used snapshots.
    /// </summary>
    public long RecoveriesWithSnapshots { get; init; }

    /// <summary>
    /// Average number of events replayed per recovery.
    /// </summary>
    public double AverageEventsReplayed { get; init; }

    /// <summary>
    /// Total amount of data processed across all recoveries.
    /// </summary>
    public long TotalDataProcessedBytes { get; init; }

    /// <summary>
    /// Time period these metrics cover.
    /// </summary>
    public TimeSpan MetricsPeriod { get; init; }

    /// <summary>
    /// Start time of the metrics period.
    /// </summary>
    public DateTimeOffset PeriodStart { get; init; }

    /// <summary>
    /// End time of the metrics period.
    /// </summary>
    public DateTimeOffset PeriodEnd { get; init; }

    /// <summary>
    /// Timestamp when these metrics were calculated.
    /// </summary>
    public DateTimeOffset CalculatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates empty metrics for initialization.
    /// </summary>
    /// <param name="periodStart">Start of metrics period</param>
    /// <param name="periodEnd">End of metrics period</param>
    /// <returns>Empty audit metrics</returns>
    public static RecoveryAuditMetrics Empty(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        return new RecoveryAuditMetrics
        {
            TotalRecoveryOperations = 0,
            SuccessfulRecoveries = 0,
            FailedRecoveries = 0,
            AverageRecoveryTime = TimeSpan.Zero,
            MedianRecoveryTime = TimeSpan.Zero,
            P95RecoveryTime = TimeSpan.Zero,
            RecoveriesWithSnapshots = 0,
            AverageEventsReplayed = 0,
            TotalDataProcessedBytes = 0,
            MetricsPeriod = periodEnd - periodStart,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd
        };
    }
}

/// <summary>
/// Health status information for the recovery audit service.
/// </summary>
public record RecoveryAuditHealthStatus
{
    /// <summary>
    /// Whether the audit service is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Name of the audit service.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// When the health check was performed.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether the audit storage is available and responsive.
    /// </summary>
    public bool StorageHealthy { get; init; }

    /// <summary>
    /// Current size of the audit database in bytes.
    /// </summary>
    public long AuditDatabaseSizeBytes { get; init; }

    /// <summary>
    /// Number of audit entries currently stored.
    /// </summary>
    public long TotalAuditEntries { get; init; }

    /// <summary>
    /// Average response time for audit queries.
    /// </summary>
    public TimeSpan AverageQueryTime { get; init; }

    /// <summary>
    /// Number of audit operations in the last hour.
    /// </summary>
    public int RecentAuditOperations { get; init; }

    /// <summary>
    /// Any health status messages.
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional health details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Creates a healthy audit status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="totalEntries">Total audit entries</param>
    /// <param name="databaseSize">Database size in bytes</param>
    /// <returns>A healthy audit status</returns>
    public static RecoveryAuditHealthStatus Healthy(
        string serviceName,
        long totalEntries = 0,
        long databaseSize = 0)
    {
        return new RecoveryAuditHealthStatus
        {
            IsHealthy = true,
            ServiceName = serviceName,
            StorageHealthy = true,
            TotalAuditEntries = totalEntries,
            AuditDatabaseSizeBytes = databaseSize
        };
    }

    /// <summary>
    /// Creates an unhealthy audit status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="messages">Health issues</param>
    /// <param name="storageHealthy">Whether storage is healthy</param>
    /// <returns>An unhealthy audit status</returns>
    public static RecoveryAuditHealthStatus Unhealthy(
        string serviceName,
        IReadOnlyList<string>? messages = null,
        bool storageHealthy = false)
    {
        return new RecoveryAuditHealthStatus
        {
            IsHealthy = false,
            ServiceName = serviceName,
            StorageHealthy = storageHealthy,
            Messages = messages ?? Array.Empty<string>()
        };
    }
}