using System.Text;
using System.Threading.Channels;
using AIChat.LoadTesting.Models;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Implementation of SSE protocol handler for parsing Server-Sent Events.
/// </summary>
public class SseProtocolHandler : ISseProtocolHandler
{
    private readonly ILogger<SseProtocolHandler> _logger;
    private readonly object _statsLock = new();
    private ProtocolStatistics _statistics;

    public event EventHandler<ProtocolErrorEventArgs>? ProtocolError;

    public SseProtocolHandler(ILogger<SseProtocolHandler> logger)
    {
        _logger = logger;
        _statistics = new ProtocolStatistics { StartTime = DateTime.UtcNow };
    }

    public async Task ProcessStreamAsync(
        Stream stream,
        ChannelWriter<SseMessage> messageWriter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(messageWriter);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var eventLines = new List<string>();
        var lineNumber = 0;

        _logger.LogDebug("Starting SSE stream processing");

        try
        {
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                lineNumber++;

                if (line == null)
                {
                    break;
                }

                UpdateStatistics(stats =>
                {
                    stats.LinesProcessed++;
                    stats.BytesProcessed += Encoding.UTF8.GetByteCount(line) + 1; // +1 for newline
                });

                // Empty line indicates end of event
                if (string.IsNullOrEmpty(line))
                {
                    if (eventLines.Count > 0)
                    {
                        var message = ParseEvent(eventLines);
                        if (message != null && ValidateMessage(message))
                        {
                            await messageWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                            UpdateStatistics(stats =>
                            {
                                stats.EventsParsed++;
                                var eventSize = eventLines.Sum(l => Encoding.UTF8.GetByteCount(l));
                                stats.MaxEventSize = Math.Max(stats.MaxEventSize, eventSize);

                                // Update event type counts
                                if (!stats.EventTypeCounts.TryGetValue(message.Event, out var count))
                                {
                                    count = 0;
                                }
                                stats.EventTypeCounts[message.Event] = count + 1;
                            });

                            _logger.LogTrace(
                                "Parsed SSE event: Type={EventType}, Id={EventId}, DataSize={DataSize}",
                                message.Event,
                                message.Id,
                                message.Data.Length);
                        }
                        else
                        {
                            HandleParseError("Failed to parse or validate SSE event", string.Join("\n", eventLines), lineNumber);
                        }
                        eventLines.Clear();
                    }
                }
                else if (!line.StartsWith(':')) // Comments start with ':' and should be ignored
                {
                    eventLines.Add(line);
                }
            }

            // Handle any remaining lines
            if (eventLines.Count > 0)
            {
                _logger.LogWarning("Stream ended with incomplete event: {Lines}", string.Join("\n", eventLines));
                HandleParseError("Incomplete event at end of stream", string.Join("\n", eventLines), lineNumber);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SSE stream");
            throw;
        }
        finally
        {
            UpdateStatistics(stats => stats.EndTime = DateTime.UtcNow);
            _logger.LogDebug(
                "SSE stream processing completed. Events parsed: {EventsParsed}, Errors: {ParseErrors}",
                _statistics.EventsParsed,
                _statistics.ParseErrors);
        }
    }

    public SseMessage? ParseEvent(IEnumerable<string> eventLines)
    {
        ArgumentNullException.ThrowIfNull(eventLines);

        var message = new SseMessage
        {
            Event = "message", // Default event type
            ReceivedAt = DateTime.UtcNow
        };

        var dataBuilder = new StringBuilder();
        var hasData = false;

        foreach (var line in eventLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex == -1)
            {
                // Line with no colon is treated as a field with empty value
                _logger.LogWarning("SSE line with no colon: {Line}", line);
                continue;
            }

            var field = line[..colonIndex];
            var value = colonIndex < line.Length - 1
                ? line[(colonIndex + 1)..].TrimStart()
                : string.Empty;

            switch (field.ToLowerInvariant())
            {
                case "id":
                    message.Id = value;
                    break;

                case "event":
                    message.Event = value;
                    break;

                case "data":
                    if (hasData)
                    {
                        dataBuilder.AppendLine();
                    }
                    dataBuilder.Append(value);
                    hasData = true;
                    break;

                case "retry":
                    if (int.TryParse(value, out var retryMs))
                    {
                        message.RetryMs = retryMs;
                    }
                    else
                    {
                        _logger.LogWarning("Invalid retry value: {Value}", value);
                    }
                    break;

                default:
                    // Unknown field - could be custom extension
                    message.Metadata ??= new Dictionary<string, object>();
                    message.Metadata[field] = value;
                    _logger.LogTrace("Unknown SSE field: {Field}={Value}", field, value);
                    break;
            }
        }

