using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services.Translation.Models;

namespace AIChat.Server.Services.Translation.Translators;

/// <summary>
/// Translates REST API requests to Orleans ChatMessage objects.
/// Handles the conversion of HTTP request data into the unified Orleans message format.
/// </summary>
public class RestToOrleansTranslator : MessageTranslatorBase<RestMessage, ChatMessage>
{
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.RestToOrleansTranslator");

    /// <summary>
    /// Initializes a new instance of the RestToOrleansTranslator.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public RestToOrleansTranslator(ILogger<RestToOrleansTranslator> logger) : base(logger)
    {
    }

    /// <inheritdoc />
    public override string TranslatorName => "REST-to-Orleans";

    /// <inheritdoc />
    public override async Task<TranslationResult<ChatMessage>> TranslateAsync(
        RestMessage source,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TranslateRestToOrleans");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate source message
            var validationError = ValidateSource(source, context);
            if (validationError != null)
            {
                return validationError;
            }

            activity?.SetTag("rest.method", source.Method);
            activity?.SetTag("rest.path", source.Path);
            activity?.SetTag("rest.operation", source.GetOperationType());
            activity?.SetTag("correlation.id", context.CorrelationId);

            Logger.LogDebug(
                "Translating REST {Method} {Path} operation {Operation}",
                source.Method,
                source.Path,
                source.GetOperationType()
            );

            // Validate REST message
            var restValidation = source.Validate();
            if (!restValidation.IsValid)
            {
                var errorMessage = $"Invalid REST message: {restValidation.GetErrorsString()}";
                Logger.LogWarning(errorMessage);
                stopwatch.Stop();
                UpdateFailureMetrics(stopwatch.Elapsed, "INVALID_REST_MESSAGE", context);
                return TranslationResult<ChatMessage>.Failure(errorMessage, "INVALID_REST_MESSAGE", stopwatch.Elapsed);
            }

            // Perform operation-specific translation
            var operationType = source.GetOperationType();
            var chatMessage = operationType switch
            {
                "SendMessage" => await TranslateSendMessageAsync(source, context, cancellationToken),
                "CreateChat" => CreateChatCreationMessage(source, context),
                "DeleteChat" => CreateChatDeletionMessage(source, context),
                "GetChat" => CreateChatRetrievalMessage(source, context),
                "GetChatHistory" => CreateChatHistoryMessage(source, context),
                _ => CreateGenericOperationMessage(source, context, operationType)
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
                "Successfully translated REST {Operation} to Orleans ChatMessage {MessageId} in {Duration}ms",
                operationType,
                chatMessage.Id,
                stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("translation.success", true);
            activity?.SetTag("orleans.message_id", chatMessage.Id);
            activity?.SetTag("orleans.chat_id", chatMessage.ChatId);

            return TranslationResult<ChatMessage>.SuccessWithTypes<RestMessage, ChatMessage>(chatMessage, stopwatch.Elapsed);
        }
        catch (JsonException ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Invalid JSON in REST body: {ex.Message}";
            Logger.LogWarning(ex, errorMessage);
            UpdateFailureMetrics(stopwatch.Elapsed, "INVALID_JSON_BODY", context);
            activity?.SetTag("translation.success", false);
            activity?.SetTag("error.type", "JsonException");
            return TranslationResult<ChatMessage>.Failure(errorMessage, "INVALID_JSON_BODY", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Translation failed: {ex.Message}";
            Logger.LogError(ex, errorMessage);
            UpdateFailureMetrics(stopwatch.Elapsed, "TRANSLATION_ERROR", context);
            activity?.SetTag("translation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            return TranslationResult<ChatMessage>.Failure(errorMessage, "TRANSLATION_ERROR", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public override bool CanTranslate(RestMessage source)
    {
        if (source == null) return false;

        var supportedOperations = new[]
        {
            "SendMessage", "CreateChat", "DeleteChat", "GetChat", "GetChatHistory"
        };

        var operationType = source.GetOperationType();
        return supportedOperations.Contains(operationType);
    }

    private async Task<ChatMessage> TranslateSendMessageAsync(
        RestMessage source,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var chatId = source.ExtractChatId();
        if (string.IsNullOrEmpty(chatId))
        {
            throw new InvalidOperationException("ChatId is required for SendMessage operation");
        }

        // Extract message content from request body
        var sendMessageRequest = source.GetBodyAs<SendMessageRequestDto>();
        if (sendMessageRequest == null || string.IsNullOrEmpty(sendMessageRequest.Message))
        {
            throw new InvalidOperationException("Invalid SendMessage request body");
        }

        var userId = context.UserId ?? source.UserId ?? "anonymous";

        // Create Orleans ChatMessage
        var chatMessage = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = sendMessageRequest.Message,
            Role = "user", // REST API messages are always from users
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["modeId"] = sendMessageRequest.ModeId ?? "default",
                ["source"] = "REST API"
            })
        };

        Logger.LogDebug(
            "Created ChatMessage {MessageId} for chat {ChatId} from REST user {UserId}",
            chatMessage.Id,
            chatMessage.ChatId,
            chatMessage.UserId
        );

        return await Task.FromResult(chatMessage);
    }

    private ChatMessage CreateChatCreationMessage(RestMessage source, TranslationContext context)
    {
        var createChatRequest = source.GetBodyAs<CreateChatRequestDto>();
        var userId = context.UserId ?? source.UserId ?? "anonymous";

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = "system", // Chat creation is system-level
            UserId = userId,
            Content = $"Chat creation request: {createChatRequest?.Title ?? "Untitled"}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "create_chat",
                ["title"] = createChatRequest?.Title ?? "Untitled",
                ["modeId"] = createChatRequest?.ModeId ?? "default"
            })
        };
    }

    private ChatMessage CreateChatDeletionMessage(RestMessage source, TranslationContext context)
    {
        var chatId = source.ExtractChatId() ?? "unknown";
        var userId = context.UserId ?? source.UserId ?? "anonymous";

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = $"Chat deletion request for chat {chatId}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "delete_chat",
                ["targetChatId"] = chatId
            })
        };
    }

    private ChatMessage CreateChatRetrievalMessage(RestMessage source, TranslationContext context)
    {
        var chatId = source.ExtractChatId() ?? "unknown";
        var userId = context.UserId ?? source.UserId ?? "anonymous";

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = $"Chat retrieval request for chat {chatId}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "get_chat",
                ["targetChatId"] = chatId
            })
        };
    }

    private ChatMessage CreateChatHistoryMessage(RestMessage source, TranslationContext context)
    {
        var userId = context.UserId ?? source.UserId ?? "anonymous";
        var page = int.TryParse(source.GetQueryParameter("page"), out var p) ? p : 1;
        var pageSize = int.TryParse(source.GetQueryParameter("pageSize"), out var ps) ? ps : 20;

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = "system", // History requests are system-level
            UserId = userId,
            Content = $"Chat history request: page {page}, size {pageSize}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "get_chat_history",
                ["page"] = page,
                ["pageSize"] = pageSize
            })
        };
    }

    private ChatMessage CreateGenericOperationMessage(RestMessage source, TranslationContext context, string operationType)
    {
        var chatId = source.ExtractChatId() ?? "system";
        var userId = context.UserId ?? source.UserId ?? "anonymous";

        return new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            UserId = userId,
            Content = $"REST operation: {operationType}",
            Role = "system",
            Timestamp = source.Timestamp,
            IsStreaming = false,
            Metadata = CreateMetadata(source, context, new Dictionary<string, object>
            {
                ["action"] = "generic_operation",
                ["operationType"] = operationType,
                ["method"] = source.Method,
                ["path"] = source.Path
            })
        };
    }

    private string CreateMetadata(RestMessage source, TranslationContext context, Dictionary<string, object>? additionalData = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceProtocol"] = "REST",
            ["method"] = source.Method,
            ["path"] = source.Path,
            ["correlationId"] = context.CorrelationId ?? Guid.NewGuid().ToString(),
            ["originalTimestamp"] = source.Timestamp,
            ["translatedAt"] = DateTime.UtcNow,
            ["contentType"] = source.ContentType ?? "unknown"
        };

        // Add headers (excluding sensitive ones)
        var safeHeaders = source.Headers
            .Where(h => !IsSensitiveHeader(h.Key))
            .ToDictionary(h => $"header_{h.Key}", h => (object)h.Value);

        foreach (var kvp in safeHeaders)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        // Add query parameters
        foreach (var kvp in source.QueryParameters)
        {
            metadata[$"query_{kvp.Key}"] = kvp.Value;
        }

        // Add path parameters
        foreach (var kvp in source.PathParameters)
        {
            metadata[$"path_{kvp.Key}"] = kvp.Value;
        }

        // Add REST metadata
        foreach (var kvp in source.Metadata)
        {
            metadata[$"rest_{kvp.Key}"] = kvp.Value;
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
            return JsonSerializer.Serialize(metadata);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to serialize metadata, using fallback");
            return JsonSerializer.Serialize(new
            {
                sourceProtocol = "REST",
                method = source.Method,
                path = source.Path,
                error = "Metadata serialization failed"
            });
        }
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        var sensitiveHeaders = new[]
        {
            "authorization", "cookie", "x-api-key", "x-auth-token"
        };

        return sensitiveHeaders.Contains(headerName.ToLowerInvariant());
    }

    /// <inheritdoc />
    protected override TranslationResult<ChatMessage>? ValidateSource(RestMessage source, TranslationContext context)
    {
        var baseValidation = base.ValidateSource(source, context);
        if (baseValidation != null) return baseValidation;

        if (string.IsNullOrEmpty(source.Method))
        {
            return TranslationResult<ChatMessage>.Failure("HTTP method is required", "MISSING_HTTP_METHOD");
        }

        if (string.IsNullOrEmpty(source.Path))
        {
            return TranslationResult<ChatMessage>.Failure("Request path is required", "MISSING_REQUEST_PATH");
        }

        return null; // No validation errors
    }

    /// <inheritdoc />
    protected override TranslationResult<ChatMessage>? ValidateTarget(ChatMessage target, TranslationContext context)
    {
        var baseValidation = base.ValidateTarget(target, context);
        if (baseValidation != null) return baseValidation;

        if (string.IsNullOrEmpty(target.ChatId))
        {
            return TranslationResult<ChatMessage>.Failure("ChatId is required in Orleans message", "MISSING_CHAT_ID");
        }

        if (string.IsNullOrEmpty(target.UserId))
        {
            return TranslationResult<ChatMessage>.Failure("UserId is required in Orleans message", "MISSING_USER_ID");
        }

        return null; // No validation errors
    }
}

/// <summary>
/// DTO for SendMessage REST API request.
/// </summary>
public class SendMessageRequestDto
{
    /// <summary>
    /// Message content.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional mode ID for the message.
    /// </summary>
    [JsonPropertyName("modeId")]
    public string? ModeId { get; set; }
}

/// <summary>
/// DTO for CreateChat REST API request.
/// </summary>
public class CreateChatRequestDto
{
    /// <summary>
    /// Chat title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional mode ID for the chat.
    /// </summary>
    [JsonPropertyName("modeId")]
    public string? ModeId { get; set; }

    /// <summary>
    /// Initial system prompt.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }
}