using AIChat.Server.Services.SignalRBuffering.Models;

namespace AIChat.Server.Services.SignalRBuffering.Abstractions;

/// <summary>
/// Abstraction for delivering SignalR messages.
/// This interface allows the buffer processing system to deliver messages
/// without tightly coupling to specific SignalR implementation details.
/// </summary>
public interface ISignalRDeliveryService
{
    /// <summary>
    /// Delivers a SignalR message to the specified group.
    /// This method performs the actual SignalR broadcast operation.
    /// </summary>
    /// <param name="message">The message to deliver</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A DeliveryResult indicating the outcome of the delivery attempt</returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when SignalR service is unavailable</exception>
    Task<DeliveryResult> DeliverMessageAsync(SignalRMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the delivery service is available and can deliver messages.
    /// </summary>
    /// <returns>True if the service can deliver messages, false otherwise</returns>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the name of this delivery service implementation.
    /// Used for logging and diagnostics.
    /// </summary>
    string ServiceName { get; }
}
