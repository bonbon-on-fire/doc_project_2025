using System.Text.Json.Serialization;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Orleans.Contracts;
using AIChat.Server.Extensions;
using AIChat.Server.Hubs;
using AIChat.Server.Services;
using AIChat.Server.Services.Streaming;
using AIChat.Server.Storage;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ChatDto = AIChat.Server.Services.ChatDto;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(
    IChatService chatService,
    ILogger<ChatController> logger,
    Services.Routing.IDualModeRouter router,
    ITaskStorage taskStorage,
    IChatStorage chatStorage,
    // Optional services for advanced features
    IServerSentEventsService? serverSentEventsService = null,
    IHubContext<ChatHub>? hubContext = null,
    IBackgroundChatService? backgroundChatService = null,
    IOperationTrackingService? operationTrackingService = null,
    IStreamingBridge? streamingBridge = null,
    IResilientStreamManager? resilientStreamManager = null
) : ControllerBase
{
    // Core dependencies (required)
    private readonly IChatService _chatService = chatService;
    private readonly ILogger<ChatController> _logger = logger;
    private readonly Services.Routing.IDualModeRouter _router = router;
    private readonly ITaskStorage _taskStorage = taskStorage;
    private readonly IChatStorage _chatStorage = chatStorage;

    // Optional dependencies for advanced features
    private readonly IServerSentEventsService? _serverSentEventsService = serverSentEventsService;
    private readonly IHubContext<ChatHub>? _hubContext = hubContext;
    private readonly IBackgroundChatService? _backgroundChatService = backgroundChatService;
    private readonly IOperationTrackingService? _operationTrackingService = operationTrackingService;
    private readonly IStreamingBridge? _streamingBridge = streamingBridge;
    private readonly IResilientStreamManager? _resilientStreamManager = resilientStreamManager;

    #region Router Pattern Helper Methods

    /// <summary>
    /// Executes a pass-through operation where both Orleans and Direct implementations are identical.
    /// This eliminates code duplication for operations that currently just call the direct service in both paths.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The operation to execute (same for both Orleans and Direct)</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation</returns>
    private async Task<ActionResult<T>> ExecutePassThroughAsync<T>(
        Func<IChatService, Task<ActionResult<T>>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        return await _router.ExecuteAsync<ActionResult<T>>(
            // Orleans operation - pass-through to direct service
            async grain => await operation(_chatService),
            // Direct service operation
            async service => await operation(service),
            operationName,
            cancellationToken
        );
    }

    /// <summary>
    /// Executes a simple operation without return value where both Orleans and Direct implementations are identical.
    /// </summary>
    /// <param name="operation">The operation to execute (same for both Orleans and Direct)</param>
    /// <param name="operationName">Name of the operation for logging and metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of the operation</returns>
    private async Task<ActionResult> ExecutePassThroughAsync(
        Func<IChatService, Task<ActionResult>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        return await _router.ExecuteAsync<ActionResult>(
            // Orleans operation - pass-through to direct service
            async grain => await operation(_chatService),
            // Direct service operation
            async service => await operation(service),
            operationName,
            cancellationToken
        );
    }

    /// <summary>
    /// Creates a standardized error response for service operation failures.
    /// </summary>
    /// <param name="errorMessage">The error message from the failed operation</param>
    /// <param name="operation">Name of the operation that failed</param>
    /// <param name="id">Optional ID related to the operation</param>
    /// <returns>Standardized error ActionResult</returns>
    private ActionResult<T> CreateErrorResponse<T>(string? errorMessage, string operation, string? id = null)
    {
        var idLog = id != null ? $" {id}" : string.Empty;
        _logger.LogError("Error {Operation}{Id}: {Error}", operation, idLog, errorMessage);

        return errorMessage switch
        {
            "Chat not found" => NotFound(new { Error = "Chat not found" }),
            "Operation not found" => NotFound(new { Error = "Operation not found" }),
            _ => StatusCode(500, new { Error = errorMessage ?? $"Failed to {operation.ToLower(System.Globalization.CultureInfo.InvariantCulture)}" })
        };
    }

    #endregion

    /// <summary>
    /// Temporary helper method for grain access in legacy streaming/operation methods.
    /// TODO: Replace with router pattern in future streaming refactor.
    /// </summary>
    /// <typeparam name="T">Grain interface type</typeparam>
    /// <param name="primaryKey">Primary key for the grain</param>
    /// <returns>Grain reference</returns>
    /// <remarks>
    /// This method should only be used by legacy streaming and operation endpoints.
    /// New endpoints should use the router pattern instead.
    /// </remarks>
    private async Task<T> GetGrainAsync<T>(string primaryKey) where T : IGrainWithStringKey
    {
        // Use router to check if Orleans is available
        var isOrleansEnabled = await _router.IsOrleansEnabledAsync();
        if (!isOrleansEnabled)
        {
            throw new InvalidOperationException("Orleans is not available for grain access");
        }

        // TODO: This requires access to IGrainFactory, which we removed.
        // For now, throw an exception to indicate this needs router-based refactoring.
        throw new NotImplementedException(
            "Direct grain access is deprecated. Please refactor to use router pattern.");
    }






    // GET: api/chat/history?userId={userId}&page={page}&pageSize={pageSize}
    [HttpGet("history")]
    public async Task<ActionResult<ChatHistoryResponse>> GetChatHistory(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default
    )
    {
        return await ExecutePassThroughAsync<ChatHistoryResponse>(
            async service =>
            {
                var result = await service.GetChatHistoryAsync(userId, page, pageSize);

                if (!result.Success)
                {
                    return CreateErrorResponse<ChatHistoryResponse>(result.Error, "retrieving chat history", userId);
                }

                var response = new ChatHistoryResponse
                {
                    Chats = result.Chats,
                    TotalCount = result.TotalCount,
                    Page = result.Page,
                    PageSize = result.PageSize,
                };

                return Ok(response);
            },
            "GetChatHistory",
            cancellationToken
        );
    }

    // GET: api/chat/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<ChatDto>> GetChat(string id, CancellationToken cancellationToken = default)
    {
        return await ExecutePassThroughAsync<ChatDto>(
            async service =>
            {
                var result = await service.GetChatAsync(id);

                if (!result.Success)
                {
                    return CreateErrorResponse<ChatDto>(result.Error, "retrieving chat", id);
                }

                return Ok(result.Chat);
            },
            "GetChat",
            cancellationToken
        );
    }

    // POST: api/chat
    [HttpPost]
    public async Task<ActionResult<ChatDto>> CreateChat([FromBody] CreateChatRequest request, CancellationToken cancellationToken = default)
    {
        // If ChatId is provided, this is a continuation of an existing chat
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            // Send message to existing chat using router
            var sendMessageRequest = new Services.SendMessageRequest
            {
                ChatId = request.ChatId,
                UserId = request.UserId,
                Message = request.Message,
                ModeId = request.ModeId,
            };

            return await _router.ExecuteAsync<ActionResult<ChatDto>>(
                // Orleans operation - TODO: Use grain.ProcessMessageAsync() when fully implemented
                // For now, pass-through to direct service to maintain compatibility
                async grain =>
                {
                    var result = await _chatService.SendMessageAsync(sendMessageRequest);

                    if (!result.Success)
                    {
                        _logger.LogError(
                            "Error sending message to chat {ChatId}: {Error}",
                            request.ChatId,
                            result.Error
                        );
                        return StatusCode(500, new { Error = result.Error ?? "Failed to send message" });
                    }

                    // Get the updated chat after processing
                    var chatResult = await _chatService.GetChatAsync(request.ChatId);
                    return Ok(chatResult.Chat);
                },
                // Direct service operation
                async service =>
                {
                    var result = await service.SendMessageAsync(sendMessageRequest);

                    if (!result.Success)
                    {
                        _logger.LogError(
                            "Error sending message to chat {ChatId}: {Error}",
                            request.ChatId,
                            result.Error
                        );
                        return StatusCode(500, new { Error = result.Error ?? "Failed to send message" });
                    }

                    // Get the updated chat after processing
                    var chatResult = await service.GetChatAsync(request.ChatId);
                    return Ok(chatResult.Chat);
                },
                "CreateChat_ExistingChat",
                cancellationToken
            );
        }

        // Create new chat using router
        return await _router.ExecuteAsync<ActionResult<ChatDto>>(
            // Orleans operation - pass-through to direct service for now
            // TODO: Use grain.InitializeAsync() when Orleans chat creation is fully implemented
            async grain =>
            {
                var createRequest = new Services.CreateChatRequest
                {
                    UserId = request.UserId,
                    Message = request.Message,
                    SystemPrompt = request.SystemPrompt,
                    ModeId = request.ModeId,
                };

                var result = await _chatService.CreateChatAsync(createRequest);

                if (!result.Success)
                {
                    _logger.LogError("Error creating chat: {Error}", result.Error);
                    return StatusCode(500, new { Error = result.Error ?? "Failed to create chat" });
                }

                return CreatedAtAction(nameof(GetChat), new { id = result.Chat!.Id }, result.Chat);
            },
            // Direct service operation
            async service =>
            {
                var createRequest = new Services.CreateChatRequest
                {
                    UserId = request.UserId,
                    Message = request.Message,
                    SystemPrompt = request.SystemPrompt,
                    ModeId = request.ModeId,
                };

                var result = await service.CreateChatAsync(createRequest);

                if (!result.Success)
                {
                    _logger.LogError("Error creating chat: {Error}", result.Error);
                    return StatusCode(500, new { Error = result.Error ?? "Failed to create chat" });
                }

                return CreatedAtAction(nameof(GetChat), new { id = result.Chat!.Id }, result.Chat);
            },
            "CreateChat_NewChat",
            cancellationToken
        );
    }

    // DELETE: api/chat/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChat(string id, CancellationToken cancellationToken = default)
    {
        return await ExecutePassThroughAsync(
            async service =>
            {
                var success = await service.DeleteChatAsync(id);
                return !success ? NotFound(new { Error = "Chat not found" }) : NoContent();
            },
            "DeleteChat",
            cancellationToken
        );
    }

    // GET: api/chat/{chatId}/tasks
    [HttpGet("{chatId}/tasks")]
    public async Task<ActionResult<GetTasksResponse>> GetTasks(string chatId, CancellationToken cancellationToken = default)
    {
        return await ExecutePassThroughAsync<GetTasksResponse>(
            async service =>
            {
                // Verify chat exists and user has access
                var (Success, _, _) = await _chatStorage.GetChatByIdAsync(chatId);
                if (!Success)
                {
                    return NotFound(new { Error = "Chat not found" });
                }

                // TODO: Add proper user authorization check here
                // For now, we'll skip authorization in development

                var taskState = await _taskStorage.GetTasksAsync(chatId);

                if (taskState == null)
                {
                    // Return empty task list if no tasks exist
                    return Ok(
                        new GetTasksResponse
                        {
                            ChatId = chatId,
                            Tasks = [],
                            Version = 0,
                        }
                    );
                }

                return Ok(
                    new GetTasksResponse
                    {
                        ChatId = taskState.ChatId,
                        Tasks = taskState.TaskManager.GetTasks(),
                        Version = taskState.Version,
                    }
                );
            },
            "GetTasks",
            cancellationToken
        );
    }

    // Note: Task updates are handled server-side only through LLM tool calls
    // The client has read-only access to task state via the GET endpoint above

    // POST: api/chat/stream-sse
    [HttpPost("stream-sse")]
    public async Task<IActionResult> StreamChatCompletionSse(
        [FromBody] CreateChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Check protocol preference from middleware
        var protocol = HttpContext.Items["PreferredProtocol"] as string ?? "SSE";

        // If SignalR is selected, handle differently
        if (protocol.Equals("SignalR", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleSignalRStreamingAsync(request, cancellationToken);
        }

        // Set response headers for SSE immediately
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Check if Orleans should be used via router health check
        var useOrleans = await _router.IsOrleansEnabledAsync(cancellationToken);

        // Set initial headers based on router decision
        Response.Headers.Append(
            "X-Orleans-Routed",
            useOrleans.ToString().ToLower(System.Globalization.CultureInfo.CurrentCulture)
        );
        Response.Headers.Append("X-Processing-Mode", useOrleans ? "orleans" : "direct");

        // Use unified service method for both new and existing chats
        var streamRequest = new StreamChatRequest
        {
            ChatId = request.ChatId, // null for new chats, populated for existing
            UserId = request.UserId,
            Message = request.Message,
            SystemPrompt = request.SystemPrompt,
            ModeId = request.ModeId,
        };

        // Get initialization metadata from service
        var initResult = await _chatService.PrepareUnifiedStreamChatAsync(streamRequest);

        // Route through Orleans if available
        if (useOrleans)
        {
            try
            {
                _logger.LogInformation(
                    "Routing SSE stream through Orleans for chat {ChatId}",
                    initResult.ChatId
                );
                await ProcessStreamViaOrleansAsync(request, initResult, cancellationToken);
                return new EmptyResult();
            }
            catch (Exception orleansEx)
            {
                _logger.LogWarning(
                    orleansEx,
                    "Orleans streaming failed for chat {ChatId}, falling back to direct processing",
                    initResult.ChatId
                );

                // Update headers to reflect fallback to direct processing
                Response.Headers["X-Orleans-Routed"] = "false";
                Response.Headers["X-Processing-Mode"] = "direct";

                // Fall through to direct processing
            }
        }

        // Direct processing path
        return await ProcessStreamDirectlyWithRouterAsync(request, cancellationToken);
    }


    private async Task<IActionResult> ProcessStreamDirectlyWithRouterAsync(
        CreateChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        string? currentChatId = null;
        string? currentAssistantMessageId = null;
        var currentAssistantSequenceNumber = 0;

        // Generic side-channel forwarder
        async Task ForwardSideChannel(StreamChunkEvent ev)
        {
            var envelope = ev.ToSSEEnvelope();
            var sid = $"{ev.ChatId}:{ev.MessageId}:{ev.SequenceNumber}:{ev.ChunkSequenceId}";
            await SendSseEvent("messageupdate", envelope, sid);
        }

        async Task ForwardMessage(MessageEvent ev)
        {
            var envelope = ev.ToSSEEnvelope();
            var sid = $"{ev.ChatId}:{ev.MessageId}:{ev.SequenceNumber}";
            await SendSseEvent("message", envelope, sid);
        }

        try
        {
            // Use unified service method for both new and existing chats
            var streamRequest = new StreamChatRequest
            {
                ChatId = request.ChatId, // null for new chats, populated for existing
                UserId = request.UserId,
                Message = request.Message,
                SystemPrompt = request.SystemPrompt,
                ModeId = request.ModeId,
            };

            // Get initialization metadata from service
            var initResult = await _chatService.PrepareUnifiedStreamChatAsync(streamRequest);
            currentChatId = initResult.ChatId;

            // Direct processing path
            _logger.LogDebug("Using direct SSE streaming for chat {ChatId}", currentChatId);

            // Subscribe to side-channel after IDs are known
            _chatService.MessageReceived += ForwardMessage;
            _chatService.StreamChunkReceived += ForwardSideChannel;

            // Send INIT event (envelope optional kind: 'meta')
            var initEnvelope = SSEEventExtensions.CreateInitEnvelope(
                initResult.ChatId,
                initResult.UserMessageId,
                initResult.UserTimestamp,
                initResult.UserSequenceNumber
            );

            var initId = $"{initResult.ChatId}<|>{initResult.UserMessageId}";
            await SendSseEvent("init", initEnvelope, initId);

            // Stream the assistant response using the assistant message created during initialization
            await _chatService.StreamAssistantResponseAsync(initResult.ChatId, cancellationToken);

            // Send completion event with final content
            var completeEnvelope = SSEEventExtensions.CreateStreamCompleteEnvelope(
                initResult.ChatId
            );
            await SendSseEvent("complete", completeEnvelope, initId);
        }
        catch (Exception ex)
        {
            // Avoid passing exception object to logger in watch/Test to prevent formatter crashes
            _logger.LogError(
                "Error streaming chat completion: {Type}: {Message}",
                ex.GetType().Name,
                ex.Message
            );
            var errorEnvelope = SSEEventExtensions.CreateErrorEnvelope(
                currentChatId ?? "unknown",
                currentAssistantMessageId,
                currentAssistantSequenceNumber,
                ex.Message
            );
            await SendSseEvent(
                "message",
                errorEnvelope,
                currentChatId != null && currentAssistantMessageId != null
                    ? $"{currentChatId}<|>{currentAssistantMessageId}"
                    : null
            );
        }
        finally
        {
            _chatService.MessageReceived -= ForwardMessage;
            _chatService.StreamChunkReceived -= ForwardSideChannel;
        }

        return new EmptyResult();
    }

    // Handle SignalR-based streaming with operation ID
    private async Task<IActionResult> HandleSignalRStreamingAsync(
        CreateChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Generate unique operation ID
        var operationId = $"op_{Guid.NewGuid():N}";
        string? chatId = null;

        try
        {
            // Prepare the chat stream (same as SSE)
            var streamRequest = new StreamChatRequest
            {
                ChatId = request.ChatId,
                UserId = request.UserId,
                Message = request.Message,
                SystemPrompt = request.SystemPrompt,
                ModeId = request.ModeId,
            };

            // Get initialization metadata
            var initResult = await _chatService.PrepareUnifiedStreamChatAsync(streamRequest);
            chatId = initResult.ChatId;

            // Return operation ID immediately to SignalR client
            var responseData = new
            {
                OperationId = operationId,
                ChatId = chatId,
                MessageId = initResult.UserMessageId,
                Protocol = "SignalR",
                Status = "Processing",
            };

            // Process the assistant response asynchronously
            // Note: The ChatHub is already subscribed to ChatService events
            // and will broadcast messages to the SignalR group automatically
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        // Send init event via SignalR
                        var initEnvelope = SSEEventExtensions.CreateInitEnvelope(
                            initResult.ChatId,
                            initResult.UserMessageId,
                            initResult.UserTimestamp,
                            initResult.UserSequenceNumber
                        );

                        await _hubContext
                            .Clients.Group($"chat_{initResult.ChatId}")
                            .SendAsync(
                                "ReceiveInit",
                                new { OperationId = operationId, Envelope = initEnvelope }
                            );

                        // Stream the assistant response
                        await _chatService.StreamAssistantResponseAsync(
                            initResult.ChatId,
                            cancellationToken
                        );

                        // Send completion event via SignalR
                        var completeEnvelope = SSEEventExtensions.CreateStreamCompleteEnvelope(
                            initResult.ChatId
                        );
                        await _hubContext
                            .Clients.Group($"chat_{initResult.ChatId}")
                            .SendAsync(
                                "ReceiveComplete",
                                new { OperationId = operationId, Envelope = completeEnvelope }
                            );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            "Error processing SignalR stream for operation {OperationId}: {Error}",
                            operationId,
                            ex.Message
                        );

                        // Send error via SignalR
                        await _hubContext
                            .Clients.Group($"chat_{initResult.ChatId}")
                            .SendAsync(
                                "ReceiveError",
                                new
                                {
                                    OperationId = operationId,
                                    ChatId = initResult.ChatId,
                                    Error = ex.Message,
                                    Timestamp = DateTime.UtcNow,
                                }
                            );
                    }
                },
                cancellationToken
            );

            // Return the operation ID response
            return Ok(responseData);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Error initiating SignalR stream for operation {OperationId}: {Error}",
                operationId,
                ex.Message
            );

            // Return error response
            return StatusCode(
                500,
                new
                {
                    OperationId = operationId,
                    Error = ex.Message,
                    Status = "Failed",
                }
            );
        }
    }

    /// <summary>
    /// Processes chat stream through Orleans UserGrain with StreamingBridge conversion.
    /// </summary>
    private async Task ProcessStreamViaOrleansAsync(
        CreateChatRequest request,
        StreamInitResult initResult,
        CancellationToken cancellationToken = default
    )
    {
        var isOrleansAvailable = await _router.IsOrleansEnabledAsync(cancellationToken);
        if (!isOrleansAvailable)
        {
            throw new InvalidOperationException("Orleans is not available for streaming operations");
        }

        // Check if we should use resilient streaming
        var useResilientStreaming =
            _resilientStreamManager != null
            && await _router.IsOrleansEnabledAsync(cancellationToken);

        if (!useResilientStreaming && _streamingBridge == null)
        {
            throw new InvalidOperationException(
                "Neither ResilientStreamManager nor StreamingBridge is available"
            );
        }

        // Validate request for Orleans processing
        if (string.IsNullOrEmpty(request.UserId))
        {
            throw new ArgumentException(
                "UserId is required for Orleans streaming",
                nameof(request)
            );
        }

        if (string.IsNullOrEmpty(initResult.ChatId))
        {
            throw new ArgumentException(
                "ChatId is required for Orleans streaming",
                nameof(initResult)
            );
        }

        try
        {
            // Get the user grain
            var userGrain = await GetGrainAsync<IUserGrain>(request.UserId);

            // Create Orleans ChatRequest from server request
            var orleansRequest = new ChatRequest
            {
                ChatId = initResult.ChatId,
                Message = request.Message,
                UserId = request.UserId,
                ModeId = request.ModeId,
                SystemPrompt = request.SystemPrompt,
                Timestamp = DateTime.UtcNow,
                RequestId = Guid.NewGuid().ToString(),
            };

            // Send INIT event before starting stream
            var initEnvelope = SSEEventExtensions.CreateInitEnvelope(
                initResult.ChatId,
                initResult.UserMessageId,
                initResult.UserTimestamp,
                initResult.UserSequenceNumber
            );

            var initId = $"{initResult.ChatId}<|>{initResult.UserMessageId}";
            await SendSseEvent("init", initEnvelope, initId);

            // Get the grain stream
            var grainStream = userGrain.ProcessChatStreamAsync(orleansRequest, cancellationToken);

            // Convert Orleans stream chunks to SSE format
            var formatter = new Func<StreamChunk, string>(chunk =>
            {
                // Convert Orleans StreamChunk to SSE envelope format
                var envelope = new
                {
                    chatId = chunk.ChatId,
                    messageId = chunk.MessageId ?? Guid.NewGuid().ToString(),
                    sequenceNumber = chunk.ChunkIndex,
                    kind = chunk.IsComplete ? "complete" : "content",
                    content = chunk.Content,
                    timestamp = chunk.Timestamp,
                    metadata = new
                    {
                        chunkIndex = chunk.ChunkIndex,
                        totalChunks = chunk.TotalChunks,
                        operationId = chunk.OperationId,
                        type = chunk.Type.ToString(),
                    },
                };
                return System.Text.Json.JsonSerializer.Serialize(
                    envelope,
                    MessageSerializationOptions.Default
                );
            });

            // Use resilient streaming if available
            if (useResilientStreaming)
            {
                _logger.LogInformation(
                    "Using ResilientStreamManager for stream {StreamId}",
                    initResult.ChatId
                );

                // Generate a unique stream ID for this request
                var streamId = $"{request.UserId}:{initResult.ChatId}:{orleansRequest.RequestId}";

                await _resilientStreamManager!.ProcessResilientStreamAsync(
                    streamId,
                    grainStream,
                    Response,
                    formatter,
                    cancellationToken
                );
            }
            else
            {
                _logger.LogInformation(
                    "Using standard StreamingBridge for stream {StreamId}",
                    initResult.ChatId
                );

                // Use standard StreamingBridge
                await _streamingBridge!.ConvertGrainToHttpStreamAsync(
                    grainStream,
                    Response,
                    formatter,
                    cancellationToken
                );
            }

            // Send completion event
            var completeEnvelope = SSEEventExtensions.CreateStreamCompleteEnvelope(
                initResult.ChatId
            );
            await SendSseEvent("complete", completeEnvelope, initId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Orleans stream cancelled for chat {ChatId}", initResult.ChatId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Orleans streaming for chat {ChatId}", initResult.ChatId);

            // Send error event
            var errorEnvelope = SSEEventExtensions.CreateErrorEnvelope(
                initResult.ChatId,
                null, // Assistant message ID not available
                0, // Sequence number not available
                ex.Message
            );
            await SendSseEvent("message", errorEnvelope, $"{initResult.ChatId}<|>error");
            throw;
        }
    }

    private async Task SendSseEvent(string eventType, object data, string? id = null)
    {
        // Always stream to the current HTTP response (client fetch())
        var json = System.Text.Json.JsonSerializer.Serialize(
            data,
            MessageSerializationOptions.Default
        );
        if (!string.IsNullOrEmpty(id))
        {
            await Response.WriteAsync($"id: {id}\n");
        }
        await Response.WriteAsync($"event: {eventType}\n");
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();

        // Additionally, broadcast via IServerSentEventsService if any listeners are connected
        var clients = _serverSentEventsService.GetClients();
        var clientsList = clients?.ToList() ?? [];
        if (clientsList.Count > 0)
        {
            var client = clientsList.First();
            var sse = new ServerSentEvent { Type = eventType, Data = [json] };
            if (!string.IsNullOrEmpty(id))
            {
                sse.Id = id;
            }
            await client.SendEventAsync(sse);
        }
    }

    // POST: api/chat/operations/{operationId}/cancel
    [HttpPost("operations/{operationId}/cancel")]
    public async Task<ActionResult> CancelOperation(string operationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return BadRequest(new { Error = "Operation ID is required" });
        }

        try
        {
            var useBackground = await _router.IsOrleansEnabledAsync(cancellationToken);

            if (useBackground && _backgroundChatService != null)
            {
                // Try to cancel through background service first
                var cancelled = await _backgroundChatService.CancelOperationAsync(operationId, cancellationToken);

                if (cancelled)
                {
                    _logger.LogInformation(
                        "Successfully cancelled operation {OperationId} through background service",
                        operationId
                    );
                    return Ok(
                        new
                        {
                            Success = true,
                            OperationId = operationId,
                            Message = "Operation cancelled successfully",
                            Method = "BackgroundService",
                        }
                    );
                }
            }

            // If background service didn't handle it, try Orleans grain cancellation
            var isOrleansAvailable = await _router.IsOrleansEnabledAsync(cancellationToken);
            if (isOrleansAvailable && _operationTrackingService != null)
            {
                try
                {
                    // Get operation context to find the associated user
                    var operationContext =
                        await _operationTrackingService.GetOperationUserContextAsync(operationId);

                    if (operationContext != null)
                    {
                        // Get the user grain and attempt cancellation
                        var userGrain = await GetGrainAsync<IUserGrain>(
                            operationContext.UserId
                        );
                        var cancelled = await userGrain.CancelOperation(operationId);

                        if (cancelled)
                        {
                            // Unregister the operation from tracking
                            await _operationTrackingService.UnregisterOperationAsync(operationId);

                            _logger.LogInformation(
                                "Successfully cancelled Orleans operation {OperationId} for user {UserId}",
                                operationId,
                                operationContext.UserId
                            );

                            return Ok(
                                new
                                {
                                    Success = true,
                                    OperationId = operationId,
                                    Message = "Operation cancelled successfully via Orleans grain",
                                    Method = "OrleansGrain",
                                    operationContext.UserId,
                                    operationContext.ChatId,
                                }
                            );
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Orleans grain cancellation failed for operation {OperationId} (user {UserId})",
                                operationId,
                                operationContext.UserId
                            );

                            return BadRequest(
                                new
                                {
                                    Error = "Operation could not be cancelled (may already be completed or not cancellable)",
                                    OperationId = operationId,
                                    operationContext.UserId,
                                }
                            );
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No operation context found for {OperationId}",
                            operationId
                        );

                        return NotFound(
                            new
                            {
                                Error = "Operation not found in tracking system",
                                OperationId = operationId,
                            }
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error during Orleans grain cancellation for operation {OperationId}",
                        operationId
                    );

                    return StatusCode(
                        500,
                        new
                        {
                            Error = "Internal error during Orleans cancellation: " + ex.Message,
                            OperationId = operationId,
                        }
                    );
                }
            }

            return NotFound(
                new
                {
                    Error = "Operation not found or cannot be cancelled",
                    OperationId = operationId,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error cancelling operation {OperationId}: {Error}",
                operationId,
                ex.Message
            );
            return StatusCode(500, new { Error = ex.Message, OperationId = operationId });
        }
    }

    // GET: api/chat/operations/{operationId}/status
    [HttpGet("operations/{operationId}/status")]
    public async Task<ActionResult> GetOperationStatus(string operationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return BadRequest(new { Error = "Operation ID is required" });
        }

        try
        {
            var useBackground = await _router.IsOrleansEnabledAsync(cancellationToken);

            if (useBackground && _backgroundChatService != null)
            {
                var status = await _backgroundChatService.GetOperationStatusAsync(operationId, cancellationToken);

                if (status != null)
                {
                    return Ok(
                        new
                        {
                            OperationId = operationId,
                            Status = status.Status.ToString(),
                            status.QueuedAt,
                            status.StartedAt,
                            status.CompletedAt,
                            status.Error,
                            status.Progress,
                            status.ProgressDescription,
                            Method = "BackgroundService",
                        }
                    );
                }
            }

            return NotFound(new { Error = "Operation not found", OperationId = operationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error getting operation status {OperationId}: {Error}",
                operationId,
                ex.Message
            );
            return StatusCode(500, new { Error = ex.Message, OperationId = operationId });
        }
    }
}

// Request DTOs for API endpoints
public record CreateChatRequest(
    string? ChatId,
    string UserId,
    string Message,
    string? SystemPrompt,
    string? ModeId
);

public class SendMessageRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("modeId")]
    public string? ModeId { get; set; }
}

public class ChatHistoryResponse
{
    public List<ChatDto> Chats { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Task-related DTOs
public class GetTasksResponse
{
    public required string ChatId { get; set; }
    public required IList<TaskManager.TaskItem> Tasks { get; set; } = [];
    public required int Version { get; set; }
}

// Note: UpdateTasksRequest and UpdateTasksResponse removed - tasks are only updated server-side via LLM tool calls

public class TaskErrorInfo
{
    public required string Code { get; set; }
    public required string Message { get; set; }
    public int? CurrentVersion { get; set; }
}
