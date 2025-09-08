using System.Threading.Channels;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Thread-safe buffer manager implementation using channels.
/// </summary>
/// <typeparam name="T">The type of data being buffered</typeparam>
public sealed class BufferManager<T> : IBufferManager<T>
{
    private readonly Channel<T> _channel;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the BufferManager class.
    /// </summary>
    /// <param name="capacity">Maximum buffer capacity</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is less than 1</exception>
    public BufferManager(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1");
        }

        Capacity = capacity;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false
        };

        _channel = Channel.CreateBounded<T>(options);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryWriteAsync(T item)
    {
        ThrowIfDisposed();
        return new ValueTask<bool>(_channel.Writer.TryWrite(item));
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(T item, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.Writer.WriteAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Complete()
    {
        ThrowIfDisposed();
        _ = _channel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _channel.Reader.Count;
        }
    }

    /// <inheritdoc />
    public int Capacity { get; }

    /// <inheritdoc />
    public float UtilizationPercentage
    {
        get
        {
            ThrowIfDisposed();
            return (float)Count / Capacity * 100;
        }
    }

    /// <inheritdoc />
    public bool IsCompleted
    {
        get
        {
            ThrowIfDisposed();
            return _channel.Reader.Completion.IsCompleted;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _channel.Writer.TryComplete();

        // Wait for reader to complete
        await _channel.Reader.Completion.ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(BufferManager<T>));
        }
    }
}
