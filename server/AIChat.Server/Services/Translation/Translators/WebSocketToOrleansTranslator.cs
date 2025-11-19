using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Orleans.Contracts;
using AIChat.Server.Models.WebSocket;

namespace AIChat.Server.Services.Translation.Translators;

/// <summary>
/// Translates WebSocket messages to Orleans ChatMessage objects.
/// Handles the conversion of structured WebSocket messages into the unified Orleans message format.
/// </summary>
public class WebSocketToOrleansTranslator : MessageTranslatorBase<WebSocketMessage, ChatMessage>
{
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.WebSocketToOrleansTranslator");

    /// <summary>
    /// Initializes a new instance of the WebSocketToOrleansTranslator.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public WebSocketToOrleansTranslator(ILogger<WebSocketToOrleansTranslator> logger) : base(logger)
    {
    }

    /// <inheritdoc />
    public override string TranslatorName => "WebSocket-to-Orleans";

    /// <inheritdoc />
    public override async Task<TranslationResult<ChatMessage>> TranslateAsync(
        WebSocketMessage source,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TranslateWebSocketToOrleans");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate source message
            var validationError = ValidateSource(source, context);
            if (validationError != null)
            {
                return validationError;
            }

            _ = (activity?.SetTag("websocket.type", source.Type));
            _ = (activity?.SetTag("websocket.session_id", source.SessionId));
            _ = (activity?.SetTag("websocket.message_id", source.MessageId));
            _ = (activity?.SetTag("correlation.id", context.CorrelationId));

            Logger.LogDebug(
                "Translating WebSocket message {MessageId} of type {Type} from session {SessionId}",
                source.MessageId,
                source.Type,
                source.SessionId
            );

            // Perform type-specific translation
            var chatMessage = source.Type.ToLowerInvariant() switch
            {
                WebSocketMessageTypes.ChatMessage => await TranslateChatMessageAsync(source, context, cancellationToken),
                WebSocketMessageTypes.ProtocolNegotiation => CreateProtocolNegotiationMessage(source, context),
                WebSocketMessageTypes.Control => CreateControlMessage(source, context),
                WebSocketMessageTypes.Heartbeat => CreateHeartbeatMessage(source, context),
                _ => throw new InvalidOperationException($"Unsupported WebSocket message type: {source.Type}")
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
                "Successfully translated WebSocket {Type} to Orleans ChatMessage {MessageId} in {Duration}ms",
                source.Type,
                chatMessage.Id,
                stopwatch.ElapsedMilliseconds
            );

            _ = (activity?.SetTag("translation.success", true));
            _ = (activity?.SetTag("orleans.message_id", chatMessage.Id));
            _ = (activity?.SetTag("orleans.chat_id", chatMessage.ChatId));

            return TranslationResult.SuccessWithTypes<WebSocketMessage, ChatMessage>(chatMessage, stopwatch.Elapsed);
        }
        catch (InvalidOperationException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Unsupported message type: {ex.Message}";
            Logger.LogWarning(ex, "Unsupported message type: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "UNSUPPORTED_MESSAGE_TYPE", context);
            _ = (activity?.SetTag("translation.success", false));
            _ = (activity?.SetTag("error.type", "UnsupportedMessageType"));
            return TranslationResult.Failure<ChatMessage>(errorMessage, "UNSUPPORTED_MESSAGE_TYPE", stopwatch.Elapsed);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Invalid JSON in WebSocket payload: {ex.Message}";
            Logger.LogWarning(ex, "Invalid JSON in WebSocket payload: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "INVALID_JSON_PAYLOAD", context);
            _ = (activity?.SetTag("translation.success", false));
            _ = (activity?.SetTag("error.type", "JsonException"));
            return TranslationResult.Failure<ChatMessage>(errorMessage, "INVALID_JSON_PAYLOAD", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Translation failed: {ex.Message}";
            Logger.LogError(ex, "Translation failed: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "TRANSLATION_ERROR", context);
            _ = (activity?.SetTag("translation.success", false));
            _ = (activity?.SetTag("error.type", ex.GetType().Name));
            return TranslationResult.Failure<ChatMessage>(errorMessage, "TRANSLATION_ERROR", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public override bool CanTranslate(WebSocketMessage source)
    {
        if (source == null)
        {
            return false;
        }

        var supportedTypes = new[]
        {
            WebSocketMessageTypes.ChatMessage,
            WebSocketMessageTypes.ProtocolNegotiation,
            WebSocketMessageTypes.Control,
            WebSocketMessageTypes.Heartbeat
        };

        return supportedTypes.Contains(source.Type, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ChatMessage> TranslateChatMessageAsync(
        WebSocketMessage source,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        // Extract chat message payload
        var payload = ExtractPayload<ChatMessagePayload>(source.Payload) ?? throw new InvalidOperationException("Invalid chat message payload format");

        // Use SessionId from context if available, otherwise use SessionId from message
        var userId = context.UserId ?? payload.UserId ?? source.SessionId;

        // Create Orleans ChatMessage
        var chatMessage = new ChatMessage
        {
            Id = source.MessageId,
            ChatId = payload.ChatId,
            UserId = userId,
            Content = payload.Content,
            Role = payload.Role ?? "user",
            Timestamp = source.Timestamp,
            IsStreaming = payload.IsStreaming,
            ParentId = payload.ParentId,
            Metadata = CreateMetadata(source, context, payload.Metadata)
        };

        Logger.LogDebug(
            "Created ChatMessage {MessageId} for chat {ChatId} from WebSocket user {UserId}",
            chatMessage.Id,
            chatMessage.ChatId,
            chatMessage.UserId
        );

        return await Task.FromResult(chatMessage);
    }

    private ChatMessage CreateProtocolNegotiationMessage(WebSocketMessage source, TranslationContext context)
    {
        var payload = ExtractPayload<ProtocolNegotiationPayload>(source.Payload);
        var userId = context.UserId ?? source.SessionId;

        return new ChatMessage
        {
            Id = source.MessageId,
            ChatId = "system", // Protocol negotiation is not chat-specific
            UserId = userId,
            Content = $"Protocol negotiation: {string.Join(", ", payload?.SupportedProtocols ?? [])}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "protocol_negotiation",
                ["supportedProtocols"] = payload?.SupportedProtocols ?? [],
                ["preferredProtocol"] = payload?.PreferredProtocol ?? "unknown"
            })
        };
    }

    private ChatMessage CreateControlMessage(WebSocketMessage source, TranslationContext context)
    {
        var payload = ExtractPayload<Dictionary<string, object>>(source.Payload) ?? [];
        var userId = context.UserId ?? source.SessionId;
        var action = payload.GetValueOrDefault("action", "unknown")?.ToString() ?? "unknown";
        var chatId = payload.GetValueOrDefault("chatId", "system").ToString();

        return new ChatMessage
        {
            Id = source.MessageId,
            ChatId = chatId ?? "system",
            UserId = userId,
            Content = $"Control action: {action}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "control",
                ["controlAction"] = action
            })
        };
    }

    private ChatMessage CreateHeartbeatMessage(WebSocketMessage source, TranslationContext context)
    {
        var payload = ExtractPayload<HeartbeatPayload>(source.Payload);
        var userId = context.UserId ?? source.SessionId;

        return new ChatMessage
        {
            Id = source.MessageId,
            ChatId = "system", // Heartbeats are not chat-specific
            UserId = userId,
            Content = "Heartbeat signal",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "heartbeat",
                ["clientTimestamp"] = payload?.ClientTimestamp ?? DateTime.UtcNow
            })
        };
    }

    private T? ExtractPayload<T>(object payload)
    {
        if (payload is T directCast)
        {
            return directCast;
        }

        if (payload is JsonElement jsonElement)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
            }
            catch (JsonException ex)
            {
                Logger.LogWarning(ex, "Failed to deserialize payload to {Type}", typeof(T).Name);
                return default;
            }
        }

        if (payload is string jsonString)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(jsonString);
            }
            catch (JsonException ex)
            {
                Logger.LogWarning(ex, "Failed to deserialize JSON string payload to {Type}", typeof(T).Name);
                return default;
            }
        }

        // Try to serialize to JSON and back to convert
        try
        {
            var json = JsonSerializer.Serialize(payload);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to convert payload to {Type} via JSON serialization", typeof(T).Name);
            return default;
        }
    }

    private string CreateMetadata(WebSocketMessage source, TranslationContext context, object? additionalData = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceProtocol"] = "WebSocket",
            ["sessionId"] = source.SessionId,
            ["correlationId"] = source.CorrelationId ?? context.CorrelationId ?? Guid.NewGuid().ToString(),
            ["originalTimestamp"] = source.Timestamp,
            ["translatedAt"] = DateTime.UtcNow,
            ["originalMessageId"] = source.MessageId,
            ["protocol"] = source.Protocol ?? "unknown"
        };

        // Add context properties
        foreach (var kvp in context.Properties)
        {
            metadata[$"context_{kvp.Key}"] = kvp.Value;
        }

        // Add additional data if provided
        if (additionalData != null)
        {
            if (additionalData is Dictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                metadata["additionalData"] = additionalData;
            }
        }

        try
        {
            return JsonSerializer.Serialize(metadata);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to serialize metadata, using fallback");
            return JsonSerializer.Serialize(new
            {
                sourceProtocol = "WebSocket",
                sessionId = source.SessionId,
                error = "Metadata serialization failed"
            });
        }
    }

    /// <inheritdoc />
    protected override TranslationResult<ChatMessage>? ValidateSource(WebSocketMessage source, TranslationContext context)
    {
        var baseValidation = base.ValidateSource(source, context);
        if (baseValidation != null)
        {
            return baseValidation;
        }

        if (string.IsNullOrEmpty(source.Type))
        {
            return TranslationResult.Failure<ChatMessage>("WebSocket message type is required", "MISSING_MESSAGE_TYPE");
        }

        if (string.IsNullOrEmpty(source.SessionId))
        {
            return TranslationResult.Failure<ChatMessage>("WebSocket session ID is required", "MISSING_SESSION_ID");
        }

        if (source.Payload == null)
        {
            return TranslationResult.Failure<ChatMessage>("WebSocket message payload is required", "MISSING_PAYLOAD");
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

        return null; // No validation errors
    }
}

/// <summary>
/// Payload structure for WebSocket chat messages.
/// </summary>
public class ChatMessagePayload
{
    /// <summary>
    /// Chat room identifier where the message belongs.
    /// </summary>
    [JsonPropertyName("chatId")]
    public required string ChatId { get; set; }

    /// <summary>
    /// User identifier who sent the message.
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>
    /// Message content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; set; }

    /// <summary>
    /// Message role (user, assistant, system).
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// Whether this is a streaming message.
    /// </summary>
    [JsonPropertyName("isStreaming")]
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Parent message ID for replies/threads.
    /// </summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>
    /// Additional message metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
