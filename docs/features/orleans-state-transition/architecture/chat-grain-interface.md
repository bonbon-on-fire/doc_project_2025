# IChatGrain Interface Architecture

## Overview

The IChatGrain interface represents a chat session in the Orleans-based state management system. It follows the Interface Segregation Principle (ISP) from SOLID, dividing chat functionality into four focused interfaces that can be used independently or together.

## Interface Structure

```
IChatGrain (Aggregate)
├── IChatStateGrain      - State management and persistence
├── IChatMessagingGrain  - Message processing and delivery
├── IChatStreamingGrain  - Real-time streaming operations
└── IChatParticipantGrain - Participant management and permissions
```

## Design Principles

### Interface Segregation

Each interface focuses on a single responsibility:

1. **IChatStateGrain**: Manages chat state, initialization, and persistence
2. **IChatMessagingGrain**: Handles message operations like sending, editing, and acknowledgment
3. **IChatStreamingGrain**: Manages streaming operations for real-time communication
4. **IChatParticipantGrain**: Controls participant management, roles, and permissions

### Grain Key Strategy

All chat grains use string-based keys (`IGrainWithStringKey`) for:
- Human readability (chat IDs like "chat-12345")
- Easy debugging and monitoring
- Compatibility with existing chat ID formats

## Core Interfaces

### IChatStateGrain

Manages the lifecycle and state of a chat session.

**Key Methods:**
- `InitializeAsync(ChatInitRequest)` - Initialize a new chat session
- `GetStateAsync()` - Retrieve current chat state
- `GetHistoryAsync(limit, beforeMessageId)` - Get paginated chat history
- `ArchiveAsync()` - Archive the chat session
- `CheckHealthAsync()` - Health monitoring

### IChatMessagingGrain

Handles all message-related operations.

**Key Methods:**
- `ProcessMessageAsync(ChatMessage)` - Process incoming messages
- `SendSystemMessageAsync(content, metadata)` - Send system messages
- `EditMessageAsync(messageId, newContent)` - Edit existing messages
- `DeleteMessageAsync(messageId)` - Delete messages
- `AcknowledgeMessageAsync(messageId, participantId)` - Track delivery

### IChatStreamingGrain

Manages real-time streaming operations for AI responses.

**Key Methods:**
- `StartStreamAsync(StreamMessage)` - Initiate streaming
- `ProcessStreamChunkAsync(StreamChunk)` - Process stream chunks
- `CompleteStreamAsync(streamId, finalContent)` - Complete streams
- `CancelStreamAsync(streamId, reason)` - Cancel active streams
- `GetActiveStreamsAsync()` - List active streams

### IChatParticipantGrain

Controls participant management and permissions.

**Key Methods:**
- `AddParticipantAsync(ChatParticipant)` - Add participants
- `RemoveParticipantAsync(participantId)` - Remove participants
- `UpdateParticipantAsync(ParticipantUpdate)` - Update participant info
- `CheckPermissionAsync(participantId, action)` - Permission checks
- `NotifyParticipantsAsync(eventType, eventData)` - Event broadcasting

## Data Models

### Request/Response Models

- **ChatInitRequest**: Initialization parameters for a new chat
- **ChatState**: Complete state representation of a chat
- **ChatMessage**: Individual message in the chat
- **MessageResult**: Result of message processing operations
- **StreamMessage**: Request to start a streaming operation
- **StreamHandle**: Handle for managing active streams

### Participant Models

- **ChatParticipant**: Represents a chat participant
- **ParticipantUpdate**: Updates to participant information
- **MessageStatus**: Delivery status tracking

### Enumerations

- **ChatStatus**: Active, Archived, Suspended, Initializing, Deleted
- **ParticipantRole**: Guest, Member, Moderator, Admin, Owner
- **PresenceStatus**: Online, Away, Busy, Offline, Invisible
- **ChatAction**: Permissions for various chat operations
- **DeliveryStatus**: Pending, Sent, Delivered, Read, Failed

## Usage Patterns

### Basic Chat Initialization

```csharp
var chatGrain = grainFactory.GetGrain<IChatGrain>("chat-12345");

var initRequest = new ChatInitRequest
{
    ChatId = "chat-12345",
    Title = "Support Chat",
    CreatedBy = "user-1",
    ChatType = "ai-assistant"
};

var chatState = await chatGrain.InitializeAsync(initRequest);
```

