using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services;
using AIChat.Server.Services.Routing;
using Microsoft.AspNetCore.SignalR;

namespace AIChat.Server.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication with Orleans integration.
/// This hub routes operations through DualModeRouter to support both Orleans grains
/// and direct service operations, providing seamless scalability and fallback capabilities.
///
/// Features:
/// - Standardized error handling with categorized responses
/// - Structured logging with performance metrics
/// - Orleans-aware connection management
/// - Backward compatibility with direct service mode
/// </summary>
public class ChatHub : Hub
{
    private readonly IDualModeRouter _dualModeRouter;
    private readonly IOrleansEventRelay _eventRelay;
    private readonly IChatService _chatService; // Kept for fallback compatibility
    private readonly ILogger<ChatHub> _logger;
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.ChatHub");

    /// <summary>
    /// Initializes a new instance of the ChatHub.
    /// </summary>
    /// <param name="dualModeRouter">Router for Orleans/direct service operations</param>
    /// <param name="eventRelay">Service for Orleans event relay</param>
    /// <param name="chatService">Fallback chat service for direct operations</param>
    /// <param name="logger">Logger instance</param>
    public ChatHub(
        IDualModeRouter dualModeRouter,
        IOrleansEventRelay eventRelay,
        IChatService chatService,
        ILogger<ChatHub> logger)
    {
        _dualModeRouter = dualModeRouter ?? throw new ArgumentNullException(nameof(dualModeRouter));
        _eventRelay = eventRelay ?? throw new ArgumentNullException(nameof(eventRelay));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to ChatService events for backward compatibility when Orleans is not available
        _chatService.MessageCreated += OnMessageCreated;
        _chatService.StreamChunkReceived += OnStreamChunkReceived;
        _chatService.MessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Joins a chat group, adding the connection to both SignalR groups and Orleans grain participant tracking.
    /// </summary>
    /// <param name="chatId">The chat ID to join</param>
    /// <returns>Task representing the async operation</returns>
    public async Task JoinChatGroup(string chatId)
    {
        using var activity = ActivitySource.StartActivity("JoinChatGroup");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate input
            var validationResult = ValidateChatId(chatId);
            if (!validationResult.IsValid)
            {
                await SendErrorResponseAsync("JoinChatGroup", validationResult.ErrorMessage!, chatId, ErrorCategory.Validation);
                return;
            }

            activity?.SetTag("chat.id", chatId);
            activity?.SetTag("connection.id", Context.ConnectionId);

            _logger.LogInformation("Starting JoinChatGroup operation for ChatId: {ChatId}, Connection: {ConnectionId}",
                chatId, Context.ConnectionId);

            // Always add to SignalR group first (fast, reliable)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");

            // Use DualModeRouter for Orleans/direct participant tracking
            await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await ExecuteOrleansJoinAsync(grain, chatId),
                directOperation: async service => await ExecuteDirectJoinAsync(chatId),
                operationName: "JoinChatGroup"
            );

            // Subscribe to Orleans events if available
            if (await _eventRelay.IsOrleansEnabledAsync())
            {
                await _eventRelay.SubscribeToGrainEventsAsync(
                    chatId,
                    OnMessageCreated,
                    OnStreamChunkReceived,
                    OnMessageReceived
                );
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "User {ConnectionId} successfully joined chat group {ChatId} in {ElapsedMs}ms",
                Context.ConnectionId, chatId, stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("operation.success", true);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleOperationErrorAsync(ex, "JoinChatGroup", chatId, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Leaves a chat group, removing the connection from both SignalR groups and Orleans grain participant tracking.
    /// </summary>
    /// <param name="chatId">The chat ID to leave</param>
    /// <returns>Task representing the async operation</returns>
    public async Task LeaveChatGroup(string chatId)
    {
        using var activity = ActivitySource.StartActivity("LeaveChatGroup");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate input
            var validationResult = ValidateChatId(chatId);
            if (!validationResult.IsValid)
            {
                await SendErrorResponseAsync("LeaveChatGroup", validationResult.ErrorMessage!, chatId, ErrorCategory.Validation);
                return;
            }

            activity?.SetTag("chat.id", chatId);
            activity?.SetTag("connection.id", Context.ConnectionId);

            _logger.LogInformation("Starting LeaveChatGroup operation for ChatId: {ChatId}, Connection: {ConnectionId}",
                chatId, Context.ConnectionId);

            // Always remove from SignalR group first
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");

            // Use DualModeRouter for Orleans/direct participant tracking
            await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await ExecuteOrleansLeaveAsync(grain, chatId),
                directOperation: async service => await ExecuteDirectLeaveAsync(chatId),
                operationName: "LeaveChatGroup"
            );

            // Unsubscribe from Orleans events
            await _eventRelay.UnsubscribeFromGrainEventsAsync(chatId);

            stopwatch.Stop();
            _logger.LogInformation(
                "User {ConnectionId} successfully left chat group {ChatId} in {ElapsedMs}ms",
                Context.ConnectionId, chatId, stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("operation.success", true);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleOperationErrorAsync(ex, "LeaveChatGroup", chatId, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Sends a message to a chat, routing through Orleans grains when available.
    /// </summary>
    /// <param name="chatId">The chat ID to send the message to</param>
    /// <param name="userId">The user ID sending the message</param>
    /// <param name="message">The message content</param>
    /// <returns>Task representing the async operation</returns>
    public async Task SendMessage(string chatId, string userId, string message)
    {
        using var activity = ActivitySource.StartActivity("SendMessage");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate inputs
            var chatValidation = ValidateChatId(chatId);
            if (!chatValidation.IsValid)
            {
                await SendErrorResponseAsync("SendMessage", chatValidation.ErrorMessage!, chatId, ErrorCategory.Validation);
                return;
            }

            var messageValidation = ValidateMessage(message);
            if (!messageValidation.IsValid)
            {
                await SendErrorResponseAsync("SendMessage", messageValidation.ErrorMessage!, chatId, ErrorCategory.Validation);
                return;
            }

            activity?.SetTag("chat.id", chatId);
            activity?.SetTag("user.id", userId);
            activity?.SetTag("connection.id", Context.ConnectionId);
            activity?.SetTag("message.length", message.Length);

            _logger.LogInformation("Starting SendMessage operation for ChatId: {ChatId}, UserId: {UserId}, MessageLength: {MessageLength}",
                chatId, userId, message.Length);

            await _dualModeRouter.ExecuteAsync(
                orleansOperation: async grain => await ExecuteOrleansSendMessageAsync(grain, chatId, userId, message),
                directOperation: async service => await ExecuteDirectSendMessageAsync(service, chatId, userId, message),
                operationName: "SendMessage"
            );

            stopwatch.Stop();
            _logger.LogInformation(
                "SendMessage operation completed for ChatId: {ChatId} in {ElapsedMs}ms",
                chatId, stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("operation.success", true);
            activity?.SetTag("operation.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleOperationErrorAsync(ex, "SendMessage", chatId, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("operation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
        }
    }

    /// <summary>
    /// Handles connection events when a client connects.
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client {ConnectionId} connected to ChatHub", Context.ConnectionId);

        // Perform Orleans-aware connection initialization if needed
        try
        {
            var isOrleansEnabled = await _eventRelay.IsOrleansEnabledAsync();
            _logger.LogDebug("Orleans enabled for connection {ConnectionId}: {IsEnabled}",
                Context.ConnectionId, isOrleansEnabled);

            // Future: Could initialize Orleans grain state for this connection here
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Orleans status for connection {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Handles disconnection events when a client disconnects.
    /// </summary>
    /// <param name="exception">Exception that caused the disconnection, if any</param>
    /// <returns>Task representing the async operation</returns>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client {ConnectionId} disconnected from ChatHub",
            Context.ConnectionId
        );

        try
        {
            // Unsubscribe from all Orleans events for this connection
            // Note: We don't have specific chat IDs here, so this is a general cleanup
            // In a production system, you'd want to track which chats this connection was in
            await _eventRelay.UnsubscribeFromGrainEventsAsync(Context.ConnectionId);

            // Update Orleans grain participant status if Orleans is available
            // This is a best-effort operation - if it fails, the grain will eventually
            // clean up stale connections through heartbeat mechanisms
            if (await _eventRelay.IsOrleansEnabledAsync())
            {
                // Future: Notify all relevant grains about this connection's disconnection
                _logger.LogDebug("Orleans cleanup for disconnected connection {ConnectionId}",
                    Context.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to perform Orleans cleanup for disconnection {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);

        // Unsubscribe from events when client disconnects (backward compatibility)
        _chatService.MessageCreated -= OnMessageCreated;
        _chatService.StreamChunkReceived -= OnStreamChunkReceived;
        _chatService.MessageReceived -= OnMessageReceived;
    }

    #region Private Helper Methods

    /// <summary>
    /// Executes Orleans-specific join chat group operation.
    /// </summary>
    private async Task ExecuteOrleansJoinAsync(IChatGrain grain, string chatId)
    {
        var participant = new ChatParticipant
        {
            ParticipantId = Context.UserIdentifier ?? Context.ConnectionId,
            DisplayName = Context.UserIdentifier ?? Context.ConnectionId,
            JoinedAt = DateTime.UtcNow,
            Status = PresenceStatus.Online,
            Role = ParticipantRole.Member, // Default role
            Metadata = System.Text.Json.JsonSerializer.Serialize(new { ConnectionId = Context.ConnectionId })
        };

        await grain.AddParticipantAsync(participant);
        _logger.LogInformation(
            "Orleans: User {ConnectionId} joined chat {ChatId} via grain",
            Context.ConnectionId,
            chatId
        );
    }

    /// <summary>
    /// Executes direct service join chat group operation.
    /// </summary>
    private async Task ExecuteDirectJoinAsync(string chatId)
    {
        // SignalR group already handles this in direct mode
        _logger.LogDebug(
            "Direct: User {ConnectionId} joined chat group {ChatId}",
            Context.ConnectionId,
            chatId
        );
        await Task.CompletedTask; // No additional operation needed
    }

    /// <summary>
    /// Executes Orleans-specific leave chat group operation.
    /// </summary>
    private async Task ExecuteOrleansLeaveAsync(IChatGrain grain, string chatId)
    {
        var participantId = Context.UserIdentifier ?? Context.ConnectionId;
        await grain.RemoveParticipantAsync(participantId);
        _logger.LogInformation(
            "Orleans: User {ConnectionId} left chat {ChatId} via grain",
            Context.ConnectionId,
            chatId
        );
    }

    /// <summary>
    /// Executes direct service leave chat group operation.
    /// </summary>
    private async Task ExecuteDirectLeaveAsync(string chatId)
    {
        // SignalR group removal already handles this in direct mode
        _logger.LogDebug(
            "Direct: User {ConnectionId} left chat group {ChatId}",
            Context.ConnectionId,
            chatId
        );
        await Task.CompletedTask; // No additional operation needed
    }

    /// <summary>
    /// Executes Orleans-specific send message operation.
    /// </summary>
    private async Task ExecuteOrleansSendMessageAsync(IChatGrain grain, string chatId, string userId, string message)
    {
        var chatMessage = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = message,
            Role = "user",
            Timestamp = DateTime.UtcNow
        };

        var result = await grain.ProcessMessageAsync(chatMessage);

        if (!result.Success)
        {
            _logger.LogError(
                "Orleans: Error processing message for chat {ChatId}: {Error}",
                chatId,
                result.ErrorMessage
            );

            await SendErrorResponseAsync("SendMessage",
                result.ErrorMessage ?? "Failed to process message via Orleans grain",
                chatId, ErrorCategory.Orleans);
        }
        else
        {
            _logger.LogInformation(
                "Orleans: Message processed successfully for chat {ChatId}",
                chatId
            );
        }
    }

    /// <summary>
    /// Executes direct service send message operation.
    /// </summary>
    private async Task ExecuteDirectSendMessageAsync(IChatService service, string chatId, string userId, string message)
    {
        // Use existing IChatService.SendMessageAsync logic
        var sendRequest = new SendMessageRequest
        {
            ChatId = chatId,
            UserId = userId,
            Message = message
        };

        var result = await service.SendMessageAsync(sendRequest);

        if (!result.Success)
        {
            _logger.LogError(
                "Direct: Error sending message for chat {ChatId}: {Error}",
                chatId,
                result.Error
            );

            await SendErrorResponseAsync("SendMessage",
                result.Error ?? "Failed to process message. Please try again.",
                chatId, ErrorCategory.Service);
        }
        else
        {
            _logger.LogInformation(
                "Direct: Message sent successfully for chat {ChatId}",
                chatId
            );
        }

        // Note: Real-time broadcasting is handled by ChatService events
        // The OnMessageCreated event handler will broadcast messages to SignalR clients
    }

    /// <summary>
    /// Validates chat ID input.
    /// </summary>
    private static ValidationResult ValidateChatId(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return ValidationResult.Failure("Chat ID cannot be null or empty");
        }

        if (chatId.Length > 100)
        {
            return ValidationResult.Failure("Chat ID cannot exceed 100 characters");
        }

        if (!Guid.TryParse(chatId, out _))
        {
            return ValidationResult.Failure("Chat ID must be a valid GUID format");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates message content.
    /// </summary>
    private static ValidationResult ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ValidationResult.Failure("Message content cannot be null or empty");
        }

        if (message.Length > 10000)
        {
            return ValidationResult.Failure("Message content cannot exceed 10,000 characters");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Sends standardized error response to the calling client.
    /// </summary>
    private async Task SendErrorResponseAsync(string operation, string errorMessage, string? chatId, ErrorCategory category)
    {
        var errorResponse = new StandardErrorResponse
        {
            Operation = operation,
            ErrorMessage = errorMessage,
            ChatId = chatId,
            Category = category.ToString(),
            Timestamp = DateTime.UtcNow,
            ConnectionId = Context.ConnectionId
        };

        await Clients.Caller.SendAsync("ReceiveError", errorResponse);

        _logger.LogWarning(
            "Error response sent for operation {Operation}: {ErrorMessage} (Category: {Category}, ChatId: {ChatId})",
            operation, errorMessage, category, chatId ?? "N/A"
        );
    }

    /// <summary>
    /// Handles operation errors with consistent logging and error reporting.
    /// </summary>
    private async Task HandleOperationErrorAsync(Exception ex, string operation, string? chatId, long elapsedMs)
    {
        var category = CategorizeException(ex);
        var userFriendlyMessage = GetUserFriendlyErrorMessage(ex, category);

        _logger.LogError(ex,
            "Operation {Operation} failed for ChatId: {ChatId}, Connection: {ConnectionId} after {ElapsedMs}ms (Category: {Category})",
            operation, chatId ?? "N/A", Context.ConnectionId, elapsedMs, category
        );

        await SendErrorResponseAsync(operation, userFriendlyMessage, chatId, category);
    }

    /// <summary>
    /// Categorizes exceptions for appropriate error handling.
    /// </summary>
    private static ErrorCategory CategorizeException(Exception ex)
    {
        return ex switch
        {
            ArgumentException or ArgumentNullException => ErrorCategory.Validation,
            TimeoutException => ErrorCategory.Timeout,
            InvalidOperationException when ex.Message.Contains("Orleans") => ErrorCategory.Orleans,
            UnauthorizedAccessException => ErrorCategory.Authentication,
            _ => ErrorCategory.General
        };
    }

    /// <summary>
    /// Gets user-friendly error messages based on exception category.
    /// </summary>
    private static string GetUserFriendlyErrorMessage(Exception ex, ErrorCategory category)
    {
        return category switch
        {
            ErrorCategory.Validation => ex.Message,
            ErrorCategory.Authentication => "You are not authorized to perform this operation",
            ErrorCategory.Orleans => "The chat service is temporarily unavailable. Please try again in a moment",
            ErrorCategory.Timeout => "The operation timed out. Please try again",
            ErrorCategory.Service => "A service error occurred. Please try again",
            _ => "An unexpected error occurred. Please try again"
        };
    }

    #endregion

    #region Supporting Types

    /// <summary>
    /// Represents the result of a validation operation.
    /// </summary>
    private readonly record struct ValidationResult(bool IsValid, string? ErrorMessage)
    {
        public static ValidationResult Success() => new(true, null);
        public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
    }

    /// <summary>
    /// Categories for error classification.
    /// </summary>
    private enum ErrorCategory
    {
        General,
        Validation,
        Orleans,
        Service,
        Authentication,
        Timeout
    }

    /// <summary>
    /// Standardized error response model.
    /// </summary>
    private sealed record StandardErrorResponse
    {
        public required string Operation { get; init; }
        public required string ErrorMessage { get; init; }
        public string? ChatId { get; init; }
        public required string Category { get; init; }
        public DateTime Timestamp { get; init; }
        public required string ConnectionId { get; init; }
    }

    #endregion

    #region Event Handlers

    // Event handlers for real-time broadcasting (backward compatibility)
    private async Task OnMessageCreated(MessageCreatedEvent messageEvent)
    {
        await Clients
            .Group($"chat_{messageEvent.ChatId}")
            .SendAsync(
                "ReceiveMessage",
                new
                {
                    messageEvent.Message.Id,
                    messageEvent.Message.ChatId,
                    messageEvent.Message.Role,
                    Content = (messageEvent.Message as TextMessageDto)?.Text ?? string.Empty,
                    messageEvent.Message.Timestamp,
                    messageEvent.Message.SequenceNumber,
                }
            );
    }

    private async Task OnStreamChunkReceived(StreamChunkEvent chunkEvent)
    {
        // Extract delta based on the specific event type
        var delta = chunkEvent switch
        {
            ReasoningStreamEvent reasoningEvent => reasoningEvent.Delta,
            TextStreamEvent textEvent => textEvent.Delta,
            _ => "",
        };

        await Clients
            .Group($"chat_{chunkEvent.ChatId}")
            .SendAsync(
                "ReceiveStreamChunk",
                new
                {
                    chunkEvent.MessageId,
                    chunkEvent.ChatId,
                    Delta = delta,
                    chunkEvent.Done,
                    chunkEvent.Kind,
                }
            );
    }

    private async Task OnMessageReceived(MessageEvent messageEvent)
    {
        // Handle complete message events (reasoning, text, usage)
        var messageData = messageEvent switch
        {
            ReasoningEvent reasoningEvent => new
            {
                messageEvent.MessageId,
                messageEvent.ChatId,
                messageEvent.Kind,
                Content = reasoningEvent.Reasoning,
                Visibility = reasoningEvent.Visibility?.ToString(),
            },
            TextEvent textEvent => new
            {
                messageEvent.MessageId,
                messageEvent.ChatId,
                messageEvent.Kind,
                Content = textEvent.Text,
                Visibility = (string?)null,
            },
            UsageEvent usageEvent => new
            {
                messageEvent.MessageId,
                messageEvent.ChatId,
                messageEvent.Kind,
                Content = System.Text.Json.JsonSerializer.Serialize(usageEvent.Usage),
                Visibility = (string?)null,
            },
            _ => new
            {
                messageEvent.MessageId,
                messageEvent.ChatId,
                messageEvent.Kind,
                Content = "",
                Visibility = (string?)null,
            },
        };

        await Clients
            .Group($"chat_{messageEvent.ChatId}")
            .SendAsync("ReceiveMessageComplete", messageData);
    }

    #endregion
}