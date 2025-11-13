using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Factory for creating SSE connections with proper dependency injection.
/// Uses service provider to ensure all dependencies are properly resolved.
/// </summary>
public class SseConnectionFactory : ISseConnectionFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SseConfiguration> _sseConfig;
    private readonly ILoggerFactory _loggerFactory;

    public SseConnectionFactory(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<SseConfiguration> sseConfig,
        ILoggerFactory loggerFactory)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _sseConfig = sseConfig ?? throw new ArgumentNullException(nameof(sseConfig));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public ISseConnection CreateConnection(string connectionId, string endpoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);

        // Create HTTP client with SSE configuration
        var httpClient = _httpClientFactory.CreateClient("SSE");
        httpClient.Timeout = TimeSpan.FromMilliseconds(_sseConfig.Value.DefaultTimeoutMs);

        // Create HTTP stream client wrapper
        var httpStreamClient = new HttpStreamClient(
            httpClient,
            _loggerFactory.CreateLogger<HttpStreamClient>());

        // Get or create protocol handler
        var protocolHandler = _serviceProvider.GetService<ISseProtocolHandler>()
            ?? new SseProtocolHandler(_loggerFactory.CreateLogger<SseProtocolHandler>());

        // Get or create metrics recorder
        var metricsRecorder = _serviceProvider.GetRequiredService<ISseMetricsRecorder>();

        // Get or create reconnection strategy
        var reconnectionStrategy = _serviceProvider.GetService<ISseReconnectionStrategy>()
            ?? new ExponentialBackoffReconnectionStrategy(
                _sseConfig,
                _loggerFactory.CreateLogger<ExponentialBackoffReconnectionStrategy>());

        return new SseConnection(
            connectionId,
            endpoint,
            httpStreamClient,
            protocolHandler,
            metricsRecorder,
            reconnectionStrategy,
            _loggerFactory.CreateLogger<SseConnection>()
        );
    }
}