### Sending Messages

```csharp
var message = new ChatMessage
{
    ChatId = "chat-12345",
    UserId = "user-1",
    Content = "Hello, I need help",
    Role = "user"
};

var result = await chatGrain.ProcessMessageAsync(message);
```

### Streaming AI Responses

```csharp
var streamMessage = new StreamMessage
{
    ChatId = "chat-12345",
    UserId = "user-1",
    Content = "Explain quantum computing"
};

var handle = await chatGrain.StartStreamAsync(streamMessage);
// Subscribe to Orleans stream using handle.OrleansStreamId
```

### Managing Participants

```csharp
var participant = new ChatParticipant
{
    ParticipantId = "user-2",
    DisplayName = "John Doe",
    Role = ParticipantRole.Member
};

await chatGrain.AddParticipantAsync(participant);
```

## Integration Points

### SignalR Hub Integration

The ChatHub will use IChatGrain for all chat operations:

```csharp
public class ChatHub : Hub
{
    private readonly IGrainFactory _grainFactory;

    public async Task SendMessage(string chatId, string message)
    {
        var chatGrain = _grainFactory.GetGrain<IChatGrain>(chatId);
        var result = await chatGrain.ProcessMessageAsync(...);
        await Clients.Group(chatId).SendAsync("ReceiveMessage", result.Message);
    }
}
```

### REST Controller Integration

Controllers will route through IChatGrain:

```csharp
[ApiController]
public class ChatController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;

    [HttpPost("chat/{chatId}/message")]
    public async Task<IActionResult> SendMessage(string chatId, [FromBody] SendMessageRequest request)
    {
        var chatGrain = _grainFactory.GetGrain<IChatGrain>(chatId);
        var result = await chatGrain.ProcessMessageAsync(...);
        return Ok(result);
    }
}
```

## Versioning Strategy

All interfaces use Orleans' `[Alias]` attribute for versioning:

```csharp
[Alias("AIChat.Orleans.Contracts.IChatGrain")]
public interface IChatGrain { ... }
```

This allows:
- Rolling updates without breaking existing grains
- Backward compatibility during migration
- Gradual feature rollout

## Migration Path

### Phase 1: Shadow Mode
- Deploy IChatGrain alongside existing chat system
- Route selected traffic through Orleans
- Monitor and compare results

### Phase 2: Dual Mode
- Use feature flags to control routing
- Gradually increase Orleans traffic
- Maintain fallback to existing system

### Phase 3: Full Migration
- Route all traffic through Orleans
- Decommission legacy chat service
- Remove fallback code

## Performance Considerations

### Grain Activation
- Grains activate on first use
- State loaded from persistence
- Warm-up time: ~50-100ms

### Message Processing
- Single-threaded execution per grain
- No locking required
- Expected throughput: 1000+ msg/sec per grain

### Streaming
- Utilize Orleans streams for real-time delivery
- Automatic buffering and recovery
- Support for 10,000+ concurrent streams

## Monitoring and Health

### Health Checks
- Each grain implements `CheckHealthAsync()`
- Monitor activation/deactivation rates
- Track message processing times

### Metrics
- Messages processed per second
- Active participants per chat
- Stream chunk delivery rates
- State size per grain

### Alerting
- Grain activation failures
- Message processing errors
- Stream timeout events
- Participant limit exceeded

## Security Considerations

### Authorization
- Permission checks via `CheckPermissionAsync()`
- Role-based access control
- Participant verification

### Data Protection
- State encryption at rest
- Secure message transmission
- Audit logging for sensitive operations

## Future Enhancements

### Planned Features
1. End-to-end encryption support
2. Voice/video chat integration
3. File attachment handling
4. Advanced moderation tools
5. Analytics and reporting

### Extensibility Points
- Custom message types
- Plugin architecture for processors
- Webhook integrations
- External storage providers

## Conclusion

The IChatGrain interface provides a robust, scalable foundation for chat functionality in the Orleans-based architecture. Its segregated design ensures maintainability, testability, and flexibility for future enhancements while maintaining backward compatibility during the migration process.