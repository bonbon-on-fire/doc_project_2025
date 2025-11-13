using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIChat.Server.Services.Recovery.Implementations;

/// <summary>
/// Implementation of recovery audit service that tracks and audits all point-in-time recovery operations.
/// Provides comprehensive audit trail for compliance, troubleshooting, and analysis.
/// Uses in-memory storage for simplicity - in production, this would use a persistent audit store.
/// </summary>
public class RecoveryAuditService : IRecoveryAuditService
{
    private readonly ILogger<RecoveryAuditService> _logger;

    /// <summary>
    /// In-memory audit storage (in production, this would be a persistent store like database or event store)
    /// </summary>
    private readonly ConcurrentDictionary<string, RecoveryAuditEntry> _auditEntries = new();

    /// <summary>
    /// Cached JSON serializer options for performance
    /// </summary>
    private static readonly JsonSerializerOptions CachedJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly ConcurrentDictionary<string, string> _operationToAuditMap = new();
    private readonly ConcurrentDictionary<string, RecoveryProgressUpdate> _progressUpdates = new();

    /// <summary>
    /// Initializes a new instance of the RecoveryAuditService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null</exception>
    public RecoveryAuditService(ILogger<RecoveryAuditService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<string> StartRecoveryAuditAsync(
        RecoveryAuditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var auditId = Guid.NewGuid().ToString();

        _auditEntries[auditId] = new RecoveryAuditEntry
        {
            AuditId = auditId,
            OperationId = request.OperationId,
            GrainId = request.GrainId,
            GrainType = request.GrainType,
            InitiatedBy = request.InitiatedBy,
            StartedAt = request.RequestedAt,
            TargetTimestamp = request.TargetTimestamp,
            TargetVersion = request.TargetVersion,
            Reason = request.Reason,
            StrategyUsed = request.Strategy,
            Success = false, // Will be updated on completion
            FinalStatus = RecoveryStatus.Pending,
            SourceSystem = request.SourceSystem,
            SourceIpAddress = request.SourceIpAddress,
            CorrelationId = request.CorrelationId,
            Metadata = request.Metadata
        };
        _operationToAuditMap[request.OperationId] = auditId;

        _logger.LogInformation(
            "Started recovery audit tracking. AuditId: {AuditId}, OperationId: {OperationId}, GrainId: {GrainId}, InitiatedBy: {InitiatedBy}",
            auditId, request.OperationId, request.GrainId, request.InitiatedBy);

        return Task.FromResult(auditId);
    }

    /// <inheritdoc />
    public Task UpdateRecoveryProgressAsync(
        string auditId,
        RecoveryProgressUpdate progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditId);
        ArgumentNullException.ThrowIfNull(progress);

        if (!_auditEntries.TryGetValue(auditId, out var auditEntry))
        {
            throw new ArgumentException($"Audit entry not found for audit ID: {auditId}", nameof(auditId));
        }

        // Store the latest progress update
        _progressUpdates[auditId] = progress;

        // Update the audit entry with current status
        _auditEntries[auditId] = (auditEntry with
        {
            FinalStatus = progress.Status
        });

        _logger.LogDebug(
            "Updated recovery progress. AuditId: {AuditId}, Status: {Status}, Progress: {Progress}%",
            auditId, progress.Status, progress.ProgressPercentage);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteRecoveryAuditAsync(
        string auditId,
        RecoveryAuditResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditId);
        ArgumentNullException.ThrowIfNull(result);

        if (!_auditEntries.TryGetValue(auditId, out var auditEntry))
        {
            throw new ArgumentException($"Audit entry not found for audit ID: {auditId}", nameof(auditId));
        }

        // Update the audit entry with final results
        _auditEntries[auditId] = (auditEntry with
        {
            CompletedAt = result.CompletedAt,
            ActualTimestamp = result.ActualTimestamp,
            ActualVersion = result.ActualVersion,
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
            DataProcessedBytes = result.DataProcessedBytes,
            PerformanceMetrics = result.PerformanceMetrics,
            Metadata = MergeMetadata(auditEntry.Metadata, result.Metadata)
        });

        _logger.LogInformation(
            "Completed recovery audit tracking. AuditId: {AuditId}, OperationId: {OperationId}, Success: {Success}, Duration: {Duration}ms",
            auditId, result.OperationId, result.Success, result.RecoveryDuration.TotalMilliseconds);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RecoveryAuditQueryResult> QueryRecoveryHistoryAsync(
        RecoveryAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var filteredEntries = _auditEntries.Values.AsEnumerable();

            // Apply filters
            if (!string.IsNullOrEmpty(query.GrainId))
            {
                filteredEntries = filteredEntries.Where(e => e.GrainId == query.GrainId);
            }

            if (!string.IsNullOrEmpty(query.GrainType))
            {
                filteredEntries = filteredEntries.Where(e => e.GrainType == query.GrainType);
            }

            if (!string.IsNullOrEmpty(query.InitiatedBy))
            {
                filteredEntries = filteredEntries.Where(e => e.InitiatedBy == query.InitiatedBy);
            }

            if (query.Success.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.Success == query.Success.Value);
            }

            if (query.FinalStatus.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.FinalStatus == query.FinalStatus.Value);
            }

