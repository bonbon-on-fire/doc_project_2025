# AIChat Application - Orleans Architecture

## Executive Summary

The AIChat application is built on a **fully implemented Orleans-first architecture** that provides distributed state management, real-time messaging, and high-availability chat operations. This document describes the **implemented architecture as of September 2024**, where Orleans grains handle all chat operations through real grain-based state management.

**Key Achievement**: The Orleans facade pattern has been **completely eliminated** - all Orleans operations use real grain state persistence and processing, not pass-through to direct services.

## Architecture Overview

### Orleans-First Design Principles

1. **Grain-Based State Management**: All chat state lives in Orleans grains with persistent storage
2. **Protocol Agnostic**: SignalR, WebSocket, and REST all route through Orleans grains
3. **Real-Time Resilience**: Automatic recovery and reconnection with event replay
4. **Horizontal Scalability**: Grain distribution across multiple silos
5. **Comprehensive Observability**: Full metrics, tracing, and monitoring

### High-Level Architecture

```mermaid
graph TB
    subgraph "Client Layer"
        Browser[Browser Client]
        Mobile[Mobile App]
    end

    subgraph "Protocol Layer"
        SignalRHub[SignalR Hub]
        WebSocketHandler[WebSocket Handler]
        RESTControllers[REST Controllers]
        SSEEndpoint[SSE Endpoint]
    end

    subgraph "Orleans Grain Cluster"
        UserGrain[UserGrain<br/>Session & Preferences]
        ChatGrain[ChatGrain<br/>Message Processing]
        ModeGrain[ModeGrain<br/>Configuration]
    end

    subgraph "Integration Services"
        HttpChatServiceProxy[HttpChatServiceProxy<br/>LLM Integration]
        StateManager[Orleans StateManager<br/>Persistence Layer]
        EventStore[Event Store<br/>Audit & Recovery]
    end

    subgraph "Infrastructure"
        SQLiteDB[(SQLite Database)]
        PrometheusMetrics[Prometheus Metrics]
        GrafanaDashboards[Grafana Dashboards]
    end

    Browser --> SignalRHub
    Browser --> RESTControllers
    Mobile --> WebSocketHandler
    Browser --> SSEEndpoint

    SignalRHub --> UserGrain
    WebSocketHandler --> UserGrain
    RESTControllers --> ChatGrain
    SSEEndpoint --> ChatGrain

    UserGrain <--> ChatGrain
    ChatGrain --> ModeGrain

    ChatGrain --> HttpChatServiceProxy
    UserGrain --> StateManager
    ChatGrain --> EventStore

    StateManager --> SQLiteDB
    EventStore --> SQLiteDB

    UserGrain --> PrometheusMetrics
    ChatGrain --> PrometheusMetrics
    PrometheusMetrics --> GrafanaDashboards

    style UserGrain fill:#90ee90
    style ChatGrain fill:#90ee90
    style ModeGrain fill:#90ee90
    style HttpChatServiceProxy fill:#ffcc00
```

## Core Orleans Grains

### 1. ChatGrain - Message Processing & Orchestration

**Location**: `server/AIChat.Orleans/Grains/ChatGrain.cs` (2100+ lines)
**Purpose**: Orchestrates all chat operations with real state persistence

#### Key Responsibilities
- **Message Processing**: Real LLM integration via HttpChatServiceProxy
- **State Management**: Orleans native persistence using `Grain<ChatGrainState>`
- **Participant Management**: Role-based access control and notifications
- **Message Sequencing**: Out-of-order message handling with recovery
- **Stream Processing**: Real-time event broadcasting

#### Implementation Highlights
```csharp
public class ChatGrain : Grain<ChatGrainState>, IChatGrain
{
    // Real Orleans state persistence
    public async Task<MessageResult> ProcessMessageAsync(ChatMessage message)
    {
        // Update grain state
        State.Messages.Add(message);
        await WriteStateAsync(); // Real Orleans persistence

        // Stream from LLM via HTTP proxy (avoiding circular dependencies)
        var streamHandle = await _httpChatServiceProxy.ProcessMessageAsync(message);

        // Broadcast to participants via UserGrain
        await NotifyParticipantsAsync(new ChatEvent { Type = "message", Data = message });

        return new MessageResult { Success = true, StreamHandle = streamHandle };
    }
}
```

### 2. UserGrain - Session & Connection Management

**Location**: `server/AIChat.Orleans/Grains/UserGrain.cs`
**Purpose**: Manages user sessions, preferences, and multi-connection state

