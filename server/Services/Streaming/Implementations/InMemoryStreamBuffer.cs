using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIChat.Server.Services.Streaming.Abstractions;
using Microsoft.Extensions.Logging;

namespace AIChat.Server.Services.Streaming.Implementations;

/// <summary>
/// In-memory implementation of stream buffer with TTL and overflow handling.
/// </summary>
public class InMemoryStreamBuffer : IStreamBuffer
{
    private readonly ILogger<InMemoryStreamBuffer> _logger;
    private readonly ConcurrentQueue<BufferedStreamMessage> _messages;
    private readonly SemaphoreSlim _semaphore;
    private readonly Timer _cleanupTimer;
    private long _totalMessagesAdded;
    private long _totalMessagesDropped;
    private long _totalMessagesExpired;
    private long _totalMessagesReplayed;
    private bool _disposed;

    /// <inheritdoc />
    public string BufferId { get; }

    /// <inheritdoc />
    public int Capacity => Configuration.MaxSize;

    /// <inheritdoc />
    public int Count => _messages.Count;

    /// <inheritdoc />
    public BufferConfiguration Configuration { get; }

    /// <summary>
    /// Initializes a new instance of the InMemoryStreamBuffer class.
    /// </summary>
    public InMemoryStreamBuffer(
        string bufferId,
        BufferConfiguration configuration,
        ILogger<InMemoryStreamBuffer> logger)
    {
        BufferId = bufferId ?? throw new ArgumentNullException(nameof(bufferId));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _messages = new ConcurrentQueue<BufferedStreamMessage>();
        _semaphore = new SemaphoreSlim(1, 1);

        // Start cleanup timer for TTL expiration
        _cleanupTimer = new Timer(
            async _ => await RemoveExpiredMessagesAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        _logger.LogInformation(
            "InMemoryStreamBuffer created for {BufferId} with capacity {Capacity}, TTL {TTL}",
            BufferId, Capacity, Configuration.MessageTTL);
    }

    /// <inheritdoc />
    public async Task<bool> AddMessageAsync(BufferedStreamMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Check message size limit
            if (Configuration.MaxMessageSizeBytes > 0 && message.SizeBytes > Configuration.MaxMessageSizeBytes)
            {
                _logger.LogWarning(
                    "Message exceeds size limit for buffer {BufferId}: {Size} > {MaxSize}",
                    BufferId, message.SizeBytes, Configuration.MaxMessageSizeBytes);
                return false;
            }

            // Remove expired messages first
            await RemoveExpiredMessagesInternalAsync();

            // Check if buffer is full
            if (_messages.Count >= Configuration.MaxSize)
            {
                bool handled = await HandleOverflowAsync(message);
                if (!handled)
                {
                    Interlocked.Increment(ref _totalMessagesDropped);
                    return false;
                }
            }

            // Add the message
            _messages.Enqueue(message);
            Interlocked.Increment(ref _totalMessagesAdded);

            _logger.LogDebug(
                "Message {Sequence} added to buffer {BufferId}, current count: {Count}",
                message.SequenceNumber, BufferId, _messages.Count);

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BufferedStreamMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Remove expired messages first
            await RemoveExpiredMessagesInternalAsync();

            // Return a snapshot of current messages
            return _messages.ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BufferedStreamMessage>> DrainMessagesAsync(int maxMessages = 0, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Remove expired messages first
            await RemoveExpiredMessagesInternalAsync();

            var drainedMessages = new List<BufferedStreamMessage>();
            int messagesToDrain = maxMessages > 0 ? Math.Min(maxMessages, _messages.Count) : _messages.Count;

            for (int i = 0; i < messagesToDrain; i++)
            {
                if (_messages.TryDequeue(out var message))
                {
                    drainedMessages.Add(message);
                }
            }

            _logger.LogDebug(
                "Drained {Count} messages from buffer {BufferId}, remaining: {Remaining}",
                drainedMessages.Count, BufferId, _messages.Count);

            return drainedMessages;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> ClearAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            int count = _messages.Count;
            
            // Clear all messages
            while (_messages.TryDequeue(out _))
            {
                // Continue dequeuing
            }

            _logger.LogInformation(
                "Cleared {Count} messages from buffer {BufferId}",
                count, BufferId);

            return count;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> RemoveExpiredMessagesAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await RemoveExpiredMessagesInternalAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StreamBufferStatistics> GetStatisticsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var messages = _messages.ToList();
            var now = DateTime.UtcNow;

            return new StreamBufferStatistics
            {
                MessageCount = messages.Count,
                TotalSizeBytes = messages.Sum(m => (long)m.SizeBytes),
                OldestMessageTimestamp = messages.FirstOrDefault()?.Timestamp,
                NewestMessageTimestamp = messages.LastOrDefault()?.Timestamp,
                TotalMessagesAdded = _totalMessagesAdded,
                TotalMessagesDropped = _totalMessagesDropped,
                TotalMessagesExpired = _totalMessagesExpired,
                TotalMessagesReplayed = _totalMessagesReplayed
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Increments the replay counter for statistics.
    /// </summary>
    public void IncrementReplayCount(int count)
    {
        Interlocked.Add(ref _totalMessagesReplayed, count);
    }

    /// <summary>
    /// Disposes the buffer resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the buffer resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cleanupTimer?.Dispose();
                _semaphore?.Dispose();
            }
            _disposed = true;
        }
    }

    private Task<bool> HandleOverflowAsync(BufferedStreamMessage newMessage)
    {
        switch (Configuration.OverflowStrategy)
        {
            case OverflowStrategy.DropOldest:
                // Remove oldest message to make room
                if (_messages.TryDequeue(out var oldestMessage))
                {
                    Interlocked.Increment(ref _totalMessagesDropped);
                    _logger.LogDebug(
                        "Dropped oldest message {Sequence} from buffer {BufferId} due to overflow",
                        oldestMessage.SequenceNumber, BufferId);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);

            case OverflowStrategy.DropNewest:
                // Reject the new message
                _logger.LogDebug(
                    "Dropping new message {Sequence} for buffer {BufferId} due to overflow",
                    newMessage.SequenceNumber, BufferId);
                return Task.FromResult(false);

            case OverflowStrategy.RejectNew:
                // Reject the new message
                _logger.LogDebug(
                    "Rejecting new message {Sequence} for buffer {BufferId} due to overflow",
                    newMessage.SequenceNumber, BufferId);
                return Task.FromResult(false);

            default:
                // Default to dropping oldest
                if (_messages.TryDequeue(out var defaultOldest))
                {
                    Interlocked.Increment(ref _totalMessagesDropped);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
        }
    }

    private async Task<int> RemoveExpiredMessagesInternalAsync()
    {
        var expiredCount = 0;
        var now = DateTime.UtcNow;
        var tempQueue = new ConcurrentQueue<BufferedStreamMessage>();

        // Check each message for expiration
        while (_messages.TryDequeue(out var message))
        {
            if (message.IsExpired(Configuration.MessageTTL))
            {
                expiredCount++;
                Interlocked.Increment(ref _totalMessagesExpired);
                _logger.LogDebug(
                    "Expired message {Sequence} from buffer {BufferId}, age: {Age}",
                    message.SequenceNumber, BufferId, now - message.Timestamp);
            }
            else
            {
                tempQueue.Enqueue(message);
            }
        }

        // Re-enqueue non-expired messages
        while (tempQueue.TryDequeue(out var message))
        {
            _messages.Enqueue(message);
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation(
                "Removed {Count} expired messages from buffer {BufferId}",
                expiredCount, BufferId);
        }

        return await Task.FromResult(expiredCount);
    }
}