            if (query.StrategyUsed.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.StrategyUsed == query.StrategyUsed.Value);
            }

            if (query.StartedAfter.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.StartedAt >= query.StartedAfter.Value);
            }

            if (query.StartedBefore.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.StartedAt <= query.StartedBefore.Value);
            }

            if (query.CompletedAfter.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.CompletedAt >= query.CompletedAfter.Value);
            }

            if (query.CompletedBefore.HasValue)
            {
                filteredEntries = filteredEntries.Where(e => e.CompletedAt <= query.CompletedBefore.Value);
            }

            if (!string.IsNullOrEmpty(query.CorrelationId))
            {
                filteredEntries = filteredEntries.Where(e => e.CorrelationId == query.CorrelationId);
            }

            if (!string.IsNullOrEmpty(query.SourceSystem))
            {
                filteredEntries = filteredEntries.Where(e => e.SourceSystem == query.SourceSystem);
            }

            if (query.OnlyWithWarnings)
            {
                filteredEntries = filteredEntries.Where(e => e.Warnings.Count > 0);
            }

            if (query.OnlyWithSnapshots)
            {
                filteredEntries = filteredEntries.Where(e => e.SnapshotUsed);
            }

            // Apply sorting
            filteredEntries = query.SortOrder switch
            {
                AuditSortOrder.StartedAtDescending => filteredEntries.OrderByDescending(e => e.StartedAt),
                AuditSortOrder.StartedAtAscending => filteredEntries.OrderBy(e => e.StartedAt),
                AuditSortOrder.CompletedAtDescending => filteredEntries.OrderByDescending(e => e.CompletedAt),
                AuditSortOrder.CompletedAtAscending => filteredEntries.OrderBy(e => e.CompletedAt),
                AuditSortOrder.DurationDescending => filteredEntries.OrderByDescending(e => e.RecoveryDuration),
                AuditSortOrder.DurationAscending => filteredEntries.OrderBy(e => e.RecoveryDuration),
                AuditSortOrder.GrainIdAscending => filteredEntries.OrderBy(e => e.GrainId),
                _ => filteredEntries.OrderByDescending(e => e.StartedAt)
            };

            var totalCount = filteredEntries.Count();

            // Apply pagination
            if (query.Skip.HasValue)
            {
                filteredEntries = filteredEntries.Skip(query.Skip.Value);
            }

            var maxResults = query.MaxResults ?? 100;
            var resultEntries = filteredEntries.Take(maxResults).ToList();

            var hasMoreResults = totalCount > (query.Skip ?? 0) + resultEntries.Count;

            stopwatch.Stop();

            _logger.LogInformation(
                "Queried recovery audit history. TotalMatching: {TotalCount}, Returned: {ReturnedCount}, Duration: {QueryDuration}ms",
                totalCount, resultEntries.Count, stopwatch.ElapsedMilliseconds);

            return Task.FromResult(RecoveryAuditQueryResult.WithEntries(
                resultEntries,
                totalCount,
                hasMoreResults,
                query,
                stopwatch.Elapsed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query recovery audit history");
            throw new RecoveryException($"Audit query failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<RecoveryAuditEntry?> GetRecoveryAuditEntryAsync(
        string auditId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditId);

        _auditEntries.TryGetValue(auditId, out var entry);
        return Task.FromResult(entry);
    }

    /// <inheritdoc />
    public Task<RecoveryAuditEntry?> GetRecoveryAuditEntryByOperationIdAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationId);

        if (_operationToAuditMap.TryGetValue(operationId, out var auditId))
        {
            _auditEntries.TryGetValue(auditId, out var entry);
            return Task.FromResult(entry);
        }

        return Task.FromResult<RecoveryAuditEntry?>(null);
    }

    /// <inheritdoc />
    public Task<RecoveryAuditMetrics> GetAuditMetricsAsync(
        DateTimeOffset? fromTime = null,
        DateTimeOffset? toTime = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveFromTime = fromTime ?? DateTimeOffset.MinValue;
        var effectiveToTime = toTime ?? DateTimeOffset.UtcNow;

        if (effectiveFromTime >= effectiveToTime)
        {
            throw new ArgumentException("fromTime must be earlier than toTime");
        }

        try
        {
            var filteredEntries = _auditEntries.Values
                .Where(e => e.StartedAt >= effectiveFromTime && e.StartedAt <= effectiveToTime)
                .ToList();

            var totalOperations = filteredEntries.Count;
            var successfulRecoveries = filteredEntries.Count(e => e.Success);
            var failedRecoveries = filteredEntries.Count(e => !e.Success);

            var completedEntries = filteredEntries.Where(e => e.CompletedAt.HasValue).ToList();
            var recoveryTimes = completedEntries.ConvertAll(e => e.RecoveryDuration);

            var averageRecoveryTime = recoveryTimes.Count != 0 ? recoveryTimes.Average(t => t.TotalMilliseconds) : 0;
            var medianRecoveryTime = recoveryTimes.Count != 0 ?
                TimeSpan.FromMilliseconds(recoveryTimes.OrderBy(t => t.TotalMilliseconds).Skip(recoveryTimes.Count / 2).First().TotalMilliseconds) :
                TimeSpan.Zero;
            var p95RecoveryTime = recoveryTimes.Count != 0 ?
                TimeSpan.FromMilliseconds(recoveryTimes.OrderBy(t => t.TotalMilliseconds).Skip((int)(recoveryTimes.Count * 0.95)).FirstOrDefault().TotalMilliseconds) :
                TimeSpan.Zero;

            var strategyUsageCount = filteredEntries
                .GroupBy(e => e.StrategyUsed)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            var initiatorUsageCount = filteredEntries
                .GroupBy(e => e.InitiatedBy)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            var reasonUsageCount = filteredEntries
                .Where(e => !string.IsNullOrEmpty(e.Reason))
                .GroupBy(e => e.Reason!)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            var grainTypeUsageCount = filteredEntries
                .GroupBy(e => e.GrainType)
                .ToDictionary(g => g.Key, g => (long)g.Count());

            var recoveriesWithSnapshots = filteredEntries.Count(e => e.SnapshotUsed);
            var averageEventsReplayed = filteredEntries.Count != 0 ? filteredEntries.Average(e => e.EventsReplayed) : 0;
            var totalDataProcessedBytes = filteredEntries.Sum(e => e.DataProcessedBytes);

            var metrics = new RecoveryAuditMetrics
            {
                TotalRecoveryOperations = totalOperations,
                SuccessfulRecoveries = successfulRecoveries,
                FailedRecoveries = failedRecoveries,
                AverageRecoveryTime = TimeSpan.FromMilliseconds(averageRecoveryTime),
                MedianRecoveryTime = medianRecoveryTime,
                P95RecoveryTime = p95RecoveryTime,
                StrategyUsageCount = strategyUsageCount,
                InitiatorUsageCount = initiatorUsageCount,
                ReasonUsageCount = reasonUsageCount,
                GrainTypeUsageCount = grainTypeUsageCount,
                RecoveriesWithSnapshots = recoveriesWithSnapshots,
                AverageEventsReplayed = averageEventsReplayed,
                TotalDataProcessedBytes = totalDataProcessedBytes,
                MetricsPeriod = effectiveToTime - effectiveFromTime,
                PeriodStart = effectiveFromTime,
                PeriodEnd = effectiveToTime
            };

            _logger.LogInformation(
                "Generated audit metrics for period {FromTime} to {ToTime}. TotalOperations: {Total}, SuccessRate: {SuccessRate:F1}%",
                effectiveFromTime, effectiveToTime, totalOperations, metrics.SuccessRate);

            return Task.FromResult(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate audit metrics");
            throw new RecoveryException($"Metrics calculation failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> ExportAuditDataAsync(
        RecoveryAuditQuery query,
        AuditExportFormat format = AuditExportFormat.Csv,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            // Get the data to export
            var queryResult = QueryRecoveryHistoryAsync(query, cancellationToken).GetAwaiter().GetResult();
            var entries = queryResult.Entries;

            _logger.LogInformation(
                "Exporting {EntryCount} audit entries in {Format} format",
                entries.Count, format);

            return await (format switch
            {
                AuditExportFormat.Csv => Task.FromResult(ExportToCsv(entries)),
                AuditExportFormat.Json => Task.FromResult(ExportToJson(entries)),
                AuditExportFormat.Xml => Task.FromResult(ExportToXml(entries)),
                _ => throw new ArgumentException($"Export format {format} is not supported", nameof(format))
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export audit data in format {Format}", format);
            throw new RecoveryException($"Audit data export failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<int> CleanupAuditEntriesAsync(
        AuditRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retentionPolicy);

        try
        {
            var cutoffTime = DateTimeOffset.UtcNow - retentionPolicy.MaxAge;
            var failedRecoveryCutoffTime = retentionPolicy.FailedRecoveryMaxAge.HasValue
                ? DateTimeOffset.UtcNow - retentionPolicy.FailedRecoveryMaxAge.Value
                : cutoffTime;

            var entriesToRemove = new List<string>();

            foreach (var kvp in _auditEntries)
            {
                var entry = kvp.Value;
                var shouldRemove = false;

                // Check age-based retention
                if (entry.Success && entry.StartedAt < cutoffTime)
                {
                    shouldRemove = true;
                }
                else if (!entry.Success && retentionPolicy.RetainFailedRecoveries && entry.StartedAt < failedRecoveryCutoffTime)
                {
                    shouldRemove = true;
                }
                else if (!entry.Success && !retentionPolicy.RetainFailedRecoveries && entry.StartedAt < cutoffTime)
                {
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    entriesToRemove.Add(kvp.Key);
                }
            }

            // Check per-grain limits if specified
            if (retentionPolicy.MaxEntriesPerGrain.HasValue)
            {
                var entriesByGrain = _auditEntries.Values
                    .GroupBy(e => e.GrainId)
                    .Where(g => g.Count() > retentionPolicy.MaxEntriesPerGrain.Value);

                foreach (var grainGroup in entriesByGrain)
                {
                    var excessEntries = grainGroup
                        .OrderByDescending(e => e.StartedAt)
                        .Skip(retentionPolicy.MaxEntriesPerGrain.Value)
                        .Select(e => e.AuditId);

                    entriesToRemove.AddRange(excessEntries);
                }
            }

            // Archive before deletion if requested
            if (retentionPolicy.ArchiveBeforeDelete && !string.IsNullOrEmpty(retentionPolicy.ArchiveLocation))
            {
                // In a real implementation, this would archive to the specified location
                _logger.LogInformation("Archiving {Count} audit entries to {Location}",
                    entriesToRemove.Count, retentionPolicy.ArchiveLocation);
            }

            // Remove entries
            var removedCount = 0;
            foreach (var auditId in entriesToRemove)
            {
                if (_auditEntries.TryRemove(auditId, out var removedEntry))
                {
                    // Also remove from operation mapping
                    _operationToAuditMap.TryRemove(removedEntry.OperationId, out _);
                    _progressUpdates.TryRemove(auditId, out _);
                    removedCount++;
                }
            }

            _logger.LogInformation(
                "Cleaned up {RemovedCount} audit entries based on retention policy. MaxAge: {MaxAge}, RetainFailed: {RetainFailed}",
                removedCount, retentionPolicy.MaxAge, retentionPolicy.RetainFailedRecoveries);

            return Task.FromResult(removedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup audit entries");
            throw new RecoveryException($"Audit cleanup failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public Task<RecoveryAuditHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var totalEntries = _auditEntries.Count;
            var estimatedSizeBytes = totalEntries * 2048; // Rough estimate

            var recentOperations = _auditEntries.Values
                .Count(e => e.StartedAt > DateTimeOffset.UtcNow.AddHours(-1));

            // Note: In-memory storage is always healthy

            // Calculate average query time (simulate with recent performance)
            var averageQueryTime = TimeSpan.FromMilliseconds(Math.Max(10, totalEntries / 1000.0)); // Rough estimate

            return Task.FromResult(RecoveryAuditHealthStatus.Healthy(
                "RecoveryAuditService",
                totalEntries,
                estimatedSizeBytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for RecoveryAuditService");

            return Task.FromResult(RecoveryAuditHealthStatus.Unhealthy(
                "RecoveryAuditService",
                [$"Health check failed: {ex.Message}"]));
        }
    }

    /// <summary>
    /// Merges metadata dictionaries, with result metadata taking precedence.
    /// </summary>
    private static Dictionary<string, object>? MergeMetadata(
        Dictionary<string, object>? metadata1,
        Dictionary<string, object>? metadata2)
    {
        if (metadata1 == null && metadata2 == null)
        {
            return null;
        }

        var merged = new Dictionary<string, object>();

        if (metadata1 != null)
        {
            foreach (var kvp in metadata1)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (metadata2 != null)
        {
            foreach (var kvp in metadata2)
            {
                merged[kvp.Key] = kvp.Value; // Result metadata overwrites
            }
        }

        return merged.Count > 0 ? merged : null;
    }

    /// <summary>
    /// Exports audit entries to CSV format.
    /// </summary>
    private static byte[] ExportToCsv(IReadOnlyList<RecoveryAuditEntry> entries)
    {
        var csv = new StringBuilder();

        // Header
        csv.AppendLine("AuditId,OperationId,GrainId,GrainType,InitiatedBy,StartedAt,CompletedAt,TargetTimestamp,ActualTimestamp,Success,StrategyUsed,EventsReplayed,RecoveryDuration,SnapshotUsed,ValidationPassed,ErrorMessage,CorrelationId");

        // Data rows
        foreach (var entry in entries)
        {
            csv.AppendLine(CultureInfo.InvariantCulture, $"{EscapeCsv(entry.AuditId)},{EscapeCsv(entry.OperationId)},{EscapeCsv(entry.GrainId)},{EscapeCsv(entry.GrainType)},{EscapeCsv(entry.InitiatedBy)}," +
                          $"{entry.StartedAt:O},{entry.CompletedAt?.ToString("O", CultureInfo.InvariantCulture)}," +
                          $"{entry.TargetTimestamp?.ToString("O", CultureInfo.InvariantCulture)},{entry.ActualTimestamp?.ToString("O", CultureInfo.InvariantCulture)}," +
                          $"{entry.Success},{entry.StrategyUsed},{entry.EventsReplayed},{entry.RecoveryDuration.TotalMilliseconds}," +
                          $"{entry.SnapshotUsed},{entry.ValidationPassed},{EscapeCsv(entry.ErrorMessage)},{EscapeCsv(entry.CorrelationId)}");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    /// <summary>
    /// Exports audit entries to JSON format.
    /// </summary>
    private static byte[] ExportToJson(IReadOnlyList<RecoveryAuditEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, CachedJsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Exports audit entries to XML format.
    /// </summary>
    private static byte[] ExportToXml(IReadOnlyList<RecoveryAuditEntry> entries)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
            .AppendLine("<RecoveryAuditEntries>");

        foreach (var entry in entries)
        {
            xml.AppendLine("  <Entry>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <AuditId>{System.Security.SecurityElement.Escape(entry.AuditId)}</AuditId>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <OperationId>{System.Security.SecurityElement.Escape(entry.OperationId)}</OperationId>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <GrainId>{System.Security.SecurityElement.Escape(entry.GrainId)}</GrainId>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <GrainType>{System.Security.SecurityElement.Escape(entry.GrainType)}</GrainType>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <InitiatedBy>{System.Security.SecurityElement.Escape(entry.InitiatedBy)}</InitiatedBy>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <StartedAt>{entry.StartedAt:O}</StartedAt>");
            if (entry.CompletedAt.HasValue)
            {
                xml.AppendLine(CultureInfo.InvariantCulture, $"    <CompletedAt>{entry.CompletedAt:O}</CompletedAt>");
            }

            xml.AppendLine(CultureInfo.InvariantCulture, $"    <Success>{entry.Success}</Success>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <StrategyUsed>{entry.StrategyUsed}</StrategyUsed>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <EventsReplayed>{entry.EventsReplayed}</EventsReplayed>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <RecoveryDuration>{entry.RecoveryDuration}</RecoveryDuration>")
                .AppendLine(CultureInfo.InvariantCulture, $"    <CorrelationId>{System.Security.SecurityElement.Escape(entry.CorrelationId)}</CorrelationId>")
                .AppendLine("  </Entry>");
        }

        xml.AppendLine("</RecoveryAuditEntries>");
        return Encoding.UTF8.GetBytes(xml.ToString());
    }

    /// <summary>
    /// Escapes a string for CSV format.
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }
}
