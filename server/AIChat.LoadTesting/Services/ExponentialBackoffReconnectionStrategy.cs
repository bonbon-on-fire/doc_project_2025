using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Implements an exponential backoff strategy for SSE reconnections.
/// </summary>
public class ExponentialBackoffReconnectionStrategy : ISseReconnectionStrategy
{
    private readonly IOptions<SseConfiguration> _config;
    private readonly ILogger<ExponentialBackoffReconnectionStrategy> _logger;

    public ExponentialBackoffReconnectionStrategy(
        IOptions<SseConfiguration> config,
        ILogger<ExponentialBackoffReconnectionStrategy> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool ShouldReconnect(int attemptNumber, Exception? lastError)
    {
        if (!_config.Value.EnableAutoReconnect)
        {
            return false;
        }

        if (attemptNumber > _config.Value.MaxReconnectAttempts)
        {
            _logger.LogWarning(
                "Maximum reconnection attempts ({MaxAttempts}) exceeded",
                _config.Value.MaxReconnectAttempts);
            return false;
        }

        // Don't reconnect for certain types of errors
        if (lastError is ObjectDisposedException or OperationCanceledException)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public TimeSpan GetReconnectDelay(int attemptNumber)
    {
        // Exponential backoff with jitter
        var baseDelay = _config.Value.ReconnectBackoffMs;
        var exponentialDelay = baseDelay * Math.Pow(2, Math.Min(attemptNumber - 1, 5));

        // Add jitter (±20% randomization)
        var jitter = Random.Shared.NextDouble() * 0.4 - 0.2; // -20% to +20%
        var delayWithJitter = exponentialDelay * (1 + jitter);

        // Cap at maximum delay (30 seconds)
        var maxDelay = 30000;
        var finalDelay = Math.Min(delayWithJitter, maxDelay);

        _logger.LogDebug(
            "Reconnection attempt {Attempt} will wait {Delay}ms",
            attemptNumber,
            finalDelay);

        return TimeSpan.FromMilliseconds(finalDelay);
    }

    /// <inheritdoc />
    public async Task HandleReconnectionAsync(ISseConnection connection, CancellationToken cancellationToken)
    {
        var attemptNumber = connection.ReconnectAttempts + 1;

        if (!ShouldReconnect(attemptNumber, null))
        {
            _logger.LogInformation(
                "Reconnection not attempted for connection {ConnectionId}",
                connection.ConnectionId);
            return;
        }

        var delay = GetReconnectDelay(attemptNumber);

        _logger.LogInformation(
            "Attempting to reconnect SSE connection {ConnectionId} (attempt {Attempt}/{Max}) after {Delay}ms",
            connection.ConnectionId,
            attemptNumber,
            _config.Value.MaxReconnectAttempts,
            delay.TotalMilliseconds);

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Successfully reconnected SSE connection {ConnectionId} on attempt {Attempt}",
                connection.ConnectionId,
                attemptNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to reconnect SSE connection {ConnectionId} on attempt {Attempt}",
                connection.ConnectionId,
                attemptNumber);

            // Recursively try again if we should
            if (ShouldReconnect(attemptNumber + 1, ex))
            {
                await HandleReconnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}