        if (!hasData)
        {
            _logger.LogWarning("SSE event with no data field");
            UpdateStatistics(stats => stats.ParseErrors++);
            return null;
        }

        message.Data = dataBuilder.ToString();

        // Generate ID if not provided
        if (string.IsNullOrEmpty(message.Id))
        {
            message.Id = Guid.NewGuid().ToString();
        }

        return message;
    }

    public bool ValidateMessage(SseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var isValid = true;
        var errors = new List<string>();

        // Validate required fields
        if (string.IsNullOrEmpty(message.Id))
        {
            errors.Add("Message ID is required");
            isValid = false;
        }

        if (string.IsNullOrEmpty(message.Event))
        {
            errors.Add("Event type is required");
            isValid = false;
        }

        if (message.Data == null) // Allow empty string but not null
        {
            errors.Add("Data field cannot be null");
            isValid = false;
        }

        // Validate reasonable limits
        if (message.Data?.Length > 1024 * 1024) // 1MB limit
        {
            errors.Add($"Data size exceeds 1MB limit: {message.Data.Length} bytes");
            isValid = false;
        }

        if (message.Event?.Length > 256)
        {
            errors.Add($"Event type name too long: {message.Event.Length} characters");
            isValid = false;
        }

        if (!isValid)
        {
            _logger.LogWarning("Message validation failed: {Errors}", string.Join(", ", errors));
            UpdateStatistics(stats => stats.ValidationErrors++);
        }

        return isValid;
    }

    public ProtocolStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            // Calculate average event size
            if (_statistics.EventsParsed > 0)
            {
                _statistics.AverageEventSize = (double)_statistics.BytesProcessed / _statistics.EventsParsed;
            }

            // Return a copy to prevent external modification
            return new ProtocolStatistics
            {
                EventsParsed = _statistics.EventsParsed,
                ParseErrors = _statistics.ParseErrors,
                ValidationErrors = _statistics.ValidationErrors,
                BytesProcessed = _statistics.BytesProcessed,
                LinesProcessed = _statistics.LinesProcessed,
                EventTypeCounts = new Dictionary<string, long>(_statistics.EventTypeCounts),
                AverageEventSize = _statistics.AverageEventSize,
                MaxEventSize = _statistics.MaxEventSize,
                StartTime = _statistics.StartTime,
                EndTime = _statistics.EndTime
            };
        }
    }

    public void Reset()
    {
        lock (_statsLock)
        {
            _statistics = new ProtocolStatistics { StartTime = DateTime.UtcNow };
        }
        _logger.LogDebug("Protocol handler statistics reset");
    }

    private void UpdateStatistics(Action<ProtocolStatistics> updateAction)
    {
        lock (_statsLock)
        {
            updateAction(_statistics);
        }
    }

    private void HandleParseError(string errorMessage, string? rawData, int lineNumber)
    {
        _logger.LogWarning("SSE parse error at line {LineNumber}: {Error}", lineNumber, errorMessage);
        UpdateStatistics(stats => stats.ParseErrors++);

        ProtocolError?.Invoke(this, new ProtocolErrorEventArgs(errorMessage, rawData, lineNumber));
    }
}