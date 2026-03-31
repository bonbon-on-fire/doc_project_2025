using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using AIChat.Server.Storage;
using System.Collections.Concurrent;
using System.Text.Json;
using AIChat.Server.Models;
using AchieveAi.LmDotnetTools.LmCore.Middleware;
using AIChat.Server.Functions;
using AchieveAi.LmDotnetTools.Misc.Utils;
using static AchieveAi.LmDotnetTools.Misc.Utils.TaskManager;
using AchieveAi.LmDotnetTools.McpMiddleware.Extensions;
using AchieveAi.LmDotnetTools.McpMiddleware;

namespace AIChat.Server.Services;

public class ChatService : IChatService, IToolResultCallback
{
    private readonly IChatStorage _storage;
    private readonly IStreamingAgent _streamingAgent;
    private readonly ILogger<ChatService> _logger;
    private readonly AiOptions _aiOptions;
    private readonly ITaskManagerService _taskManagerService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMcpClientManager _mcpClientManager;

    public ChatService(
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        ILogger<ChatService> logger,
        IOptions<AiOptions> aiOptions,
        ITaskManagerService taskManagerService,
        IServiceProvider serviceProvider,
        IMcpClientManager mcpClientManager)
    {
        _storage = storage;
        _streamingAgent = streamingAgent;
        _logger = logger;
        _aiOptions = aiOptions.Value;
        _taskManagerService = taskManagerService;
        _serviceProvider = serviceProvider;
        _mcpClientManager = mcpClientManager;
    }
    
    private async Task<FunctionCallMiddleware?> CreateChatSpecificFunctionCallMiddleware(string chatId)
    {
        try
        {
            _logger.LogInformation("Creating chat-specific FunctionCallMiddleware for chat {ChatId}", chatId);
            
            // Create function registry
            var registry = new FunctionRegistry();
            
            // Add weather function provider
            var weatherLogger = _serviceProvider.GetRequiredService<ILogger<WeatherFunction>>();
            var weatherProvider = new WeatherFunction(weatherLogger);
            registry.AddProvider(weatherProvider);
            _logger.LogInformation("Added WeatherFunction provider to registry");
            
            // Get or create TaskManager for this specific chat
            var taskManager = await _taskManagerService.GetTaskManagerAsync(chatId);
            registry.AddFunctionsFromObject(taskManager, "TaskManager");
            _logger.LogInformation("Added TaskManager functions for chat {ChatId}", chatId);
            
            // Add MCP clients to the registry
            try
            {
                var mcpClients = await _mcpClientManager.GetActiveClientsAsync();
                if (mcpClients.Any())
                {
                    var mcpLogger = _serviceProvider.GetService<ILogger<McpClientFunctionProvider>>();
                    await registry.AddMcpClientsAsync(mcpClients, "McpServers", mcpLogger);
                    _logger.LogInformation("Added {Count} MCP clients to function registry", mcpClients.Count);
                }
                else
                {
                    _logger.LogInformation("No MCP clients configured or available");
                }
            }
            catch (Exception mcpEx)
            {
                _logger.LogError(mcpEx, "Failed to add MCP clients to function registry");
            }
            
            // Build function contracts and handlers with conflict resolution
            registry.WithConflictResolution(ConflictResolution.PreferMcp);
            var (contracts, handlers) = registry.Build();
            
            if (!contracts.Any())
            {
                _logger.LogWarning("No functions registered for FunctionCallMiddleware");
                return null;
            }
            
            _logger.LogInformation("Registered {Count} functions for tool calling in chat {ChatId}", contracts.Count(), chatId);
            foreach (var contract in contracts)
            {
                _logger.LogInformation("Registered function: {Name} - {Description}", contract.Name, contract.Description);
            }
            
            // Create middleware with callback
            var middlewareLogger = _serviceProvider.GetRequiredService<ILogger<FunctionCallMiddleware>>();
            var middleware = new FunctionCallMiddleware(
                contracts,
                handlers,
                name: "FunctionCall",
                logger: middlewareLogger,
                resultCallback: this); // Use this ChatService as the IToolResultCallback
            
            _logger.LogInformation("FunctionCallMiddleware created successfully for chat {ChatId}", chatId);    
            return middleware;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FunctionCallMiddleware for chat {ChatId}", chatId);
            return null;
        }
    }

    // Fields for tracking tool execution state
    private string? _currentChatId;
    private string? _currentMessageId;
    private int _nextSequence;
    // Map ToolCallId to MessageId and SequenceNumber for proper correlation
    // Using ConcurrentDictionary for thread-safe access during async streaming operations
    private readonly ConcurrentDictionary<string, (string MessageId, int SequenceNumber)> _toolCallToMessageMap = new();
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
            var create = await _storage.CreateChatAsync(request.UserId, title, now, now, null);
            if (!create.Success || create.Chat == null)
            {
                return new ChatResult { Success = false, Error = create.Error ?? "Failed to create chat" };
            }

            var chat = create.Chat;