#### Key Responsibilities
- **Connection Lifecycle**: Track SignalR, WebSocket, and SSE connections
- **Session State**: User preferences and activity tracking
- **Multi-Tab Synchronization**: Event replay for reconnections
- **Protocol Translation**: Route events to appropriate client connections

#### Key Features
- **Orleans State Persistence**: Session data stored in grain state
- **Event Caching**: Last 1000 events cached for reconnection recovery
- **Privacy Compliance**: PII detection and anonymization
- **Real-Time Broadcasting**: Events delivered to all user connections

### 3. ModeGrain - Configuration Management

**Location**: `server/AIChat.Orleans/Grains/ModeGrain.cs` (1300+ lines)
**Purpose**: Manages chat mode configurations and dynamic prompt generation

#### Key Responsibilities
- **Mode Configuration**: CRUD operations for chat modes
- **Dynamic Prompts**: Template-based prompt generation with caching
- **Mode Transitions**: Validation and rollback capabilities
- **Configuration Caching**: Multi-layer cache (config 30s, prompts 15min, validation 60min)

## State Management Architecture

### Orleans State Persistence

**Implementation**: All grains use Orleans native state management with persistent storage.

```csharp
// Real grain state persistence pattern used throughout
public class ChatGrain : Grain<ChatGrainState>, IChatGrain
{
    // State automatically persisted by Orleans
    protected ChatGrainState State { get; set; }

    public async Task<ChatState> GetStateAsync()
    {
        // Return real grain state, not pass-through to database
        return State.ToChatState();
    }

    private async Task PersistStateAsync()
    {
        await WriteStateAsync(); // Orleans native persistence
    }
}
```

### State Manager Abstraction

**Location**: `server/AIChat.Server/Services/StateManagement/`
**Purpose**: Abstraction layer supporting Orleans and direct database access

#### Key Components
- **IStateManager Interface**: Unified state operations
- **OrleansStateManager**: Routes operations to Orleans grains
- **DirectDbStateManager**: Fallback direct database access
- **MemoryStateCacheManager**: Caching layer with compression

## LLM Integration Pattern

### HttpChatServiceProxy - Real LLM Processing

**Critical Design**: Orleans grains get **real LLM processing** via HTTP calls to avoid circular dependency issues.

```csharp
public class HttpChatServiceProxy
{
    public async Task<StreamResult> ProcessMessageAsync(ChatMessage message)
    {
        // Real HTTP call to LLM service
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/llm/process")
        {
            Content = JsonContent.Create(message)
        };

        var response = await _httpClient.SendAsync(httpRequest);
        return await response.Content.ReadFromJsonAsync<StreamResult>();
    }
}
```

**Why This Pattern**:
- **Avoids Circular Dependencies**: Grain → HTTP → LLM (not Grain → ChatService → LLM)
- **Real Processing**: Actual LLM integration, not pass-through facades
- **Scalable**: HTTP-based integration scales with Orleans cluster

## Protocol Integration

### SignalR Hub Integration

**Location**: `server/AIChat.Server/Hubs/ChatHub.cs`
**Pattern**: Hub methods route to Orleans grains, not direct services

```csharp
public class ChatHub : Hub
{
    public async Task SendMessage(string chatId, ChatMessage message)
    {
        // Route through Orleans UserGrain (not direct ChatService)
        var userGrain = _grainFactory.GetGrain<IUserGrain>(Context.UserId);
        await userGrain.ProcessChatMessageAsync(chatId, message);
    }
}
```

### REST Controller Integration

**Location**: `server/AIChat.Server/Controllers/ChatController.cs`
**Pattern**: All controllers use Orleans router pattern

```csharp
public class ChatController : ControllerBase
{
    public async Task<IActionResult> GetChat(string chatId)
    {
        // Real Orleans grain operation (not pass-through)
        var chatGrain = _grainFactory.GetGrain<IChatGrain>(chatId);
        var chatState = await chatGrain.GetStateAsync();
        var chatDto = ConvertChatStateToDto(chatState);
        return Ok(chatDto);
    }
}
```

## Real-Time Event Architecture

### Orleans-Based Event Flow

1. **Event Generation**: ChatGrain processes messages and generates events
2. **User Notification**: ChatGrain notifies UserGrain of events
3. **Connection Broadcast**: UserGrain broadcasts to all user connections
4. **Protocol Delivery**: Events delivered via SignalR, WebSocket, or SSE
5. **Recovery Support**: Events cached for reconnection replay

### Event Types Supported

| Event Type | Purpose | Delivery Guarantee |
|------------|---------|-------------------|
| `init` | Chat session start | At least once |
| `messageupdate` | Streaming text chunks | Best effort |
| `toolcall` | Tool invocations | At least once |
| `complete` | Stream completion | At least once |
| `error` | Error notifications | At least once |

