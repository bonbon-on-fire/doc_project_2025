using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Models;
using Microsoft.Extensions.Options;

namespace AIChat.Server.Services.SignalRBuffering.Implementations;

/// <summary>
/// <para>
/// Background service that continuously processes the SignalR message buffer.
/// This service runs in the background and periodically calls the buffer's ProcessBufferAsync
/// method to deliver queued messages to SignalR clients.
/// </para>
/// <para>
/// Features:
/// - Configurable processing intervals for optimal performance tuning
/// - Automatic error handling and recovery
/// - Graceful shutdown with proper cleanup
/// - Comprehensive logging and metrics integration
/// - Health monitoring and performance tracking
/// </para>
/// </summary>
public sealed class SignalRBufferProcessorService : BackgroundService
{
    private readonly ISignalRMessageBuffer _messageBuffer;
    private readonly ISignalRDeliveryService _deliveryService;
    private readonly SignalRBufferConfiguration _config;
    private readonly ILogger<SignalRBufferProcessorService> _logger;

    /// <summary>
    /// Performance and health tracking
    /// </summary>
    private long _totalProcessingCycles;
    private long _totalMessagesProcessed;
    private long _totalProcessingErrors;
    private DateTime _lastSuccessfulProcessing = DateTime.UtcNow;
    private DateTime _serviceStartTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the SignalRBufferProcessorService.
    /// </summary>
    /// <param name="messageBuffer">The message buffer to process</param>
    /// <param name="deliveryService">The service to handle message delivery</param>
    /// <param name="config">Configuration for buffer processing</param>
    /// <param name="logger">Logger instance</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    public SignalRBufferProcessorService(
        ISignalRMessageBuffer messageBuffer,
        ISignalRDeliveryService deliveryService,
        IOptions<SignalRBufferConfiguration> config,
        ILogger<SignalRBufferProcessorService> logger)
    {
        _messageBuffer = messageBuffer ?? throw new ArgumentNullException(nameof(messageBuffer));
        _deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "SignalRBufferProcessorService initialized: ProcessingInterval={ProcessingInterval}, " +
            "BatchSize={BatchSize}, BufferingEnabled={BufferingEnabled}",
            _config.ProcessingInterval, _config.BatchSize, _config.Enabled);
    }

    /// <summary>
    /// Gets comprehensive metrics about the buffer processor performance.
    /// </summary>
    /// <returns>Processor performance metrics</returns>
    public BufferProcessorMetrics GetMetrics()
    {
        var uptime = DateTime.UtcNow - _serviceStartTime;
        var totalCycles = Interlocked.Read(ref _totalProcessingCycles);
        var totalMessages = Interlocked.Read(ref _totalMessagesProcessed);
        var totalErrors = Interlocked.Read(ref _totalProcessingErrors);

        return new BufferProcessorMetrics
        {
            Timestamp = DateTime.UtcNow,
            ServiceUptime = uptime,
            TotalProcessingCycles = totalCycles,
            TotalMessagesProcessed = totalMessages,
            TotalProcessingErrors = totalErrors,
            LastSuccessfulProcessing = _lastSuccessfulProcessing,
            AverageMessagesPerCycle = totalCycles > 0 ? (double)totalMessages / totalCycles : 0.0,
            ErrorRate = totalCycles > 0 ? (double)totalErrors / totalCycles * 100.0 : 0.0,
            IsHealthy = IsServiceHealthy(),
            CurrentBufferSize = _messageBuffer.GetCurrentSize(),
            BufferCapacity = _messageBuffer.GetMaxCapacity(),
            DeliveryServiceAvailable = _deliveryService.IsAvailable,
            ProcessingInterval = _config.ProcessingInterval
        };
    }

    /// <summary>
    /// The main execution loop for the background service.
    /// </summary>
    /// <param name="stoppingToken">Token to signal service shutdown</param>
    /// <returns>Task representing the service execution</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SignalRBufferProcessorService starting execution");
        _serviceStartTime = DateTime.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBufferCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Service is shutting down - this is expected
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalProcessingErrors);
                    _logger.LogError(ex, "Error during buffer processing cycle");

                    // Continue processing after error - don't let single failures stop the service
                    // Add a delay to prevent rapid error loops
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }

                // Wait for the configured interval before next processing cycle
                try
                {
                    await Task.Delay(_config.ProcessingInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Service is shutting down
                    break;
                }
            }
        }
        finally
        {
            await PerformShutdownCleanupAsync();
        }

        _logger.LogInformation(
            "SignalRBufferProcessorService execution completed. Total cycles: {TotalCycles}, " +
            "Total messages: {TotalMessages}, Total errors: {TotalErrors}",
            Interlocked.Read(ref _totalProcessingCycles),
            Interlocked.Read(ref _totalMessagesProcessed),
            Interlocked.Read(ref _totalProcessingErrors));
    }

    /// <summary>
    /// Performs a single buffer processing cycle.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    private async Task ProcessBufferCycleAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            // Buffering is disabled - sleep longer to avoid unnecessary CPU usage
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return;
        }

        if (!_deliveryService.IsAvailable)
        {
            _logger.LogWarning("Delivery service is not available, skipping buffer processing cycle");
            return;
        }

        var bufferSize = _messageBuffer.GetCurrentSize();
        if (bufferSize == 0)
        {
            // No messages to process
            _logger.LogTrace("Buffer is empty, skipping processing cycle");
            return;
        }

        _logger.LogDebug("Starting buffer processing cycle: BufferSize={BufferSize}", bufferSize);

        var cycleStartTime = DateTime.UtcNow;
        Interlocked.Increment(ref _totalProcessingCycles);

        try
        {
            // Process the buffer
            var result = await _messageBuffer.ProcessBufferAsync(_deliveryService, cancellationToken);

            if (result.Success)
            {
                _lastSuccessfulProcessing = DateTime.UtcNow;
                var cycleTime = DateTime.UtcNow - cycleStartTime;

                // Extract message count from the result (this could be improved with better result structure)
                var messagesProcessed = EstimateMessagesProcessedFromResult(result);
                Interlocked.Add(ref _totalMessagesProcessed, messagesProcessed);

                _logger.LogDebug(
                    "Buffer processing cycle completed successfully: " +
                    "ProcessedMessages={EstimatedMessages}, CycleTime={CycleTime:F2}ms, " +
                    "RemainingBufferSize={RemainingBufferSize}",
                    messagesProcessed, cycleTime.TotalMilliseconds, _messageBuffer.GetCurrentSize());
            }
            else
            {
                _logger.LogWarning(
                    "Buffer processing cycle completed with errors: Status={Status}, Error={Error}",
                    result.Status, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Buffer processing cycle failed: BufferSize={BufferSize}, " +
                "CycleTime={CycleTime:F2}ms",
                bufferSize, (DateTime.UtcNow - cycleStartTime).TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Estimates the number of messages processed from the delivery result.
    /// This is a temporary solution until we improve the result structure.
    /// </summary>
    private static int EstimateMessagesProcessedFromResult(DeliveryResult result)
    {
        // For now, assume 1 message per successful result
        // This could be improved by enhancing the DeliveryResult to include message counts
        return result.Success ? 1 : 0;
    }

    /// <summary>
    /// Checks if the service is healthy based on recent processing activity.
    /// </summary>
    private bool IsServiceHealthy()
    {
        var timeSinceLastSuccess = DateTime.UtcNow - _lastSuccessfulProcessing;
        var maxHealthyInterval = TimeSpan.FromMinutes(5); // Consider unhealthy if no success in 5 minutes

        // Service is healthy if it's been successful recently or if there are no messages to process
        return timeSinceLastSuccess <= maxHealthyInterval || _messageBuffer.GetCurrentSize() == 0;
    }

    /// <summary>
    /// Performs cleanup operations during service shutdown.
    /// </summary>
    private async Task PerformShutdownCleanupAsync()
    {
        try
        {
            _logger.LogInformation("Performing shutdown cleanup for SignalRBufferProcessorService");

            // Try to process any remaining messages during shutdown
            var remainingMessages = _messageBuffer.GetCurrentSize();
            if (remainingMessages > 0)
            {
                _logger.LogInformation(
                    "Processing {RemainingMessages} remaining messages during shutdown",
                    remainingMessages);

                // Give ourselves a reasonable timeout for shutdown processing
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                try
                {
                    // Process remaining messages
                    var finalProcessingAttempts = 0;
                    const int maxFinalAttempts = 5;

                    while (_messageBuffer.GetCurrentSize() > 0 &&
                           finalProcessingAttempts < maxFinalAttempts &&
                           !shutdownCts.Token.IsCancellationRequested)
                    {
                        await _messageBuffer.ProcessBufferAsync(_deliveryService, shutdownCts.Token);
                        finalProcessingAttempts++;

                        if (_messageBuffer.GetCurrentSize() > 0)
                        {
                            await Task.Delay(100, shutdownCts.Token); // Brief delay between attempts
                        }
                    }

                    var finalRemainingMessages = _messageBuffer.GetCurrentSize();
                    if (finalRemainingMessages > 0)
                    {
                        _logger.LogWarning(
                            "Service shutdown with {FinalRemainingMessages} unprocessed messages",
                            finalRemainingMessages);
                    }
                    else
                    {
                        _logger.LogInformation("All messages processed successfully during shutdown");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Shutdown processing was cancelled due to timeout");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during shutdown message processing");
                }
            }

            // Perform any additional cleanup
            await _messageBuffer.CleanupAsync();

            _logger.LogInformation("SignalRBufferProcessorService shutdown cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SignalRBufferProcessorService shutdown cleanup");
        }
    }
}

/// <summary>
/// Represents comprehensive metrics for the buffer processor service.
/// Provides insights into processing performance and service health.
/// </summary>
public record BufferProcessorMetrics
{
    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets how long the service has been running.
    /// </summary>
    public TimeSpan ServiceUptime { get; init; }

    /// <summary>
    /// Gets the total number of processing cycles completed.
    /// </summary>
    public long TotalProcessingCycles { get; init; }

    /// <summary>
    /// Gets the total number of messages processed by the service.
    /// </summary>
    public long TotalMessagesProcessed { get; init; }

    /// <summary>
    /// Gets the total number of processing errors encountered.
    /// </summary>
    public long TotalProcessingErrors { get; init; }

    /// <summary>
    /// Gets the timestamp of the last successful processing cycle.
    /// </summary>
    public DateTime LastSuccessfulProcessing { get; init; }

    /// <summary>
    /// Gets the average number of messages processed per cycle.
    /// </summary>
    public double AverageMessagesPerCycle { get; init; }

    /// <summary>
    /// Gets the error rate as a percentage of total cycles.
    /// </summary>
    public double ErrorRate { get; init; }

    /// <summary>
    /// Gets whether the service is currently healthy.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the current buffer size.
    /// </summary>
    public int CurrentBufferSize { get; init; }

    /// <summary>
    /// Gets the buffer capacity.
    /// </summary>
    public int BufferCapacity { get; init; }

    /// <summary>
    /// Gets whether the delivery service is available.
    /// </summary>
    public bool DeliveryServiceAvailable { get; init; }

    /// <summary>
    /// Gets the configured processing interval.
    /// </summary>
    public TimeSpan ProcessingInterval { get; init; }
}