namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for bridging Orleans grain streams to HTTP Server-Sent Events (SSE) streams.
/// Provides methods for stream conversion, backpressure handling, and error propagation.
/// </summary>
public interface IStreamingBridge : IAsyncDisposable
{
    /// <summary>
    /// Converts an Orleans grain stream to an HTTP SSE stream.
    /// </summary>
    /// <typeparam name="T">The type of data being streamed</typeparam>
    /// <param name="grainStream">The source stream from an Orleans grain</param>
    /// <param name="httpResponse">The HTTP response to write SSE events to</param>
    /// <param name="formatter">Function to format grain data as SSE events</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async streaming operation</returns>
    Task ConvertGrainToHttpStreamAsync<T>(
        IAsyncEnumerable<T> grainStream,
        HttpResponse httpResponse,
        Func<T, string> formatter,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Handles backpressure when the consumer cannot keep up with the producer.
    /// </summary>
    /// <param name="bufferUtilization">Current buffer utilization percentage (0-100)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task with a boolean indicating if backpressure was successfully handled</returns>
    Task<bool> HandleBackpressureAsync(
        float bufferUtilization,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Propagates errors from the grain stream to the HTTP client.
    /// </summary>
    /// <param name="exception">The exception to propagate</param>
    /// <param name="httpResponse">The HTTP response to write the error to</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async error propagation</returns>
    Task PropagateErrorAsync(
        Exception exception,
        HttpResponse httpResponse,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Gets the current buffer statistics for monitoring.
    /// </summary>
    /// <returns>Buffer statistics including size, utilization, and throughput</returns>
    BufferStatistics GetBufferStatistics();
}

/// <summary>
/// Statistics about the streaming buffer for monitoring and telemetry.
/// </summary>
public record BufferStatistics
{
    /// <summary>
    /// Maximum buffer capacity in items.
    /// </summary>
    public int Capacity { get; init; }

    /// <summary>
    /// Current number of items in the buffer.
    /// </summary>
    public int CurrentSize { get; init; }

    /// <summary>
    /// Buffer utilization percentage (0-100).
    /// </summary>
    public float UtilizationPercentage { get; init; }

    /// <summary>
    /// Number of items processed.
    /// </summary>
    public long ItemsProcessed { get; init; }

    /// <summary>
    /// Number of backpressure events triggered.
    /// </summary>
    public long BackpressureEvents { get; init; }

    /// <summary>
    /// Average processing time per item in milliseconds.
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }
}
