using System.Globalization;
using System.Text;
using System.Text.Json;
using AIChat.Orleans.Tests.TestUtilities.Mocks.SSE;

namespace AIChat.Orleans.Tests.TestUtilities;

/// <summary>
/// Helper utilities for testing Server-Sent Events (SSE) functionality.
/// </summary>
public static class SseTestHelpers
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Represents a parsed SSE event.
    /// </summary>
    public class SseEvent
    {
        public string? EventType { get; set; }
        public string? Data { get; set; }
        public string? Id { get; set; }
        public int? Retry { get; set; }
        public SSEEnvelope? Envelope { get; set; }
    }

    /// <summary>
    /// Parses SSE events from a stream.
    /// </summary>
    public static async Task<List<SseEvent>> ParseSseStreamAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        var events = new List<SseEvent>();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        SseEvent? currentEvent = null;
        var dataLines = new List<string>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrEmpty(line))
            {
                // Empty line signals end of event
                if (currentEvent != null && dataLines.Count > 0)
                {
                    currentEvent.Data = string.Join("\n", dataLines);

                    // Try to parse as SSEEnvelope
                    if (!string.IsNullOrEmpty(currentEvent.Data))
                    {
                        try
                        {
                            currentEvent.Envelope = JsonSerializer.Deserialize<SSEEnvelope>(
                                currentEvent.Data,
                                s_jsonOptions
                            );
                        }
                        catch
                        {
                            // Not all data is JSON envelope
                        }
                    }

                    events.Add(currentEvent);
                    currentEvent = null;
                    dataLines.Clear();
                }
                continue;
            }

            currentEvent ??= new SseEvent();

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent.EventType = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line[5..].Trim());
            }
            else if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                currentEvent.Id = line[3..].Trim();
            }
            else if (line.StartsWith("retry:", StringComparison.Ordinal))
            {
                if (int.TryParse(line[6..].Trim(), out var retry))
                {
                    currentEvent.Retry = retry;
                }
            }
        }

        // Handle last event if stream ends without empty line
        if (currentEvent != null && dataLines.Count > 0)
        {
            currentEvent.Data = string.Join("\n", dataLines);
            events.Add(currentEvent);
        }

        return events;
    }

    /// <summary>
    /// Creates a mock SSE stream from events.
    /// </summary>
    public static Stream CreateSseStream(params SseEvent[] events)
    {
        var sb = new StringBuilder();

        foreach (var evt in events)
        {
            if (!string.IsNullOrEmpty(evt.EventType))
            {
                _ = sb.AppendLine(CultureInfo.InvariantCulture, $"event: {evt.EventType}");
            }

            if (!string.IsNullOrEmpty(evt.Id))
            {
                _ = sb.AppendLine(CultureInfo.InvariantCulture, $"id: {evt.Id}");
            }

            if (evt.Retry.HasValue)
            {
                _ = sb.AppendLine(CultureInfo.InvariantCulture, $"retry: {evt.Retry}");
            }

            if (!string.IsNullOrEmpty(evt.Data))
            {
                foreach (var line in evt.Data.Split('\n'))
                {
                    _ = sb.AppendLine(CultureInfo.InvariantCulture, $"data: {line}");
                }
            }

            _ = sb.AppendLine(); // Empty line to signal end of event
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Validates that an SSE stream contains expected events in order.
    /// </summary>
    public static void ValidateEventSequence(
        List<SseEvent> actualEvents,
        params string[] expectedEventTypes
    )
    {
        var actualTypes = actualEvents.Select(e => e.EventType).ToList();

        if (actualTypes.Count != expectedEventTypes.Length)
        {
            throw new AssertionException(
                $"Expected {expectedEventTypes.Length} events but got {actualTypes.Count}. "
                    + $"Actual: [{string.Join(", ", actualTypes)}]"
            );
        }

        for (var i = 0; i < expectedEventTypes.Length; i++)
        {
            if (actualTypes[i] != expectedEventTypes[i])
            {
                throw new AssertionException(
                    $"Event {i}: Expected '{expectedEventTypes[i]}' but got '{actualTypes[i]}'"
                );
            }
        }
    }

    /// <summary>
    /// Waits for a specific event type in the stream.
    /// </summary>
    public static async Task<SseEvent?> WaitForEventAsync(
        Stream stream,
        string eventType,
        TimeSpan timeout
    )
    {
        using var cts = new CancellationTokenSource(timeout);
        var events = await ParseSseStreamAsync(stream, cts.Token);
        return events.FirstOrDefault(e => e.EventType == eventType);
    }

    /// <summary>
    /// Creates a test SSE envelope.
    /// </summary>
    public static SSEEnvelope CreateTestEnvelope(
        string chatId,
        string? messageId = null,
        string? content = null
    )
    {
        return new SSEEnvelope
        {
            ChatId = chatId,
            MessageId = messageId,
            Content = content,
            Timestamp = DateTime.UtcNow,
            Metadata = [],
        };
    }

    /// <summary>
    /// Custom assertion exception for test failures.
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message)
            : base(message) { }
    }
}
