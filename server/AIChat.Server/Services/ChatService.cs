using System.Text.Json;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AIChat.Orleans.Client.Services;
using AIChat.Orleans.Contracts;
using AIChat.Server.Storage;

namespace AIChat.Server.Services;

public class ChatService(ILogger<ChatService> logger)
    : IChatServiceStreaming,
        IToolResultCallback
{
    // Note: Stateful fields removed for thread-safety
    // Tool execution state is now passed via StreamingContext parameter

    // Note: Events removed for stateless design
    // ChatServiceFacade will handle events for controller scenarios
    // Background processing uses callback parameters instead

    public async Task<ChatResult> CreateChatAsync(
        CreateChatRequest request,
        IChatStorage storage,
        IModeService modeService,
        IOrleansIntegrationService? orleansService = null
    )
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

            // Phase 3: AI response generation moved to Orleans - pure proxy doesn't generate responses
            // This method should route to Orleans ChatGrain for AI processing
            logger.LogInformation(
                "Chat {ChatId} created - AI response generation handled by Orleans ChatGrain",
                chat.Id
            );

            _ = await storage.UpdateChatUpdatedAtAsync(chat.Id, DateTime.UtcNow);

            // Build DTO with ordered messages
            var (Success, Error, Messages) = await storage.ListChatMessagesOrderedAsync(chat.Id);

            var messages =
                Success && Messages != null
                    ?
                    [
                        .. Messages.Select(m =>
                            JsonSerializer.Deserialize<MessageDto>(
                                m.MessageJson,
                                MessageSerializationOptions.Default
                            )!
                        ),
                    ]
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

    public async Task<ChatResult> GetChatAsync(
        string chatId,
        IChatStorage storage,
        ITaskManagerService taskManagerService
    )
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

    public async Task<ChatHistoryResult> GetChatHistoryAsync(
        string userId,
        int page,
        int pageSize,
        IChatStorage storage
    )
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

    public async Task<bool> DeleteChatAsync(
        string chatId,
        IChatStorage storage,
        ITaskManagerService taskManagerService
    )
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

    public async Task<MessageResult> SendMessageAsync(
        SendMessageRequest request,
        IChatStorage storage,
        IModeService modeService,
        IOrleansIntegrationService? orleansService = null
    )
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

            // Phase 1: Orleans shadow mode - record user message activity
            if (orleansService != null && !string.IsNullOrEmpty(request.UserId))
            {
                _ = orleansService.RecordUserActivityAsync(
                    request.UserId,
                    ActivityType.MessageSent,
                    new
                    {
                        request.ChatId,
                        MessageId = userDto.Id,
                        MessageLength = request.Message.Length,
                        userDto.SequenceNumber,
                        request.ModeId,
                    }
                );
            }

            // NOTE: Event handling moved to ChatServiceFacade for controller scenarios
            // Background processing scenarios will use callbacks instead

            // Phase 3: AI response generation moved to Orleans - pure proxy doesn't generate responses
            // This method should route to Orleans ChatGrain for AI processing
            logger.LogInformation(
                "User message added to chat {ChatId} - AI response generation handled by Orleans ChatGrain",
                request.ChatId
            );

            _ = await storage.UpdateChatUpdatedAtAsync(request.ChatId, DateTime.UtcNow);

            // Return only the user message - AI response is handled by streaming methods
            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = null,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending message for chat {ChatId}", request.ChatId);
            return new MessageResult { Success = false, Error = "Failed to send message" };
        }
    }

    public async Task<StreamInitResult> PrepareStreamChatAsync(
        StreamChatRequest request,
        IChatStorage storage,
        IModeService modeService
    )
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
            var (Success, _, SystemPrompt) = await modeService.GetModeSystemPromptAsync(
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
        IChatStorage storage,
        IModeService modeService,
        IStreamingAgent? streamingAgent,
        IToolingService? toolingService,
        IOrleansIntegrationService? orleansService = null,
        CancellationToken cancellationToken = default
    )
    {
        // Phase 3: Streaming chat completion moved to Orleans - pure proxy doesn't stream LLM responses
        // This method should route to Orleans ChatGrain for streaming operations
        await Task.CompletedTask;
        throw new NotSupportedException(
            "Phase 3: Direct service streaming deprecated. Use Orleans ChatGrain.StartStreamAsync() via router."
        );
    }

    /// <summary>
    /// <para>IMPORTANT ARCHITECTURE NOTE:</para>
    /// <para>This streaming pipeline has two distinct message flows:</para>
    /// <para>
    /// 1. STREAMING CHUNKS (for real-time client display only):
    ///    - TextUpdateMessage, ReasoningUpdateMessage, ToolsCallUpdateMessage
    ///    - Sent to client via StreamChunkReceived events
    ///    - NOT PERSISTED to database
    ///    - Only used for progressive UI rendering
    /// </para>
    /// <para>
    /// 2. COMPLETE MESSAGES (for persistence):
    ///    - TextMessage, ReasoningMessage, ToolsCallMessage (final accumulated results)
    ///    - Come from MessageUpdateJoinerMiddleware after accumulating all chunks
    ///    - PERSISTED to database via PersistFullMessage()
    ///    - Each gets ONE sequence number per logical message
    /// </para>
    /// <para>
    /// SEQUENCE NUMBER CONFLICTS:
    /// If you see sequence conflicts, the issue is likely in CLIENT-SIDE streaming
    /// chunk management (SlimChatSyncManager), not server-side persistence.
    /// The server only persists final complete messages with proper sequences.
    /// </para>
    /// </summary>
    public async Task<MessageResult> AddUserMessageToExistingChatAsync(
        string chatId,
        string userId,
        string message,
        IChatStorage storage
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

            // NOTE: Event handling moved to ChatServiceFacade for controller scenarios
            // Background processing scenarios will use callbacks instead

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

    public async Task<int> GetNextSequenceNumberAsync(string chatId, IChatStorage storage)
    {
        var (success, error, nextSequence) = await storage.AllocateSequenceAsync(chatId);
        return !success ? throw new InvalidOperationException(error) : nextSequence;
    }

    public async Task<string> CreateAssistantMessageForStreamingAsync(
        string chatId,
        int sequenceNumber,
        IChatStorage storage
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

    public async Task<string> GetMessageContentAsync(string messageId, IChatStorage storage)
    {
        var (resSuccess, _, resContent) = await storage.GetMessageContentAsync(messageId);
        return resSuccess ? resContent ?? string.Empty : string.Empty;
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(
        StreamChatRequest request,
        IChatStorage storage,
        IModeService modeService
    )
    {
        try
        {
            if (!string.IsNullOrEmpty(request.ChatId))
            {
                var userMessageResult = await AddUserMessageToExistingChatAsync(
                    request.ChatId,
                    request.UserId,
                    request.Message,
                    storage
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

            return await PrepareStreamChatAsync(request, storage, modeService);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error preparing unified stream chat");
            throw;
        }
    }

    public async Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        IChatStorage storage,
        IModeService modeService,
        IStreamingAgent? streamingAgent,
        IToolingService? toolingService,
        IOrleansIntegrationService? orleansService = null,
        CancellationToken cancellationToken = default
    )
    {
        // Phase 3: Unified streaming moved to Orleans - pure proxy doesn't stream LLM responses
        // This method should route to Orleans ChatGrain for streaming operations
        await Task.CompletedTask;
        throw new NotSupportedException(
            "Phase 3: Direct service streaming deprecated. Use Orleans ChatGrain.StartStreamAsync() via router."
        );
    }


    /// <summary>
    /// Helper methods
    /// </summary>
    private static string GenerateChatTitle(string firstMessage)
    {
        return firstMessage.Length > 50 ? firstMessage[..47] + "..." : firstMessage;
    }

    #region IToolResultCallback Implementation - TODO: Refactor for stateless design

    public async Task OnToolResultAvailableAsync(
        string toolCallId,
        ToolCallResult result,
        CancellationToken cancellationToken = default
    )
    {
        // TODO: Implement stateless version with callback parameters
        // The current implementation uses instance fields that are removed
        logger.LogInformation(
            "Tool result available for {ToolCallId} - stateless implementation needed",
            toolCallId
        );
        await Task.CompletedTask;
    }

    public async Task OnToolCallStartedAsync(
        string toolCallId,
        string functionName,
        string functionArgs,
        CancellationToken cancellationToken = default
    )
    {
        // TODO: Implement stateless version with callback parameters
        logger.LogInformation(
            "Tool call started: {ToolCallId}, Function: {FunctionName}",
            toolCallId,
            functionName
        );
        await Task.CompletedTask;
    }

    public async Task OnToolCallErrorAsync(
        string toolCallId,
        string functionName,
        string error,
        CancellationToken cancellationToken = default
    )
    {
        // TODO: Implement stateless version with callback parameters
        logger.LogError(
            "Tool call error: {ToolCallId}, Function: {FunctionName}, Error: {Error}",
            toolCallId,
            functionName,
            error
        );
        await Task.CompletedTask;
    }

    #endregion IToolResultCallback Implementation - TODO: Refactor for stateless design

}
