using System.Collections.Concurrent;
using AIChat.LoadTesting.Configuration;
using AIChat.LoadTesting.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIChat.LoadTesting.Services;

/// <summary>
/// Manages SignalR connections for load testing scenarios
/// </summary>
public class SignalRConnectionManager : IDisposable
{
    private readonly LoadTestingConfiguration _config;
    private readonly ILogger<SignalRConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, HubConnection> _connections = new();
    private readonly ConcurrentDictionary<string, TestUser> _users = new();
    private readonly ConcurrentDictionary<string, TestMessage> _pendingMessages = new();
    private readonly SemaphoreSlim _connectionSemaphore;
    private bool _disposed;

    public SignalRConnectionManager(
        IOptions<LoadTestingConfiguration> config,
        ILogger<SignalRConnectionManager> logger
    )
    {
        _config = config.Value;
        _logger = logger;
        _connectionSemaphore = new SemaphoreSlim(100, 100); // Limit concurrent connection attempts
    }

    /// <summary>
    /// Gets the current number of active connections
    /// </summary>
    public int ActiveConnectionCount =>
        _connections.Count(kvp => kvp.Value.State == HubConnectionState.Connected);

    /// <summary>
    /// Gets all test users
    /// </summary>
    public IEnumerable<TestUser> GetUsers()
    {
        return _users.Values;
    }

    /// <summary>
    /// Creates and connects a new SignalR connection for a test user
    /// </summary>
    public async Task<TestUser> CreateConnectionAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            var user = new TestUser
            {
                UserId = userId,
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
            };

            var connectionStart = DateTime.UtcNow;
            user.Metrics.ConnectionAttempts++;

            // Create connection with optimized settings for load testing
            var connection = new HubConnectionBuilder()
                .WithUrl(
                    _config.GetFullSignalRUrl(),
                    options =>
                    {
                        options.SkipNegotiation = true;
                        options.Transports = Microsoft
                            .AspNetCore
                            .Http
                            .Connections
                            .HttpTransportType
                            .WebSockets;
                        options.CloseTimeout = TimeSpan.FromSeconds(30);
                    }
                )
                .WithAutomaticReconnect(new SignalRReconnectPolicy())
                .Build();

            // Setup message handlers
            SetupMessageHandlers(connection, user);

