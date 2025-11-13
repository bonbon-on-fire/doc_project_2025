using System.Collections.Concurrent;
using AIChat.Server.Services.Abstractions;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Service for managing message replay during stream recovery.
/// </summary>
public sealed class MessageReplayService : IMessageReplayService
{
    private readonly ConcurrentQueue<ReplayMessage> _replayBuffer;
    private readonly ISystemTime _systemTime;
    private readonly ILogger<MessageReplayService> _logger;
    private readonly int _maxBufferSize;

    public MessageReplayService(
        ISystemTime systemTime,
        ILogger<MessageReplayService> logger,
        int maxBufferSize = 1000)
    {
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxBufferSize = maxBufferSize;
        _replayBuffer = new ConcurrentQueue<ReplayMessage>();
    }

    /// <inheritdoc />
    public int BufferSize => _replayBuffer.Count;

    /// <inheritdoc />
    public void AddMessage(ReplayMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _replayBuffer.Enqueue(message);

        // Enforce buffer size limit
        while (_replayBuffer.Count > _maxBufferSize)
        {
            if (_replayBuffer.TryDequeue(out var removed))
            {
                _logger.LogDebug(
                    "Removed old message from replay buffer for stream {StreamId}, buffer at capacity",
                    removed.StreamId);
            }
        }

        _logger.LogDebug(
            "Added message to replay buffer for stream {StreamId}, sequence {Sequence}",
            message.StreamId,
            message.SequenceNumber);
    }

    /// <inheritdoc />
    public IEnumerable<ReplayMessage> GetMessagesForReplay(string streamId, int maxAgeSeconds)
    {
        ArgumentNullException.ThrowIfNull(streamId);

        var cutoff = _systemTime.UtcNow.AddSeconds(-maxAgeSeconds);
        var messages = _replayBuffer
            .Where(m => m.StreamId == streamId && m.Timestamp > cutoff)
            .OrderBy(m => m.SequenceNumber)
            .ToList();

        _logger.LogInformation(
            "Found {Count} messages for replay for stream {StreamId} within {MaxAge} seconds",
            messages.Count,
            streamId,
            maxAgeSeconds);

        return messages;
    }

    /// <inheritdoc />
    public int CleanupOldMessages(int maxAgeSeconds)
    {
        var cutoff = _systemTime.UtcNow.AddSeconds(-maxAgeSeconds);
        var itemsRemoved = 0;

        // Remove old messages
        while (_replayBuffer.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            if (_replayBuffer.TryDequeue(out _))
            {
                itemsRemoved++;
            }
        }

        if (itemsRemoved > 0)
        {
            _logger.LogDebug(
                "Cleaned up {Count} old messages from replay buffer older than {MaxAge} seconds",
                itemsRemoved,
                maxAgeSeconds);
        }

        return itemsRemoved;
    }

    /// <inheritdoc />
    public void Clear()
    {
        var count = _replayBuffer.Count;
        _replayBuffer.Clear();

        if (count > 0)
        {
            _logger.LogInformation("Cleared {Count} messages from replay buffer", count);
        }
    }
}
