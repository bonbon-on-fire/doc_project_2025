using System.Diagnostics;
using AIChat.Orleans.Services;
using AIChat.Server.Services.SignalRBuffering.Abstractions;
using AIChat.Server.Services.SignalRBuffering.Models;

namespace AIChat.Server.Services.SignalRBuffering.Implementations;

/// <summary>
/// Implementation of ISignalRDeliveryService that wraps ISignalRBroadcastService.
/// This service handles the actual delivery of buffered messages to SignalR clients
/// by delegating to the underlying SignalR broadcast infrastructure.
/// </summary>
public class SignalRDeliveryService : ISignalRDeliveryService
{
    private readonly ISignalRBroadcastService _broadcastService;
    private readonly ILogger<SignalRDeliveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the SignalRDeliveryService.
    /// </summary>
    /// <param name="broadcastService">The underlying SignalR broadcast service</param>
    /// <param name="logger">Logger instance</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
    public SignalRDeliveryService(
        ISignalRBroadcastService broadcastService,
        ILogger<SignalRDeliveryService> logger)
    {
        _broadcastService = broadcastService ?? throw new ArgumentNullException(nameof(broadcastService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsAvailable => _broadcastService.IsAvailable;

    /// <inheritdoc />
    public string ServiceName => $"SignalRDeliveryService({_broadcastService.GetType().Name})";

    /// <inheritdoc />
    public async Task<DeliveryResult> DeliverMessageAsync(SignalRMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogTrace(
                "Delivering buffered message: ID={MessageId}, Group={GroupName}, Method={MethodName}",
                message.Id, message.GroupName, message.MethodName);

            // Perform the actual SignalR broadcast
            await _broadcastService.BroadcastToGroupAsync(
                message.GroupName,
                message.MethodName,
                message.Payload);

            stopwatch.Stop();

            var deliveryTime = stopwatch.Elapsed;
            var totalTime = DateTime.UtcNow - message.Timestamp;

            _logger.LogTrace(
                "Message delivered successfully: ID={MessageId}, DeliveryTime={DeliveryTime:F2}ms, " +
                "TotalTime={TotalTime:F2}ms, RetryCount={RetryCount}",
                message.Id, deliveryTime.TotalMilliseconds, totalTime.TotalMilliseconds, message.RetryCount);

            return DeliveryResult.CreateSuccess(message.Id, deliveryTime, message.RetryCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "Message delivery cancelled: ID={MessageId}, DeliveryTime={DeliveryTime:F2}ms",
                message.Id, stopwatch.Elapsed.TotalMilliseconds);

            return DeliveryResult.Failed(
                message.Id,
                DeliveryStatus.Cancelled,
                "Delivery operation was cancelled",
                message.RetryCount);
        }
        catch (TimeoutException ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex,
                "Message delivery timed out: ID={MessageId}, DeliveryTime={DeliveryTime:F2}ms",
                message.Id, stopwatch.Elapsed.TotalMilliseconds);

            return DeliveryResult.Failed(
                message.Id,
                DeliveryStatus.Timeout,
                $"Delivery timed out: {ex.Message}",
                message.RetryCount);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "Message delivery failed: ID={MessageId}, Group={GroupName}, Method={MethodName}, " +
                "DeliveryTime={DeliveryTime:F2}ms, RetryCount={RetryCount}",
                message.Id, message.GroupName, message.MethodName,
                stopwatch.Elapsed.TotalMilliseconds, message.RetryCount);

            // Categorize the exception to provide appropriate delivery status
            var deliveryStatus = CategorizeDeliveryException(ex);

            return DeliveryResult.Failed(
                message.Id,
                deliveryStatus,
                ex.Message,
                message.RetryCount);
        }
    }

    /// <summary>
    /// Categorizes delivery exceptions to provide appropriate DeliveryStatus values.
    /// </summary>
    /// <param name="exception">The exception that occurred during delivery</param>
    /// <returns>The appropriate DeliveryStatus for the exception</returns>
    private static DeliveryStatus CategorizeDeliveryException(Exception exception)
    {
        return exception switch
        {
            TimeoutException => DeliveryStatus.Timeout,
            OperationCanceledException => DeliveryStatus.Cancelled,
            UnauthorizedAccessException => DeliveryStatus.Rejected,
            InvalidOperationException when exception.Message.Contains("not available") => DeliveryStatus.Rejected,
            ArgumentException => DeliveryStatus.Rejected,
            _ => DeliveryStatus.Failed
        };
    }
}
