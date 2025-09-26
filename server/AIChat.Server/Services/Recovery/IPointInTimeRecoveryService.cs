using AIChat.Server.Services.EventStore;

namespace AIChat.Server.Services.Recovery;

/// <summary>
/// Core point-in-time recovery operations.
/// Focuses solely on executing recovery operations following Single Responsibility Principle.
/// </summary>
public interface IPointInTimeRecovery
{
    /// <summary>
    /// Recovers grain state to a specific timestamp using optimal strategy.
    /// Leverages snapshots and event replay for efficient recovery.
    /// </summary>
    /// <typeparam name="T">The type of state to recover</typeparam>
    /// <param name="request">The point-in-time recovery request</param>
    /// <param name="projection">The projection logic for rebuilding state</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projection is null</exception>
    /// <exception cref="RecoveryException">Thrown when recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryResult<T>> RecoverToTimestampAsync<T>(
        PointInTimeRecoveryRequest request,
        IEventProjection<T> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers grain state to a specific event version.
    /// Direct version-based recovery for precise control.
    /// </summary>
    /// <typeparam name="T">The type of state to recover</typeparam>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="targetVersion">The target version to recover to</param>
    /// <param name="projection">The projection logic for rebuilding state</param>
    /// <param name="validationLevel">The level of validation to perform</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The result of the recovery operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId or projection is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when targetVersion is negative</exception>
    /// <exception cref="RecoveryException">Thrown when recovery fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryResult<T>> RecoverToVersionAsync<T>(
        string grainId,
        long targetVersion,
        IEventProjection<T> projection,
        RecoveryValidationLevel validationLevel = RecoveryValidationLevel.Standard,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recovery validation and possibility checking operations.
/// Focused on assessing recovery feasibility and providing recommendations.
/// </summary>
public interface IRecoveryValidation
{
    /// <summary>
    /// Validates if recovery to a specific point in time is possible.
    /// Checks data availability and integrity before attempting recovery.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="targetTimestamp">The target timestamp to validate</param>
    /// <param name="validationLevel">The depth of validation to perform</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with confidence level and recommendations</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryValidationResult> ValidateRecoveryPossibilityAsync(
        string grainId,
        DateTimeOffset targetTimestamp,
        RecoveryValidationLevel validationLevel = RecoveryValidationLevel.Standard,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if recovery to a specific version is possible.
    /// Checks data availability and integrity before attempting recovery.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="targetVersion">The target version to validate</param>
    /// <param name="validationLevel">The depth of validation to perform</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Validation result with confidence level and recommendations</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when targetVersion is negative</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryValidationResult> ValidateRecoveryPossibilityAsync(
        string grainId,
        long targetVersion,
        RecoveryValidationLevel validationLevel = RecoveryValidationLevel.Standard,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recovery point discovery and exploration operations.
/// Focused on finding and analyzing available recovery points.
/// </summary>
public interface IRecoveryDiscovery
{
    /// <summary>
    /// Gets available recovery points for a grain within a time range.
    /// Returns timestamps/versions where recovery is possible with confidence levels.
    /// </summary>
    /// <param name="grainId">The grain identifier</param>
    /// <param name="fromTime">The start of the time range (inclusive), or null for all history</param>
    /// <param name="toTime">The end of the time range (inclusive), or null for current time</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Available recovery points ordered by timestamp</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainId is null</exception>
    /// <exception cref="ArgumentException">Thrown when time range is invalid</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<IReadOnlyList<RecoveryPoint>> GetAvailableRecoveryPointsAsync(
        string grainId,
        DateTimeOffset? fromTime = null,
        DateTimeOffset? toTime = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Background recovery operation management.
/// Handles asynchronous recovery operations with progress tracking and cancellation.
/// </summary>
public interface IRecoveryManagement
{
    /// <summary>
    /// Starts an asynchronous background recovery operation.
    /// Returns immediately with an operation ID for tracking progress.
    /// </summary>
    /// <param name="request">The recovery request</param>
    /// <param name="projectionFactory">Factory to create projection for the grain type</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The operation ID for tracking progress</returns>
    /// <exception cref="ArgumentNullException">Thrown when request or projectionFactory is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<string> StartBackgroundRecoveryAsync(
        PointInTimeRecoveryRequest request,
        Func<string, object> projectionFactory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a background recovery operation.
    /// </summary>
    /// <param name="operationId">The operation identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The current status of the operation, or null if not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when operationId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryResult<object>?> GetRecoveryStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a background recovery operation.
    /// </summary>
    /// <param name="operationId">The operation identifier</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the operation was cancelled, false if it was already completed or not found</returns>
    /// <exception cref="ArgumentNullException">Thrown when operationId is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<bool> CancelRecoveryAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recovery monitoring and health tracking operations.
/// Provides metrics, health status, and operational insights.
/// </summary>
public interface IRecoveryMonitoring
{
    /// <summary>
    /// Gets comprehensive metrics about recovery operations.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Recovery metrics and statistics</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<RecoveryMetrics> GetRecoveryMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the health status of the point-in-time recovery service.
    /// Checks connectivity to underlying services and system availability.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Health status information</returns>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled</exception>
    Task<PointInTimeRecoveryHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Comprehensive point-in-time recovery service that combines all recovery capabilities.
/// This aggregate interface maintains backward compatibility while providing access to all segregated interfaces.
/// Follows SOLID principles by composing focused interfaces rather than implementing everything in one interface.
/// </summary>
public interface IPointInTimeRecoveryService :
    IPointInTimeRecovery,
    IRecoveryValidation,
    IRecoveryDiscovery,
    IRecoveryManagement,
    IRecoveryMonitoring
{
    // Interface intentionally empty - inherits all methods from segregated interfaces
    // This maintains backward compatibility while following Interface Segregation Principle
}

/// <summary>
/// Factory interface for creating event projections for different grain types.
/// Used by the point-in-time recovery service to create appropriate projections.
/// </summary>
public interface IEventProjectionFactory
{
    /// <summary>
    /// Creates an event projection for the specified grain type.
    /// </summary>
    /// <typeparam name="T">The type of state the projection will create</typeparam>
    /// <param name="grainType">The grain type identifier</param>
    /// <returns>An event projection for the grain type</returns>
    /// <exception cref="ArgumentNullException">Thrown when grainType is null</exception>
    /// <exception cref="NotSupportedException">Thrown when the grain type is not supported</exception>
    IEventProjection<T> CreateProjection<T>(string grainType);

