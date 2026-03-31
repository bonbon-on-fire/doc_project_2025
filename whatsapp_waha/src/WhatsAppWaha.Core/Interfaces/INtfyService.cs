using WhatsAppWaha.Core.Models;

namespace WhatsAppWaha.Core.Interfaces;

/// <summary>
/// Contract for interacting with the ntfy service for message polling and notifications.
/// </summary>
public interface INtfyService
{
    /// <summary>
    /// Polls the specified topic for new messages.
    /// </summary>
    /// <param name="topic">The topic to poll for messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A polling response containing retrieved messages and metadata.</returns>
    Task<NtfyPollingResponse> PollMessagesAsync(string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the configured messages topic for new messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A polling response containing retrieved messages and metadata.</returns>
    Task<NtfyPollingResponse> PollMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the specified topic for new messages since a given timestamp.
    /// </summary>
    /// <param name="topic">The topic to poll for messages.</param>
    /// <param name="since">Poll for messages since this timestamp (Unix timestamp).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A polling response containing retrieved messages and metadata.</returns>
    Task<NtfyPollingResponse> PollMessagesSinceAsync(string topic, long since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notification to the specified topic.
    /// </summary>
    /// <param name="notification">The notification to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification was sent successfully; otherwise false.</returns>
    Task<bool> SendNotificationAsync(NtfyNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a notification using fire-and-forget pattern (non-blocking).
    /// </summary>
    /// <param name="notification">The notification to send.</param>
    /// <remarks>
    /// This method does not wait for the notification to be sent and does not throw exceptions.
    /// Failures are logged but do not affect the caller.
    /// </remarks>
    void SendNotificationFireAndForget(NtfyNotification notification);

    /// <summary>
    /// Sends a success notification to the configured notifications topic.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification was sent successfully; otherwise false.</returns>
    Task<bool> SendSuccessNotificationAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an error notification to the configured notifications topic.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification was sent successfully; otherwise false.</returns>
    Task<bool> SendErrorNotificationAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an info notification to the configured notifications topic.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification was sent successfully; otherwise false.</returns>
    Task<bool> SendInfoNotificationAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a warning notification to the configured notifications topic.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification was sent successfully; otherwise false.</returns>
    Task<bool> SendWarningNotificationAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends success notifications using fire-and-forget pattern.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    void SendSuccessNotificationFireAndForget(string title, string message);

    /// <summary>
    /// Sends error notifications using fire-and-forget pattern.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    void SendErrorNotificationFireAndForget(string title, string message);

    /// <summary>
    /// Sends info notifications using fire-and-forget pattern.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification message.</param>
    void SendInfoNotificationFireAndForget(string title, string message);

    /// <summary>
    /// Gets the message processing context for deduplication.
    /// </summary>
    /// <returns>The message processing context.</returns>
    MessageProcessingContext GetProcessingContext();

    /// <summary>
    /// Validates that the ntfy service is accessible and the configured topics are valid.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the service is accessible and configured correctly; otherwise false.</returns>
    Task<bool> ValidateServiceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the ntfy service usage.
    /// </summary>
    /// <returns>A dictionary containing service statistics.</returns>
    Dictionary<string, object> GetServiceStatistics();
}