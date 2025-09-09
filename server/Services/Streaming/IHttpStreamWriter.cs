namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Interface for writing data to HTTP streams using Server-Sent Events (SSE) format.
/// Provides thread-safe write operations with timeout and error handling.
/// </summary>
public interface IHttpStreamWriter : IAsyncDisposable
{
    /// <summary>
    /// Writes formatted data to the HTTP stream as an SSE event.
    /// </summary>
    /// <param name="data">The data to write (already formatted for SSE)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async write operation</returns>
    Task WriteDataAsync(string data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an error to the HTTP stream in SSE format.
    /// </summary>
    /// <param name="exception">The error information to write</param>
    /// <param name="traceId">The trace identifier for correlation</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async write operation</returns>
    Task WriteErrorAsync(
        Exception exception,
        string traceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a keepalive ping to maintain the connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async write operation</returns>
    Task WriteKeepAliveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any buffered data to the underlying stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task representing the async flush operation</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the underlying HTTP connection is still active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the number of bytes written to the stream.
    /// </summary>
    long BytesWritten { get; }

    /// <summary>
    /// Gets the number of write operations performed.
    /// </summary>
    long WriteCount { get; }
}
