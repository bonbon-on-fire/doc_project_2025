using System.Text;
using AIChat.Server.Extensions;
using AIChat.Server.Models.SSE;

namespace AIChat.Server.Services.Streaming;

/// <summary>
/// Thread-safe HTTP stream writer implementation for Server-Sent Events.
/// </summary>
public sealed class HttpStreamWriter : IHttpStreamWriter
{
    private readonly HttpResponse _httpResponse;
    private readonly ILogger<HttpStreamWriter> _logger;
    private readonly SemaphoreSlim _writeSemaphore;
    private readonly int _writeTimeoutMs;
    private long _bytesWritten;
    private long _writeCount;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the HttpStreamWriter class.
    /// </summary>
    /// <param name="httpResponse">The HTTP response to write to</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="writeTimeoutMs">Timeout for write operations in milliseconds</param>
    public HttpStreamWriter(
        HttpResponse httpResponse,
        ILogger<HttpStreamWriter> logger,
        int writeTimeoutMs = 30000)
    {
        _httpResponse = httpResponse ?? throw new ArgumentNullException(nameof(httpResponse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writeTimeoutMs = writeTimeoutMs;
        _writeSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc />
    public async Task WriteDataAsync(string data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(data))
            return;

        var sseData = $"data: {data}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);

        await WriteInternalAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteErrorAsync(
        Exception error,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(error);

        var errorEnvelope = SSEEventExtensions.CreateErrorEnvelope(
            traceId ?? "unknown",
            null,
            null,
            error.Message,
            error.GetType().Name);

        var errorData = $"data: {System.Text.Json.JsonSerializer.Serialize(errorEnvelope)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(errorData);

        await WriteInternalAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteKeepAliveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var keepAlive = ":keepalive\n\n";
        var bytes = Encoding.UTF8.GetBytes(keepAlive);

        await WriteInternalAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(_writeTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, cts.Token);
            
            await _httpResponse.Body.FlushAsync(linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            ThrowIfDisposed();
            return !_httpResponse.HttpContext.RequestAborted.IsCancellationRequested;
        }
    }

    /// <inheritdoc />
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    /// <inheritdoc />
    public long WriteCount => Interlocked.Read(ref _writeCount);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // Final flush if connected
            if (IsConnected)
            {
                await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during final flush in disposal");
        }
        finally
        {
            _writeSemaphore?.Dispose();
            
            _logger.LogInformation(
                "HttpStreamWriter disposed. Total bytes: {Bytes}, Total writes: {Writes}",
                BytesWritten,
                WriteCount);
        }

        GC.SuppressFinalize(this);
    }

    private async Task WriteInternalAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        var cts = new CancellationTokenSource(_writeTimeoutMs);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _httpResponse.HttpContext.RequestAborted,
                cts.Token);

            await _httpResponse.Body.WriteAsync(bytes, linkedCts.Token).ConfigureAwait(false);
            
            Interlocked.Add(ref _bytesWritten, bytes.Length);
            Interlocked.Increment(ref _writeCount);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Write operation timed out after {Timeout}ms", _writeTimeoutMs);
            throw new TimeoutException($"Write operation timed out after {_writeTimeoutMs}ms");
        }
        finally
        {
            cts.Dispose();
            _writeSemaphore.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HttpStreamWriter));
    }
}