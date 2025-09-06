using System.Text.Json.Serialization;
using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Orleans.Contracts;
using AIChat.Server.Extensions;
using AIChat.Server.Hubs;
using AIChat.Server.Models;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Orleans;
using ChatDto = AIChat.Server.Services.ChatDto;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(
    IChatService chatService,
    ILogger<ChatController> logger,
    IServerSentEventsService serverSentEventsService,
    ITaskStorage taskStorage,
    IChatStorage chatStorage,
    IHubContext<ChatHub> hubContext,
    IFeatureManager featureManager,
    IOptions<BackgroundProcessingOptions> backgroundProcessingOptions,
    IClusterClient? clusterClient = null,
    IBackgroundChatService? backgroundChatService = null,
    IOperationTrackingService? operationTrackingService = null
    ) : ControllerBase
{
    private readonly IChatService _chatService = chatService;
    private readonly ILogger<ChatController> _logger = logger;
    private readonly IServerSentEventsService _serverSentEventsService = serverSentEventsService;
    private readonly ITaskStorage _taskStorage = taskStorage;
    private readonly IChatStorage _chatStorage = chatStorage;
    private readonly IHubContext<ChatHub> _hubContext = hubContext;
    private readonly IFeatureManager _featureManager = featureManager;
    private readonly BackgroundProcessingOptions _backgroundProcessingOptions = backgroundProcessingOptions.Value;
    private readonly IClusterClient? _clusterClient = clusterClient;
    private readonly IBackgroundChatService? _backgroundChatService = backgroundChatService;
    private readonly IOperationTrackingService? _operationTrackingService = operationTrackingService;

    /// <summary>
    /// Determines whether background processing via Orleans should be used.
    /// </summary>
    private async Task<bool> ShouldUseBackgroundProcessingAsync()
    {
        // Check if background processing feature is enabled
        var backgroundProcessingEnabled = await _featureManager.IsEnabledAsync("BackgroundProcessing");
        if (!backgroundProcessingEnabled)
        {
            _logger.LogDebug("Background processing feature is disabled");
            return false;
        }

        // Check if Orleans integration is available
        var orleansEnabled = await _featureManager.IsEnabledAsync("OrleansIntegration");
        if (!orleansEnabled)
        {
            _logger.LogDebug("Orleans integration feature is disabled");
            return false;
        }

        // Check if required services are available
        if (_clusterClient == null)
        {
            _logger.LogWarning("Background processing enabled but Orleans cluster client is not available");
            return false;
        }

        // Check cluster health - Orleans IClusterClient doesn't have IsInitialized property
        try
        {
            // Try a simple grain call to check if Orleans is working
            var healthGrain = _clusterClient.GetGrain<IUserGrain>("health-check-user");
            // This will throw if Orleans is not available
            _ = Task.Run(async () => await healthGrain.GetState(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Orleans cluster client health check failed, falling back to direct processing");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Routes a send message request through Orleans background processing or falls back to direct processing.
    /// </summary>
    private async Task<(bool Success, string? Error, ChatDto? Chat, string? OperationId)> ProcessSendMessageAsync(
        Services.SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var useBackground = await ShouldUseBackgroundProcessingAsync();

        if (useBackground && _clusterClient != null)
        {
            try
            {
                return await ProcessSendMessageViaOrleansAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Orleans background processing failed for send message, falling back to direct processing");
                
                // Fall back to direct processing
                var fallbackResult = await _chatService.SendMessageAsync(request);
                
                // Get the updated chat after direct processing
                ChatDto? fallbackChat = null;
                if (fallbackResult.Success)
                {
                    var chatResult = await _chatService.GetChatAsync(request.ChatId);
                    fallbackChat = chatResult.Chat;
                }
                
                return (fallbackResult.Success, fallbackResult.Error, fallbackChat, null);
            }
        }

        // Direct processing
        var result = await _chatService.SendMessageAsync(request);
        
        // Get the updated chat after direct processing
        ChatDto? directChat = null;
        if (result.Success)
        {
            var chatResult = await _chatService.GetChatAsync(request.ChatId);
            directChat = chatResult.Chat;
        }
        
        return (result.Success, result.Error, directChat, null);
    }

    /// <summary>
    /// Processes a send message request via Orleans UserGrain background processing.
    /// </summary>
    private async Task<(bool Success, string? Error, ChatDto? Chat, string? OperationId)> ProcessSendMessageViaOrleansAsync(
        Services.SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        // Get the user grain
        var userGrain = _clusterClient!.GetGrain<IUserGrain>(request.UserId);

        // Create metadata with mode information
        var metadata = request.ModeId != null 
            ? System.Text.Json.JsonSerializer.Serialize(new { ModeId = request.ModeId })
            : null;

        // Create the chat message for background processing
        var chatMessage = new AIChat.Orleans.Contracts.ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = request.ChatId,
            UserId = request.UserId,
            Content = request.Message,
            Timestamp = DateTime.UtcNow,
            Role = "user",
            Metadata = metadata
        };

        // Process via Orleans background processing
        var operationId = await userGrain.ProcessMessageWithBackground(chatMessage);

        // Register the operation for tracking (enables cancellation)
        if (_operationTrackingService != null)
        {
            await _operationTrackingService.RegisterOperationAsync(
                operationId,
                request.UserId,
                request.ChatId,
                "SendMessage");
        }

        // Get the updated chat (operation is async, so we return current state)
        var chatResult = await _chatService.GetChatAsync(request.ChatId);
        return (chatResult.Success, chatResult.Error, chatResult.Chat, operationId);
    }

    // GET: api/chat/history?userId={userId}&page={page}&pageSize={pageSize}
    [HttpGet("history")]
    public async Task<ActionResult<ChatHistoryResponse>> GetChatHistory(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20
    )
    {
        var result = await _chatService.GetChatHistoryAsync(userId, page, pageSize);

        if (!result.Success)
        {
            _logger.LogError(
                "Error retrieving chat history for user {UserId}: {Error}",
                userId,
                result.Error
            );
            return StatusCode(
                500,
                new { Error = result.Error ?? "Failed to retrieve chat history" }
            );
        }

        var response = new ChatHistoryResponse
        {
            Chats = result.Chats,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
        };

        return Ok(response);
    }

    // GET: api/chat/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<ChatDto>> GetChat(string id)
    {
        var result = await _chatService.GetChatAsync(id);

        if (!result.Success)
        {
            if (result.Error == "Chat not found")
            {
                return NotFound(new { Error = "Chat not found" });
            }

            _logger.LogError("Error retrieving chat {ChatId}: {Error}", id, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve chat" });
        }

        return Ok(result.Chat);
    }

    // POST: api/chat
    [HttpPost]
    public async Task<ActionResult<ChatDto>> CreateChat([FromBody] CreateChatRequest request)
    {
        // If ChatId is provided, this is a continuation of an existing chat
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            // Send message to existing chat
            var sendMessageRequest = new Services.SendMessageRequest
            {
                ChatId = request.ChatId,
                UserId = request.UserId,
                Message = request.Message,
                ModeId = request.ModeId,
            };

            // Use dual-mode processing for sending message
            var (success, error, chat, operationId) = await ProcessSendMessageAsync(sendMessageRequest);

            if (!success)
            {
                _logger.LogError(
                    "Error sending message to chat {ChatId}: {Error}",
                    request.ChatId,
                    error
                );
                return StatusCode(
                    500,
                    new { Error = error ?? "Failed to send message" }
                );
            }

            // If Orleans background processing was used, include operation ID in response
            if (!string.IsNullOrEmpty(operationId))
            {
                return Ok(new
                {
                    Chat = chat,
                    OperationId = operationId,
                    ProcessingMode = "Background"
                });
            }

            return Ok(chat);
        }

        // Create new chat
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
    }

    // DELETE: api/chat/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChat(string id)
    {
        var success = await _chatService.DeleteChatAsync(id);

        return !success ? NotFound(new { Error = "Chat not found" }) : NoContent();
    }

    // GET: api/chat/{chatId}/tasks
    [HttpGet("{chatId}/tasks")]
    public async Task<ActionResult<GetTasksResponse>> GetTasks(string chatId)
    {
        // Verify chat exists and user has access
        var (Success, Error, Chat) = await _chatStorage.GetChatByIdAsync(chatId);
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
        
        // Continue with existing SSE implementation
        // Set response headers for SSE
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

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
                Status = "Processing"
            };
            
            // Process the assistant response asynchronously
            // Note: The ChatHub is already subscribed to ChatService events
            // and will broadcast messages to the SignalR group automatically
            _ = Task.Run(async () =>
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
                    
                    await _hubContext.Clients
                        .Group($"chat_{chatId}")
                        .SendAsync("ReceiveInit", new
                        {
                            OperationId = operationId,
                            Envelope = initEnvelope
                        });
                    
                    // Stream the assistant response
                    await _chatService.StreamAssistantResponseAsync(initResult.ChatId, cancellationToken);
                    
                    // Send completion event via SignalR
                    var completeEnvelope = SSEEventExtensions.CreateStreamCompleteEnvelope(initResult.ChatId);
                    await _hubContext.Clients
                        .Group($"chat_{chatId}")
                        .SendAsync("ReceiveComplete", new
                        {
                            OperationId = operationId,
                            Envelope = completeEnvelope
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        "Error processing SignalR stream for operation {OperationId}: {Error}",
                        operationId,
                        ex.Message
                    );
                    
                    // Send error via SignalR
                    await _hubContext.Clients
                        .Group($"chat_{chatId}")
                        .SendAsync("ReceiveError", new
                        {
                            OperationId = operationId,
                            ChatId = chatId,
                            Error = ex.Message,
                            Timestamp = DateTime.UtcNow
                        });
                }
            }, cancellationToken);
            
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
            return StatusCode(500, new
            {
                OperationId = operationId,
                Error = ex.Message,
                Status = "Failed"
            });
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
            var sse = new ServerSentEvent
            {
                Type = eventType,
                Data = [json],
            };
            if (!string.IsNullOrEmpty(id))
            {
                sse.Id = id;
            }
            await client.SendEventAsync(sse);
        }
    }

    // POST: api/chat/operations/{operationId}/cancel
    [HttpPost("operations/{operationId}/cancel")]
    public async Task<ActionResult> CancelOperation(string operationId)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return BadRequest(new { Error = "Operation ID is required" });
        }

        try
        {
            var useBackground = await ShouldUseBackgroundProcessingAsync();

            if (useBackground && _backgroundChatService != null)
            {
                // Try to cancel through background service first
                var cancelled = await _backgroundChatService.CancelOperationAsync(operationId);
                
                if (cancelled)
                {
                    _logger.LogInformation("Successfully cancelled operation {OperationId} through background service", operationId);
                    return Ok(new 
                    { 
                        Success = true, 
                        OperationId = operationId, 
                        Message = "Operation cancelled successfully",
                        Method = "BackgroundService"
                    });
                }
            }

            // If background service didn't handle it, try Orleans grain cancellation
            if (_clusterClient != null && _operationTrackingService != null)
            {
                try
                {
                    // Get operation context to find the associated user
                    var operationContext = await _operationTrackingService.GetOperationUserContextAsync(operationId);
                    
                    if (operationContext != null)
                    {
                        // Get the user grain and attempt cancellation
                        var userGrain = _clusterClient.GetGrain<IUserGrain>(operationContext.UserId);
                        var cancelled = await userGrain.CancelOperation(operationId);
                        
                        if (cancelled)
                        {
                            // Unregister the operation from tracking
                            await _operationTrackingService.UnregisterOperationAsync(operationId);
                            
                            _logger.LogInformation(
                                "Successfully cancelled Orleans operation {OperationId} for user {UserId}",
                                operationId, operationContext.UserId);
                                
                            return Ok(new 
                            { 
                                Success = true, 
                                OperationId = operationId, 
                                Message = "Operation cancelled successfully via Orleans grain",
                                Method = "OrleansGrain",
                                UserId = operationContext.UserId,
                                ChatId = operationContext.ChatId
                            });
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Orleans grain cancellation failed for operation {OperationId} (user {UserId})",
                                operationId, operationContext.UserId);
                                
                            return BadRequest(new 
                            { 
                                Error = "Operation could not be cancelled (may already be completed or not cancellable)",
                                OperationId = operationId,
                                UserId = operationContext.UserId
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No operation context found for {OperationId}", operationId);
                        
                        return NotFound(new 
                        { 
                            Error = "Operation not found in tracking system", 
                            OperationId = operationId 
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during Orleans grain cancellation for operation {OperationId}", operationId);
                    
                    return StatusCode(500, new 
                    { 
                        Error = "Internal error during Orleans cancellation: " + ex.Message,
                        OperationId = operationId 
                    });
                }
            }

            return NotFound(new 
            { 
                Error = "Operation not found or cannot be cancelled", 
                OperationId = operationId 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling operation {OperationId}: {Error}", 
                operationId, ex.Message);
            return StatusCode(500, new 
            { 
                Error = ex.Message, 
                OperationId = operationId 
            });
        }
    }

    // GET: api/chat/operations/{operationId}/status  
    [HttpGet("operations/{operationId}/status")]
    public async Task<ActionResult> GetOperationStatus(string operationId)
    {
        if (string.IsNullOrEmpty(operationId))
        {
            return BadRequest(new { Error = "Operation ID is required" });
        }

        try
        {
            var useBackground = await ShouldUseBackgroundProcessingAsync();

            if (useBackground && _backgroundChatService != null)
            {
                var status = await _backgroundChatService.GetOperationStatusAsync(operationId);
                
                if (status != null)
                {
                    return Ok(new
                    {
                        OperationId = operationId,
                        Status = status.Status.ToString(),
                        QueuedAt = status.QueuedAt,
                        StartedAt = status.StartedAt,
                        CompletedAt = status.CompletedAt,
                        Error = status.Error,
                        Progress = status.Progress,
                        ProgressDescription = status.ProgressDescription,
                        Method = "BackgroundService"
                    });
                }
            }

            return NotFound(new 
            { 
                Error = "Operation not found", 
                OperationId = operationId 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting operation status {OperationId}: {Error}", 
                operationId, ex.Message);
            return StatusCode(500, new 
            { 
                Error = ex.Message, 
                OperationId = operationId 
            });
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
    public required IList<TaskManager.TaskItem> Tasks { get; set; } =
        [];
    public required int Version { get; set; }
}

// Note: UpdateTasksRequest and UpdateTasksResponse removed - tasks are only updated server-side via LLM tool calls

public class TaskErrorInfo
{
    public required string Code { get; set; }
    public required string Message { get; set; }
    public int? CurrentVersion { get; set; }
}
