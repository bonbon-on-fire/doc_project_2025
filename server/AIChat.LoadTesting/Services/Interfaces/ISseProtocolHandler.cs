using System.Threading.Channels;
using AIChat.LoadTesting.Models;

namespace AIChat.LoadTesting.Services.Interfaces;

/// <summary>
/// Handles SSE protocol parsing and message processing.
/// Responsible for converting raw SSE stream data into structured messages.
/// </summary>
public interface ISseProtocolHandler
{
    /// <summary>
    /// Processes an SSE stream and produces parsed messages.
    /// </summary>
    /// <param name="stream">The input stream containing SSE data</param>
    /// <param name="messageWriter">Channel writer to output parsed messages</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the asynchronous processing operation</returns>
    Task ProcessStreamAsync(
        Stream stream,
        ChannelWriter<SseMessage> messageWriter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a single SSE event from the provided lines.
    /// </summary>
    /// <param name="eventLines">Lines that comprise the SSE event</param>
    /// <returns>Parsed SSE message, or null if invalid</returns>
    SseMessage? ParseEvent(IEnumerable<string> eventLines);

    /// <summary>
    /// Validates an SSE message for correctness.
    /// </summary>
    /// <param name="message">The message to validate</param>
    /// <returns>True if the message is valid; otherwise, false</returns>
    bool ValidateMessage(SseMessage message);

    /// <summary>
    /// Event raised when a protocol error occurs.
    /// </summary>
    event EventHandler<ProtocolErrorEventArgs>? ProtocolError;

    /// <summary>
    /// Gets statistics about protocol processing.
    /// </summary>
    ProtocolStatistics GetStatistics();

    /// <summary>
    /// Resets the protocol handler state and statistics.
    /// </summary>
    void Reset();
}

/// <summary>
/// Event arguments for protocol errors.
/// </summary>
public class ProtocolErrorEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// Gets the raw data that caused the error, if available.
    /// </summary>
    public string? RawData { get; }

    /// <summary>
    /// Gets the line number where the error occurred, if applicable.
    /// </summary>
    public int? LineNumber { get; }

    /// <summary>
    /// Gets the timestamp of the error.
    /// </summary>
    public DateTime Timestamp { get; }

    public ProtocolErrorEventArgs(string errorMessage, string? rawData = null, int? lineNumber = null)
    {
        ErrorMessage = errorMessage;
        RawData = rawData;
        LineNumber = lineNumber;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// Statistics for SSE protocol processing.
/// </summary>
public class ProtocolStatistics
{
    /// <summary>
    /// Gets or sets the total number of events parsed.
    /// </summary>
    public long EventsParsed { get; set; }

    /// <summary>
    /// Gets or sets the total number of parse errors.
    /// </summary>
    public long ParseErrors { get; set; }

    /// <summary>
    /// Gets or sets the total number of validation errors.
    /// </summary>
    public long ValidationErrors { get; set; }

    /// <summary>
    /// Gets or sets the total bytes processed.
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the total lines processed.
    /// </summary>
    public long LinesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the count of each event type received.
    /// </summary>
    public Dictionary<string, long> EventTypeCounts { get; set; } = [];

    /// <summary>
    /// Gets or sets the average event size in bytes.
    /// </summary>
    public double AverageEventSize { get; set; }

    /// <summary>
    /// Gets or sets the maximum event size in bytes.
    /// </summary>
    public long MaxEventSize { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when processing started.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when processing ended.
    /// </summary>
    public DateTime? EndTime { get; set; }
}