    /// <summary>
    /// Gets the supported grain types.
    /// </summary>
    /// <returns>A collection of supported grain type identifiers</returns>
    IReadOnlyCollection<string> GetSupportedGrainTypes();

    /// <summary>
    /// Checks if a grain type is supported.
    /// </summary>
    /// <param name="grainType">The grain type to check</param>
    /// <returns>True if the grain type is supported, false otherwise</returns>
    bool IsGrainTypeSupported(string grainType);
}

/// <summary>
/// Health status information for point-in-time recovery service.
/// </summary>
public record PointInTimeRecoveryHealthStatus
{
    /// <summary>
    /// Whether the service is healthy overall.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// When the health check was performed.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Health status of dependent services.
    /// </summary>
    public Dictionary<string, bool> DependencyHealth { get; init; } = [];

    /// <summary>
    /// Any health status messages.
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    /// <summary>
    /// Additional health details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Number of active background recovery operations.
    /// </summary>
    public int ActiveOperations { get; init; }

    /// <summary>
    /// Number of pending recovery operations in queue.
    /// </summary>
    public int PendingOperations { get; init; }

    /// <summary>
    /// Average recovery time over the last hour.
    /// </summary>
    public TimeSpan AverageRecoveryTime { get; init; }

    /// <summary>
    /// Success rate percentage over the last 24 hours.
    /// </summary>
    public double RecentSuccessRate { get; init; }

    /// <summary>
    /// Creates a healthy status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="dependencyHealth">Health of dependencies</param>
    /// <returns>A healthy status</returns>
    public static PointInTimeRecoveryHealthStatus Healthy(
        string serviceName,
        Dictionary<string, bool>? dependencyHealth = null)
    {
        return new PointInTimeRecoveryHealthStatus
        {
            IsHealthy = true,
            ServiceName = serviceName,
            DependencyHealth = dependencyHealth ?? []
        };
    }

    /// <summary>
    /// Creates an unhealthy status.
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="messages">Health issues</param>
    /// <param name="dependencyHealth">Health of dependencies</param>
    /// <returns>An unhealthy status</returns>
    public static PointInTimeRecoveryHealthStatus Unhealthy(
        string serviceName,
        IReadOnlyList<string>? messages = null,
        Dictionary<string, bool>? dependencyHealth = null)
    {
        return new PointInTimeRecoveryHealthStatus
        {
            IsHealthy = false,
            ServiceName = serviceName,
            Messages = messages ?? [],
            DependencyHealth = dependencyHealth ?? []
        };
    }
}