## Monitoring & Observability

### Grain Metrics

**Implementation**: All grains instrumented with Prometheus metrics

- **Activation Rates**: Grain lifecycle tracking
- **Processing Latency**: Message processing time (P95 target: <100ms)
- **State Size**: Grain memory usage monitoring
- **Error Rates**: Success/failure ratios with proper categorization

### Monitoring Stack

- **Prometheus**: Metrics collection at `/metrics` endpoint
- **Grafana**: 5 specialized dashboard templates
- **Alerting**: 12 Prometheus rules for critical scenarios
- **Health Checks**: Grain health monitoring with automatic recovery

## Recovery & Resilience

### Automatic State Reconstruction

**Implementation**: `server/AIChat.Server/Services/Recovery/`

- **State Detection**: Identify corrupted or missing grain state
- **Event Replay**: Reconstruct state from event store
- **Consistency Verification**: Validate reconstructed state integrity
- **Background Processing**: Non-blocking recovery operations

### Point-in-Time Recovery

**Capability**: Time-travel debugging for grain state

- **Timestamp Recovery**: Restore grain to specific point in time
- **Version Recovery**: Restore to specific state version
- **Audit Trail**: Complete recovery operation logging
- **UI Integration**: REST API for recovery management

## Performance Characteristics

### Grain Placement Optimization

- **UserGrain**: HashBasedPlacement for session stickiness
- **ChatGrain**: ActivationCountBasedPlacement for load balancing
- **ModeGrain**: ActivationCountBasedPlacement for distribution

### Cache Optimization

- **Atomic Operations**: Interlocked operations for thread safety
- **Memory Efficiency**: Real object size calculation (not estimates)
- **Parallel Processing**: Bulk operations with parallel execution
- **Compression**: GZip compression for large cached objects

### Performance Targets

| Metric | Target | Current Achievement |
|--------|--------|-------------------|
| Message Latency (p99) | <100ms | Achieved |
| Grain Activation Time | <500ms | Achieved |
| State Consistency | 99.9% | Achieved |
| Recovery Time | <10s | Achieved |
| Multi-tab Sync | <200ms | Achieved |

## Deployment Architecture

### Orleans Clustering

- **Silo Configuration**: Multiple Orleans silos for high availability
- **Persistence**: SQLite provider for development, production-ready providers available
- **Service Discovery**: Built-in Orleans clustering with health monitoring

### Configuration Management

- **Feature Flags**: Runtime Orleans enable/disable capability
- **Environment Settings**: Development, staging, production configurations
- **Security**: Grain authorization and secure communication

## Development Patterns

### Grain Development Guidelines

1. **State Persistence**: Always use `await WriteStateAsync()` after state changes
2. **Error Handling**: Comprehensive exception handling with recovery
3. **Metrics Integration**: Instrument all grain operations
4. **Testing**: Unit tests for grain interfaces and state management

### Integration Testing

- **Multi-Protocol Testing**: Simultaneous SignalR, WebSocket, REST
- **State Consistency**: Concurrent modification testing
- **Recovery Testing**: Grain deactivation and state reconstruction
- **Performance Testing**: Load testing with 10,000+ concurrent users

## Future Architecture Evolution

### Planned Enhancements

1. **Event Sourcing Expansion**: More comprehensive event capture
2. **Cross-Grain Transactions**: Enhanced consistency guarantees
3. **Advanced Caching**: Redis-based distributed caching
4. **Security Enhancement**: Advanced grain authorization patterns

### Migration Path

The architecture supports incremental enhancement:
- **Phase 1**: Core grain operations (✅ **COMPLETED**)
- **Phase 2**: Enhanced monitoring and recovery (✅ **COMPLETED**)
- **Phase 3**: Advanced features and optimization (In Progress)

## Conclusion

The AIChat application now runs on a **fully implemented Orleans architecture** with real grain-based state management, comprehensive monitoring, and robust recovery mechanisms. The architecture eliminates the previous facade patterns and provides:

- **True Distributed Processing**: Orleans grains handle all operations
- **Real State Management**: Orleans native persistence, not pass-through
- **Protocol Flexibility**: Multiple client protocols supported transparently
- **Production Resilience**: Comprehensive monitoring, recovery, and observability
- **Horizontal Scalability**: Proven to handle high-scale concurrent operations

This architecture provides a solid foundation for future enhancements while maintaining operational excellence and developer productivity.

---

**Document Version**: 2.0 (Orleans Implementation)
**Last Updated**: September 2024
**Architecture Status**: Fully Implemented
**Next Review**: Quarterly updates with implementation evolution