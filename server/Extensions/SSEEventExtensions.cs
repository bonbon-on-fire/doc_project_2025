using AIChat.Server.Models.SSE;
using AIChat.Server.Services;

namespace AIChat.Server.Extensions;

/// <summary>
/// Extension methods for converting domain events to SSE DTOs
/// This provides a clean separation between domain events and API contracts
/// </summary>
public static class SSEEventExtensions
{
    /// <summary>
    /// Convert a StreamChunkEvent to the appropriate SSE envelope
    /// </summary>
    public static StreamChunkEventEnvelope ToSSEEnvelope(
        this StreamChunkEvent streamEvent)
    {
        object payload = streamEvent switch
        {
            TextStreamEvent textEvent => new TextStreamChunkPayload
            {
                Delta = textEvent.Delta,
                Done = textEvent.Done
            },
            ReasoningStreamEvent reasoningEvent => new ReasoningStreamChunkPayload
            {
                Delta = reasoningEvent.Delta,
                Visibility = reasoningEvent.Visibility?.ToString().ToLowerInvariant()
            },
            ToolsCallUpdateStreamEvent toolCallUpdateEvent => new ToolCallUpdateStreamChunkPayload
            {
                Delta = "",
                ToolCallUpdate = toolCallUpdateEvent.ToolCallUpdate
            },
            ToolsCallAggregateStreamEvent aggregateEvent => new ToolsCallAggregatePayload
            {
                Delta = "", // Required property for base class
                ToolCalls = aggregateEvent.ToolCalls,
                ToolResults = aggregateEvent.ToolResults
            },
            ToolResultStreamEvent toolResultEvent => new ToolResultStreamChunkPayload
            {
                Delta = "", // Required property for base class
                ToolCallId = toolResultEvent.ToolCallId,
                Result = toolResultEvent.Result,
                IsError = toolResultEvent.IsError
            },
            TaskUpdateStreamEvent taskUpdateEvent => new TaskUpdateStreamChunkPayload
            {
                Delta = "", // Required property for base class
                TaskState = taskUpdateEvent.TaskState,
                OperationType = taskUpdateEvent.OperationType
            },
            _ => throw new InvalidOperationException($"Unsupported stream event type: {streamEvent.GetType().Name}")
        };

        var envelope = new StreamChunkEventEnvelope
        {
            ChatId = streamEvent.ChatId,
            MessageId = streamEvent.MessageId,
            Kind = streamEvent.Kind,
            SequenceId = streamEvent.SequenceNumber,
            Payload = payload
        };

        return envelope;
    }

    /// <summary>
    /// Convert a MessageEvent to the appropriate SSE envelope
    /// </summary>
    public static MessageCompleteEventEnvelope ToSSEEnvelope(this MessageEvent messageEvent)
    {
        object payload = messageEvent switch
        {
            TextEvent textEvent => new TextCompletePayload
            {
                Text = textEvent.Text
            },
            ReasoningEvent reasoningEvent => new ReasoningCompletePayload
            {
                Reasoning = reasoningEvent.Reasoning,
                Visibility = reasoningEvent.Visibility?.ToString().ToLowerInvariant()
            },
            ToolCallEvent toolCallEvent => new ToolCallCompletePayload
            {
                ToolCalls = toolCallEvent.ToolCalls
            },
            ToolsCallAggregateEvent toolsAggregateEvent => new ToolsCallAggregateCompletePayload
            {
                ToolCalls = toolsAggregateEvent.ToolCalls,
                ToolResults = toolsAggregateEvent.ToolResults
            },
            UsageEvent usageEvent => new UsageCompletePayload
            {
                Usage = ConvertUsageToDictionary(usageEvent.Usage)
            },
            _ => throw new InvalidOperationException($"Unsupported message event type: {messageEvent.GetType().Name}")
        };

        var envelope = new MessageCompleteEventEnvelope
        {
            ChatId = messageEvent.ChatId,
            MessageId = messageEvent.MessageId,
            Kind = messageEvent.Kind,
            SequenceId = messageEvent.SequenceNumber,
            Payload = payload
        };

        return envelope;
    }

    /// <summary>
    /// Create an initialization event envelope
    /// </summary>
    public static InitEventEnvelope CreateInitEnvelope(
        string chatId,
        string userMessageId,
        DateTime userTimestamp,
        int userSequenceNumber)
    {
        return new InitEventEnvelope
        {
            ChatId = chatId,
            Kind = "meta",
            Payload = new InitPayload
            {
                UserMessageId = userMessageId,
                UserTimestamp = userTimestamp,
                UserSequenceNumber = userSequenceNumber
            }
        };
    }

    /// <summary>
    /// Create a stream completion event envelope
    /// </summary>
    public static StreamCompleteEventEnvelope CreateStreamCompleteEnvelope(string chatId)
    {
        return new StreamCompleteEventEnvelope
        {
            ChatId = chatId,
            Kind = "complete"
        };
    }

    /// <summary>
    /// Create an error event envelope
    /// </summary>
    public static ErrorEventEnvelope CreateErrorEnvelope(
        string chatId,
        string? messageId,
        int? sequenceId,
        string errorMessage,
        string? errorCode = null)
    {
        return new ErrorEventEnvelope
        {
            ChatId = chatId,
            MessageId = messageId,
            SequenceId = sequenceId,
            Kind = "error",
            Payload = new ErrorPayload
            {
                Message = errorMessage,
                Code = errorCode
            }
        };
    }

    /// <summary>
    /// Convert Usage object to dictionary for JSON serialization
    /// </summary>
    private static Dictionary<string, object> ConvertUsageToDictionary(AchieveAi.LmDotnetTools.LmCore.Core.Usage usage)
    {
        var result = new Dictionary<string, object>
        {
            ["promptTokens"] = usage.PromptTokens,
            ["completionTokens"] = usage.CompletionTokens,
            ["totalTokens"] = usage.TotalTokens
        };

        if (usage.TotalCost.HasValue)
        {
            result["totalCost"] = usage.TotalCost.Value;
        }

        return result;
    }
}
