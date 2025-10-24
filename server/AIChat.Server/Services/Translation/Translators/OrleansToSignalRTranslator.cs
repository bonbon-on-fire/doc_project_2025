using System.Diagnostics;
using AIChat.Orleans.Contracts;
using AIChat.Server.Services.Translation.Models;

namespace AIChat.Server.Services.Translation.Translators;

/// <summary>
/// Translates Orleans MessageResult objects to SignalR response messages.
/// Handles the conversion of Orleans operation results back to SignalR client notifications.
/// </summary>
public class OrleansToSignalRTranslator : MessageTranslatorBase<MessageResult, SignalRResponse>
{
    private static readonly ActivitySource ActivitySource = new("AIChat.Server.OrleansToSignalRTranslator");

    /// <summary>
    /// Initializes a new instance of the OrleansToSignalRTranslator.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public OrleansToSignalRTranslator(ILogger<OrleansToSignalRTranslator> logger) : base(logger)
    {
    }

    /// <inheritdoc />
    public override string TranslatorName => "Orleans-to-SignalR";

    /// <inheritdoc />
    public override async Task<TranslationResult<SignalRResponse>> TranslateAsync(
        MessageResult source,
        TranslationContext context,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("TranslateOrleansToSignalR");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate source message
            var validationError = ValidateSource(source, context);
            if (validationError != null)
            {
                return validationError;
            }

            activity?.SetTag("orleans.success", source.Success);
            activity?.SetTag("orleans.has_message", source.Message != null);
            activity?.SetTag("correlation.id", context.CorrelationId);

            Logger.LogDebug(
                "Translating Orleans MessageResult (Success: {Success}) to SignalR response",
                source.Success
            );

            SignalRResponse signalRResponse;

            if (source.Success && source.Message != null)
                signalRResponse = await TranslateSuccessfulMessageAsync(source, context, cancellationToken);
            else
                signalRResponse = CreateErrorResponse(source, context);

            // Validate the result
            var targetValidationError = ValidateTarget(signalRResponse, context);
            if (targetValidationError != null)
            {
                return targetValidationError;
            }

            stopwatch.Stop();
            UpdateSuccessMetrics(stopwatch.Elapsed, context);

            Logger.LogDebug(
                "Successfully translated Orleans result to SignalR {Method} in {Duration}ms",
                signalRResponse.Method,
                stopwatch.ElapsedMilliseconds
            );

            activity?.SetTag("translation.success", true);
            activity?.SetTag("signalr.method", signalRResponse.Method);
            activity?.SetTag("signalr.target_type", signalRResponse.Target.Type.ToString());

            return TranslationResult.SuccessWithTypes<MessageResult, SignalRResponse>(signalRResponse, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorMessage = $"Translation failed: {ex.Message}";
            Logger.LogError(ex, "Translation failed: {ExceptionMessage}", ex.Message);
            UpdateFailureMetrics(stopwatch.Elapsed, "TRANSLATION_ERROR", context);
            activity?.SetTag("translation.success", false);
            activity?.SetTag("error.type", ex.GetType().Name);
            return TranslationResult.Failure<SignalRResponse>(errorMessage, "TRANSLATION_ERROR", stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public override bool CanTranslate(MessageResult source)
    {
        // Can translate any MessageResult
        return source != null;
    }

    private static async Task<SignalRResponse> TranslateSuccessfulMessageAsync(
        MessageResult source,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Suppress CS1998
        var message = source.Message!;

        // Determine the target for the SignalR response
        var target = DetermineTarget(message, context);

        // Create appropriate response based on message metadata
        var metadata = ParseMetadata(message.Metadata);
        var action = metadata.GetValueOrDefault("action", "message").ToString();

        return action?.ToLowerInvariant() switch
        {
            "join_group" => CreateJoinGroupResponse(message, target),
            "leave_group" => CreateLeaveGroupResponse(message, target),
            "heartbeat" => CreateHeartbeatResponse(message, target),
            "control" => CreateControlResponse(message, target, metadata),
            _ => CreateMessageReceivedResponse(message, target)
        };
    }

    private static SignalRResponse CreateErrorResponse(MessageResult source, TranslationContext context)
    {
        var connectionId = context.ConnectionId ?? "unknown";

        var errorData = new
        {
            success = false,
            error = source.ErrorMessage ?? "Unknown error occurred",
            errorCode = source.ErrorCode ?? "UNKNOWN_ERROR",
            timestamp = source.Timestamp
        };

        return SignalRResponse.CreateError(connectionId, errorData);
    }

    private static SignalRResponse CreateMessageReceivedResponse(ChatMessage message, SignalRTarget target)
    {
        var messageData = new
        {
            id = message.Id,
            chatId = message.ChatId,
            userId = message.UserId,
            content = message.Content,
            role = message.Role,
            timestamp = message.Timestamp,
            isStreaming = message.IsStreaming,
            parentId = message.ParentId
        };

        return new SignalRResponse
        {
            Method = "MessageReceived",
            Arguments = [messageData],
            Target = target,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SignalRResponse CreateJoinGroupResponse(ChatMessage message, SignalRTarget target)
    {
        var joinData = new
        {
            chatId = message.ChatId,
            userId = message.UserId,
            action = "user_joined",
            timestamp = message.Timestamp,
            message = message.Content
        };

        return new SignalRResponse
        {
            Method = "UserJoined",
            Arguments = [joinData],
            Target = target,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SignalRResponse CreateLeaveGroupResponse(ChatMessage message, SignalRTarget target)
    {
        var leaveData = new
        {
            chatId = message.ChatId,
            userId = message.UserId,
            action = "user_left",
            timestamp = message.Timestamp,
            message = message.Content
        };

        return new SignalRResponse
        {
            Method = "UserLeft",
            Arguments = [leaveData],
            Target = target,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SignalRResponse CreateHeartbeatResponse(ChatMessage message, SignalRTarget target)
    {
        var heartbeatData = new
        {
            serverTimestamp = DateTime.UtcNow,
            originalTimestamp = message.Timestamp,
            sessionId = ExtractSessionId(message),
            status = "alive"
        };

        return new SignalRResponse
        {
            Method = "HeartbeatResponse",
            Arguments = [heartbeatData],
            Target = target,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SignalRResponse CreateControlResponse(ChatMessage message, SignalRTarget target, Dictionary<string, object> metadata)
    {
        var controlAction = metadata.GetValueOrDefault("controlAction", "unknown").ToString();

        var controlData = new
        {
            action = controlAction,
            chatId = message.ChatId,
            userId = message.UserId,
            result = "success",
            timestamp = message.Timestamp,
            details = metadata
        };

        return new SignalRResponse
        {
            Method = "ControlResponse",
            Arguments = [controlData],
            Target = target,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SignalRTarget DetermineTarget(ChatMessage message, TranslationContext context)
    {
        // Priority order for determining target:
        // 1. Use ConnectionId from context if available (specific connection)
        // 2. Use ChatId for group broadcast (all users in chat)
        // 3. Use UserId for user-specific messages
        // 4. Default to all clients

        if (!string.IsNullOrEmpty(context.ConnectionId))
        {
            return SignalRTarget.Connection(context.ConnectionId);
        }

        if (!string.IsNullOrEmpty(message.ChatId) && message.ChatId != "system")
        {
            return SignalRTarget.Group($"chat_{message.ChatId}");
        }

        if (!string.IsNullOrEmpty(message.UserId))
        {
            return SignalRTarget.User(message.UserId);
        }

        return SignalRTarget.All();
    }

    private static Dictionary<string, object> ParseMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadata)
                   ?? [];
        }
        catch (Exception)
        {
            // Failed to parse metadata, return empty dictionary
            return [];
        }
    }

    private static string? ExtractSessionId(ChatMessage message)
    {
        var metadata = ParseMetadata(message.Metadata);
        return metadata.TryGetValue("sessionId", out var value) ? value?.ToString() : null;
    }

    /// <inheritdoc />
    protected override TranslationResult<SignalRResponse>? ValidateSource(MessageResult source, TranslationContext context)
    {
        var baseValidation = base.ValidateSource(source, context);
        if (baseValidation != null)
        {
            return baseValidation;
        }

        // MessageResult can be successful or failed, both are valid for translation
        return null; // No validation errors
    }

    /// <inheritdoc />
    protected override TranslationResult<SignalRResponse>? ValidateTarget(SignalRResponse target, TranslationContext context)
    {
        var baseValidation = base.ValidateTarget(target, context);
        if (baseValidation != null)
        {
            return baseValidation;
        }

        if (string.IsNullOrEmpty(target.Method))
        {
            return TranslationResult.Failure<SignalRResponse>("SignalR method is required", "MISSING_SIGNALR_METHOD");
        }

        if (target.Arguments == null)
        {
            return TranslationResult.Failure<SignalRResponse>("SignalR arguments cannot be null", "NULL_SIGNALR_ARGUMENTS");
        }

        if (target.Target == null)
        {
            return TranslationResult.Failure<SignalRResponse>("SignalR target cannot be null", "NULL_SIGNALR_TARGET");
        }

        return null; // No validation errors
    }
}