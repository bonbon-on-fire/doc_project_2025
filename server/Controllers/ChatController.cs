using AchieveAi.LmDotnetTools.Misc.Utils;
using AIChat.Server.Extensions;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using Lib.AspNetCore.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using ChatDto = AIChat.Server.Services.ChatDto;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;
    private readonly IServerSentEventsService _serverSentEventsService;
    private readonly ITaskStorage _taskStorage;
    private readonly IChatStorage _chatStorage;

    public ChatController(
        IChatService chatService,
        ILogger<ChatController> logger,
        IServerSentEventsService serverSentEventsService,
        ITaskStorage taskStorage,
        IChatStorage chatStorage)
    {
        _chatService = chatService;
        _logger = logger;
        _serverSentEventsService = serverSentEventsService;
        _taskStorage = taskStorage;
        _chatStorage = chatStorage;
    }

    // GET: api/chat/history?userId={userId}&page={page}&pageSize={pageSize}
    [HttpGet("history")]
    public async Task<ActionResult<ChatHistoryResponse>> GetChatHistory(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _chatService.GetChatHistoryAsync(userId, page, pageSize);

        if (!result.Success)
        {
            _logger.LogError("Error retrieving chat history for user {UserId}: {Error}", userId, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve chat history" });
        }

        var response = new ChatHistoryResponse
        {
            Chats = result.Chats,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
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
        var createRequest = new Services.CreateChatRequest
        {
            UserId = request.UserId,
            Message = request.Message,
            SystemPrompt = request.SystemPrompt
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

        if (!success)
        {
            return NotFound(new { Error = "Chat not found" });
        }

        return NoContent();
    }

    // GET: api/chat/{chatId}/tasks
    [HttpGet("{chatId}/tasks")]
    public async Task<ActionResult<GetTasksResponse>> GetTasks(string chatId)
    {
        // Verify chat exists and user has access
        var chatResult = await _chatStorage.GetChatByIdAsync(chatId);
        if (!chatResult.Success)
        {
            return NotFound(new { Error = "Chat not found" });
        }

        // TODO: Add proper user authorization check here
        // For now, we'll skip authorization in development

        var taskState = await _taskStorage.GetTasksAsync(chatId);
        
        if (taskState == null)
        {
            // Return empty task list if no tasks exist
            return Ok(new GetTasksResponse
            {
                ChatId = chatId,
                Tasks = new List<TaskManager.TaskItem>(),
                Version = 0
            });
        }

        return Ok(new GetTasksResponse
        {
            ChatId = taskState.ChatId,
            Tasks = taskState.TaskManager.GetTasks(),
            Version = taskState.Version
        });
    }

    // Note: Task updates are handled server-side only through LLM tool calls
    // The client has read-only access to task state via the GET endpoint above

    // POST: api/chat/stream-sse
    [HttpPost("stream-sse")]
    public async Task StreamChatCompletionSse(
        [FromBody] CreateChatRequest request,
        CancellationToken cancellationToken = default)
    {
        // Set response headers for SSE
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        string? currentChatId = null;
        string? currentAssistantMessageId = null;
        int currentAssistantSequenceNumber = 0;

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
            var streamRequest = new Services.StreamChatRequest
            {
                ChatId = request.ChatId, // null for new chats, populated for existing
                UserId = request.UserId,
                Message = request.Message,
                SystemPrompt = request.SystemPrompt
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
                initResult.UserSequenceNumber);

            var initId = $"{initResult.ChatId}<|>{initResult.UserMessageId}";
            await SendSseEvent("init", initEnvelope, initId);

            // Stream the assistant response using the assistant message created during initialization
            await _chatService.StreamAssistantResponseAsync(
                initResult.ChatId,
                cancellationToken);

            // Send completion event with final content
            var completeEnvelope = SSEEventExtensions.CreateStreamCompleteEnvelope(initResult.ChatId);
            await SendSseEvent("complete", completeEnvelope, initId);
        }
        catch (Exception ex)
        {
            // Avoid passing exception object to logger in watch/Test to prevent formatter crashes
            _logger.LogError("Error streaming chat completion: {Type}: {Message}", ex.GetType().Name, ex.Message);
            var errorEnvelope = SSEEventExtensions.CreateErrorEnvelope(
                currentChatId ?? "unknown",
                currentAssistantMessageId,
                currentAssistantSequenceNumber,
                ex.Message);
            await SendSseEvent(
                "message",
                errorEnvelope,
                currentChatId != null && currentAssistantMessageId != null
                    ? $"{currentChatId}<|>{currentAssistantMessageId}"
                    : null);
        }
        finally
        {
            _chatService.MessageReceived -= ForwardMessage;
            _chatService.StreamChunkReceived -= ForwardSideChannel;
        }
    }

    private async Task SendSseEvent(string eventType, object data, string? id = null)
    {
        // Always stream to the current HTTP response (client fetch())
        var json = System.Text.Json.JsonSerializer.Serialize(data, Services.MessageSerializationOptions.Default);
        if (!string.IsNullOrEmpty(id))
        {
            await Response.WriteAsync($"id: {id}\n");
        }
        await Response.WriteAsync($"event: {eventType}\n");
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();

        // Additionally, broadcast via IServerSentEventsService if any listeners are connected
        var clients = _serverSentEventsService.GetClients();
        if (clients.Any())
        {
            var client = clients.First();
            var sse = new ServerSentEvent { Type = eventType, Data = new List<string> { json } };
            if (!string.IsNullOrEmpty(id))
            {
                sse.Id = id;
            }
            await client.SendEventAsync(sse);
        }
    }
}

// Request DTOs for API endpoints
public record CreateChatRequest(string? ChatId, string UserId, string Message, string? SystemPrompt);

public class SendMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ChatHistoryResponse
{
    public List<Services.ChatDto> Chats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Task-related DTOs
public class GetTasksResponse
{
    public required string ChatId { get; set; }
    public required IList<TaskManager.TaskItem> Tasks { get; set; } = new List<TaskManager.TaskItem>();
    public required int Version { get; set; }
}

// Note: UpdateTasksRequest and UpdateTasksResponse removed - tasks are only updated server-side via LLM tool calls

public class TaskErrorInfo
{
    public required string Code { get; set; }
    public required string Message { get; set; }
    public int? CurrentVersion { get; set; }
}