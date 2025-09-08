namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for handling backpressure in streaming scenarios.
/// Provides adaptive strategies to manage flow control when consumers cannot keep up with producers.
/// </summary>
public interface IBackpressureHandler
{
    /// <summary>
    /// Determines whether backpressure should be applied based on current conditions.
    /// </summary>
    /// <param name="utilization">Current resource utilization percentage (0-100)</param>
    /// <returns>True if backpressure should be applied, false otherwise</returns>
    bool ShouldApplyBackpressure(float utilization);

    /// <summary>
    /// Applies backpressure by introducing appropriate delays or throttling.
    /// </summary>
    /// <param name="utilization">Current resource utilization percentage (0-100)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The actual delay applied in milliseconds</returns>
    Task<int> ApplyBackpressureAsync(
        float utilization,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the appropriate delay based on utilization and configuration.
    /// </summary>
    /// <param name="utilization">Current resource utilization percentage (0-100)</param>
    /// <returns>The calculated delay in milliseconds</returns>
    int CalculateDelay(float utilization);

    /// <summary>
    /// Resets backpressure statistics and state.
    /// </summary>
    void Reset();

    /// <summary>
    /// Gets the configured threshold for triggering backpressure.
    /// </summary>
    float Threshold { get; }

    /// <summary>
    /// Gets whether adaptive backpressure is enabled.
    /// </summary>
    bool IsAdaptive { get; }

    /// <summary>
    /// Gets the total number of backpressure events triggered.
    /// </summary>
    long BackpressureEventCount { get; }

    /// <summary>
    /// Gets the total time spent in backpressure delays (milliseconds).
    /// </summary>
    long TotalDelayMs { get; }
}
