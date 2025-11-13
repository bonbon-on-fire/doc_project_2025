using System.Reflection;
using System.Text.Json;

namespace AIChat.Server.Services.EventStore.Orleans;

/// <summary>
/// Service for managing snapshots of Orleans grain states.
/// Provides integration between Orleans grains and the snapshot management system
/// without creating circular dependencies between projects.
/// </summary>
public interface IOrleansSnapshotService
{
    /// <summary>
    /// Creates a snapshot of the specified Orleans grain state.
    /// </summary>
    /// <typeparam name="TGrainState">The type of grain state</typeparam>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="grainState">The current grain state</param>
    /// <param name="policy">Snapshot creation policy</param>
    /// <param name="metadata">Additional metadata for the snapshot</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the snapshot creation operation</returns>
    Task<SnapshotWriteResult> CreateGrainSnapshotAsync<TGrainState>(
        string grainId,
        TGrainState grainState,
        SnapshotCreationPolicy? policy = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        where TGrainState : class;

    /// <summary>
    /// Attempts to restore Orleans grain state from a snapshot.
    /// </summary>
    /// <typeparam name="TGrainState">The type of grain state</typeparam>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="targetVersion">Optional target version to restore to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The restore result with the restored state if successful</returns>
    Task<OrleansSnapshotRestoreResult<TGrainState>> RestoreGrainSnapshotAsync<TGrainState>(
        string grainId,
        long? targetVersion = null,
        CancellationToken cancellationToken = default)
        where TGrainState : class, new();

    /// <summary>
    /// Checks if a snapshot should be created for the specified grain based on policy.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="currentVersion">Current grain state version</param>
    /// <param name="policy">Snapshot creation policy</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a snapshot should be created</returns>
    Task<bool> ShouldCreateGrainSnapshotAsync(
        string grainId,
        long currentVersion,
        SnapshotCreationPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest snapshot metadata for a grain.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The latest snapshot metadata if available</returns>
    Task<SnapshotMetadata?> GetLatestGrainSnapshotMetadataAsync(
        string grainId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup of old snapshots based on retention policy.
    /// </summary>
    /// <param name="retentionPolicy">The retention policy to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The cleanup result</returns>
    Task<SnapshotCleanupResult> CleanupGrainSnapshotsAsync(
        SnapshotRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of the Orleans snapshot service.
/// </summary>
public class OrleansSnapshotService : IOrleansSnapshotService
{
    private readonly ISnapshotManager _snapshotManager;
    private readonly ISnapshotStore _snapshotStore;
    private readonly ILogger<OrleansSnapshotService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the OrleansSnapshotService.
    /// </summary>
    /// <param name="snapshotManager">The snapshot manager</param>
    /// <param name="snapshotStore">The snapshot store</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public OrleansSnapshotService(
        ISnapshotManager snapshotManager,
        ISnapshotStore snapshotStore,
        ILogger<OrleansSnapshotService> logger)
    {
        _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public async Task<SnapshotWriteResult> CreateGrainSnapshotAsync<TGrainState>(
        string grainId,
        TGrainState grainState,
        SnapshotCreationPolicy? policy = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
        where TGrainState : class
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(grainState);

        try
        {
            var streamId = GetGrainStreamId(grainId, typeof(TGrainState));
            var version = GetGrainStateVersion(grainState);

            // Check policy if provided
            if (policy != null)
            {
                var shouldCreate = await _snapshotManager.ShouldCreateSnapshotAsync(
                    streamId, version, policy, cancellationToken);

                if (!shouldCreate)
                {
                    return SnapshotWriteResult.CreateFailure("Snapshot creation policy conditions not met");
                }
            }

            // Create enhanced metadata
            var enhancedMetadata = CreateGrainMetadata(grainId, typeof(TGrainState), metadata);

            _logger.LogDebug("Creating snapshot for Orleans grain {GrainType} {GrainId}",
                typeof(TGrainState).Name, grainId);

            var result = await _snapshotManager.CreateSnapshotAsync(
                streamId,
                version,
                grainState,
                enhancedMetadata,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation("Created snapshot for Orleans grain {GrainType} {GrainId} " +
                                     "(version: {Version}, size: {Size} bytes)",
                    typeof(TGrainState).Name, grainId, version, result.CompressedSize);
            }
            else
            {
                _logger.LogWarning("Failed to create snapshot for Orleans grain {GrainType} {GrainId}: {Error}",
                    typeof(TGrainState).Name, grainId, result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating snapshot for Orleans grain {GrainType} {GrainId}",
                typeof(TGrainState).Name, grainId);

            return SnapshotWriteResult.CreateFailure($"Error creating snapshot: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OrleansSnapshotRestoreResult<TGrainState>> RestoreGrainSnapshotAsync<TGrainState>(
        string grainId,
        long? targetVersion = null,
        CancellationToken cancellationToken = default)
        where TGrainState : class, new()
    {
        ArgumentNullException.ThrowIfNull(grainId);

        try
        {
            var streamId = GetGrainStreamId(grainId, typeof(TGrainState));

            _logger.LogDebug("Attempting to restore Orleans grain {GrainType} {GrainId} from snapshot",
                typeof(TGrainState).Name, grainId);

            // Create a simple projection for Orleans grain state restoration
            var projection = new OrleansGrainProjection<TGrainState>();

            var restoreResult = await _snapshotManager.RestoreFromSnapshotAsync<TGrainState>(
                streamId,
                projection,
                targetVersion,
                cancellationToken);

            if (restoreResult.Success && restoreResult.State != null)
            {
                var snapshotAge = restoreResult.SnapshotMetadata?.Timestamp != null
                    ? DateTimeOffset.UtcNow - restoreResult.SnapshotMetadata.Timestamp
                    : TimeSpan.Zero;

                _logger.LogInformation("Restored Orleans grain {GrainType} {GrainId} from snapshot " +
                                     "(version: {Version}, age: {Age})",
                    typeof(TGrainState).Name, grainId, restoreResult.SnapshotVersion, snapshotAge);

                return OrleansSnapshotRestoreResult.CreateSuccess(
                    restoreResult.State,
                    restoreResult.SnapshotVersion,
                    snapshotAge,
                    restoreResult.EventsReplayed);
            }
            _logger.LogDebug("No snapshot available for Orleans grain {GrainType} {GrainId}",
                typeof(TGrainState).Name, grainId);

            return OrleansSnapshotRestoreResult.CreateNotFound<TGrainState>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring snapshot for Orleans grain {GrainType} {GrainId}",
                typeof(TGrainState).Name, grainId);

            return OrleansSnapshotRestoreResult.CreateFailure<TGrainState>($"Error restoring snapshot: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ShouldCreateGrainSnapshotAsync(
        string grainId,
        long currentVersion,
        SnapshotCreationPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);
        ArgumentNullException.ThrowIfNull(policy);

        var streamId = GetGrainStreamId(grainId, typeof(object)); // Type doesn't matter for this check

        return await _snapshotManager.ShouldCreateSnapshotAsync(
            streamId, currentVersion, policy, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SnapshotMetadata?> GetLatestGrainSnapshotMetadataAsync(
        string grainId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grainId);

        try
        {
            var streamId = GetGrainStreamId(grainId, typeof(object)); // Type doesn't matter for metadata

            var latestSnapshot = await _snapshotStore.GetLatestSnapshotAsync<object>(streamId, cancellationToken);

            return latestSnapshot.Success ? latestSnapshot.Metadata : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest snapshot metadata for Orleans grain {GrainId}", grainId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SnapshotCleanupResult> CleanupGrainSnapshotsAsync(
        SnapshotRetentionPolicy retentionPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retentionPolicy);

        try
        {
            _logger.LogInformation("Starting Orleans grain snapshots cleanup with retention policy");

            var result = await _snapshotManager.CleanupSnapshotsAsync(retentionPolicy, false, cancellationToken);

            _logger.LogInformation("Orleans grain snapshots cleanup completed: {CleanedCount} snapshots removed",
                result.SnapshotsDeleted);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Orleans grain snapshots cleanup");
            throw;
        }
    }

    #region Helper Methods

    /// <summary>
    /// Gets the stream ID for a grain snapshot.
    /// </summary>
    private static string GetGrainStreamId(string grainId, Type grainStateType)
    {
        return $"orleans-grain:{grainStateType.Name}:{grainId}";
    }

    /// <summary>
    /// Extracts the version from grain state if available.
    /// </summary>
    private static long GetGrainStateVersion<TGrainState>(TGrainState grainState)
        where TGrainState : class
    {
        // Look for common version properties
        var versionProperty = typeof(TGrainState).GetProperty("Version", BindingFlags.Public | BindingFlags.Instance) ??
                             typeof(TGrainState).GetProperty("StateVersion", BindingFlags.Public | BindingFlags.Instance);

        if (versionProperty?.PropertyType == typeof(long))
        {
            return (long)(versionProperty.GetValue(grainState) ?? 1L);
        }

        // Default to version 1 if no version property found
        return 1L;
    }

    /// <summary>
    /// Creates enhanced metadata for grain snapshots.
    /// </summary>
    private static Dictionary<string, object> CreateGrainMetadata(
        string grainId,
        Type grainStateType,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["grainId"] = grainId,
            ["grainStateType"] = grainStateType.FullName ?? grainStateType.Name,
            ["snapshotSource"] = "Orleans",
            ["createdAt"] = DateTimeOffset.UtcNow
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }

    #endregion Helper Methods
}

/// <summary>
/// Simple projection implementation for Orleans grain state restoration.
/// Orleans grains typically restore directly from snapshot state without event replay.
/// </summary>
/// <typeparam name="TState">The type of grain state</typeparam>
internal sealed class OrleansGrainProjection<TState> : IEventProjection<TState>
    where TState : class, new()
{
    /// <summary>
    /// Gets the name of this projection.
    /// </summary>
    public string Name => $"Orleans{typeof(TState).Name}Projection";

    /// <summary>
    /// Gets the version of this projection.
    /// </summary>
    public int Version => 1;

    /// <summary>
    /// Creates the initial state for Orleans grain restoration.
    /// </summary>
    /// <returns>A new instance of the grain state</returns>
    public TState CreateInitialState()
    {
        return new TState();
    }

    /// <summary>
    /// Applies an event to the current state. Orleans grains typically don't use event sourcing,
    /// so this returns the current state unchanged.
    /// </summary>
    /// <param name="currentState">The current state</param>
    /// <param name="eventData">The event to apply (ignored for Orleans grains)</param>
    /// <returns>The unchanged current state</returns>
    public TState Apply(TState currentState, IEvent eventData)
    {
        // Orleans grains restore directly from snapshot state, no event application needed
        return currentState;
    }

    /// <summary>
    /// Determines if this projection can handle the given event type.
    /// Orleans grains typically don't handle events during restoration.
    /// </summary>
    /// <param name="eventType">The event type to check</param>
    /// <returns>Always false as Orleans grains don't process events during snapshot restoration</returns>
    public bool CanHandle(string eventType)
    {
        // Orleans grains restore directly from snapshot, no event handling needed
        return false;
    }

    /// <summary>
    /// Projects the current state. For Orleans grains, this returns a new instance.
    /// </summary>
    public Task<TState> ProjectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateInitialState());
    }

    /// <summary>
    /// Orleans grains typically don't use event sourcing, so this is a no-op.
    /// The grain state is restored directly from the snapshot.
    /// </summary>
    public Task ApplyEventsAsync(IEnumerable<IEvent> events, CancellationToken cancellationToken = default)
    {
        // Orleans grains restore directly from snapshot state, no event replay needed
        return Task.CompletedTask;
    }
}

/// <summary>
/// Result of Orleans grain snapshot restoration.
/// </summary>
/// <typeparam name="TGrainState">The type of grain state</typeparam>
public record OrleansSnapshotRestoreResult<TGrainState>
    where TGrainState : class
{
    /// <summary>
    /// Gets whether the restoration was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the restored grain state if successful.
    /// </summary>
    public TGrainState? State { get; init; }

    /// <summary>
    /// Gets the version of the restored snapshot.
    /// </summary>
    public long SnapshotVersion { get; init; }

    /// <summary>
    /// Gets the age of the snapshot that was restored.
    /// </summary>
    public TimeSpan SnapshotAge { get; init; }

    /// <summary>
    /// Gets the number of events replayed from the snapshot (typically 0 for Orleans grains).
    /// </summary>
    public int EventsReplayedFromSnapshot { get; init; }

    /// <summary>
    /// Gets the error message if restoration failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Factory methods for creating OrleansSnapshotRestoreResult instances.
/// </summary>
public static class OrleansSnapshotRestoreResult
{
    /// <summary>
    /// Creates a successful restoration result.
    /// </summary>
    public static OrleansSnapshotRestoreResult<TGrainState> CreateSuccess<TGrainState>(
        TGrainState state,
        long snapshotVersion,
        TimeSpan snapshotAge,
        int eventsReplayed = 0)
        where TGrainState : class
    {
        return new OrleansSnapshotRestoreResult<TGrainState>
        {
            Success = true,
            State = state,
            SnapshotVersion = snapshotVersion,
            SnapshotAge = snapshotAge,
            EventsReplayedFromSnapshot = eventsReplayed
        };
    }

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    public static OrleansSnapshotRestoreResult<TGrainState> CreateNotFound<TGrainState>()
        where TGrainState : class
    {
        return new OrleansSnapshotRestoreResult<TGrainState>
        {
            Success = false,
            Error = "No snapshot found"
        };
    }

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static OrleansSnapshotRestoreResult<TGrainState> CreateFailure<TGrainState>(string error)
        where TGrainState : class
    {
        return new OrleansSnapshotRestoreResult<TGrainState>
        {
            Success = false,
            Error = error
        };
    }
}
