using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppWaha.Core.Configuration;
using WhatsAppWaha.Core.Exceptions;
using WhatsAppWaha.Core.Interfaces;
using WhatsAppWaha.Core.Models;

namespace WhatsAppWaha.Core.Services;

/// <summary>
/// Implementation of ntfy service for message polling and notification sending.
/// </summary>
public sealed class NtfyService : INtfyService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NtfyService> _logger;
    private readonly NtfySettings _settings;
    private readonly MessageProcessingContext _processingContext;
    private readonly JsonSerializerOptions _jsonOptions;

    // Statistics tracking
    private long _totalPollRequests = 0;
    private long _totalMessagesRetrieved = 0;
    private long _totalNotificationsSent = 0;
    private long _totalErrors = 0;
    private DateTime _lastPollTime = DateTime.MinValue;
    private DateTime _serviceStartTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of the <see cref="NtfyService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="ntfySettings">The ntfy configuration settings.</param>
    public NtfyService(
        IHttpClientFactory httpClientFactory,
        ILogger<NtfyService> logger,
        IOptions<NtfySettings> ntfySettings)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient("NtfyClient");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = ntfySettings?.Value ?? throw new ArgumentNullException(nameof(ntfySettings));
        _processingContext = new MessageProcessingContext(_settings.MaxProcessedMessageIds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        _logger.LogInformation(
            "NtfyService initialized with BaseUrl: {BaseUrl}, MessagesTopic: {MessagesTopic}, NotificationsTopic: {NotificationsTopic}",
            _settings.BaseUrl, _settings.MessagesTopic, _settings.NotificationsTopic);
    }

    /// <inheritdoc />
    public async Task<NtfyPollingResponse> PollMessagesAsync(CancellationToken cancellationToken = default)
    {
        return await PollMessagesAsync(_settings.MessagesTopic, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NtfyPollingResponse> PollMessagesAsync(string topic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new NtfyServiceException(
                "Topic cannot be null or empty",
                NtfyServiceException.ErrorCodes.InvalidTopic)
                .WithContext("topic", topic);
        }

        Interlocked.Increment(ref _totalPollRequests);
        _lastPollTime = DateTime.UtcNow;

        _logger.LogDebug("Polling messages from topic: {Topic}", topic);

        try
        {
            var url = BuildPollingUrl(topic);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return await ProcessPollingResponseAsync(responseContent, topic);
            }
            else
            {
                Interlocked.Increment(ref _totalErrors);
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogWarning(
                    "Polling failed for topic {Topic} with status {StatusCode}: {Error}",
                    topic, response.StatusCode, errorContent);

                return HandlePollingError(topic, response.StatusCode, errorContent);
            }
        }
        catch (TaskCanceledException ex)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogWarning(ex, "Polling request timed out for topic: {Topic}", topic);

            throw new NtfyServiceException(
                $"Polling request timed out for topic: {topic}",
                NtfyServiceException.ErrorCodes.ConnectionFailed,
                ex)
                .WithContext("topic", topic)
                .WithContext("timeout", _settings.TimeoutSeconds);
        }
        catch (HttpRequestException ex)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogError(ex, "Failed to connect to ntfy service for polling topic: {Topic}", topic);

            throw new NtfyServiceException(
                $"Failed to connect to ntfy service",
                NtfyServiceException.ErrorCodes.ConnectionFailed,
                ex)
                .WithContext("topic", topic)
                .WithContext("baseUrl", _settings.BaseUrl);
        }
        catch (Exception ex) when (!(ex is NtfyServiceException))
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogError(ex, "Unexpected error polling messages from topic: {Topic}", topic);

            throw new NtfyServiceException(
                "Unexpected error occurred while polling messages",
                NtfyServiceException.ErrorCodes.PollingFailed,
                ex)
                .WithContext("topic", topic);
        }
    }

    /// <inheritdoc />
    public async Task<NtfyPollingResponse> PollMessagesSinceAsync(string topic, long since, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new NtfyServiceException(
                "Topic cannot be null or empty",
                NtfyServiceException.ErrorCodes.InvalidTopic)
                .WithContext("topic", topic);
        }

        if (since < 0)
        {
            throw new NtfyServiceException(
                "Since timestamp must be non-negative",
                NtfyServiceException.ErrorCodes.PollingFailed)
                .WithContext("since", since);
        }

        Interlocked.Increment(ref _totalPollRequests);
        _lastPollTime = DateTime.UtcNow;

        _logger.LogDebug("Polling messages from topic: {Topic} since {Since}", topic, since);

        try
        {
            var url = BuildPollingUrl(topic, since);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return await ProcessPollingResponseAsync(responseContent, topic);
            }
            else
            {
                Interlocked.Increment(ref _totalErrors);
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogWarning(
                    "Polling since failed for topic {Topic} with status {StatusCode}: {Error}",
                    topic, response.StatusCode, errorContent);

                return HandlePollingError(topic, response.StatusCode, errorContent);
            }
        }
        catch (Exception ex) when (ex is not NtfyServiceException)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogError(ex, "Error polling messages since {Since} from topic: {Topic}", since, topic);

            throw new NtfyServiceException(
                "Error occurred while polling messages with since parameter",
                NtfyServiceException.ErrorCodes.PollingFailed,
                ex)
                .WithContext("topic", topic)
                .WithContext("since", since);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendNotificationAsync(NtfyNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        if (string.IsNullOrWhiteSpace(notification.Topic))
        {
            throw new NtfyServiceException(
                "Notification topic cannot be null or empty",
                NtfyServiceException.ErrorCodes.InvalidTopic)
                .WithContext("notification", notification);
        }

        if (string.IsNullOrWhiteSpace(notification.Message))
        {
            throw new NtfyServiceException(
                "Notification message cannot be null or empty",
                NtfyServiceException.ErrorCodes.NotificationSendFailed)
                .WithContext("topic", notification.Topic);
        }

        _logger.LogDebug(
            "Sending notification to topic: {Topic}, Title: {Title}",
            notification.Topic, notification.Title);

        try
        {
            var json = JsonSerializer.Serialize(notification, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(notification.Topic, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _totalNotificationsSent);

                _logger.LogInformation(
                    "Notification sent successfully to topic: {Topic}",
                    notification.Topic);
                return true;
            }
            else
            {
                Interlocked.Increment(ref _totalErrors);
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogWarning(
                    "Failed to send notification to topic {Topic} with status {StatusCode}: {Error}",
                    notification.Topic, response.StatusCode, errorContent);

                return false;
            }
        }
        catch (TaskCanceledException ex)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogWarning(ex, "Notification send timed out for topic: {Topic}", notification.Topic);
            throw new NtfyServiceException(
                $"Notification send timed out for topic: {notification.Topic}",
                NtfyServiceException.ErrorCodes.NotificationSendFailed,
                ex)
                .WithContext("topic", notification.Topic);
        }
        catch (HttpRequestException ex)
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogError(ex, "Failed to connect to ntfy service for notification to topic: {Topic}", notification.Topic);
            throw new NtfyServiceException(
                "Failed to connect to ntfy service for notification",
                NtfyServiceException.ErrorCodes.ConnectionFailed,
                ex)
                .WithContext("topic", notification.Topic);
        }
        catch (Exception ex) when (!(ex is NtfyServiceException))
        {
            Interlocked.Increment(ref _totalErrors);
            _logger.LogError(ex, "Unexpected error sending notification to topic: {Topic}", notification.Topic);
            throw new NtfyServiceException(
                "Unexpected error occurred while sending notification",
                NtfyServiceException.ErrorCodes.NotificationSendFailed,
                ex)
                .WithContext("topic", notification.Topic);
        }
    }

    /// <inheritdoc />
    public void SendNotificationFireAndForget(NtfyNotification notification)
    {
        if (!_settings.EnableFireAndForget)
        {
            _logger.LogDebug("Fire-and-forget notifications are disabled, skipping notification");
            return;
        }

        if (notification == null)
        {
            _logger.LogWarning("Cannot send null notification in fire-and-forget mode");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SendNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Fire-and-forget notification failed for topic: {Topic}. Error: {Error}",
                    notification.Topic, ex.Message);
            }
        });
    }

    /// <inheritdoc />
    public async Task<bool> SendSuccessNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var notification = NtfyNotification.CreateSuccess(_settings.NotificationsTopic, title, message);
        return await SendNotificationAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendErrorNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var notification = NtfyNotification.CreateError(_settings.NotificationsTopic, title, message);
        return await SendNotificationAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendInfoNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var notification = NtfyNotification.CreateInfo(_settings.NotificationsTopic, title, message);
        return await SendNotificationAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendWarningNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var notification = NtfyNotification.CreateWarning(_settings.NotificationsTopic, title, message);
        return await SendNotificationAsync(notification, cancellationToken);
    }

    /// <inheritdoc />
    public void SendSuccessNotificationFireAndForget(string title, string message)
    {
        var notification = NtfyNotification.CreateSuccess(_settings.NotificationsTopic, title, message);
        SendNotificationFireAndForget(notification);
    }

    /// <inheritdoc />
    public void SendErrorNotificationFireAndForget(string title, string message)
    {
        var notification = NtfyNotification.CreateError(_settings.NotificationsTopic, title, message);
        SendNotificationFireAndForget(notification);
    }

    /// <inheritdoc />
    public void SendInfoNotificationFireAndForget(string title, string message)
    {
        var notification = NtfyNotification.CreateInfo(_settings.NotificationsTopic, title, message);
        SendNotificationFireAndForget(notification);
    }

    /// <inheritdoc />
    public MessageProcessingContext GetProcessingContext()
    {
        return _processingContext;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateServiceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating ntfy service accessibility");

        try
        {
            // Try to access the ntfy root endpoint to check if service is available
            var response = await _httpClient.GetAsync("/", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("ntfy service is accessible");
                return true;
            }
            else
            {
                _logger.LogWarning(
                    "ntfy service validation failed with status {StatusCode}",
                    response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate ntfy service accessibility");
            return false;
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object> GetServiceStatistics()
    {
        var contextStats = _processingContext.GetStatistics();
        var uptime = DateTime.UtcNow - _serviceStartTime;

        var stats = new Dictionary<string, object>
        {
            ["ServiceStartTime"] = _serviceStartTime,
            ["UptimeMinutes"] = uptime.TotalMinutes,
            ["TotalPollRequests"] = _totalPollRequests,
            ["TotalMessagesRetrieved"] = _totalMessagesRetrieved,
            ["TotalNotificationsSent"] = _totalNotificationsSent,
            ["TotalErrors"] = _totalErrors,
            ["LastPollTime"] = _lastPollTime,
            ["SuccessRate"] = _totalPollRequests > 0
                ? (double)(_totalPollRequests - _totalErrors) / _totalPollRequests
                : 1.0,
            ["Settings"] = new Dictionary<string, object>
            {
                ["BaseUrl"] = _settings.BaseUrl,
                ["MessagesTopic"] = _settings.MessagesTopic,
                ["NotificationsTopic"] = _settings.NotificationsTopic,
                ["PollingIntervalMs"] = _settings.PollingIntervalMs,
                ["MaxMessagesPerPoll"] = _settings.MaxMessagesPerPoll,
                ["EnableFireAndForget"] = _settings.EnableFireAndForget
            }
        };

        // Merge context statistics
        foreach (var kvp in contextStats)
        {
            stats[$"Context_{kvp.Key}"] = kvp.Value;
        }

        return stats;
    }

    /// <summary>
    /// Builds the URL for polling messages from a topic.
    /// </summary>
    /// <param name="topic">The topic to poll.</param>
    /// <param name="since">Optional since timestamp.</param>
    /// <returns>The polling URL.</returns>
    private string BuildPollingUrl(string topic, long? since = null)
    {
        var url = $"{topic}/json";
        var parameters = new List<string>();

        if (_settings.MaxMessagesPerPoll > 0)
        {
            parameters.Add($"poll={_settings.MaxMessagesPerPoll}");
        }

        if (since.HasValue)
        {
            parameters.Add($"since={since.Value}");
        }

        if (parameters.Count > 0)
        {
            url += "?" + string.Join("&", parameters);
        }

        return url;
    }

    /// <summary>
    /// Processes the polling response from ntfy.
    /// </summary>
    /// <param name="responseContent">The response content.</param>
    /// <param name="topic">The topic that was polled.</param>
    /// <returns>A processed polling response.</returns>
    private Task<NtfyPollingResponse> ProcessPollingResponseAsync(string responseContent, string topic)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return Task.FromResult(NtfyPollingResponse.CreateSuccess(Array.Empty<NtfyMessage>(), topic));
        }

        try
        {
            // Parse the messages from JSON
            var messages = ParseNtfyMessages(responseContent);

            if (messages == null || messages.Count == 0)
            {
                _logger.LogDebug("No messages found in polling response for topic: {Topic}", topic);
                return Task.FromResult(NtfyPollingResponse.CreateSuccess(Array.Empty<NtfyMessage>(), topic));
            }

            Interlocked.Add(ref _totalMessagesRetrieved, messages.Count);

            // Filter out already processed messages
            var unprocessedMessages = _processingContext.FilterUnprocessedMessages(messages).ToList();
            var duplicateCount = messages.Count - unprocessedMessages.Count;

            // Mark new messages as processed
            var newlyProcessedCount = _processingContext.MarkMessagesAsProcessed(
                unprocessedMessages.Select(m => m.Id));

            _logger.LogInformation(
                "Polled {TotalMessages} messages from topic {Topic}: {NewMessages} new, {DuplicateMessages} duplicates",
                messages.Count, topic, newlyProcessedCount, duplicateCount);

            return Task.FromResult(NtfyPollingResponse.CreateSuccess(messages, topic, duplicateCount));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse polling response for topic: {Topic}", topic);
            throw new NtfyServiceException(
                $"Failed to parse polling response for topic: {topic}",
                NtfyServiceException.ErrorCodes.MessageRetrievalFailed,
                ex)
                .WithContext("topic", topic)
                .WithContext("responseContent", responseContent);
        }
    }

    /// <summary>
    /// Parses ntfy messages from JSON response content.
    /// </summary>
    /// <param name="responseContent">The response content.</param>
    /// <returns>The parsed messages.</returns>
    private List<NtfyMessage> ParseNtfyMessages(string responseContent)
    {
        // ntfy returns messages as newline-delimited JSON
        var messages = new List<NtfyMessage>();
        var lines = responseContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            try
            {
                var message = JsonSerializer.Deserialize<NtfyMessage>(line, _jsonOptions);
                if (message != null)
                {
                    messages.Add(message);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse individual message line: {Line}", line);
                // Continue processing other messages
            }
        }

        return messages;
    }

    /// <summary>
    /// Handles polling errors and returns an appropriate response.
    /// </summary>
    /// <param name="topic">The topic that was polled.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="errorContent">The error content.</param>
    /// <returns>A failed polling response.</returns>
    private NtfyPollingResponse HandlePollingError(string topic, System.Net.HttpStatusCode statusCode, string errorContent)
    {
        var errorMessage = $"HTTP {(int)statusCode} {statusCode}";
        if (!string.IsNullOrWhiteSpace(errorContent))
        {
            errorMessage += $": {errorContent}";
        }

        // For certain errors, we should throw exceptions instead of returning failed responses
        if (statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
            statusCode == System.Net.HttpStatusCode.InternalServerError)
        {
            throw new NtfyServiceException(
                $"ntfy service error: {errorMessage}",
                NtfyServiceException.ErrorCodes.ServiceUnavailable)
                .WithContext("topic", topic)
                .WithContext("statusCode", statusCode);
        }

        return NtfyPollingResponse.CreateFailure(topic, errorMessage);
    }
}