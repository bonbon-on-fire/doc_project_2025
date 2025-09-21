using AIChat.Orleans.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.Orleans.Metrics;

/// <summary>
/// Background service responsible for periodic cleanup of SSE metrics data.
/// Implements proper lifecycle management with cancellation support.
/// </summary>
public class SseMetricsCleanupService : BackgroundService
{
    private readonly ILogger<SseMetricsCleanupService> _logger;
    private readonly ISseMetricsCollector _metricsCollector;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SseMetricsCleanupService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output</param>
    /// <param name="metricsCollector">SSE metrics collector to clean up</param>
    /// <param name="options">Configuration options</param>
    public SseMetricsCleanupService(
        ILogger<SseMetricsCleanupService> logger,
        ISseMetricsCollector metricsCollector,
        IOptions<SseMetricsOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _ = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Executes the background service logic.
    /// </summary>
    /// <param name="stoppingToken">Token to monitor for cancellation requests</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SSE Metrics Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                {
                    await PerformCleanupAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                _logger.LogInformation("SSE Metrics Cleanup Service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SSE metrics cleanup");

                // Wait before retrying to avoid tight error loops
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("SSE Metrics Cleanup Service stopped");
    }

    /// <summary>
    /// Performs the actual cleanup of old metrics data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task PerformCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting SSE metrics cleanup");

            // The actual cleanup logic will be moved here from SseMetricsCollector
            // For now, we trigger cleanup via the metrics collector
            if (_metricsCollector is SseMetricsCollector collector)
            {
                await collector.PerformManualCleanupAsync(cancellationToken);
            }

            _logger.LogDebug("SSE metrics cleanup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform SSE metrics cleanup");
            throw;
        }
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SSE Metrics Cleanup Service is shutting down");
        await base.StopAsync(cancellationToken);
    }
}