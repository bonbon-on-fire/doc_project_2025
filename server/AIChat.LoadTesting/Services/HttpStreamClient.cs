using System.Net.Http.Headers;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Implementation of HTTP stream client for SSE connections.
/// Provides abstraction over HttpClient for better testability.
/// </summary>
public class HttpStreamClient : IHttpStreamClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpStreamClient> _logger;
    private bool _disposed;

    public HttpStreamClient(HttpClient httpClient, ILogger<HttpStreamClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StreamReader> OpenStreamAsync(
        string url,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        try
        {
            _logger.LogDebug("Opening HTTP stream to {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Set SSE-specific headers
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

            // Add custom headers if provided
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _ = request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Send request with response streaming
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            // Validate response
            _ = response.EnsureSuccessStatusCode();

            // Validate content type
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != null && !contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unexpected content type for SSE: {ContentType}", contentType);
            }

            // Get the response stream
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Successfully opened HTTP stream to {Url}", url);

            return new StreamReader(stream);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed for {Url}", url);
            throw new InvalidOperationException($"Failed to open HTTP stream to {url}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "HTTP request cancelled or timed out for {Url}", url);
            throw new TimeoutException($"Request to {url} timed out", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening HTTP stream to {Url}", url);
            throw;
        }
    }

    public void SetTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive");
        }

        _httpClient.Timeout = timeout;
        _logger.LogDebug("HTTP client timeout set to {Timeout}", timeout);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // HttpClient is typically managed by IHttpClientFactory, so we don't dispose it
                _logger.LogDebug("HttpStreamClient disposed");
            }
            _disposed = true;
        }
    }
}
