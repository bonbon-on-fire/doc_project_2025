using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AIChat.Server.Models;
using AIChat.Server.Storage;
using Microsoft.Extensions.Options;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;

namespace AIChat.Server.Services;

public class ChatService(
    IChatStorage storage,
    IStreamingAgent streamingAgent,
    ILogger<ChatService> logger,
    IOptions<AiOptions> aiOptions,
    ITaskManagerService taskManagerService,
    IToolingService toolingService,
    IModeService modeService
) : IChatService, IToolResultCallback
{
    private readonly AiOptions _aiOptions = aiOptions.Value;

    private async Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddleware(
        string chatId,
        string? modeId = null,
        string? userId = null
    )
    {
        // Delegate to the tooling service
        return await toolingService.CreateChatSpecificFunctionCallMiddlewareAsync(
            chatId,
            modeId,
            userId,
            this, // Use this ChatService as the IToolResultCallback
            CancellationToken.None
        );
    }

    // Fields for tracking tool execution state
    private string? _currentChatId;
    private string? _currentMessageId;
    private int _nextSequence;

    // Map ToolCallId to MessageId and SequenceNumber for proper correlation
    // Using ConcurrentDictionary for thread-safe access during async streaming operations
    private readonly ConcurrentDictionary<
        string,
        (string MessageId, int SequenceNumber)
    > _toolCallToMessageMap = new();

    // Map ToolCallId to FunctionName for TaskManager detection
    private readonly ConcurrentDictionary<string, string> _toolCallToFunctionMap = new();

    // Track the last seen tool call ID for sequential streaming updates
    private string? _lastSeenToolCallId;

    // Events for real-time notifications
    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    public event Func<MessageEvent, Task>? MessageReceived;

    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
    {
        try
        {
            var now = DateTime.UtcNow;
            var title = GenerateChatTitle(request.Message);
            var create = await storage.CreateChatAsync(request.UserId, title, now, now, null);
            if (!create.Success || create.Chat == null)
            {
                return new ChatResult
                {
                    Success = false,
                    Error = create.Error ?? "Failed to create chat",
                };
            }

            var chat = create.Chat;

            // Insert initial user message
            var (allocSeqSuccess, allocSeqError, allocSeqNextSequence) =
                await storage.AllocateSequenceAsync(chat.Id);
            if (!allocSeqSuccess)
            {
                return new ChatResult { Success = false, Error = allocSeqError };
            }

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = allocSeqNextSequence,
                Text = request.Message,
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chat.Id,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    userDto,
                    MessageSerializationOptions.Default
                ),
            };

            var insUser = await storage.InsertMessageAsync(userRecord);
            if (!insUser.Success)
            {
                return new ChatResult { Success = false, Error = insUser.Error };
            }

            // Handle mode-based system prompt or explicit system prompt
            var systemPrompt = request.SystemPrompt;
            if (!string.IsNullOrEmpty(request.ModeId))
            {
                var modePromptResult = await modeService.GetModeSystemPromptAsync(
                    request.ModeId,
                    request.UserId
                );
                if (
                    modePromptResult.Success && !string.IsNullOrEmpty(modePromptResult.SystemPrompt)
                )
                {
                    // Mode system prompt takes precedence over request system prompt
                    systemPrompt = modePromptResult.SystemPrompt;
                    logger.LogInformation(
                        "Applied system prompt from mode {ModeId} for chat {ChatId}",
                        request.ModeId,
                        chat.Id
                    );
                }
            }

            // Insert system prompt if available
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                (allocSeqSuccess, allocSeqError, allocSeqNextSequence) =
                    await storage.AllocateSequenceAsync(chat.Id);
                if (!allocSeqSuccess)
                {
                    return new ChatResult { Success = false, Error = allocSeqError };
                }

                var sysDto = new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "system",
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                    SequenceNumber = allocSeqNextSequence,
                    Text = systemPrompt!,
                };
                var sysRecord = new MessageRecord
                {
                    Id = sysDto.Id,
                    ChatId = chat.Id,
                    Role = sysDto.Role,
                    Kind = "text",
                    TimestampUtc = sysDto.Timestamp,
                    SequenceNumber = sysDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(
                        sysDto,
                        MessageSerializationOptions.Default
                    ),
                };
                var insSys = await storage.InsertMessageAsync(sysRecord);
                if (!insSys.Success)
                {
                    return new ChatResult { Success = false, Error = insSys.Error };
                }
            }

            // Generate AI response with mode configuration
            var aiResponse = await GenerateAIResponseAsync(chat.Id, request.ModeId, request.UserId);

            _ = await storage.UpdateChatUpdatedAtAsync(chat.Id, DateTime.UtcNow);

            // Build DTO with ordered messages
            var (Success, Error, Messages) = await storage.ListChatMessagesOrderedAsync(chat.Id);

            var messages =
                Success && Messages != null
                    ? [.. Messages
                        .Select(m =>
                            JsonSerializer.Deserialize<MessageDto>(
                                m.MessageJson,
                                MessageSerializationOptions.Default
                            )!
                        )]
                    : new List<MessageDto> { userDto };

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Id,
                    UserId = chat.UserId,
                    Title = chat.Title,
                    CreatedAt = chat.CreatedAtUtc,
                    UpdatedAt = DateTime.UtcNow,
                    Messages = messages,
                },
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating chat");
            return new ChatResult { Success = false, Error = "Failed to create chat" };
        }
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        try
        {
            var chat = await storage.GetChatByIdAsync(chatId);
            if (!chat.Success || chat.Chat == null)
            {
                return new ChatResult { Success = false, Error = chat.Error ?? "Chat not found" };
            }
            var (Success, Error, Messages) = await storage.ListChatMessagesOrderedAsync(chatId);

            logger.LogInformation(
                "Loading messages for chat {ChatId}: Found {Count} messages",
                chatId,
                Messages?.Count ?? 0
            );

            var messages = Success
                ? Messages!
                    .Select(m =>
                    {
                        var dto = JsonSerializer.Deserialize<MessageDto>(
                            m.MessageJson,
                            MessageSerializationOptions.Default
                        )!;

                        // Log tool call messages for debugging
                        if (dto is ToolCallMessageDto toolCallDto)
                        {
                            logger.LogInformation(
                                "Loaded tool call message: Id={MessageId}, ToolCalls={ToolCallCount}",
                                dto.Id,
                                toolCallDto.ToolCalls?.Length ?? 0
                            );
                        }

                        return dto;
                    })
                    .ToList()
                : [];

            // Get task snapshot from TaskManagerService
            var taskSnapshot = await taskManagerService.GetTaskStateAsync(
                chatId,
                CancellationToken.None
            );

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Chat.Id,
                    UserId = chat.Chat.UserId,
                    Title = chat.Chat.Title,
                    Messages = messages,
                    CreatedAt = chat.Chat.CreatedAtUtc,
                    UpdatedAt = chat.Chat.UpdatedAtUtc,
                    Tasks = taskSnapshot?.Item2,
                },
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
            return new ChatResult { Success = false, Error = "Failed to retrieve chat" };
        }
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
    {
        try
        {
            var (histSuccess, histError, histChats, histTotalCount) =
                await storage.GetChatHistoryByUserAsync(userId, page, pageSize);
            var chats = histSuccess ? histChats : [];

            var chatDtos = new List<ChatDto>(chats.Count);
            foreach (var c in chats)
            {
                var (msgsSuccess, msgsError, msgsMessages) =
                    await storage.ListChatMessagesOrderedAsync(c.Id);
                var messages = msgsSuccess
                    ?
                    [
                        .. msgsMessages.Select(m =>
                            JsonSerializer.Deserialize<MessageDto>(
                                m.MessageJson,
                                MessageSerializationOptions.Default
                            )!
                        ),
                    ]
                    : new List<MessageDto>();

                chatDtos.Add(
                    new ChatDto
                    {
                        Id = c.Id,
                        UserId = c.UserId,
                        Title = c.Title,
                        Messages = messages,
                        CreatedAt = c.CreatedAtUtc,
                        UpdatedAt = c.UpdatedAtUtc,
                    }
                );
            }

            return new ChatHistoryResult
            {
                Success = true,
                Chats = chatDtos,
                TotalCount = histTotalCount,
                Page = page,
                PageSize = pageSize,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving chat history for user {UserId}", userId);
            return new ChatHistoryResult
            {
                Success = false,
                Error = "Failed to retrieve chat history",
            };
        }
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        try
        {
            // Clear TaskManager for this chat
            await taskManagerService.ClearTaskManagerAsync(chatId);

            // Delete the chat
            var (Success, Error) = await storage.DeleteChatAsync(chatId);
            return Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
            return false;
        }
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        try
        {
            var (Success, Error, NextSequence) = await storage.AllocateSequenceAsync(
                request.ChatId
            );
            if (!Success)
            {
                return new MessageResult { Success = false, Error = Error };
            }

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = NextSequence,
                Text = request.Message,
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = request.ChatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    userDto,
                    MessageSerializationOptions.Default
                ),
            };
            var insUser = await storage.InsertMessageAsync(userRecord);
            if (!insUser.Success)
            {
                return new MessageResult { Success = false, Error = insUser.Error };
            }

            if (MessageCreated != null)
            {
                await MessageCreated(
                    new MessageCreatedEvent { ChatId = request.ChatId, Message = userDto }
                );
            }

            var aiResponse = await GenerateAIResponseAsync(
                request.ChatId,
                request.ModeId,
                request.UserId
            );

            var asq = await storage.AllocateSequenceAsync(request.ChatId);
            if (!asq.Success)
            {
                return new MessageResult { Success = false, Error = asq.Error };
            }

            var assistantDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = asq.NextSequence,
                Text = aiResponse,
            };
            var assistantRecord = new MessageRecord
            {
                Id = assistantDto.Id,
                ChatId = request.ChatId,
                Role = assistantDto.Role,
                Kind = "text",
                TimestampUtc = assistantDto.Timestamp,
                SequenceNumber = assistantDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    assistantDto,
                    MessageSerializationOptions.Default
                ),
            };
            var insAsst = await storage.InsertMessageAsync(assistantRecord);
            if (!insAsst.Success)
            {
                return new MessageResult { Success = false, Error = insAsst.Error };
            }

            _ = await storage.UpdateChatUpdatedAtAsync(request.ChatId, DateTime.UtcNow);

            if (MessageCreated != null)
            {
                await MessageCreated(
                    new MessageCreatedEvent { ChatId = request.ChatId, Message = assistantDto }
                );
            }

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = assistantDto,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending message for chat {ChatId}", request.ChatId);
            return new MessageResult { Success = false, Error = "Failed to send message" };
        }
    }

    public async Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request)
    {
        logger.LogInformation(
            "[DEBUG] PrepareStreamChatAsync - UserId: {UserId}, Message: {Message}",
            request.UserId,
            request.Message
        );
        var now = DateTime.UtcNow;
        var createRes = await storage.CreateChatAsync(
            request.UserId,
            GenerateChatTitle(request.Message),
            now,
            now,
            null
        );

        if (!createRes.Success || createRes.Chat == null)
        {
            throw new InvalidOperationException(createRes.Error ?? "Create chat failed");
        }

        var chatId = createRes.Chat.Id;

        var (seqSuccess, seqError, userMsgSequence) = await storage.AllocateSequenceAsync(chatId);
        if (!seqSuccess)
        {
            throw new InvalidOperationException(seqError);
        }

        // Handle mode-based system prompt or explicit system prompt
        var systemPrompt = request.SystemPrompt;
        if (!string.IsNullOrEmpty(request.ModeId))
        {
            var (Success, Error, SystemPrompt) = await modeService.GetModeSystemPromptAsync(
                request.ModeId,
                request.UserId
            );
            if (Success && !string.IsNullOrEmpty(SystemPrompt))
            {
                // Mode system prompt takes precedence over request system prompt
                systemPrompt = SystemPrompt;
                logger.LogInformation(
                    "Applied system prompt from mode {ModeId} for stream chat {ChatId}",
                    request.ModeId,
                    chatId
                );
            }
        }

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var sysDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "system",
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                SequenceNumber = userMsgSequence++,
                Text = systemPrompt!,
            };

            _ = await storage.InsertMessageAsync(
                new MessageRecord
                {
                    Id = sysDto.Id,
                    ChatId = chatId,
                    Role = sysDto.Role,
                    Kind = "text",
                    TimestampUtc = sysDto.Timestamp,
                    SequenceNumber = sysDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(
                        sysDto,
                        MessageSerializationOptions.Default
                    ),
                }
            );
        }

        var userDto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = userMsgSequence,
            Text = request.Message,
        };

        _ = await storage.InsertMessageAsync(
            new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    userDto,
                    MessageSerializationOptions.Default
                ),
            }
        );

        return new StreamInitResult
        {
            ChatId = chatId,
            UserMessageId = userDto.Id,
            UserTimestamp = userDto.Timestamp,
            UserSequenceNumber = userDto.SequenceNumber,
        };
    }

    public async Task StreamChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var init = await PrepareStreamChatAsync(request);
        var chatId = init.ChatId;

        var (Success, Error, Messages) = await storage.ListChatMessagesOrderedAsync(
            chatId,
            cancellationToken
        );
        var history = Messages
            .Select(m =>
                JsonSerializer.Deserialize<MessageDto>(
                    m.MessageJson,
                    MessageSerializationOptions.Default
                )!
            )
            .Where(d =>
                (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
                || d is ReasoningMessageDto
            ) // Include ALL reasoning messages for LLM context
            .ToList();

        await StreamChatCompletionAsync(
            chatId,
            history,
            cancellationToken,
            request.ModeId,
            request.UserId
        );
    }

    public async Task StreamAssistantResponseAsync(
        string chatId,
        CancellationToken cancellationToken = default
    )
    {
        var (_, _, messages) = await storage.ListChatMessagesOrderedAsync(
            chatId,
            cancellationToken
        );
        var history = messages
            .Select(m =>
                JsonSerializer.Deserialize<MessageDto>(
                    m.MessageJson,
                    MessageSerializationOptions.Default
                )!
            )
            .Where(d =>
                (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
                || d is ReasoningMessageDto
            ) // Include ALL reasoning messages for LLM context
            .ToList();

        // Note: This method doesn't have mode context, will use default behavior
        await StreamChatCompletionAsync(chatId, history, cancellationToken);
    }

    private async Task StreamChatCompletionAsync(
        string chatId,
        List<MessageDto> history,
        CancellationToken cancellationToken,
        string? modeId = null,
        string? userId = null
    )
    {
        // Set context for tool result callbacks
        _currentChatId = chatId;
        _nextSequence = history.Count > 0 ? history.Max(m => m.SequenceNumber) + 1 : 0;
        _toolCallToMessageMap.Clear(); // Clear mapping for new stream
        _toolCallToFunctionMap.Clear(); // Clear function mapping for new stream
        _lastSeenToolCallId = null; // Clear last seen tool call ID for new stream

        logger.LogInformation(
            "[DEBUG] StreamChatCompletionAsync - ChatId: {ChatId}, History count: {Count}",
            chatId,
            history.Count
        );
        foreach (var msg in history)
        {
            logger.LogInformation(
                "[DEBUG] History message - Role: {Role}, Type: {Type}, Content: {Content}",
                msg.Role,
                msg.GetType().Name,
                msg is TextMessageDto textMsg
                    ? textMsg.Text?[..Math.Min(100, textMsg.Text.Length)] + "..."
                    : "N/A"
            );
        }

        var lmMessages = history.Select(ConvertToLmMessage).ToList();
        var userMsgSequence = history.Count > 0 ? history.Max(m => m.SequenceNumber) : 0;

        logger.LogInformation("[DEBUG] Converted to {Count} LM messages", lmMessages.Count);
        foreach (var lmMsg in lmMessages)
        {
            logger.LogInformation(
                "[DEBUG] LM message - Role: {Role}, Type: {Type}",
                lmMsg.Role,
                lmMsg.GetType().Name
            );
        }

        logger.LogInformation("Making API call to LLM for chat {ChatId}", chatId);
        var modelId = await GetModelIdAsync(modeId, userId);
        var options = new GenerateReplyOptions
        {
            ModelId = modelId,
            ExtraProperties = new Dictionary<string, object?>()
            {
                ["reasoning"] = "low",
                // ["frequency_penalty"] = 1.0f,
                // ["parallel_tool_calls"] = true,
                // ["provider"] = new
                // {
                //     order = new[] { "chutes" }
                // }
            }.ToImmutableDictionary(),
        };
        var streamProcessor = ProcessStream(chatId, userMsgSequence);

        // Build middleware chain
        var agent = streamingAgent
            .WithMiddleware(new JsonFragmentUpdateMiddleware())
            .WithMiddleware(
                (context, agent, cancellationToken) => agent.GenerateReplyAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken
                    ),
                async (context, agent, cancellationToken) =>
                {
                    var stream = await agent.GenerateReplyStreamingAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken
                    );

                    return streamProcessor(stream, cancellationToken);
                }
            );

        // Create and add chat-specific FunctionCallMiddleware
        var functionCallMiddleware = await CreateChatSpecificFunctionCallMiddleware(
            chatId,
            modeId,
            userId
        );
        if (functionCallMiddleware != null)
        {
            logger.LogInformation(
                "Adding chat-specific FunctionCallMiddleware to processing chain for chat {ChatId}",
                chatId
            );
            agent = agent.WithMiddleware(functionCallMiddleware);
        }
        else
        {
            logger.LogWarning(
                "FunctionCallMiddleware is null for chat {ChatId}, tool calls will not be processed",
                chatId
            );
        }

        var loop = false;
        var hasTextMessage = false;
        var fullMessageIndex = userMsgSequence;
        do
        {
            loop = false;
            var streamingResponse = await agent
                .WithMiddleware(new MessageUpdateJoinerMiddleware())
                .GenerateReplyStreamingAsync(
                    lmMessages,
                    options,
                    cancellationToken: cancellationToken
                );

            logger.LogInformation("Received response from LLM for chat {ChatId}", chatId);

            Type? lastFullMessageType = null;
            var replies = new List<IMessage>();
            await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
            {
                replies.Add(message);
                logger.LogInformation(
                    "[MIDDLEWARE] Message from middleware stream - Type: {MessageType}, ChatId: {ChatId}",
                    message.GetType().Name,
                    chatId
                );

                fullMessageIndex++;

                lastFullMessageType = message.GetType();
                // Ensure we have a valid and unique generation ID (add time salt for cache scenarios)
                var generationId = EnsureUniqueGenerationId(message.GenerationId, chatId);
                var fullMessageId = generationId + $"-{fullMessageIndex:D3}";
                _currentMessageId = fullMessageId; // Track for tool result callbacks

                logger.LogInformation(
                    "Persisting message from middleware - Type: {MessageType}, MessageId: {MessageId}, ChatId: {ChatId}",
                    message.GetType().Name,
                    fullMessageId,
                    chatId
                );

                var sequenceNumber = await PersistFullMessage(chatId, message, fullMessageId);

                // Skip sending encrypted reasoning messages to client (they have sequence -1)
                if (sequenceNumber == -1)
                {
                    logger.LogInformation(
                        "Encrypted reasoning persisted but not sent to client - ChatId: {ChatId}, MessageId: {MessageId}",
                        chatId,
                        fullMessageId
                    );
                    continue; // Skip to next message without sending to client
                }

                if (sequenceNumber != fullMessageIndex)
                {
                    logger.LogError(
                        "Sequence number mismatch: {Expected}, {Actual}",
                        fullMessageIndex,
                        sequenceNumber
                    );
                }

                if (MessageReceived != null)
                {
                    MessageEvent? evt = message switch
                    {
                        TextMessage textMessage => new TextEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "text",
                            SequenceNumber = sequenceNumber,
                            Text = textMessage.Text,
                        },
                        ReasoningMessage reasoningMessage => new ReasoningEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "reasoning",
                            SequenceNumber = sequenceNumber,
                            Reasoning = reasoningMessage.Reasoning,
                            Visibility = reasoningMessage.Visibility,
                        },
                        ToolsCallMessage toolsCallMessage => new ToolCallEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "tools_call",
                            SequenceNumber = sequenceNumber,
                            ToolCalls = [.. toolsCallMessage.ToolCalls],
                        },
                        UsageMessage usageMessage => new UsageEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "usage",
                            SequenceNumber = sequenceNumber,
                            Usage = usageMessage.Usage,
                        },
                        ToolsCallAggregateMessage toolsAggregateMessage =>
                            new ToolsCallAggregateEvent
                            {
                                ChatId = chatId,
                                MessageId = fullMessageId,
                                Kind = "tools_aggregate",
                                SequenceNumber = sequenceNumber,
                                ToolCalls = [.. toolsAggregateMessage.ToolsCallMessage.ToolCalls],
                                ToolResults =
                                    toolsAggregateMessage.ToolsCallResult?.ToolCallResults?.ToArray(),
                            },
                        _ => null,
                    };

                    if (evt != null)
                    {
                        // Log tool call completion details and map tool calls to message IDs
                        if (evt is ToolCallEvent toolCallEvt)
                        {
                            logger.LogInformation(
                                "Sending ToolCallEvent - ChatId: {ChatId}, MessageId: {MessageId}, ToolCount: {ToolCount}, Sequence: {Sequence}",
                                toolCallEvt.ChatId,
                                toolCallEvt.MessageId,
                                toolCallEvt.ToolCalls.Length,
                                toolCallEvt.SequenceNumber
                            );
                        }

                        await MessageReceived(evt);
                    }
                }

                loop = loop || message is ToolsCallAggregateMessage;
                hasTextMessage =
                    hasTextMessage
                    || (message is TextMessage tmpTm && !string.IsNullOrWhiteSpace(tmpTm.Text));
            }

            loop = loop || !hasTextMessage;
            _ = await storage.UpdateChatUpdatedAtAsync(chatId, DateTime.UtcNow, cancellationToken);

            lmMessages.Add(
                replies.Count == 1
                    ? replies[0]
                    : new CompositeMessage
                    {
                        Messages = [.. replies],
                        Role = Role.Assistant,
                    }
            );
        } while (loop);
    }

    /// <summary>
    /// IMPORTANT ARCHITECTURE NOTE:
    ///
    /// This streaming pipeline has two distinct message flows:
    ///
    /// 1. STREAMING CHUNKS (for real-time client display only):
    ///    - TextUpdateMessage, ReasoningUpdateMessage, ToolsCallUpdateMessage
    ///    - Sent to client via StreamChunkReceived events
    ///    - NOT PERSISTED to database
    ///    - Only used for progressive UI rendering
    ///
    /// 2. COMPLETE MESSAGES (for persistence):
    ///    - TextMessage, ReasoningMessage, ToolsCallMessage (final accumulated results)
    ///    - Come from MessageUpdateJoinerMiddleware after accumulating all chunks
    ///    - PERSISTED to database via PersistFullMessage()
    ///    - Each gets ONE sequence number per logical message
    ///
    /// SEQUENCE NUMBER CONFLICTS:
    /// If you see sequence conflicts, the issue is likely in CLIENT-SIDE streaming
    /// chunk management (SlimChatSyncManager), not server-side persistence.
    /// The server only persists final complete messages with proper sequences.
    /// </summary>
    private Func<
        IAsyncEnumerable<IMessage>,
        CancellationToken,
        IAsyncEnumerable<IMessage>
    > ProcessStream(string chatId, int userMsgSequence)
    {
        var messageIndex = userMsgSequence;
        var chunkSequenceId = 0;
        Type? lastType = null;

        async IAsyncEnumerable<IMessage> ProcessStreamInternal(
            IAsyncEnumerable<IMessage> stream,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            await foreach (var message in stream.WithCancellation(cancellationToken))
            {
                // Log every message type we receive from the stream
                logger.LogTrace(
                    "Received message from stream - Type: {MessageType}, ChatId: {ChatId}, MessageIndex: {MessageIndex}",
                    message.GetType().Name,
                    chatId,
                    messageIndex
                );

                if (message.GetType() != lastType)
                {
                    messageIndex++;
                    chunkSequenceId = 0;
                    // Clear last seen tool call ID when message type changes
                    _lastSeenToolCallId = null;
                }

                chunkSequenceId++;

                lastType = message.GetType();
                // Ensure we have a valid and unique generation ID for streaming messages too
                var generationId = EnsureUniqueGenerationId(message.GenerationId, chatId);
                var messageId = generationId + $"-{messageIndex:D3}";

                if (message is TextUpdateMessage textMessage)
                {
                    var content = textMessage.Text;
                    if (!string.IsNullOrEmpty(content))
                    {
                        logger.LogTrace(
                            "TextUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                            chatId,
                            messageId,
                            "text",
                            content
                        );

                        if (StreamChunkReceived != null)
                        {
                            await StreamChunkReceived(
                                new TextStreamEvent
                                {
                                    ChatId = chatId,
                                    MessageId = messageId,
                                    Kind = "text",
                                    Done = false,
                                    Delta = content,
                                    ChunkSequenceId = chunkSequenceId,
                                    SequenceNumber = messageIndex,
                                }
                            );
                        }
                    }
                }
                else if (message is ReasoningUpdateMessage reasoningUpdate)
                {
                    if (reasoningUpdate.Visibility == ReasoningVisibility.Encrypted)
                    {
                        continue;
                    }

                    var delta = reasoningUpdate.Reasoning;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        logger.LogTrace(
                            "ReasoningUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                            chatId,
                            messageId,
                            "reasoning",
                            delta
                        );

                        if (StreamChunkReceived != null)
                        {
                            await StreamChunkReceived(
                                new ReasoningStreamEvent
                                {
                                    ChatId = chatId,
                                    MessageId = messageId,
                                    Kind = "reasoning",
                                    Done = false,
                                    Delta = delta,
                                    ChunkSequenceId = chunkSequenceId,
                                    SequenceNumber = messageIndex,
                                    Visibility = reasoningUpdate.Visibility,
                                }
                            );
                        }
                    }
                }
                else if (message is ToolsCallUpdateMessage toolsCallUpdateMessage)
                {
                    // These are streaming chunks - just pass them through for real-time display
                    // The message joiner middleware will accumulate these into a complete ToolsCallMessage
                    var toolCallIndex = 0;
                    var toolCallCount = toolsCallUpdateMessage.ToolCallUpdates.Count;

                    logger.LogTrace(
                        "Processing ToolsCallUpdateMessage (streaming chunk) - ChatId: {ChatId}, MessageId: {MessageId}, ToolCallCount: {ToolCallCount}, GenerationId: {GenerationId}",
                        chatId,
                        messageId,
                        toolCallCount,
                        generationId
                    );

                    foreach (var toolCallUpdate in toolsCallUpdateMessage.ToolCallUpdates)
                    {
                        // Generate unique message ID for each tool call to avoid duplicate keys on client
                        var toolCallMessageId = $"{messageId}";
                        var toolCallSequence = messageIndex + toolCallIndex;

                        // Resolve tool_call_id for sequential streaming updates
                        string effectiveToolCallId;

                        if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                        {
                            // Update with tool_call_id - store as last seen
                            effectiveToolCallId = toolCallUpdate.ToolCallId;
                            _lastSeenToolCallId = effectiveToolCallId;
                            _ = _toolCallToMessageMap.TryAdd(
                                effectiveToolCallId,
                                (toolCallMessageId, toolCallSequence)
                            );

                            logger.LogInformation(
                                "Established ToolCallId {ToolCallId} for MessageId {MessageId} during streaming",
                                effectiveToolCallId,
                                toolCallMessageId
                            );
                        }
                        else
                        {
                            // Update without tool_call_id - use last seen (sequential continuation)
                            if (!string.IsNullOrEmpty(_lastSeenToolCallId))
                            {
                                effectiveToolCallId = _lastSeenToolCallId;
                                logger.LogTrace(
                                    "Reused last seen ToolCallId {ToolCallId} for MessageId {MessageId}",
                                    effectiveToolCallId,
                                    toolCallMessageId
                                );
                            }
                            else
                            {
                                // No last seen tool call ID - this indicates a system bug
                                logger.LogError(
                                    "Tool call update without ToolCallId and no last seen ID - MessageId: {MessageId}, Index: {Index}",
                                    toolCallMessageId,
                                    toolCallUpdate.Index ?? toolCallIndex
                                );
                                throw new InvalidOperationException(
                                    $"Tool call update without ToolCallId and no last seen ID for message {toolCallMessageId}"
                                );
                            }
                        }

                        // Create a corrected tool call update with the effective tool_call_id
                        var correctedToolCallUpdate = string.IsNullOrEmpty(
                            toolCallUpdate.ToolCallId
                        )
                            ? new ToolCallUpdate
                            {
                                ToolCallId = effectiveToolCallId,
                                Index = toolCallUpdate.Index,
                                FunctionName = toolCallUpdate.FunctionName,
                                FunctionArgs = toolCallUpdate.FunctionArgs,
                            }
                            : toolCallUpdate;

                        logger.LogTrace(
                            "Streaming tool call update - Index: {Index}, FunctionName: {FunctionName}, ArgsLength: {ArgsLength}, ToolCallId: {ToolCallId}",
                            correctedToolCallUpdate.Index ?? toolCallIndex,
                            correctedToolCallUpdate.FunctionName ?? "null",
                            correctedToolCallUpdate.FunctionArgs?.Length ?? 0,
                            correctedToolCallUpdate.ToolCallId ?? "null"
                        );

                        logger.LogInformation(
                            "ToolCall {ToolIndex}/{ToolCount} - ChatId: {ChatId}, MessageId: {MessageId}, Sequence: {Sequence}, ToolName: {ToolName}, ToolId: {ToolId}",
                            toolCallIndex + 1,
                            toolCallCount,
                            chatId,
                            toolCallMessageId,
                            toolCallSequence,
                            correctedToolCallUpdate.FunctionName ?? "unknown",
                            correctedToolCallUpdate.ToolCallId
                                ?? $"idx_{correctedToolCallUpdate.Index}"
                        );

                        if (StreamChunkReceived != null)
                        {
                            await StreamChunkReceived(
                                new ToolsCallUpdateStreamEvent
                                {
                                    ChatId = chatId,
                                    MessageId = toolCallMessageId,
                                    Kind = "tools_call_update",
                                    Done = false,
                                    ChunkSequenceId = chunkSequenceId,
                                    SequenceNumber = toolCallSequence, // Unique sequence for each tool
                                    ToolCallUpdate = correctedToolCallUpdate,
                                }
                            );
                        }
                        toolCallIndex++;
                    }

                    logger.LogTrace(
                        "Streamed {Count} tool call updates - ChatId: {ChatId}, GenerationId: {GenerationId}",
                        toolCallIndex,
                        chatId,
                        generationId
                    );
                }

                yield return message;
            }

            yield break;
        }

        return ProcessStreamInternal;
    }

    private async Task<int> PersistFullMessage(
        string chatId,
        IMessage message,
        string fullMessageId
    )
    {
        // For encrypted reasoning messages, persist but don't assign sequence number
        var isEncryptedReasoning =
            message is ReasoningMessage reasoningMsg
            && reasoningMsg.Visibility == ReasoningVisibility.Encrypted;

        int nextSequence;
        var (_, _, allocatedSequence) = await storage.AllocateSequenceAsync(chatId);
        nextSequence = allocatedSequence;

        var timestamp = DateTime.UtcNow;
        var messageRecord = message switch
        {
            ReasoningMessage reasoning => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = message.Role.ToString(),
                Kind = "reasoning",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new ReasoningMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = message.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        Reasoning = reasoning.Reasoning,
                        Visibility = reasoning.Visibility,
                        IsHidden = reasoning.Visibility == ReasoningVisibility.Encrypted,
                    },
                    MessageSerializationOptions.Default
                ),
            },
            TextMessage text => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = message.Role.ToString(),
                Kind = "text",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new TextMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = message.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        Text = text.Text,
                    },
                    MessageSerializationOptions.Default
                ),
            },
            UsageMessage usage => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = usage.Role.ToString(),
                Kind = "usage",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new UsageMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = usage.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        Usage = usage.Usage,
                        IsHidden = true,
                    },
                    MessageSerializationOptions.Default
                ),
            },
            ToolsCallMessage toolsCall => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = toolsCall.Role.ToString(),
                Kind = "tools_call",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new ToolCallMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = toolsCall.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        ToolCalls = [.. toolsCall.ToolCalls],
                    },
                    MessageSerializationOptions.Default
                ),
            },
            ToolsCallAggregateMessage aggregate => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = aggregate.Role.ToString(),
                Kind = "tools_aggregate",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new ToolsCallAggregateMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = aggregate.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        ToolCalls = [.. aggregate.ToolsCallMessage.ToolCalls],
                        ToolResults = aggregate.ToolsCallResult?.ToolCallResults?.ToArray(),
                    },
                    MessageSerializationOptions.Default
                ),
            },
            _ => null,
        };

        if (messageRecord != null)
        {
            logger.LogInformation(
                "Persisting message - ChatId: {ChatId}, MessageId: {MessageId}, Role: {Role}, Kind: {Kind}, Sequence: {Sequence}",
                chatId,
                fullMessageId,
                messageRecord.Role,
                messageRecord.Kind,
                nextSequence
            );
            _ = await storage.InsertMessageAsync(messageRecord);
            logger.LogInformation(
                "Message persisted successfully - ChatId: {ChatId}, MessageId: {MessageId}",
                chatId,
                fullMessageId
            );

            // Verify the message was actually persisted
            var (_, _, messages) = await storage.ListChatMessagesOrderedAsync(chatId);
            var persistedMsg = messages.FirstOrDefault(m => m.Id == fullMessageId);
            if (persistedMsg != null)
            {
                logger.LogInformation(
                    "Verified message persistence - MessageId: {MessageId}, Kind: {Kind}, Role: {Role}",
                    fullMessageId,
                    persistedMsg.Kind,
                    persistedMsg.Role
                );
            }
            else
            {
                logger.LogError(
                    "Failed to verify message persistence - MessageId: {MessageId} not found in database",
                    fullMessageId
                );
            }
        }
        else
        {
            logger.LogWarning(
                "Unable to persist message - ChatId: {ChatId}, MessageId: {MessageId}, MessageType: {MessageType}",
                chatId,
                fullMessageId,
                message.GetType().Name
            );
        }

        return nextSequence;
    }

    public async Task<MessageResult> AddUserMessageToExistingChatAsync(
        string chatId,
        string userId,
        string message
    )
    {
        try
        {
            var (seqSuccess, seqError, nextSequence) = await storage.AllocateSequenceAsync(chatId);
            if (!seqSuccess)
            {
                return new MessageResult { Success = false, Error = seqError };
            }

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = nextSequence,
                Text = message,
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    userDto,
                    MessageSerializationOptions.Default
                ),
            };
            var ins = await storage.InsertMessageAsync(userRecord);
            if (!ins.Success)
            {
                return new MessageResult { Success = false, Error = ins.Error };
            }

            if (MessageCreated != null)
            {
                await MessageCreated(
                    new MessageCreatedEvent { ChatId = chatId, Message = userDto }
                );
            }

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = null,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding user message to existing chat {ChatId}", chatId);
            return new MessageResult { Success = false, Error = "Failed to add user message" };
        }
    }

    public async Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        var (success, error, nextSequence) = await storage.AllocateSequenceAsync(chatId);
        return !success ? throw new InvalidOperationException(error) : nextSequence;
    }

    public async Task<string> CreateAssistantMessageForStreamingAsync(
        string chatId,
        int sequenceNumber
    )
    {
        var dto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = sequenceNumber,
            Text = "",
        };
        var record = new MessageRecord
        {
            Id = dto.Id,
            ChatId = chatId,
            Role = dto.Role,
            Kind = "text",
            TimestampUtc = dto.Timestamp,
            SequenceNumber = dto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(
                dto,
                MessageSerializationOptions.Default
            ),
        };
        var ins = await storage.InsertMessageAsync(record);
        return !ins.Success ? throw new InvalidOperationException(ins.Error) : dto.Id;
    }

    public async Task<string> GetMessageContentAsync(string messageId)
    {
        var (resSuccess, _, resContent) = await storage.GetMessageContentAsync(messageId);
        return resSuccess ? resContent ?? string.Empty : string.Empty;
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        try
        {
            if (!string.IsNullOrEmpty(request.ChatId))
            {
                var userMessageResult = await AddUserMessageToExistingChatAsync(
                    request.ChatId,
                    request.UserId,
                    request.Message
                );

                return !userMessageResult.Success
                    ? throw new InvalidOperationException(
                        userMessageResult.Error ?? "Failed to add user message"
                    )
                    : new StreamInitResult
                    {
                        ChatId = request.ChatId,
                        UserMessageId = userMessageResult.UserMessage!.Id,
                        UserTimestamp = userMessageResult.UserMessage.Timestamp,
                        UserSequenceNumber = userMessageResult.UserMessage.SequenceNumber,
                    };
            }
            else
            {
                return await PrepareStreamChatAsync(request);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error preparing unified stream chat");
            throw;
        }
    }

    public async Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            var assistantSeqNumber = await GetNextSequenceNumberAsync(request.ChatId) - 1;
            var (_, _, listMessages) = await storage.ListChatMessagesOrderedAsync(
                request.ChatId,
                cancellationToken
            );
            var lastAssistant = listMessages.FirstOrDefault(m =>
            {
                var dto = JsonSerializer.Deserialize<MessageDto>(
                    m.MessageJson,
                    MessageSerializationOptions.Default
                )!;
                return dto.Role == "assistant" && dto.SequenceNumber == assistantSeqNumber;
            });
            if (lastAssistant == null)
            {
                logger.LogError(
                    "Assistant message not found for streaming in chat {ChatId}",
                    request.ChatId
                );
                throw new InvalidOperationException("Assistant message not found for streaming");
            }

            await StreamAssistantResponseAsync(request.ChatId, cancellationToken);
        }
        else
        {
            await StreamChatCompletionAsync(request, cancellationToken);
        }
    }

    // Helper methods
    private async Task<string> GenerateAIResponseAsync(
        string chatId,
        string? modeId = null,
        string? userId = null
    )
    {
        try
        {
            var (_, _, listMessages) = await storage.ListChatMessagesOrderedAsync(chatId);
            var history = listMessages
                .Select(m =>
                    JsonSerializer.Deserialize<MessageDto>(
                        m.MessageJson,
                        MessageSerializationOptions.Default
                    )!
                )
                .Where(d =>
                    (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
                    || d is ReasoningMessageDto
                ) // Include ALL reasoning messages for LLM context
                .ToList();
            var lmMessages = history.Select(ConvertToLmMessage).ToList();

            // Get model ID from mode preference or fallback to default
            var modelId = await GetModelIdAsync(modeId, userId);
            var options = new GenerateReplyOptions { ModelId = modelId };
            var messages = await streamingAgent.GenerateReplyAsync(lmMessages, options);
            return string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating AI response");
            return $"Error: Failed to generate AI response. {ex.Message}";
        }
    }

    private async Task<string> GetModelIdAsync(string? modeId = null, string? userId = null)
    {
        // Try to get model preference from mode first
        if (!string.IsNullOrEmpty(modeId) && !string.IsNullOrEmpty(userId))
        {
            var (Success, Error, DefaultModel) = await modeService.GetModeDefaultModelAsync(
                modeId,
                userId
            );
            if (Success && !string.IsNullOrEmpty(DefaultModel))
            {
                logger.LogInformation(
                    "Using model {ModelId} from mode {ModeId}",
                    DefaultModel,
                    modeId
                );
                return DefaultModel;
            }
        }

        // Fallback to configuration or default
        var defaultModel = _aiOptions.ModelId ?? "openrouter/horizon-beta";
        logger.LogDebug("Using default model {ModelId}", defaultModel);
        return defaultModel;
    }

    private string GetModelId()
    {
        return _aiOptions.ModelId ?? "openrouter/horizon-beta";
    }

    private static IMessage ConvertToLmMessage(MessageDto message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "user" => Role.User,
            "assistant" => Role.Assistant,
            "system" => Role.System,
            _ => Role.User,
        };

        return message is TextMessageDto t
            ? new TextMessage
            {
                Role = role,
                Text = t.Text ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty,
            }
            : message is ReasoningMessageDto r
            ? new TextMessage
            {
                Role = role,
                Text = r.GetText() ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty,
            }
            : new TextMessage
            {
                Role = role,
                Text = string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty,
            };
    }

    private static string GenerateChatTitle(string firstMessage)
    {
        var title = firstMessage.Length > 50 ? firstMessage[..47] + "..." : firstMessage;
        return title;
    }

    #region IToolResultCallback Implementation

    public async Task OnToolResultAvailableAsync(
        string toolCallId,
        ToolCallResult result,
        CancellationToken cancellationToken = default
    )
    {
        // Stream result to client immediately if we're in a streaming context
        if (StreamChunkReceived != null && !string.IsNullOrEmpty(_currentChatId))
        {
            // Resolve the messageId and sequence number from our mapping
            string messageId;
            int sequenceNumber;
            if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData))
            {
                messageId = mappedData.MessageId;
                sequenceNumber = mappedData.SequenceNumber;
                logger.LogInformation(
                    "Resolved ToolCallId {ToolCallId} to MessageId {MessageId} with Sequence {Sequence} for result streaming",
                    toolCallId,
                    messageId,
                    sequenceNumber
                );
            }
            else
            {
                // Fallback to current message ID and next sequence if mapping not found
                messageId = _currentMessageId ?? $"unmapped-{toolCallId}";
                sequenceNumber = _nextSequence++;
                logger.LogWarning(
                    "Could not resolve ToolCallId {ToolCallId} to MessageId, using fallback: {MessageId} with Sequence {Sequence}",
                    toolCallId,
                    messageId,
                    sequenceNumber
                );
            }

            await StreamChunkReceived(
                new ToolResultStreamEvent
                {
                    ChatId = _currentChatId,
                    MessageId = messageId,
                    Kind = "tool_result",
                    Done = true,
                    SequenceNumber = sequenceNumber,
                    ChunkSequenceId = 0,
                    ToolCallId = toolCallId,
                    Result = result.Result,
                    IsError = result.Result.StartsWith("Error"),
                }
            );

            // Check if this was a TaskManager function and save/broadcast task state if so
            if (
                _toolCallToFunctionMap.TryGetValue(toolCallId, out var functionName)
                && IsTaskManagerFunction(functionName)
            )
            {
                logger.LogInformation(
                    "TaskManager function {FunctionName} completed, saving and broadcasting task state for chat {ChatId}",
                    functionName,
                    _currentChatId
                );

                // Save the TaskManager state after the operation
                await taskManagerService.SaveTaskManagerStateAsync(
                    _currentChatId,
                    cancellationToken
                );

                // Get the updated task state and broadcast it
                var taskState = await taskManagerService.GetTaskStateAsync(
                    _currentChatId,
                    cancellationToken
                );

                if (taskState.HasValue)
                {
                    await StreamChunkReceived(
                        new TaskUpdateStreamEvent
                        {
                            ChatId = _currentChatId,
                            MessageId = messageId,
                            Kind = "task_update",
                            Done = true,
                            SequenceNumber = _nextSequence++,
                            ChunkSequenceId = 0,
                            TaskState = taskState?.Item2 ?? Array.Empty<TaskItem>(),
                            OperationType = "sync",
                        }
                    );
                }
            }
        }
    }

    public async Task OnToolCallStartedAsync(
        string toolCallId,
        string functionName,
        string functionArgs,
        CancellationToken cancellationToken = default
    )
    {
        // Store the function name for later use in OnToolResultAvailableAsync
        _toolCallToFunctionMap[toolCallId] = functionName;

        // Log with message ID and sequence mapping if available
        if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData))
        {
            logger.LogInformation(
                "Tool call started - ToolCallId: {ToolCallId}, MessageId: {MessageId}, Sequence: {Sequence}, Function: {FunctionName}",
                toolCallId,
                mappedData.MessageId,
                mappedData.SequenceNumber,
                functionName
            );
        }
        else
        {
            logger.LogInformation(
                "Tool call started - ToolCallId: {ToolCallId}, Function: {FunctionName} (no message mapping yet)",
                toolCallId,
                functionName
            );
        }
        await Task.CompletedTask;
    }

    public async Task OnToolCallErrorAsync(
        string toolCallId,
        string functionName,
        string error,
        CancellationToken cancellationToken = default
    )
    {
        // Log with message ID and sequence mapping if available
        if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData))
        {
            logger.LogError(
                "Tool call error - ToolCallId: {ToolCallId}, MessageId: {MessageId}, Sequence: {Sequence}, Function: {FunctionName}, Error: {Error}",
                toolCallId,
                mappedData.MessageId,
                mappedData.SequenceNumber,
                functionName,
                error
            );
        }
        else
        {
            logger.LogError(
                "Tool call error - ToolCallId: {ToolCallId}, Function: {FunctionName}, Error: {Error} (no message mapping)",
                toolCallId,
                functionName,
                error
            );
        }

        // Stream error events to client if needed
        if (StreamChunkReceived != null && !string.IsNullOrEmpty(_currentChatId))
        {
            string resolvedMessageId;
            int resolvedSequence;
            if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData2))
            {
                resolvedMessageId = mappedData2.MessageId;
                resolvedSequence = mappedData2.SequenceNumber;
            }
            else
            {
                resolvedMessageId = _currentMessageId ?? $"unmapped-{toolCallId}";
                resolvedSequence = _nextSequence++;
            }

            await StreamChunkReceived(
                new ToolResultStreamEvent
                {
                    ChatId = _currentChatId,
                    MessageId = resolvedMessageId,
                    Kind = "tool_result",
                    Done = true,
                    SequenceNumber = resolvedSequence,
                    ChunkSequenceId = 0,
                    ToolCallId = toolCallId,
                    Result = $"Error: {error}",
                    IsError = true,
                }
            );
        }

        await Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Simple check to determine if a function name is a TaskManager function
    /// </summary>
    private static bool IsTaskManagerFunction(string functionName)
    {
        // Function names are likely prefixed (e.g., "TaskManager_add_task")
        // and use underscores instead of hyphens
        var taskManagerFunctions = new[]
        {
            "add-task",
            "bulk-initialize",
            "update-task",
            "delete-task",
            "manage-notes",
            "list-notes",
            "get-task",
            "list-tasks",
            "search-tasks",
        };

        return taskManagerFunctions.Any(func =>
            functionName.EndsWith(func, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// Ensures generation ID is unique by adding time-based salt if needed.
    /// This prevents primary key conflicts when using cached LLM responses.
    /// </summary>
    private static string EnsureUniqueGenerationId(string? providedGenerationId, string chatId)
    {
        // If no generation ID provided, create a new one with high precision timestamp
        if (string.IsNullOrEmpty(providedGenerationId))
        {
            var timestamp = DateTimeOffset.UtcNow;
            var microseconds = timestamp.Ticks / 10; // Convert ticks to microseconds
            return $"gen-{timestamp.ToUnixTimeSeconds()}-{microseconds % 1000000:D6}-{Guid.NewGuid():N}"[
..32
            ];
        }

        // If generation ID is provided (possibly from cache), add a unique suffix to ensure uniqueness
        // Check if it already has our timestamp pattern to avoid double-salting
        if (providedGenerationId.StartsWith("gen-") && providedGenerationId.Length >= 32)
        {
            // Already has our format, likely unique
            return providedGenerationId + $"-{chatId[..8]}";
        }

        // Add time-based salt to the provided ID to ensure uniqueness
        var saltedId = $"{providedGenerationId}-{DateTimeOffset.UtcNow.Ticks:X}";

        // Ensure it fits within reasonable ID length (32 chars)
        if (saltedId.Length > 32)
        {
            // Take first 16 chars of original and add time-based suffix
            var truncated = providedGenerationId[
..Math.Min(16, providedGenerationId.Length)
            ];
            return $"{truncated}-{DateTimeOffset.UtcNow.Ticks:X}"[..32];
        }

        return saltedId;
    }
}