            // Insert initial user message
            var (allocSeqSuccess, allocSeqError, allocSeqNextSequence) = await _storage.AllocateSequenceAsync(chat.Id);
            if (!allocSeqSuccess) return new ChatResult { Success = false, Error = allocSeqError };
            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = allocSeqNextSequence,
                Text = request.Message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chat.Id,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto, MessageSerializationOptions.Default)
            };

            var insUser = await _storage.InsertMessageAsync(userRecord);
            if (!insUser.Success) return new ChatResult { Success = false, Error = insUser.Error };

            // Optional system prompt
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                (allocSeqSuccess, allocSeqError, allocSeqNextSequence) = await _storage.AllocateSequenceAsync(chat.Id);
                if (!allocSeqSuccess) return new ChatResult { Success = false, Error = allocSeqError };
                var sysDto = new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "system",
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                    SequenceNumber = allocSeqNextSequence,
                    Text = request.SystemPrompt!
                };
                var sysRecord = new MessageRecord
                {
                    Id = sysDto.Id,
                    ChatId = chat.Id,
                    Role = sysDto.Role,
                    Kind = "text",
                    TimestampUtc = sysDto.Timestamp,
                    SequenceNumber = sysDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(sysDto, MessageSerializationOptions.Default)
                };
                var insSys = await _storage.InsertMessageAsync(sysRecord);
                if (!insSys.Success) return new ChatResult { Success = false, Error = insSys.Error };
            }

            // Generate AI response
            var aiResponse = await GenerateAIResponseAsync(chat.Id);

            await _storage.UpdateChatUpdatedAtAsync(chat.Id, DateTime.UtcNow);

            // Build DTO with ordered messages
            var (Success, Error, Messages) = await _storage.ListChatMessagesOrderedAsync(chat.Id);
            var messages = Success
                ? Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!).ToList()
                : [ userDto ];

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Id,
                    UserId = chat.UserId,
                    Title = chat.Title,
                    CreatedAt = chat.CreatedAtUtc,
                    UpdatedAt = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chat");
            return new ChatResult { Success = false, Error = "Failed to create chat" };
        }
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        try
        {
            var chat = await _storage.GetChatByIdAsync(chatId);
            if (!chat.Success || chat.Chat == null)
            {
                return new ChatResult { Success = false, Error = chat.Error ?? "Chat not found" };
            }
            var (Success, Error, Messages) = await _storage.ListChatMessagesOrderedAsync(chatId);
            
            _logger.LogInformation("Loading messages for chat {ChatId}: Found {Count} messages", chatId, Messages?.Count ?? 0);
            
            var messages = Success
                ? Messages!.Select(m => {
                    var dto = JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!;
                    
                    // Log tool call messages for debugging
                    if (dto is ToolCallMessageDto toolCallDto)
                    {
                        _logger.LogInformation("Loaded tool call message: Id={MessageId}, ToolCalls={ToolCallCount}", 
                            dto.Id, toolCallDto.ToolCalls?.Length ?? 0);
                    }
                    
                    return dto;
                })
                .ToList()
                : [];

            // Get task snapshot from TaskManagerService
            var taskSnapshot = await _taskManagerService.GetTaskStateAsync(chatId, CancellationToken.None);
            
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
                    Tasks = taskSnapshot?.Item2
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
            return new ChatResult { Success = false, Error = "Failed to retrieve chat" };
        }
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
    {
        try
        {
            var (histSuccess, histError, histChats, histTotalCount) = await _storage.GetChatHistoryByUserAsync(userId, page, pageSize);
            var chats = histSuccess ? histChats : Array.Empty<ChatRecord>();

            var chatDtos = new List<ChatDto>(chats.Count);
            foreach (var c in chats)
            {
                var (msgsSuccess, msgsError, msgsMessages) = await _storage.ListChatMessagesOrderedAsync(c.Id);
                var messages = msgsSuccess
                    ? [.. msgsMessages
                        .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!)]
                    : new List<MessageDto>();

                chatDtos.Add(new ChatDto
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    Title = c.Title,
                    Messages = messages,
                    CreatedAt = c.CreatedAtUtc,
                    UpdatedAt = c.UpdatedAtUtc
                });
            }

            return new ChatHistoryResult
            {
                Success = true,
                Chats = chatDtos,
                TotalCount = histTotalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat history for user {UserId}", userId);
            return new ChatHistoryResult { Success = false, Error = "Failed to retrieve chat history" };
        }
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        try
        {
            // Clear TaskManager for this chat
            await _taskManagerService.ClearTaskManagerAsync(chatId);
            
            // Delete the chat
            var res = await _storage.DeleteChatAsync(chatId);
            return res.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
            return false;
        }
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        try
        {
            var userSeq = await _storage.AllocateSequenceAsync(request.ChatId);
            if (!userSeq.Success) return new MessageResult { Success = false, Error = userSeq.Error };

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = userSeq.NextSequence,
                Text = request.Message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = request.ChatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto, MessageSerializationOptions.Default)
            };
            var insUser = await _storage.InsertMessageAsync(userRecord);
            if (!insUser.Success) return new MessageResult { Success = false, Error = insUser.Error };

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent {
                ChatId = request.ChatId,
                Message = userDto
            });

            var aiResponse = await GenerateAIResponseAsync(request.ChatId);

            var asq = await _storage.AllocateSequenceAsync(request.ChatId);
            if (!asq.Success) return new MessageResult { Success = false, Error = asq.Error };

            var assistantDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = asq.NextSequence,
                Text = aiResponse
            };
            var assistantRecord = new MessageRecord
            {
                Id = assistantDto.Id,
                ChatId = request.ChatId,
                Role = assistantDto.Role,
                Kind = "text",
                TimestampUtc = assistantDto.Timestamp,
                SequenceNumber = assistantDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(assistantDto, MessageSerializationOptions.Default)
            };
            var insAsst = await _storage.InsertMessageAsync(assistantRecord);
            if (!insAsst.Success) return new MessageResult { Success = false, Error = insAsst.Error };

            await _storage.UpdateChatUpdatedAtAsync(request.ChatId, DateTime.UtcNow);

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent
            {
                ChatId = request.ChatId,
                Message = assistantDto
            });

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = assistantDto
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message for chat {ChatId}", request.ChatId);
            return new MessageResult { Success = false, Error = "Failed to send message" };
        }
    }

    public async Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request)
    {
        _logger.LogInformation("[DEBUG] PrepareStreamChatAsync - UserId: {UserId}, Message: {Message}", request.UserId, request.Message);
        var now = DateTime.UtcNow;
        var createRes = await _storage.CreateChatAsync(
            request.UserId,
            GenerateChatTitle(request.Message),
            now,
            now,
            null);

        if (!createRes.Success || createRes.Chat == null)
        {
            throw new InvalidOperationException(createRes.Error ?? "Create chat failed");
        }

        var chatId = createRes.Chat.Id;

        var (seqSuccess, seqError, userMsgSequence) = await _storage.AllocateSequenceAsync(chatId);
        if (!seqSuccess) throw new InvalidOperationException(seqError);

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            var sysDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "system",
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                SequenceNumber = userMsgSequence++,
                Text = request.SystemPrompt!
            };

            await _storage.InsertMessageAsync(new MessageRecord
            {
                Id = sysDto.Id,
                ChatId = chatId,
                Role = sysDto.Role,
                Kind = "text",
                TimestampUtc = sysDto.Timestamp,
                SequenceNumber = sysDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(sysDto, MessageSerializationOptions.Default)
            });
        }

        var userDto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = userMsgSequence,
            Text = request.Message
        };

        await _storage.InsertMessageAsync(new MessageRecord
        {
            Id = userDto.Id,
            ChatId = chatId,
            Role = userDto.Role,
            Kind = "text",
            TimestampUtc = userDto.Timestamp,
            SequenceNumber = userDto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(userDto, MessageSerializationOptions.Default)
        });

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
        CancellationToken cancellationToken = default)
    {
        var init = await PrepareStreamChatAsync(request);
        var chatId = init.ChatId;

        var convo = await _storage.ListChatMessagesOrderedAsync(chatId);
        var history = convo.Messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!)
            .Where(d => (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text)) || 
                       d is ReasoningMessageDto) // Include ALL reasoning messages for LLM context
            .ToList();

        await StreamChatCompletionAsync(chatId, history, cancellationToken);
    }

    public async Task StreamAssistantResponseAsync(
        string chatId,
        CancellationToken cancellationToken = default)
    {
        var (_, _, messages) = await _storage.ListChatMessagesOrderedAsync(chatId, cancellationToken);
        var history = messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!)
            .Where(d => (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text)) || 
                       d is ReasoningMessageDto) // Include ALL reasoning messages for LLM context
            .ToList();

        await StreamChatCompletionAsync(chatId, history, cancellationToken);
    }

    private async Task StreamChatCompletionAsync(
        string chatId,
        List<MessageDto> history,
        CancellationToken cancellationToken)
    {
        // Set context for tool result callbacks
        _currentChatId = chatId;
        _nextSequence = history.Count > 0 ? history.Max(m => m.SequenceNumber) + 1 : 0;
        _toolCallToMessageMap.Clear(); // Clear mapping for new stream
        _toolCallToFunctionMap.Clear(); // Clear function mapping for new stream
        _lastSeenToolCallId = null; // Clear last seen tool call ID for new stream
        
        _logger.LogInformation("[DEBUG] StreamChatCompletionAsync - ChatId: {ChatId}, History count: {Count}", chatId, history.Count);
        foreach (var msg in history)
        {
            _logger.LogInformation("[DEBUG] History message - Role: {Role}, Type: {Type}, Content: {Content}", 
                msg.Role, msg.GetType().Name, 
                msg is TextMessageDto textMsg ? textMsg.Text?.Substring(0, Math.Min(100, textMsg.Text.Length)) + "..." : "N/A");
        }
        
        var lmMessages = history.Select(ConvertToLmMessage).ToList();
        var userMsgSequence = history.Count > 0 ? history.Max(m => m.SequenceNumber) : 0;

        _logger.LogInformation("[DEBUG] Converted to {Count} LM messages", lmMessages.Count);
        foreach (var lmMsg in lmMessages)
        {
            _logger.LogInformation("[DEBUG] LM message - Role: {Role}, Type: {Type}", lmMsg.Role, lmMsg.GetType().Name);
        }
        
        _logger.LogInformation("Making API call to LLM for chat {ChatId}", chatId);
        var options = new GenerateReplyOptions {
            ModelId = GetModelId(),
            ExtraProperties = new Dictionary<string, object?>()
            {
                ["reasoning"] = "low"
                // ["frequency_penalty"] = 1.0f,
                // ["parallel_tool_calls"] = true,
                // ["provider"] = new
                // {
                //     order = new[] { "chutes" }
                // }
            }.ToImmutableDictionary()
        };
        var streamProcessor = ProcessStream(chatId, userMsgSequence);

        // Build middleware chain
        var agent = _streamingAgent
            .WithMiddleware(new JsonFragmentUpdateMiddleware())
            .WithMiddleware(
                (context, agent, cancellationToken) =>
                {
                    return agent.GenerateReplyAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken);
                },
                async (context, agent, cancellationToken) =>
                {
                    var stream = await agent.GenerateReplyStreamingAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken);

                    return streamProcessor(stream, cancellationToken);
                }
            );
            
        // Create and add chat-specific FunctionCallMiddleware
        var functionCallMiddleware = await CreateChatSpecificFunctionCallMiddleware(chatId);
        if (functionCallMiddleware != null)
        {
            _logger.LogInformation("Adding chat-specific FunctionCallMiddleware to processing chain for chat {ChatId}", chatId);
            agent = agent.WithMiddleware(functionCallMiddleware);
        }
        else
        {
            _logger.LogWarning("FunctionCallMiddleware is null for chat {ChatId}, tool calls will not be processed", chatId);
        }

        bool loop = false;
        bool hasTextMessage = false;
        int fullMessageIndex = userMsgSequence;
        do
        {
            loop = false;
            var streamingResponse = await agent
                .WithMiddleware(new MessageUpdateJoinerMiddleware())
                .GenerateReplyStreamingAsync(
                    lmMessages,
                    options,
                    cancellationToken: cancellationToken);

            _logger.LogInformation("Received response from LLM for chat {ChatId}", chatId);

            Type? lastFullMessageType = null;
            var replies = new List<IMessage>();
            await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
            {
                replies.Add(message);
                _logger.LogInformation(
                    "[MIDDLEWARE] Message from middleware stream - Type: {MessageType}, ChatId: {ChatId}",
                    message.GetType().Name,
                    chatId);

                fullMessageIndex++;

                lastFullMessageType = message.GetType();
                // Ensure we have a valid and unique generation ID (add time salt for cache scenarios)
                var generationId = EnsureUniqueGenerationId(message.GenerationId, chatId);
                var fullMessageId = generationId + $"-{fullMessageIndex:D3}";
                _currentMessageId = fullMessageId; // Track for tool result callbacks

                _logger.LogInformation(
                    "Persisting message from middleware - Type: {MessageType}, MessageId: {MessageId}, ChatId: {ChatId}",
                    message.GetType().Name,
                    fullMessageId,
                    chatId);

                var sequenceNumber = await PersistFullMessage(chatId, message, fullMessageId);

                // Skip sending encrypted reasoning messages to client (they have sequence -1)
                if (sequenceNumber == -1)
                {
                    _logger.LogInformation(
                        "Encrypted reasoning persisted but not sent to client - ChatId: {ChatId}, MessageId: {MessageId}",
                        chatId, fullMessageId);
                    continue; // Skip to next message without sending to client
                }

                if (sequenceNumber != fullMessageIndex)
                {
                    _logger.LogError(
                        "Sequence number mismatch: {Expected}, {Actual}",
                        fullMessageIndex,
                        sequenceNumber);
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
                            Text = textMessage.Text
                        },
                        ReasoningMessage reasoningMessage => new ReasoningEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "reasoning",
                            SequenceNumber = sequenceNumber,
                            Reasoning = reasoningMessage.Reasoning,
                            Visibility = reasoningMessage.Visibility
                        },
                        ToolsCallMessage toolsCallMessage => new ToolCallEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "tools_call",
                            SequenceNumber = sequenceNumber,
                            ToolCalls = [.. toolsCallMessage.ToolCalls]
                        },
                        UsageMessage usageMessage => new UsageEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "usage",
                            SequenceNumber = sequenceNumber,
                            Usage = usageMessage.Usage
                        },
                        ToolsCallAggregateMessage toolsAggregateMessage => new ToolsCallAggregateEvent
                        {
                            ChatId = chatId,
                            MessageId = fullMessageId,
                            Kind = "tools_aggregate",
                            SequenceNumber = sequenceNumber,
                            ToolCalls = [.. toolsAggregateMessage.ToolsCallMessage.ToolCalls],
                            ToolResults = toolsAggregateMessage.ToolsCallResult?.ToolCallResults?.ToArray()
                        },
                        _ => null
                    };

                    if (evt != null)
                    {
                        // Log tool call completion details and map tool calls to message IDs
                        if (evt is ToolCallEvent toolCallEvt)
                        {
                            _logger.LogInformation(
                                "Sending ToolCallEvent - ChatId: {ChatId}, MessageId: {MessageId}, ToolCount: {ToolCount}, Sequence: {Sequence}",
                                toolCallEvt.ChatId,
                                toolCallEvt.MessageId,
                                toolCallEvt.ToolCalls.Length,
                                toolCallEvt.SequenceNumber);
                        }

                        await MessageReceived(evt);
                    }
                }

                loop = loop ||
                    message is ToolsCallAggregateMessage;
                hasTextMessage = hasTextMessage ||
                    (message is TextMessage tmpTm && !string.IsNullOrWhiteSpace(tmpTm.Text));
            }

            loop = loop || !hasTextMessage;
            await _storage.UpdateChatUpdatedAtAsync(chatId, DateTime.UtcNow, cancellationToken);

            lmMessages.Add(
                replies.Count == 1
                ? replies[0]
                : new CompositeMessage
                {
                    Messages = replies.ToImmutableList(),
                    Role = Role.Assistant,
                });
        } while (loop);
    }

    private Func<IAsyncEnumerable<IMessage>, CancellationToken, IAsyncEnumerable<IMessage>> ProcessStream(
        string chatId,
        int userMsgSequence)
    {
        int messageIndex = userMsgSequence;
        int chunkSequenceId = 0;
        Type? lastType = null;

        async IAsyncEnumerable<IMessage> ProcessStreamInternal(IAsyncEnumerable<IMessage> stream, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var message in stream.WithCancellation(cancellationToken))
            {
                // Log every message type we receive from the stream
                _logger.LogTrace(
                    "Received message from stream - Type: {MessageType}, ChatId: {ChatId}, MessageIndex: {MessageIndex}",
                    message.GetType().Name,
                    chatId,
                    messageIndex);

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
                        _logger.LogTrace(
                            "TextUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                            chatId,
                            messageId,
                            "text",
                            content);

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
                                });
                        }
                    }
                }
                else if (message is ReasoningUpdateMessage reasoningUpdate)
                {
                    if (reasoningUpdate.Visibility == ReasoningVisibility.Encrypted) continue;
                    var delta = reasoningUpdate.Reasoning;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        _logger.LogTrace(
                            "ReasoningUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                            chatId,
                            messageId,
                            "reasoning",
                            delta);

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
                                    Visibility = reasoningUpdate.Visibility
                                });
                        }
                    }
                }
                else if (message is ToolsCallUpdateMessage toolsCallUpdateMessage)
                {
                    // These are streaming chunks - just pass them through for real-time display
                    // The message joiner middleware will accumulate these into a complete ToolsCallMessage
                    int toolCallIndex = 0;
                    var toolCallCount = toolsCallUpdateMessage.ToolCallUpdates.Count();

                    _logger.LogTrace(
                        "Processing ToolsCallUpdateMessage (streaming chunk) - ChatId: {ChatId}, MessageId: {MessageId}, ToolCallCount: {ToolCallCount}, GenerationId: {GenerationId}",
                        chatId,
                        messageId,
                        toolCallCount,
                        generationId);

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
                            _toolCallToMessageMap.TryAdd(effectiveToolCallId, (toolCallMessageId, toolCallSequence));
                            
                            _logger.LogInformation(
                                "Established ToolCallId {ToolCallId} for MessageId {MessageId} during streaming",
                                effectiveToolCallId,
                                toolCallMessageId);
                        }
                        else
                        {
                            // Update without tool_call_id - use last seen (sequential continuation)
                            if (!string.IsNullOrEmpty(_lastSeenToolCallId))
                            {
                                effectiveToolCallId = _lastSeenToolCallId;
                                _logger.LogTrace(
                                    "Reused last seen ToolCallId {ToolCallId} for MessageId {MessageId}",
                                    effectiveToolCallId,
                                    toolCallMessageId);
                            }
                            else
                            {
                                // No last seen tool call ID - this indicates a system bug
                                _logger.LogError(
                                    "Tool call update without ToolCallId and no last seen ID - MessageId: {MessageId}, Index: {Index}",
                                    toolCallMessageId,
                                    toolCallUpdate.Index ?? toolCallIndex);
                                throw new InvalidOperationException($"Tool call update without ToolCallId and no last seen ID for message {toolCallMessageId}");
                            }
                        }

                        // Create a corrected tool call update with the effective tool_call_id
                        var correctedToolCallUpdate = string.IsNullOrEmpty(toolCallUpdate.ToolCallId)
                            ? new ToolCallUpdate
                            {
                                ToolCallId = effectiveToolCallId,
                                Index = toolCallUpdate.Index,
                                FunctionName = toolCallUpdate.FunctionName,
                                FunctionArgs = toolCallUpdate.FunctionArgs
                            }
                            : toolCallUpdate;

                        _logger.LogTrace(
                            "Streaming tool call update - Index: {Index}, FunctionName: {FunctionName}, ArgsLength: {ArgsLength}, ToolCallId: {ToolCallId}",
                            correctedToolCallUpdate.Index ?? toolCallIndex,
                            correctedToolCallUpdate.FunctionName ?? "null",
                            correctedToolCallUpdate.FunctionArgs?.Length ?? 0,
                            correctedToolCallUpdate.ToolCallId ?? "null");

                        _logger.LogInformation(
                            "ToolCall {ToolIndex}/{ToolCount} - ChatId: {ChatId}, MessageId: {MessageId}, Sequence: {Sequence}, ToolName: {ToolName}, ToolId: {ToolId}",
                            toolCallIndex + 1,
                            toolCallCount,
                            chatId,
                            toolCallMessageId,
                            toolCallSequence,
                            correctedToolCallUpdate.FunctionName ?? "unknown",
                            correctedToolCallUpdate.ToolCallId ?? $"idx_{correctedToolCallUpdate.Index}");

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
                                    ToolCallUpdate = correctedToolCallUpdate
                                });
                        }
                        toolCallIndex++;
                    }

                    _logger.LogTrace(
                        "Streamed {Count} tool call updates - ChatId: {ChatId}, GenerationId: {GenerationId}",
                        toolCallIndex,
                        chatId,
                        generationId);
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
        string fullMessageId)
    {
        // For encrypted reasoning messages, persist but don't assign sequence number
        bool isEncryptedReasoning = message is ReasoningMessage reasoningMsg && reasoningMsg.Visibility == ReasoningVisibility.Encrypted;
        
        int nextSequence;
        var (_, _, allocatedSequence) = await _storage.AllocateSequenceAsync(chatId);
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
                        IsHidden = reasoning.Visibility == ReasoningVisibility.Encrypted
                    }, MessageSerializationOptions.Default)
            },
            TextMessage text => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = message.Role.ToString(),
                Kind = "text",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(new TextMessageDto
                {
                    Id = fullMessageId,
                    ChatId = chatId,
                    Role = message.Role.ToString(),
                    Timestamp = timestamp,
                    SequenceNumber = nextSequence,
                    Text = text.Text
                }, MessageSerializationOptions.Default)
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
                        IsHidden = true
                    }, MessageSerializationOptions.Default)
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
                        ToolCalls = [.. toolsCall.ToolCalls]
                    }, MessageSerializationOptions.Default)
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
                        ToolCalls = aggregate.ToolsCallMessage.ToolCalls.ToArray(),
                        ToolResults = aggregate.ToolsCallResult?.ToolCallResults?.ToArray()
                    }, MessageSerializationOptions.Default)
            },
            _ => null
        };

        if (messageRecord != null)
        {
            _logger.LogInformation(
                "Persisting message - ChatId: {ChatId}, MessageId: {MessageId}, Role: {Role}, Kind: {Kind}, Sequence: {Sequence}",
                chatId, fullMessageId, messageRecord.Role, messageRecord.Kind, nextSequence);
            await _storage.InsertMessageAsync(messageRecord);
            _logger.LogInformation(
                "Message persisted successfully - ChatId: {ChatId}, MessageId: {MessageId}",
                chatId, fullMessageId);
            
            // Verify the message was actually persisted
            var (_, _, messages) = await _storage.ListChatMessagesOrderedAsync(chatId);
            var persistedMsg = messages.FirstOrDefault(m => m.Id == fullMessageId);
            if (persistedMsg != null)
            {
                _logger.LogInformation(
                    "Verified message persistence - MessageId: {MessageId}, Kind: {Kind}, Role: {Role}",
                    fullMessageId, persistedMsg.Kind, persistedMsg.Role);
            }
            else
            {
                _logger.LogError(
                    "Failed to verify message persistence - MessageId: {MessageId} not found in database",
                    fullMessageId);
            }
        }
        else
        {
            _logger.LogWarning(
                "Unable to persist message - ChatId: {ChatId}, MessageId: {MessageId}, MessageType: {MessageType}",
                chatId, fullMessageId, message.GetType().Name);
        }

        return nextSequence;
    }

    public async Task<MessageResult> AddUserMessageToExistingChatAsync(string chatId, string userId, string message)
    {
        try
        {
            var (seqSuccess, seqError, nextSequence) = await _storage.AllocateSequenceAsync(chatId);
            if (!seqSuccess) return new MessageResult { Success = false, Error = seqError };

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = nextSequence,
                Text = message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto, MessageSerializationOptions.Default)
            };
            var ins = await _storage.InsertMessageAsync(userRecord);
            if (!ins.Success) return new MessageResult { Success = false, Error = ins.Error };

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent
            {
                ChatId = chatId,
                Message = userDto
            });

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user message to existing chat {ChatId}", chatId);
            return new MessageResult { Success = false, Error = "Failed to add user message" };
        }
    }

    public async Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        var (success, error, nextSequence) = await _storage.AllocateSequenceAsync(chatId);
        if (!success) throw new InvalidOperationException(error);
        return nextSequence;
    }

    public async Task<string> CreateAssistantMessageForStreamingAsync(string chatId, int sequenceNumber)
    {
        var dto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = sequenceNumber,
            Text = ""
        };
        var record = new MessageRecord
        {
            Id = dto.Id,
            ChatId = chatId,
            Role = dto.Role,
            Kind = "text",
            TimestampUtc = dto.Timestamp,
            SequenceNumber = dto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(dto, MessageSerializationOptions.Default)
        };
        var ins = await _storage.InsertMessageAsync(record);
        if (!ins.Success) throw new InvalidOperationException(ins.Error);
        return dto.Id;
    }

    public async Task<string> GetMessageContentAsync(string messageId)
    {
        var (resSuccess, _, resContent) = await _storage.GetMessageContentAsync(messageId);
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
                    request.Message);

                if (!userMessageResult.Success)
                {
                    throw new InvalidOperationException(userMessageResult.Error ?? "Failed to add user message");
                }

                return new StreamInitResult
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
            _logger.LogError(ex, "Error preparing unified stream chat");
            throw;
        }
    }

    public async Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            var assistantSeqNumber = await GetNextSequenceNumberAsync(request.ChatId) - 1;
            var (_, _, listMessages) = await _storage.ListChatMessagesOrderedAsync(request.ChatId, cancellationToken);
            var lastAssistant = listMessages.FirstOrDefault(m =>
            {
                var dto = JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!;
                return dto.Role == "assistant" && dto.SequenceNumber == assistantSeqNumber;
            });
            if (lastAssistant == null)
            {
                _logger.LogError("Assistant message not found for streaming in chat {ChatId}", request.ChatId);
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
    private async Task<string> GenerateAIResponseAsync(string chatId)
    {
        try
        {
            var (_, _, listMessages) = await _storage.ListChatMessagesOrderedAsync(chatId);
            var history = listMessages
                .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!)
                .Where(d => (d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text)) || 
                           d is ReasoningMessageDto) // Include ALL reasoning messages for LLM context
                .ToList();
            var lmMessages = history.Select(ConvertToLmMessage).ToList();

            var options = new GenerateReplyOptions { ModelId = GetModelId() };
            var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, options);
            return string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response");
            return $"Error: Failed to generate AI response. {ex.Message}";
        }
    }

    private string GetModelId() => _aiOptions.ModelId ?? "openrouter/horizon-beta";

    private static IMessage ConvertToLmMessage(MessageDto message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "user" => Role.User,
            "assistant" => Role.Assistant,
            "system" => Role.System,
            _ => Role.User
        };

        if (message is TextMessageDto t)
        {
            return new TextMessage
            {
                Role = role,
                Text = t.Text ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty
            };
        }
        if (message is ReasoningMessageDto r)
        {
            return new TextMessage
            {
                Role = role,
                Text = r.GetText() ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty
            };
        }

        return new TextMessage
        {
            Role = role,
            Text = string.Empty,
            Metadata = ImmutableDictionary<string, object>.Empty
        };
    }

    private static string GenerateChatTitle(string firstMessage)
    {
        var title = firstMessage.Length > 50
            ? firstMessage[..47] + "..."
            : firstMessage;
        return title;
    }
    
    #region IToolResultCallback Implementation
    
    public async Task OnToolResultAvailableAsync(string toolCallId, ToolCallResult result, CancellationToken cancellationToken = default)
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
                _logger.LogInformation(
                    "Resolved ToolCallId {ToolCallId} to MessageId {MessageId} with Sequence {Sequence} for result streaming",
                    toolCallId,
                    messageId,
                    sequenceNumber);
            }
            else
            {
                // Fallback to current message ID and next sequence if mapping not found
                messageId = _currentMessageId ?? $"unmapped-{toolCallId}";
                sequenceNumber = _nextSequence++;
                _logger.LogWarning(
                    "Could not resolve ToolCallId {ToolCallId} to MessageId, using fallback: {MessageId} with Sequence {Sequence}",
                    toolCallId,
                    messageId,
                    sequenceNumber);
            }
            
            await StreamChunkReceived(new ToolResultStreamEvent
            {
                ChatId = _currentChatId,
                MessageId = messageId,
                Kind = "tool_result",
                Done = true,
                SequenceNumber = sequenceNumber,
                ChunkSequenceId = 0,
                ToolCallId = toolCallId,
                Result = result.Result,
                IsError = result.Result.StartsWith("Error")
            });
            
            // Check if this was a TaskManager function and save/broadcast task state if so
            if (_toolCallToFunctionMap.TryGetValue(toolCallId, out var functionName) && 
                IsTaskManagerFunction(functionName))
            {
                _logger.LogInformation(
                    "TaskManager function {FunctionName} completed, saving and broadcasting task state for chat {ChatId}",
                    functionName,
                    _currentChatId);
                
                // Save the TaskManager state after the operation
                await _taskManagerService.SaveTaskManagerStateAsync(_currentChatId, cancellationToken);
                
                // Get the updated task state and broadcast it
                var taskState = await _taskManagerService.GetTaskStateAsync(_currentChatId, cancellationToken);
                
                if (taskState.HasValue)
                {
                    await StreamChunkReceived(new TaskUpdateStreamEvent
                    {
                        ChatId = _currentChatId,
                        MessageId = messageId,
                        Kind = "task_update",
                        Done = true,
                        SequenceNumber = _nextSequence++,
                        ChunkSequenceId = 0,
                        TaskState = taskState?.Item2 ?? Array.Empty<TaskItem>(),
                        OperationType = "sync"
                    });
                }
            }
        }
    }
    
    public async Task OnToolCallStartedAsync(string toolCallId, string functionName, string functionArgs, CancellationToken cancellationToken = default)
    {
        // Store the function name for later use in OnToolResultAvailableAsync
        _toolCallToFunctionMap[toolCallId] = functionName;
        
        // Log with message ID and sequence mapping if available
        if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData))
        {
            _logger.LogInformation("Tool call started - ToolCallId: {ToolCallId}, MessageId: {MessageId}, Sequence: {Sequence}, Function: {FunctionName}", 
                toolCallId, mappedData.MessageId, mappedData.SequenceNumber, functionName);
        }
        else
        {
            _logger.LogInformation("Tool call started - ToolCallId: {ToolCallId}, Function: {FunctionName} (no message mapping yet)", 
                toolCallId, functionName);
        }
        await Task.CompletedTask;
    }
    
    public async Task OnToolCallErrorAsync(string toolCallId, string functionName, string error, CancellationToken cancellationToken = default)
    {
        // Log with message ID and sequence mapping if available
        if (_toolCallToMessageMap.TryGetValue(toolCallId, out var mappedData))
        {
            _logger.LogError("Tool call error - ToolCallId: {ToolCallId}, MessageId: {MessageId}, Sequence: {Sequence}, Function: {FunctionName}, Error: {Error}", 
                toolCallId, mappedData.MessageId, mappedData.SequenceNumber, functionName, error);
        }
        else
        {
            _logger.LogError("Tool call error - ToolCallId: {ToolCallId}, Function: {FunctionName}, Error: {Error} (no message mapping)", 
                toolCallId, functionName, error);
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
            
            await StreamChunkReceived(new ToolResultStreamEvent
            {
                ChatId = _currentChatId,
                MessageId = resolvedMessageId,
                Kind = "tool_result",
                Done = true,
                SequenceNumber = resolvedSequence,
                ChunkSequenceId = 0,
                ToolCallId = toolCallId,
                Result = $"Error: {error}",
                IsError = true
            });
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
            "search-tasks"
        };
        
        return taskManagerFunctions.Any(func => functionName.EndsWith(func, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures generation ID is unique by adding time-based salt if needed.
    /// This prevents primary key conflicts when using cached LLM responses.
    /// </summary>
    private string EnsureUniqueGenerationId(string? providedGenerationId, string chatId)
    {
        // If no generation ID provided, create a new one with high precision timestamp
        if (string.IsNullOrEmpty(providedGenerationId))
        {
            var timestamp = DateTimeOffset.UtcNow;
            var microseconds = timestamp.Ticks / 10; // Convert ticks to microseconds
            return $"gen-{timestamp.ToUnixTimeSeconds()}-{microseconds % 1000000:D6}-{Guid.NewGuid():N}".Substring(0, 32);
        }

        // If generation ID is provided (possibly from cache), add a unique suffix to ensure uniqueness
        // Check if it already has our timestamp pattern to avoid double-salting
        if (providedGenerationId.StartsWith("gen-") && providedGenerationId.Length >= 32)
        {
            // Already has our format, likely unique
            return providedGenerationId + $"-{chatId.Substring(0, 8)}";
        }

        // Add time-based salt to the provided ID to ensure uniqueness
        var saltedId = $"{providedGenerationId}-{DateTimeOffset.UtcNow.Ticks:X}";
        
        // Ensure it fits within reasonable ID length (32 chars)
        if (saltedId.Length > 32)
        {
            // Take first 16 chars of original and add time-based suffix
            var truncated = providedGenerationId.Substring(0, Math.Min(16, providedGenerationId.Length));
            return $"{truncated}-{DateTimeOffset.UtcNow.Ticks:X}".Substring(0, 32);
        }

        return saltedId;
    }
}
