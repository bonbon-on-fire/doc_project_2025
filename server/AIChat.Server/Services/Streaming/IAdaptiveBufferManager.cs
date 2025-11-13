namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for managing adaptive buffer sizing based on usage patterns and trends.
/// </summary>
public interface IAdaptiveBufferManager : IDisposable
{
    /// <summary>
    /// Event raised when buffer size should be adjusted.
    /// </summary>
    event EventHandler<BufferSizeChangedEventArgs>? BufferSizeChanged;

    /// <summary>
    /// Gets the current recommended buffer size.
    /// </summary>
    int CurrentBufferSize { get; }

    /// <summary>
    /// Records current buffer usage for trend analysis.
    /// </summary>
    /// <param name="currentUsage">Current number of items in buffer</param>
    /// <param name="capacity">Current buffer capacity</param>
    void RecordUsage(int currentUsage, int capacity);

    /// <summary>
    /// Gets statistics about buffer sizing operations.
    /// </summary>
    AdaptiveBufferStatistics GetStatistics();
}
