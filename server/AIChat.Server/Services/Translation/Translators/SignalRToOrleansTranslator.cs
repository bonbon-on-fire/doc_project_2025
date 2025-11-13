using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services.Translation.Models;

namespace AIChat.Server.Services.Translation.Translators;

/// <summary>
/// Translates SignalR hub method calls to Orleans ChatMessage objects.
/// Handles the conversion of SignalR-specific operations into the unified Orleans message format.
/// </summary>
public class SignalRToOrleansTranslator : MessageTranslatorBase<SignalRMessage, ChatMessage>
{
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.SignalRToOrleansTranslator");

    /// <summary>
    /// Initializes a new instance of the SignalRToOrleansTranslator.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public SignalRToOrleansTranslator(ILogger<SignalRToOrleansTranslator> logger) : base(logger)
    {
    }

    /// <inheritdoc />
    public override string TranslatorName => "SignalR-to-Orleans";

    /// <inheritdoc />
    public override async Task<TranslationResult<ChatMessage>> TranslateAsync(
        SignalRMessage source,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TranslateSignalRToOrleans");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate source message
            var validationError = ValidateSource(source, context);
            if (validationError != null)
            {
                return validationError;
            }

            activity?.SetTag("signalr.operation", source.Operation);
            activity?.SetTag("signalr.connection_id", source.ConnectionId);
            activity?.SetTag("correlation.id", context.CorrelationId);

            Logger.LogDebug(
                "Translating SignalR operation {Operation} from connection {ConnectionId}",
                source.Operation,
                source.ConnectionId
            );

            // Validate SignalR message
            var signalRValidation = source.Validate();
            if (!signalRValidation.IsValid)
            {
                var errorMessage = $"Invalid SignalR message: {signalRValidation.GetErrorsString()}";
                Logger.LogWarning("Invalid SignalR message: {ValidationErrors}", signalRValidation.GetErrorsString());
                stopwatch.Stop();
                UpdateFailureMetrics(stopwatch.Elapsed, "INVALID_SIGNALR_MESSAGE", context);
                return TranslationResult.Failure<ChatMessage>(errorMessage, "INVALID_SIGNALR_MESSAGE", stopwatch.Elapsed);
            }

            // Perform operation-specific translation
            var chatMessage = source.Operation.ToLowerInvariant() switch
            {
                "sendmessage" => await TranslateSendMessageAsync(source, context, cancellationToken),
                "joinchatgroup" => CreateJoinGroupMessage(source, context),
                "leavechatgroup" => CreateLeaveGroupMessage(source, context),
                _ => throw new InvalidOperationException($"Unsupported SignalR operation: {source.Operation}")
            };

            // Validate the result
            var targetValidationError = ValidateTarget(chatMessage, context);
            if (targetValidationError != null)
            {
                return targetValidationError;
            }

            stopwatch.Stop();
            UpdateSuccessMetrics(stopwatch.Elapsed, context);

            Logger.LogDebug(
                "Successfully translated SignalR {Operation} to Orleans ChatMessage {MessageId} in {Duration}ms",
                source.Operation,
                chatMessage.Id,
                stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("translation.success", true);
            activity?.SetTag("orleans.message_id", chatMessage.Id);
            activity?.SetTag("orleans.chat_id", chatMessage.ChatId);

            return TranslationResult.SuccessWithTypes<SignalRMessage, ChatMessage>(chatMessage, stopwatch.Elapsed);
        }
        catch (InvalidOperationException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Unsupported operation: {ex.Message}";
            Logger.LogWarning(ex, "Unsupported operation: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "UNSUPPORTED_OPERATION", context);
            activity?.SetTag("translation.success", false);
            activity?.SetTag("error.type", "UnsupportedOperation");
            return TranslationResult.Failure<ChatMessage>(errorMessage, "UNSUPPORTED_OPERATION", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Translation failed: {ex.Message}";
            Logger.LogError(ex, "Translation failed: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "TRANSLATION_ERROR", context);
            activity?.SetTag("translation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            return TranslationResult.Failure<ChatMessage>(errorMessage, "TRANSLATION_ERROR", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public override bool CanTranslate(SignalRMessage source)
    {
        if (source == null)
        {
            return false;
        }

        var supportedOperations = new[] { "sendmessage", "joinchatgroup", "leavechatgroup" };
        return supportedOperations.Contains(source.Operation.ToLowerInvariant());
    }

    private async Task<ChatMessage> TranslateSendMessageAsync(
        SignalRMessage source,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var chatId = source.GetParameterAsString("chatId");
        var userId = source.GetParameterAsString("userId");
        var messageContent = source.GetParameterAsString("message");

        // Use UserId from context if not provided in parameters
        if (string.IsNullOrEmpty(userId))
        {
            userId = context.UserId ?? source.UserId ?? source.ConnectionId;
        }

        // Create Orleans ChatMessage
        var chatMessage = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = messageContent,
            Role = "user", // SignalR messages are always from users
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context)
        };

        Logger.LogDebug(
            "Created ChatMessage {MessageId} for chat {ChatId} from SignalR user {UserId}",
            chatMessage.Id,
            chatMessage.ChatId,
            chatMessage.UserId
        );

        return await Task.FromResult(chatMessage);
    }

    private ChatMessage CreateJoinGroupMessage(SignalRMessage source, TranslationContext context)
    {
        var chatId = source.GetParameterAsString("chatId");
        var userId = context.UserId ?? source.UserId ?? source.ConnectionId;

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = $"User {userId} joined the chat",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "join_group",
                ["originalOperation"] = source.Operation
            })
        };
    }

    private ChatMessage CreateLeaveGroupMessage(SignalRMessage source, TranslationContext context)
    {
        var chatId = source.GetParameterAsString("chatId");
        var userId = context.UserId ?? source.UserId ?? source.ConnectionId;

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = $"User {userId} left the chat",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "leave_group",
                ["originalOperation"] = source.Operation
            })
        };
    }

    private string CreateMetadata(SignalRMessage source, TranslationContext context, Dictionary<string, object>? additionalData = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceProtocol"] = "SignalR",
            ["connectionId"] = source.ConnectionId,
            ["correlationId"] = context.CorrelationId ?? Guid.NewGuid().ToString(),
            ["originalTimestamp"] = source.Timestamp,
            ["translatedAt"] = DateTime.UtcNow
        };

        // Add any additional SignalR metadata
        foreach (var kvp in source.Metadata)
        {
            metadata[$"signalr_{kvp.Key}"] = kvp.Value;
        }

        // Add context properties
        foreach (var kvp in context.Properties)
        {
            metadata[$"context_{kvp.Key}"] = kvp.Value;
        }

        // Add additional data if provided
        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(metadata);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to serialize metadata, using fallback");
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceProtocol = "SignalR",
                connectionId = source.ConnectionId,
                error = "Metadata serialization failed"
            });
        }
    }

    /// <inheritdoc />
    protected override TranslationResult<ChatMessage>? ValidateSource(SignalRMessage source, TranslationContext context)
    {
        var baseValidation = base.ValidateSource(source, context);
        if (baseValidation != null)
        {
            return baseValidation;
        }

        if (string.IsNullOrEmpty(source.Operation))
        {
            return TranslationResult.Failure<ChatMessage>("SignalR operation is required", "MISSING_OPERATION");
        }

        if (string.IsNullOrEmpty(source.ConnectionId))
        {
            return TranslationResult.Failure<ChatMessage>("SignalR connection ID is required", "MISSING_CONNECTION_ID");
        }

        return null; // No validation errors
    }

    /// <inheritdoc />
    protected override TranslationResult<ChatMessage>? ValidateTarget(ChatMessage target, TranslationContext context)
    {
        var baseValidation = base.ValidateTarget(target, context);
        if (baseValidation != null)
        {
            return baseValidation;
        }

        if (string.IsNullOrEmpty(target.ChatId))
        {
            return TranslationResult.Failure<ChatMessage>("ChatId is required in Orleans message", "MISSING_CHAT_ID");
        }

        if (string.IsNullOrEmpty(target.UserId))
        {
            return TranslationResult.Failure<ChatMessage>("UserId is required in Orleans message", "MISSING_USER_ID");
        }

        if (string.IsNullOrEmpty(target.Content))
        {
            return TranslationResult.Failure<ChatMessage>("Content is required in Orleans message", "MISSING_CONTENT");
        }

        return null; // No validation errors
    }
}