            // Connect with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token
            );

            try
            {
                await connection.StartAsync(combinedCts.Token);

                user.ConnectionId = connection.ConnectionId ?? Guid.NewGuid().ToString();
                user.IsConnected = true;
                user.Metrics.ConnectionTime = DateTime.UtcNow - connectionStart;

                _connections[userId] = connection;
                _users[userId] = user;

                _logger.LogDebug(
                    "User {UserId} connected successfully in {ConnectionTime}ms",
                    userId,
                    user.Metrics.ConnectionTime.TotalMilliseconds
                );

                return user;
            }
            catch (Exception ex)
            {
                user.Metrics.ConnectionFailures++;
                user.Metrics.LastError = DateTime.UtcNow;
                user.Metrics.LastErrorMessage = ex.Message;

                _logger.LogWarning(ex, "Failed to connect user {UserId}", userId);

                await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _ = _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends a message through SignalR for a specific user
    /// </summary>
    public async Task<TestMessage> SendMessageAsync(
        string userId,
        string chatId,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !_connections.TryGetValue(userId, out var connection)
            || !_users.TryGetValue(userId, out var user)
        )
        {
            throw new InvalidOperationException($"User {userId} is not connected");
        }

        var message = new TestMessage
        {
            ChatId = chatId,
            UserId = userId,
            Content = content,
            SentAt = DateTime.UtcNow,
        };

        _pendingMessages[message.Id] = message;

        try
        {
            await connection.InvokeAsync("SendMessage", chatId, userId, content, cancellationToken);

            user.MessagesSent++;
            user.LastActivity = DateTime.UtcNow;

            _logger.LogTrace(
                "Message {MessageId} sent for user {UserId} to chat {ChatId}",
                message.Id,
                userId,
                chatId
            );

            return message;
        }
        catch (Exception ex)
        {
            user.Metrics.MessageFailures++;
            user.Metrics.LastError = DateTime.UtcNow;
            user.Metrics.LastErrorMessage = ex.Message;

            _ = _pendingMessages.TryRemove(message.Id, out _);

            _logger.LogWarning(ex, "Failed to send message for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Joins a chat group for the specified user
    /// </summary>
    public async Task JoinChatAsync(
        string userId,
        string chatId,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !_connections.TryGetValue(userId, out var connection)
            || !_users.TryGetValue(userId, out var user)
        )
        {
            throw new InvalidOperationException($"User {userId} is not connected");
        }

        try
        {
            await connection.InvokeAsync("JoinChatGroup", chatId, cancellationToken);

            if (!user.JoinedChats.Contains(chatId))
            {
                user.JoinedChats.Add(chatId);
            }

            user.LastActivity = DateTime.UtcNow;

            _logger.LogTrace("User {UserId} joined chat {ChatId}", userId, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to join chat {ChatId} for user {UserId}",
                chatId,
                userId
            );
            throw;
        }
    }

    /// <summary>
    /// Disconnects a specific user
    /// </summary>
    public async Task DisconnectUserAsync(string userId)
    {
        if (_connections.TryRemove(userId, out var connection))
        {
            try
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting user {UserId}", userId);
            }
        }

        if (_users.TryGetValue(userId, out var user))
        {
            user.IsConnected = false;
            _ = _users.TryUpdate(userId, user, user);
        }

        _logger.LogDebug("User {UserId} disconnected", userId);
    }

    /// <summary>
    /// Disconnects all users
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        _logger.LogInformation("Disconnecting all {Count} users", _connections.Count);

        var disconnectTasks = _connections.Keys.Select(DisconnectUserAsync);
        await Task.WhenAll(disconnectTasks);

        _logger.LogInformation("All users disconnected");
    }

    /// <summary>
    /// Gets connection metrics for all users
    /// </summary>
    public ConnectionMetrics GetConnectionMetrics()
    {
        var users = _users.Values.ToList();

        return new ConnectionMetrics
        {
            TotalUsers = users.Count,
            ConnectedUsers = users.Count(u => u.IsConnected),
            AverageConnectionTime =
                users.Count > 0
                    ? users.Average(u => u.Metrics.ConnectionTime.TotalMilliseconds)
                    : 0,
            ConnectionSuccessRate =
                users.Count > 0 ? users.Average(u => u.Metrics.ConnectionSuccessRate) : 0,
            TotalMessagesSent = users.Sum(u => u.MessagesSent),
            TotalMessagesReceived = users.Sum(u => u.MessagesReceived),
            AverageLatency = CalculateAverageLatency(users),
            ErrorCount = users.Sum(u => u.Metrics.ConnectionFailures + u.Metrics.MessageFailures),
        };
    }

    private void SetupMessageHandlers(HubConnection connection, TestUser user)
    {
        // Handle received messages
        _ = connection.On<object>(
            "ReceiveMessage",
            (messageData) =>
            {
                user.MessagesReceived++;
                user.LastActivity = DateTime.UtcNow;

                // Try to extract message ID for latency calculation
                if (
                    ExtractMessageId(messageData, out var messageId)
                    && _pendingMessages.TryRemove(messageId, out var pendingMessage)
                )
                {
                    pendingMessage.ReceivedAt = DateTime.UtcNow;
                    pendingMessage.IsDelivered = true;

                    if (pendingMessage.Latency.HasValue)
                    {
                        user.Metrics.MessageLatencies.Add(
                            pendingMessage.Latency.Value.TotalMilliseconds
                        );
                    }
                }

                _logger.LogTrace("User {UserId} received message", user.UserId);
            }
        );

        // Handle streaming chunks
        _ = connection.On<object>(
            "ReceiveStreamChunk",
            (chunkData) =>
            {
                user.LastActivity = DateTime.UtcNow;
                _logger.LogTrace("User {UserId} received stream chunk", user.UserId);
            }
        );

        // Handle errors
        _ = connection.On<object>(
            "ReceiveError",
            (errorData) =>
            {
                user.Metrics.MessageFailures++;
                user.Metrics.LastError = DateTime.UtcNow;

                if (errorData != null)
                {
                    user.Metrics.LastErrorMessage = errorData.ToString() ?? "Unknown error";
                }

                _logger.LogWarning("User {UserId} received error: {Error}", user.UserId, errorData);
            }
        );

        // Handle connection events
        connection.Closed += (error) =>
        {
            user.IsConnected = false;
            if (error != null)
            {
                user.Metrics.LastError = DateTime.UtcNow;
                user.Metrics.LastErrorMessage = error.Message;
                _logger.LogWarning(
                    "Connection closed for user {UserId}: {Error}",
                    user.UserId,
                    error.Message
                );
            }
            else
            {
                _logger.LogDebug("Connection closed for user {UserId}", user.UserId);
            }
            return Task.CompletedTask;
        };

        connection.Reconnected += (connectionId) =>
        {
            user.IsConnected = true;
            user.ConnectionId = connectionId ?? user.ConnectionId;
            user.LastActivity = DateTime.UtcNow;
            _logger.LogInformation(
                "User {UserId} reconnected with connection ID {ConnectionId}",
                user.UserId,
                connectionId
            );
            return Task.CompletedTask;
        };
    }

    private bool ExtractMessageId(object messageData, out string messageId)
    {
        messageId = string.Empty;

        try
        {
            // Try to extract message ID from the received data
            // This depends on the message format from the server
            var json = System.Text.Json.JsonSerializer.Serialize(messageData);
            var document = System.Text.Json.JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("Id", out var idProperty))
            {
                messageId = idProperty.GetString() ?? string.Empty;
                return !string.IsNullOrEmpty(messageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to extract message ID from message data");
        }

        return false;
    }

    private static double CalculateAverageLatency(List<TestUser> users)
    {
        var allLatencies = users.SelectMany(u => u.Metrics.MessageLatencies).ToList();
        return allLatencies.Count > 0 ? allLatencies.Average() : 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = Task.Run(async () =>
            {
                try
                {
                    await DisconnectAllAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during disposal");
                }
            })
            .Wait(TimeSpan.FromSeconds(30));

        _connectionSemaphore?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Connection metrics summary
/// </summary>
public class ConnectionMetrics
{
    public int TotalUsers { get; set; }
    public int ConnectedUsers { get; set; }
    public double AverageConnectionTime { get; set; }
    public double ConnectionSuccessRate { get; set; }
    public int TotalMessagesSent { get; set; }
    public int TotalMessagesReceived { get; set; }
    public double AverageLatency { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Custom reconnection policy for load testing
/// </summary>
public class SignalRReconnectPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // Don't retry in load testing to avoid skewing results
        return null;
    }
}
