namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for managing stream recovery with automatic reconnection and message replay.
/// </summary>
public interface IStreamRecoveryManager : IDisposable
{
    /// <summary>
    /// Event raised when recovery is initiated.
    /// </summary>
    event EventHandler<RecoveryEventArgs>? RecoveryStarted;

    /// <summary>
    /// Event raised when recovery is completed.
    /// </summary>
    event EventHandler<RecoveryEventArgs>? RecoveryCompleted;

    /// <summary>
    /// Event raised when recovery fails.
    /// </summary>
    event EventHandler<RecoveryEventArgs>? RecoveryFailed;

    /// <summary>
    /// Initiates recovery for a stream.
    /// </summary>
    /// <param name="streamId">Stream identifier</param>
    /// <param name="reconnectFunc">Function to attempt reconnection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if recovery succeeded, false otherwise</returns>
    Task<bool> RecoverStreamAsync(
        string streamId,
        Func<CancellationToken, Task<bool>> reconnectFunc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a message to the replay buffer.
    /// </summary>
    /// <param name="streamId">Stream identifier</param>
    /// <param name="message">Message content</param>
    /// <param name="sequenceNumber">Optional sequence number</param>
    void AddToReplayBuffer(string streamId, string message, long? sequenceNumber = null);

    /// <summary>
    /// Creates a checkpoint for a stream.
    /// </summary>
    /// <param name="streamId">Stream identifier</param>
    /// <param name="sequenceNumber">Sequence number at checkpoint</param>
    void CreateCheckpoint(string streamId, long sequenceNumber);

    /// <summary>
    /// Gets recovery statistics.
    /// </summary>
    RecoveryStatistics GetStatistics();
}
