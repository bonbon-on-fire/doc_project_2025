using System.Collections.Concurrent;
using System.Text;
using AIChat.Server.Services.Streaming.Abstractions;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// Service for replaying buffered messages with duplicate detection and partial message merging.
/// </summary>
public class BufferReplayService : IBufferReplayService
{
    private readonly ILogger<BufferReplayService> _logger;
    private readonly ConcurrentDictionary<string, DeliveryTracker> _deliveryTrackers;
    private readonly ConcurrentDictionary<string, PartialMessageCollector> _partialCollectors;

    /// <summary>
    /// Initializes a new instance of the BufferReplayService class.
    /// </summary>
    public BufferReplayService(ILogger<BufferReplayService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryTrackers = new ConcurrentDictionary<string, DeliveryTracker>();
        _partialCollectors = new ConcurrentDictionary<string, PartialMessageCollector>();
    }

    /// <inheritdoc />
    public async Task<ReplayResult> ReplayMessagesAsync(
        string streamId,
        IEnumerable<BufferedStreamMessage> messages,
        HttpResponse httpResponse,
        ReplayOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(httpResponse);

        options ??= new ReplayOptions();
        var startTime = DateTime.UtcNow;

        // Track statistics with mutable variables
        var messagesReplayed = 0;
        var duplicatesSkipped = 0;
        var messagesFailed = 0;
        const int partialMessagesMerged = 0;
        var bytesReplayed = 0L;
        var replayedSequenceNumbers = new List<long>();
        var success = true;
        string? errorMessage = null;

        var tracker = GetOrCreateTracker(streamId);
        var messageList = messages.ToList();

        _logger.LogInformation(
            "Starting replay of {Count} messages for stream {StreamId}",
            messageList.Count,
            streamId
        );

        try
        {
            // Sort messages chronologically by sequence number
            var sortedMessages = messageList.OrderBy(m => m.SequenceNumber).ToList();

            // Apply max message limit if specified
            if (options.MaxMessages > 0 && sortedMessages.Count > options.MaxMessages)
            {
                sortedMessages = [.. sortedMessages.Take(options.MaxMessages)];
            }

            // Process messages in batches if specified
            var batchSize = options.BatchSize > 0 ? options.BatchSize : sortedMessages.Count;

            for (var i = 0; i < sortedMessages.Count; i += batchSize)
            {
                var batch = sortedMessages.Skip(i).Take(batchSize).ToList();

                foreach (var message in batch)
                {
                    // Check for duplicates unless skipped
                    if (
                        !options.SkipDuplicateDetection
                        && await IsDuplicateMessageAsync(streamId, message.SequenceNumber)
                    )
                    {
                        duplicatesSkipped++;
                        _logger.LogDebug(
                            "Skipping duplicate message {Sequence} for stream {StreamId}",
                            message.SequenceNumber,
                            streamId
                        );
                        continue;
                    }

                    // Replay the message
                    try
                    {
                        var replayData = PrepareReplayData(message, options.IncludeReplayMetadata);
                        var bytes = Encoding.UTF8.GetBytes(replayData);

                        await httpResponse.Body.WriteAsync(bytes, cancellationToken);
                        await httpResponse.Body.FlushAsync(cancellationToken);

                        // Record successful delivery
                        await RecordMessageDeliveryAsync(
                            streamId,
                            message.SequenceNumber,
                            cancellationToken
                        );

                        messagesReplayed++;
                        bytesReplayed += bytes.Length;
                        replayedSequenceNumbers.Add(message.SequenceNumber);

                        // Mark message as replayed
                        message.IsReplayed = true;
                        message.ReplayAttempts++;

                        // Add delay if specified
                        if (options.MessageDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(options.MessageDelay, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        messagesFailed++;
                        _logger.LogError(
                            ex,
                            "Failed to replay message {Sequence} for stream {StreamId}",
                            message.SequenceNumber,
                            streamId
                        );

                        if (messagesFailed > 5) // Fail fast after too many errors
                        {
                            success = false;
                            errorMessage = $"Too many replay failures: {ex.Message}";
                            break;
                        }
                    }
                }

                // Flush after each batch
                await httpResponse.Body.FlushAsync(cancellationToken);
            }

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Replay completed for stream {StreamId}: {Replayed} replayed, {Duplicates} duplicates, {Failed} failed in {Duration}ms",
                streamId,
                messagesReplayed,
                duplicatesSkipped,
                messagesFailed,
                duration.TotalMilliseconds
            );

            return new ReplayResult
            {
                Success = success,
                MessagesReplayed = messagesReplayed,
                DuplicatesSkipped = duplicatesSkipped,
                MessagesFailed = messagesFailed,
                PartialMessagesMerged = partialMessagesMerged,
                BytesReplayed = bytesReplayed,
                Duration = duration,
                ReplayedSequenceNumbers = replayedSequenceNumbers,
                ErrorMessage = errorMessage,
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;

            _logger.LogError(ex, "Replay failed for stream {StreamId}", streamId);

            return new ReplayResult
            {
                Success = false,
                MessagesReplayed = messagesReplayed,
                DuplicatesSkipped = duplicatesSkipped,
                MessagesFailed = messagesFailed,
                PartialMessagesMerged = partialMessagesMerged,
                BytesReplayed = bytesReplayed,
                Duration = duration,
                ReplayedSequenceNumbers = replayedSequenceNumbers,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsDuplicateMessageAsync(string streamId, long sequenceNumber)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var tracker = GetOrCreateTracker(streamId);
        return await Task.FromResult(tracker.IsDelivered(sequenceNumber));
    }

    /// <inheritdoc />
    public async Task RecordMessageDeliveryAsync(
        string streamId,
        long sequenceNumber,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        var tracker = GetOrCreateTracker(streamId);
        tracker.RecordDelivery(sequenceNumber);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BufferedStreamMessage>> MergePartialMessagesAsync(
        string streamId,
        IEnumerable<PartialMessage> partialMessages,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        ArgumentNullException.ThrowIfNull(partialMessages);

        var collector = _partialCollectors.GetOrAdd(streamId, _ => new PartialMessageCollector());
        var mergedMessages = new List<BufferedStreamMessage>();

        foreach (var partial in partialMessages)
        {
            collector.AddPartial(partial);

            // Check if we have all chunks for this sequence
            if (collector.IsComplete(partial.SequenceNumber))
            {
                var merged = collector.GetMergedMessage(partial.SequenceNumber);
                if (merged != null)
                {
                    mergedMessages.Add(merged);
                    collector.RemoveSequence(partial.SequenceNumber);
                }
            }
        }

        _logger.LogDebug(
            "Merged {Count} complete messages from partials for stream {StreamId}",
            mergedMessages.Count,
            streamId
        );

        return await Task.FromResult(mergedMessages);
    }

    /// <inheritdoc />
    public async Task ClearDeliveryTrackingAsync(
        string streamId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (_deliveryTrackers.TryRemove(streamId, out var tracker))
        {
            _logger.LogInformation(
                "Cleared delivery tracking for stream {StreamId}. Had {Count} tracked deliveries",
                streamId,
                tracker.DeliveredCount
            );
        }

        if (_partialCollectors.TryRemove(streamId, out _))
        {
            _logger.LogDebug("Cleared partial message collector for stream {StreamId}", streamId);
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ReplayStatistics?> GetReplayStatisticsAsync(string streamId)
    {
        if (string.IsNullOrEmpty(streamId))
        {
            throw new ArgumentNullException(nameof(streamId));
        }

        if (!_deliveryTrackers.TryGetValue(streamId, out var tracker))
        {
            return null;
        }

        var stats = new ReplayStatistics
        {
            StreamId = streamId,
            TotalReplayOperations = tracker.ReplayOperations,
            TotalMessagesReplayed = tracker.TotalMessagesReplayed,
            TotalDuplicatesDetected = tracker.TotalDuplicatesDetected,
            TotalPartialMessagesMerged = tracker.TotalPartialsMerged,
            TotalBytesReplayed = tracker.TotalBytesReplayed,
            LastReplayTimestamp = tracker.LastReplayTime,
            AverageReplayDuration = tracker.AverageReplayDuration,
            HighestSequenceDelivered = tracker.HighestSequenceDelivered,
        };

        return await Task.FromResult(stats);
    }

    private DeliveryTracker GetOrCreateTracker(string streamId)
    {
        return _deliveryTrackers.GetOrAdd(streamId, _ => new DeliveryTracker());
    }

    private static string PrepareReplayData(BufferedStreamMessage message, bool includeMetadata)
    {
        if (includeMetadata)
        {
            // Include replay metadata in SSE comment
            return $": replay sequence={message.SequenceNumber} replayed={message.IsReplayed} attempts={message.ReplayAttempts}\ndata: {message.Data}\n\n";
        }
        else
        {
            // Standard SSE format
            return $"data: {message.Data}\n\n";
        }
    }

    /// <summary>
    /// Tracks message delivery for duplicate detection.
    /// </summary>
    private sealed class DeliveryTracker
    {
        private readonly HashSet<long> _deliveredSequences = [];
        private readonly Lock _lock = new();

        public int DeliveredCount => _deliveredSequences.Count;
        public int ReplayOperations { get; private set; }
        public long TotalMessagesReplayed { get; private set; }
        public long TotalDuplicatesDetected { get; private set; }
        public long TotalPartialsMerged { get; }
        public long TotalBytesReplayed { get; }
        public DateTime? LastReplayTime { get; private set; }
        public TimeSpan AverageReplayDuration { get; }
        public long HighestSequenceDelivered { get; private set; }

        public bool IsDelivered(long sequenceNumber)
        {
            lock (_lock)
            {
                if (_deliveredSequences.Contains(sequenceNumber))
                {
                    TotalDuplicatesDetected++;
                    return true;
                }
                return false;
            }
        }

        public void RecordDelivery(long sequenceNumber)
        {
            lock (_lock)
            {
                _ = _deliveredSequences.Add(sequenceNumber);
                TotalMessagesReplayed++;
                LastReplayTime = DateTime.UtcNow;

                if (sequenceNumber > HighestSequenceDelivered)
                {
                    HighestSequenceDelivered = sequenceNumber;
                }
            }
        }

        public void IncrementReplayOperation()
        {
            ReplayOperations++;
        }
    }

    /// <summary>
    /// Collects and merges partial messages.
    /// </summary>
    private sealed class PartialMessageCollector
    {
        private readonly ConcurrentDictionary<long, List<PartialMessage>> _partials = new();

        public void AddPartial(PartialMessage partial)
        {
            _ = _partials.AddOrUpdate(
                partial.SequenceNumber,
                _ => [partial],
                (_, list) =>
                {
                    list.Add(partial);
                    return list;
                }
            );
        }

        public bool IsComplete(long sequenceNumber)
        {
            if (!_partials.TryGetValue(sequenceNumber, out var partials))
            {
                return false;
            }

            var firstPartial = partials.FirstOrDefault();
            return firstPartial != null
                && partials.Count == firstPartial.TotalChunks
                && partials.Select(p => p.ChunkIndex).Distinct().Count()
                    == firstPartial.TotalChunks;
        }

        public BufferedStreamMessage? GetMergedMessage(long sequenceNumber)
        {
            if (!_partials.TryGetValue(sequenceNumber, out var partials))
            {
                return null;
            }

            // Sort by chunk index and concatenate data
            var sortedPartials = partials.OrderBy(p => p.ChunkIndex).ToList();
            var mergedData = string.Concat(sortedPartials.Select(p => p.Data));
            var totalSize = Encoding.UTF8.GetByteCount(mergedData);

            return new BufferedStreamMessage
            {
                SequenceNumber = sequenceNumber,
                Data = mergedData,
                Timestamp = sortedPartials[0].Timestamp,
                SizeBytes = totalSize,
                Metadata = new Dictionary<string, object>
                {
                    ["merged"] = true,
                    ["chunks"] = sortedPartials.Count,
                },
            };
        }

        public void RemoveSequence(long sequenceNumber)
        {
            _ = _partials.TryRemove(sequenceNumber, out _);
        }
    }
}
