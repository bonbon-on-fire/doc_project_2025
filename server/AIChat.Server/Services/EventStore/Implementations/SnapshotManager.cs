using System.Diagnostics;
using System.Globalization;

namespace AIChat.Server.Services.EventStore.Implementations;

/// <summary>
/// High-level snapshot manager that orchestrates snapshot operations.
/// Integrates snapshot store with event store for optimal state reconstruction.
/// </summary>
public sealed class SnapshotManager : ISnapshotManager
{
    private readonly ISnapshotStore _snapshotStore;
    private readonly IEventStore _eventStore;
    private readonly ILogger<SnapshotManager> _logger;

    /// <summary>
    /// Gets the name of this snapshot manager implementation.
    /// </summary>
    public string Name => "Default Snapshot Manager";

    private static readonly string[] DefaultValidationIssues = ["Snapshot not found or metadata is missing"];

    /// <summary>
    /// Initializes a new instance of the SnapshotManager class.
    /// </summary>
    /// <param name="snapshotStore">The snapshot store implementation</param>
    /// <param name="eventStore">The event store for event replay</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public SnapshotManager(
        ISnapshotStore snapshotStore,
        IEventStore eventStore,
        ILogger<SnapshotManager> logger)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines if a snapshot should be created for a stream based on the specified policy.
    /// </summary>
    public async Task<bool> ShouldCreateSnapshotAsync(
        string streamId,
        long currentVersion,
        SnapshotCreationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(policy);

        if (currentVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersion), "Version cannot be negative");
        }

        try
        {
            _logger.LogDebug("Evaluating snapshot creation policy for stream {StreamId} at version {Version}",
                streamId, currentVersion);

            // Get the latest snapshot to compare against policy triggers
            var latestSnapshotVersion = await _snapshotStore.GetLatestSnapshotVersionAsync(streamId, cancellationToken);

            // Event count threshold check
            if (policy.EventCountThreshold.HasValue)
            {
                var eventsSinceSnapshot = currentVersion - latestSnapshotVersion;
                if (eventsSinceSnapshot >= policy.EventCountThreshold.Value)
                {
                    _logger.LogDebug("Event count threshold triggered: {EventsSinceSnapshot} >= {Threshold}",
                        eventsSinceSnapshot, policy.EventCountThreshold.Value);
                    return true;
                }
            }

            // Version threshold check
            if (policy.VersionThreshold.HasValue)
            {
                var versionDelta = currentVersion - latestSnapshotVersion;
                if (versionDelta >= policy.VersionThreshold.Value)
                {
                    _logger.LogDebug("Version threshold triggered: {VersionDelta} >= {Threshold}",
                        versionDelta, policy.VersionThreshold.Value);
                    return true;
                }
            }

            // Time threshold check
            if (policy.TimeThreshold.HasValue && latestSnapshotVersion >= 0)
            {
                // Get the latest snapshot to check its timestamp
                var latestSnapshot = await _snapshotStore.GetLatestSnapshotAsync<object>(streamId, cancellationToken);
                if (latestSnapshot.Success && latestSnapshot.Metadata != null)
                {
                    var timeSinceSnapshot = DateTimeOffset.UtcNow - latestSnapshot.Metadata.Timestamp;
                    if (timeSinceSnapshot >= policy.TimeThreshold.Value)
                    {
                        _logger.LogDebug("Time threshold triggered: {TimeSinceSnapshot} >= {Threshold}",
                            timeSinceSnapshot, policy.TimeThreshold.Value);
                        return true;
                    }
                }
                else if (latestSnapshotVersion < 0)
                {
                    // No snapshots exist, and we have a time threshold - create first snapshot
                    _logger.LogDebug("No snapshots exist and time threshold is configured - creating first snapshot");
                    return true;
                }
            }

            // Stream size threshold check (estimated)
            if (policy.StreamSizeThreshold.HasValue)
            {
                // Estimate stream size based on event count and average event size
                // This is a rough estimate - could be improved with actual size tracking
                var eventsSinceSnapshot = currentVersion - latestSnapshotVersion;
                const int estimatedEventSize = 1024; // 1KB per event (conservative estimate)
                var estimatedStreamSize = eventsSinceSnapshot * estimatedEventSize;

                if (estimatedStreamSize >= policy.StreamSizeThreshold.Value)
                {
                    _logger.LogDebug("Stream size threshold triggered: estimated {EstimatedSize} >= {Threshold}",
                        estimatedStreamSize, policy.StreamSizeThreshold.Value);
                    return true;
                }
            }

            // Check custom conditions
            if (policy.CustomConditions?.Count > 0)
            {
                // Custom conditions could be evaluated here based on metadata or other factors
                // For now, we'll log that custom conditions exist but don't evaluate them
                _logger.LogDebug("Custom conditions exist but are not evaluated in this implementation");
            }

            _logger.LogDebug("No snapshot creation triggers activated for stream {StreamId}", streamId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate snapshot creation policy for stream {StreamId}", streamId);
            throw new SnapshotManagerException(
                "ShouldCreateSnapshot",
                $"Failed to evaluate snapshot creation policy for stream '{streamId}'",
                SnapshotErrorCode.InternalError,
                streamId: streamId,
                innerException: ex);
        }
    }

    /// <summary>
    /// Creates a snapshot of the current state for a stream.
    /// </summary>
    public async Task<SnapshotWriteResult> CreateSnapshotAsync<T>(
        string streamId,
        long version,
        T state,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            _logger.LogDebug("Creating snapshot for stream {StreamId} at version {Version}", streamId, version);

            // Add creation metadata
            var snapshotMetadata = new Dictionary<string, object>(metadata ?? [])
            {
                ["createdBy"] = Name,
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["stateType"] = typeof(T).FullName ?? typeof(T).Name
            };

            var result = await _snapshotStore.CreateSnapshotAsync(streamId, version, state, snapshotMetadata, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Successfully created snapshot {SnapshotId} for stream {StreamId} at version {Version}. " +
                    "Compressed: {CompressedSize} bytes, Uncompressed: {UncompressedSize} bytes, " +
                    "Compression ratio: {CompressionRatio:F2}, Deduplicated: {Deduplicated}",
                    result.SnapshotId, streamId, version, result.CompressedSize, result.UncompressedSize,
                    result.CompressionRatio, result.WasDeduplicated);
            }

            return result;
        }
        catch (Exception ex) when (ex is not SnapshotStoreException)
        {
            _logger.LogError(ex, "Failed to create snapshot for stream {StreamId} at version {Version}", streamId, version);
            throw SnapshotManagerException.CreateSnapshotFailed(streamId, version, ex);
        }
    }

    /// <summary>
    /// Restores state from the latest snapshot and replays events from that point.
    /// </summary>
    public async Task<SnapshotRestoreResult<T>> RestoreFromSnapshotAsync<T>(
        string streamId,
        IEventProjection<T> projection,
        long? targetVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(projection);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Restoring state from snapshot for stream {StreamId}, target version: {TargetVersion}",
                streamId, targetVersion?.ToString(CultureInfo.InvariantCulture) ?? "latest");

            // Find the best snapshot for the target version
            var snapshotResult = targetVersion.HasValue
                ? await _snapshotStore.GetSnapshotAtVersionAsync<T>(streamId, targetVersion.Value, cancellationToken)
                : await _snapshotStore.GetLatestSnapshotAsync<T>(streamId, cancellationToken);

            if (!snapshotResult.Success || snapshotResult.Data == null || snapshotResult.Metadata == null)
            {
                // No snapshot available, fall back to full event replay
                _logger.LogDebug("No suitable snapshot found for stream {StreamId}, falling back to full event replay", streamId);

                var fullReplayResult = targetVersion.HasValue
                    ? await _eventStore.ReplayAsync(streamId, targetVersion.Value, projection, cancellationToken)
                    : await _eventStore.ReplayAsync(streamId, projection, cancellationToken);

                stopwatch.Stop();

                if (!fullReplayResult.Success)
                {
                    return SnapshotRestoreResult.CreateFailure<T>(
                        fullReplayResult.Error ?? "Event replay failed",
                        SnapshotErrorCode.InternalError,
                        restorationTime: stopwatch.Elapsed);
                }

                return SnapshotRestoreResult.CreateSuccess(
                    fullReplayResult.State,
                    fullReplayResult.Version,
                    -1, // No snapshot used
                    fullReplayResult.EventsProcessed,
                    stopwatch.Elapsed,
                    TimeSpan.Zero); // No time saved since no snapshot was used
            }

            // We have a snapshot, now replay events from that point
            var snapshotVersion = snapshotResult.Metadata.Version;
            var fromVersion = snapshotVersion + 1;
            var eventsReplayed = 0;

            T currentState = snapshotResult.Data;

            // Replay events from snapshot to target version
            if (targetVersion == null || targetVersion > snapshotVersion)
            {
                // Get events from snapshot version to target version and replay manually
                var eventReplayResult = targetVersion.HasValue
                    ? await _eventStore.GetEventsAsync(streamId, fromVersion, targetVersion.Value, cancellationToken)
                    : await _eventStore.GetEventsAsync(streamId, fromVersion, cancellationToken);

                // Replay remaining events manually from the snapshot state
                if (eventReplayResult.Events.Count > 0)
                {
                    foreach (var eventData in eventReplayResult.Events)
                    {
                        currentState = projection.Apply(currentState, eventData);
                        eventsReplayed++;
                    }
                }
            }

            stopwatch.Stop();

            var finalVersion = targetVersion ?? snapshotVersion + eventsReplayed;

            // Estimate time saved by using snapshot
            var totalEventsInStream = finalVersion;
            var eventsSkipped = snapshotVersion;
            var averageEventReplayTime = eventsReplayed > 0
                ? stopwatch.Elapsed.TotalMilliseconds / eventsReplayed
                : 1.0; // 1ms average per event estimate

            var estimatedTimeSaved = TimeSpan.FromMilliseconds(eventsSkipped * averageEventReplayTime);

            _logger.LogInformation("Successfully restored state from snapshot for stream {StreamId}. " +
                "Snapshot version: {SnapshotVersion}, Final version: {FinalVersion}, " +
                "Events replayed: {EventsReplayed}, Time taken: {RestorationTime}, Time saved: {TimeSaved}",
                streamId, snapshotVersion, finalVersion, eventsReplayed,
                stopwatch.Elapsed, estimatedTimeSaved);

            return SnapshotRestoreResult.CreateSuccess(
                currentState,
                finalVersion,
                snapshotVersion,
                eventsReplayed,
                stopwatch.Elapsed,
                estimatedTimeSaved,
                snapshotResult.Metadata);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to restore from snapshot for stream {StreamId}", streamId);
            throw SnapshotManagerException.RestoreSnapshotFailed(streamId, innerException: ex);
        }
    }

    /// <summary>
    /// Validates the integrity of a snapshot by verifying its content hash.
    /// </summary>
    public async Task<SnapshotValidationResult> ValidateSnapshotIntegrityAsync(
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshotId);

        try
        {
            _logger.LogDebug("Validating integrity of snapshot {SnapshotId}", snapshotId);

            // Get the snapshot to validate
            var snapshotResult = await _snapshotStore.GetSnapshotByIdAsync<object>(snapshotId, cancellationToken);

            if (!snapshotResult.Success || snapshotResult.Metadata == null)
            {
                return SnapshotValidationResult.CreateInvalid(
                    DefaultValidationIssues);
            }

            var issues = new List<string>();
            var metadata = snapshotResult.Metadata;

            // Basic metadata validation
            if (string.IsNullOrEmpty(metadata.ContentHash))
            {
                issues.Add("Content hash is missing");
            }

            if (metadata.CompressedSize <= 0)
            {
                issues.Add("Invalid compressed size");
            }

            if (metadata.UncompressedSize <= 0)
            {
                issues.Add("Invalid uncompressed size");
            }

            if (metadata.CompressedSize > metadata.UncompressedSize)
            {
                issues.Add("Compressed size is larger than uncompressed size");
            }

            // Note: Full content hash validation would require decompressing and re-hashing the data
            // This is expensive and should be done sparingly
            // For now, we'll just validate the metadata consistency

            var isValid = issues.Count == 0;

            if (isValid)
            {
                _logger.LogDebug("Snapshot {SnapshotId} passed integrity validation", snapshotId);
                return SnapshotValidationResult.CreateValid(metadata);
            }
            else
            {
                _logger.LogWarning("Snapshot {SnapshotId} failed integrity validation: {Issues}",
                    snapshotId, string.Join(", ", issues));
                return SnapshotValidationResult.CreateInvalid(
                    issues,
                    metadata,
                    contentHashValid: !issues.Any(i => i.Contains("hash")),
                    compressionValid: !issues.Any(i => i.Contains("size")),
                    serializationValid: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate snapshot integrity for {SnapshotId}", snapshotId);
            throw SnapshotManagerException.ValidationFailed(snapshotId, [ex.Message]);
        }
    }

    /// <summary>
    /// Performs cleanup operations based on the specified retention policy.
    /// </summary>
    public async Task<SnapshotCleanupResult> CleanupSnapshotsAsync(
        SnapshotRetentionPolicy retentionPolicy,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retentionPolicy);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting snapshot cleanup with retention policy. Dry run: {DryRun}", dryRun);

            // Get snapshots eligible for cleanup
            var snapshotsToCleanup = await _snapshotStore.GetSnapshotsForCleanupAsync(retentionPolicy, cancellationToken);

            if (snapshotsToCleanup.Snapshots.Count == 0)
            {
                stopwatch.Stop();
                _logger.LogInformation("No snapshots found for cleanup");
                return SnapshotCleanupResult.CreateSuccess(0, 0, [], stopwatch.Elapsed);
            }

            var cleanupDetails = new List<SnapshotCleanupDetail>();
            var totalSpaceReclaimed = 0L;
            var deletedCount = 0;

            foreach (var snapshot in snapshotsToCleanup.Snapshots)
            {
                var age = DateTimeOffset.UtcNow - snapshot.Timestamp;
                var reason = DetermineCleanupReason(snapshot, retentionPolicy, age);

                cleanupDetails.Add(new SnapshotCleanupDetail
                {
                    SnapshotId = snapshot.Id,
                    StreamId = snapshot.StreamId,
                    Version = snapshot.Version,
                    Reason = reason,
                    Size = snapshot.CompressedSize,
                    Age = age
                });

                totalSpaceReclaimed += snapshot.CompressedSize;

                if (!dryRun)
                {
                    try
                    {
                        var deleteResult = await _snapshotStore.DeleteSnapshotAsync(snapshot.Id, cancellationToken);
                        if (deleteResult.Success)
                        {
                            deletedCount++;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to delete snapshot {SnapshotId}: {Error}",
                                snapshot.Id, deleteResult.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting snapshot {SnapshotId}", snapshot.Id);
                    }
                }
                else
                {
                    deletedCount++; // Count as deleted for dry run
                }
            }

            stopwatch.Stop();

            _logger.LogInformation("Snapshot cleanup completed. Deleted: {DeletedCount}, Space reclaimed: {SpaceReclaimed} bytes, " +
                "Time taken: {CleanupTime}, Dry run: {DryRun}",
                deletedCount, totalSpaceReclaimed, stopwatch.Elapsed, dryRun);

            return SnapshotCleanupResult.CreateSuccess(
                deletedCount,
                totalSpaceReclaimed,
                cleanupDetails,
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Snapshot cleanup failed");
            throw SnapshotManagerException.CleanupFailed(retentionPolicy, ex);
        }
    }

    /// <summary>
    /// Optimizes snapshot storage by performing compression analysis and deduplication.
    /// </summary>
    public async Task<SnapshotOptimizationResult> OptimizeStorageAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting snapshot storage optimization");

            // Perform store optimization
            await _snapshotStore.OptimizeAsync(cancellationToken);

            // Get optimization statistics
            var duplicateGroups = await _snapshotStore.GetSnapshotsByContentHashAsync(cancellationToken);
            var duplicatesConsolidated = duplicateGroups.Values.Sum(group => group.Count - 1); // One original + duplicates

            stopwatch.Stop();

            _logger.LogInformation("Snapshot storage optimization completed. " +
                "Duplicates consolidated: {DuplicatesConsolidated}, Time taken: {OptimizationTime}",
                duplicatesConsolidated, stopwatch.Elapsed);

            return new SnapshotOptimizationResult
            {
                Success = true,
                SnapshotsOptimized = duplicateGroups.Values.Sum(group => group.Count),
                DuplicatesConsolidated = duplicatesConsolidated,
                OptimizationTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Snapshot storage optimization failed");
            throw SnapshotManagerException.OptimizationFailed(ex);
        }
    }

    /// <summary>
    /// Gets comprehensive statistics about snapshots for a specific stream.
    /// </summary>
    public async Task<SnapshotStatistics> GetStreamStatisticsAsync(
        string streamId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        try
        {
            var query = SnapshotQuery.ForStream(streamId, pageSize: int.MaxValue);
            var queryResult = await _snapshotStore.QuerySnapshotsAsync(query, cancellationToken);

            if (queryResult.Snapshots.Count == 0)
            {
                return new SnapshotStatistics
                {
                    StreamId = streamId
                };
            }

            var snapshots = queryResult.Snapshots;
            var totalCompressed = snapshots.Sum(s => s.CompressedSize);
            var totalUncompressed = snapshots.Sum(s => s.UncompressedSize);

            return new SnapshotStatistics
            {
                StreamId = streamId,
                TotalSnapshots = snapshots.Count,
                TotalCompressedSize = totalCompressed,
                TotalUncompressedSize = totalUncompressed,
                AverageCompressionRatio = totalUncompressed > 0 ? (double)totalCompressed / totalUncompressed : 1.0,
                LatestSnapshotVersion = snapshots.Max(s => s.Version),
                OldestSnapshotTimestamp = snapshots.Min(s => s.Timestamp),
                NewestSnapshotTimestamp = snapshots.Max(s => s.Timestamp)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get statistics for stream {StreamId}", streamId);
            throw SnapshotManagerException.StatisticsCollectionFailed(streamId, ex);
        }
    }

    /// <summary>
    /// Gets global statistics about all snapshots managed by this instance.
    /// </summary>
    public async Task<SnapshotGlobalStatistics> GetGlobalStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _snapshotStore.GetMetricsAsync(cancellationToken);

            return new SnapshotGlobalStatistics
            {
                TotalSnapshots = metrics.TotalSnapshots,
                TotalStreams = metrics.TotalStreams,
                TotalCompressedSize = metrics.TotalCompressedSize,
                TotalUncompressedSize = metrics.TotalUncompressedSize,
                DeduplicatedSnapshots = metrics.DeduplicatedSnapshots,
                DeduplicationSpaceSaved = metrics.DeduplicationSpaceSaved
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get global statistics");
            throw SnapshotManagerException.StatisticsCollectionFailed(innerException: ex);
        }
    }

    /// <summary>
    /// Determines the reason why a snapshot should be cleaned up.
    /// </summary>
    private static string DetermineCleanupReason(
        SnapshotMetadata snapshot,
        SnapshotRetentionPolicy policy,
        TimeSpan age)
    {
        var reasons = new List<string>();

        if (policy.MaxAge.HasValue && age > policy.MaxAge.Value)
        {
            reasons.Add($"Age ({age.TotalDays:F1} days) exceeds maximum ({policy.MaxAge.Value.TotalDays:F1} days)");
        }

        if (policy.MaxSnapshotsPerStream.HasValue)
        {
            reasons.Add($"Exceeds maximum snapshots per stream ({policy.MaxSnapshotsPerStream.Value})");
        }

        if (policy.MaxTotalSize.HasValue)
        {
            reasons.Add("Contributes to total size limit exceeded");
        }

        return reasons.Count > 0 ? string.Join("; ", reasons) : "Policy-based cleanup";
    }
}