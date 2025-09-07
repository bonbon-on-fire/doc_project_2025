# Feature Specification: Orleans Grain-Based Chat Architecture

## Executive Summary

This specification defines the migration from a request-scoped ChatService architecture to a distributed, grain-based architecture using Microsoft Orleans. The primary goal is to resolve multi-tab synchronization issues, eliminate race conditions, and provide a scalable foundation for real-time chat operations.

The solution introduces UserGrain as the central coordination point for each user's chat activities, replaces Server-Sent Events (SSE) with SignalR for bidirectional communication, and moves ChatService to background execution. Implementation follows a three-phase approach, ensuring 100% functionality at each phase with zero downtime.

**Timeline**: 9 weeks (including 1-week buffer)  
**Risk Level**: Medium  
**Business Impact**: High - Resolves critical UX issues affecting multi-tab users

---

## Problem Statement

### Current Architecture Issues

The existing chat system suffers from fundamental architectural limitations:

1. **Request-Scoped Service Lifetime**: ChatService is instantiated per HTTP request, causing:
   - Service termination when request completes
   - Inability to handle long-running operations
   - Loss of context during user navigation

2. **Multi-Tab Synchronization Problems**: Each browser tab creates an independent service instance, resulting in:
   - Duplicate API calls to LLM providers (cost impact)
   - Inconsistent state across tabs
   - Race conditions when accessing same chat
   - Poor user experience with interrupted streams

3. **Concurrent Access Issues**: Users switching between chats experience:
   - Interrupted response streams
   - Lost conversation context
   - Incomplete AI responses
   - Confusing state transitions

4. **SSE Limitations**: Server-Sent Events provide only unidirectional communication:
   - No connection state management
   - No automatic reconnection with state recovery
   - No bidirectional messaging capability
   - No built-in message acknowledgment

### Business Impact

- **User Satisfaction**: 30% of users report issues with multi-tab usage
- **Support Burden**: 15% of support tickets related to chat synchronization
- **Cost Overrun**: Duplicate API calls increasing LLM costs by ~20%
- **Scalability Limit**: Current architecture cannot scale beyond 5K concurrent users

---

## Solution Architecture

### High-Level Design

The solution implements a distributed actor model using Microsoft Orleans, with the following key components:

```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│   Client    │◄────►│ SignalR Hub  │◄────►│  UserGrain  │
│  (Browser)  │      │              │      │   (Actor)   │
└─────────────┘      └──────────────┘      └─────────────┘
                                                   │
                                                   ▼
                                            ┌─────────────┐
                                            │ ChatService │
                                            │(Background) │
                                            └─────────────┘
                                                   │
                                                   ▼
                                            ┌─────────────┐
                                            │   LLM API   │
                                            └─────────────┘
```

### Core Components

#### 1. UserGrain (Orleans Actor)

- **Identity**: Unique per authenticated user
- **Lifetime**: Long-lived with automatic activation/deactivation
- **Responsibilities**:
  - Manage all SignalR connections for a user
  - Route messages to appropriate tabs/connections
  - Track active chat subscriptions
  - Coordinate with background ChatService
  - Buffer messages during connection transitions

#### 2. SignalR Hub

- **Purpose**: Real-time bidirectional communication
- **Features**:
  - Automatic reconnection
  - Connection state management
  - Built-in scaling
  - Message acknowledgment

#### 3. Background ChatService

- **Execution**: Hosted service independent of HTTP requests
- **Responsibilities**:
  - Process chat operations asynchronously
  - Stream responses from LLM
  - Publish updates to UserGrain
  - Handle cancellations and timeouts

### Key Architectural Decisions

1. **One Grain Per User**: Ensures all user sessions share same state
2. **Orleans-First Processing**: All chat message processing routes through Orleans grains when available, including SSE streaming endpoints
3. **Dual Communication Support**: SignalR for new bidirectional features while maintaining SSE for backward compatibility during transition
4. **Background Processing**: Decouples long-running operations from HTTP lifecycle
5. **Event-Driven Updates**: Loose coupling between components
6. **Phased Migration**: Minimizes risk with gradual rollout
7. **Resilient Processing**: Orleans grains continue processing even if HTTP connections are lost, enabling recovery and reconnection

---

## Detailed Requirements

### Functional Requirements

#### FR-001: Multi-Tab Synchronization

**User Story**: As a user with multiple browser tabs open, I want all tabs to show consistent chat state so that I can work efficiently across multiple windows.

**Acceptance Criteria**:
1. WHEN a message is received in one tab, THEN all other tabs SHALL receive the same message within 200ms
2. WHEN a user has tabs viewing different chats, THEN only the tab viewing the relevant chat SHALL display incoming messages
3. WHEN a new tab is opened, THEN it SHALL synchronize with existing state within 1 second
4. WHEN a tab is closed, THEN other tabs SHALL continue functioning without interruption

#### FR-002: Concurrent Chat Access

**User Story**: As a user, I want to switch between chats without losing ongoing operations so that I can multitask effectively.

**Acceptance Criteria**:
1. WHEN switching to a different chat while a response is streaming, THEN the stream SHALL continue in the background
2. WHEN returning to a chat with an active stream, THEN the user SHALL see all accumulated updates
3. WHEN multiple tabs access the same chat, THEN they SHALL see identical content without conflicts
4. WHEN an operation completes in the background, THEN all relevant tabs SHALL be notified

#### FR-003: Real-Time Message Delivery

**User Story**: As a user, I want to receive chat updates in real-time so that conversations feel natural and responsive.

**Acceptance Criteria**:
1. WHEN a message is sent, THEN it SHALL appear in the UI within 100ms
2. WHEN an AI response is streaming, THEN chunks SHALL be delivered with < 50ms latency
3. WHEN a connection is lost, THEN it SHALL automatically reconnect within 10 seconds
4. WHEN reconnecting, THEN any missed messages SHALL be delivered in order

#### FR-004: Chat Session Management

**User Story**: As a user, I want my chat sessions properly managed so that I can navigate between conversations seamlessly.

**Acceptance Criteria**:
1. WHEN subscribing to a chat, THEN the subscription SHALL be tracked with timestamp
2. WHEN messages arrive for different chats, THEN they SHALL be routed only to relevant connections
3. WHEN unsubscribing from a chat, THEN no further messages SHALL be received for that chat
4. WHEN switching chats, THEN the subscription state SHALL update within 500ms

#### FR-005: Orleans-First Message Processing

**User Story**: As a system architect, I want all chat message processing to be handled by Orleans grains when Orleans is enabled, so that we achieve consistent resilience and state management across all chat scenarios.

**Rationale**: Chat requests are long-running operations that benefit from Orleans' resilience. If a client connection is lost or a web request terminates, Orleans continues processing and results can be retrieved later or streamed when the client reconnects. This requirement ensures Orleans is used consistently for ALL chat scenarios, not just some endpoints.

**Acceptance Criteria**:
1. WHEN Orleans is enabled (based on feature flags and health checks), THEN all chat message processing SHALL be delegated to Orleans grains
2. WHEN the SSE streaming endpoint (/api/chat/stream-sse) receives a request and Orleans is available, THEN it SHALL route the processing through Orleans grains rather than directly calling ChatService
3. WHEN Orleans grains process chat messages, THEN they SHALL stream response chunks back to the ChatController for forwarding to clients
4. WHEN Orleans grains stream chunks to ChatController, THEN the SSE event format and client experience SHALL remain unchanged
5. WHEN Orleans is unavailable (disabled or unhealthy), THEN the system SHALL seamlessly fall back to direct ChatService processing
6. WHEN switching between Orleans and direct processing, THEN there SHALL be no observable difference in the client API contract
7. WHEN Orleans processes a streaming request, THEN it SHALL continue processing even if the original HTTP connection is terminated
8. WHEN a client reconnects after disconnection, THEN they SHALL be able to retrieve or continue receiving the Orleans-processed response

### Non-Functional Requirements

#### NFR-001: Performance

- Message relay latency: < 100ms (p99)
- Grain activation time: < 500ms
- SignalR connection establishment: < 1 second
- Support 10,000+ concurrent users
- Linear scaling with cluster size

#### NFR-002: Reliability

- No message loss during failures
- Automatic recovery from grain deactivation
- Graceful degradation if Orleans unavailable
- 99.9% uptime SLA

#### NFR-003: Security

- Maintain existing authentication/authorization
- Message isolation between users
- Encrypted SignalR connections
- Audit trail for all operations

#### NFR-004: Maintainability

- Clear separation of concerns
- Comprehensive logging and monitoring
- Automated testing coverage > 80%
- Documentation for operations team

---

## Implementation Phases

### Phase 1: Orleans Foundation (Weeks 1-2)

#### Objectives

- Establish Orleans infrastructure
- Deploy UserGrain in passive mode
- Maintain 100% backward compatibility

#### Deliverables

1. Orleans cluster configuration
2. UserGrain implementation with state management
3. Integration layer with feature flags
4. Monitoring and health checks
5. Unit and integration tests

#### Success Criteria

- Orleans cluster stable for 48 hours
- No performance degradation
- All existing tests pass
- Feature flag controls Orleans usage

### Phase 2: SignalR Integration with Orleans Routing (Weeks 3-5)

#### Objectives

- Introduce SignalR alongside SSE for transition period
- Activate UserGrain message routing for ALL endpoints
- Ensure Orleans processes both SSE and SignalR requests
- Support dual-mode operation with seamless fallback

#### Deliverables

1. SignalR hub implementation
2. Client-side SignalR integration
3. UserGrain active mode for all chat operations
4. SSE endpoint refactoring to route through Orleans
5. Fallback mechanisms for both SSE and SignalR
6. Multi-tab synchronization tests

#### Success Criteria

- Both SSE and SignalR route through Orleans when available
- SignalR adoption > 80% for new features
- SSE continues working with Orleans processing
- Multi-tab synchronization working
- No increase in error rates
- Performance within targets

### Phase 3: Background ChatService (Weeks 6-8)

#### Objectives

- Move ChatService to background execution
- Complete grain coordination for all scenarios
- Prepare for SSE deprecation (optional, based on adoption)

#### Deliverables

1. Background service infrastructure
2. Refactored ChatService with full Orleans integration
3. Full grain coordination for all endpoints
4. SSE deprecation plan (execute only after full SignalR adoption)
5. Production monitoring for both SSE and SignalR paths

#### Success Criteria

- All chat operations route through Orleans when available
- All multi-tab issues resolved
- Performance targets met for both SSE and SignalR
- Zero message loss
- Successful load testing with Orleans handling all traffic

### Week 9: Buffer and Stabilization

- Performance optimization
- Documentation completion
- Team training
- Production readiness review

---

## Technical Implementation Details

### UserGrain Interface

```csharp
public interface IUserGrain : IGrainWithStringKey
{
    // Connection Management
    Task RegisterConnection(string connectionId, string clientId);
    Task UnregisterConnection(string connectionId);
    
    // Chat Operations
    Task SubscribeToChat(string connectionId, string chatId);
    Task UnsubscribeFromChat(string connectionId, string chatId);
    
    // Message Handling
    Task RelayMessage(ChatMessage message);
    Task RelayStreamChunk(StreamChunk chunk);
    Task HandleChatServiceUpdate(ChatUpdate update);
    
    // SSE Stream Processing (Orleans-routed)
    Task<IAsyncEnumerable<StreamChunk>> ProcessChatStreamAsync(ChatRequest request, CancellationToken cancellationToken);
    Task CancelStream(string streamId);
    
    // Health and State
    Task<UserGrainState> GetState();
    Task<HealthStatus> CheckHealth();
}
```

### SignalR Hub Methods

```csharp
public class ChatHub : Hub
{
    // Client -> Server
    Task SendMessage(string chatId, string message);
    Task SubscribeToChat(string chatId);
    Task UnsubscribeFromChat(string chatId);
    Task CancelOperation(string operationId);
    
    // Server -> Client
    Task ReceiveMessage(ChatMessage message);
    Task ReceiveStreamChunk(StreamChunk chunk);
    Task OperationComplete(string operationId);
    Task ConnectionStateChanged(ConnectionState state);
}
```

### Message Flow Sequences

#### New Message Flow (SignalR)

1. Client sends message via SignalR
2. Hub authenticates and validates
3. Hub forwards to UserGrain
4. UserGrain queues with ChatService
5. ChatService processes with LLM
6. Response streams back through UserGrain
7. UserGrain distributes to connections
8. Clients receive filtered updates

#### SSE Streaming Flow (with Orleans)

1. Client sends request to /api/chat/stream-sse
2. ChatController checks Orleans availability
3. If Orleans available:
   a. Controller delegates to UserGrain
   b. UserGrain initiates ChatService processing
   c. ChatService streams chunks to UserGrain
   d. UserGrain relays chunks to ChatController
   e. ChatController forwards as SSE events
4. If Orleans unavailable:
   a. Controller directly calls ChatService
   b. ChatService streams directly to SSE response
5. Client receives consistent SSE format regardless of path

#### Connection Recovery Flow

1. Client detects connection loss
2. SignalR initiates reconnection
3. Hub re-authenticates user
4. UserGrain recovers connection state
5. Buffered messages delivered
6. Normal operation resumes

---

## Risk Analysis and Mitigation

### Technical Risks

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Orleans learning curve | High | Medium | Training, PoC, documentation |
| State synchronization complexity | High | Medium | Clear ownership, event sourcing |
| Performance overhead | Medium | Low | Benchmarking, optimization |
| Integration failures | High | Low | Extensive testing, gradual rollout |

### Operational Risks

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Deployment complexity | Medium | Medium | Automation, runbooks |
| Debugging difficulty | Medium | High | Distributed tracing, logging |
| Rollback complexity | High | Low | Feature flags, compatibility |

---

## Testing Strategy

### Unit Testing

- UserGrain state management
- Message routing logic
- Connection handling
- Error recovery

### Integration Testing

- Orleans cluster setup
- SignalR connectivity
- End-to-end message flow
- Multi-tab scenarios

### Performance Testing

- Load testing with 10K users
- Latency measurements
- Resource utilization
- Scaling validation

### Acceptance Testing

- User journey validation
- Multi-tab workflows
- Error scenarios
- Recovery procedures

---

## Monitoring and Operations

### Key Metrics

#### Application Metrics

- Active grains count
- Message throughput
- Average latency
- Error rate

#### Infrastructure Metrics

- CPU/Memory usage
- Network bandwidth
- Storage I/O
- Cluster health

### Alerting Rules

- Grain activation latency > 1s
- Message delivery latency > 200ms
- Error rate > 1%
- Connection failure rate > 5%

### Operational Procedures

- Grain state inspection
- Manual grain deactivation
- Cluster scaling
- Emergency rollback

---

## Success Metrics

### Technical Success

- Zero multi-tab synchronization issues
- Sub-100ms message latency (p99)
- 99.9% uptime achieved
- Successful 10K user load test

### Business Success

- 90% reduction in related support tickets
- 20% reduction in LLM API costs
- Positive user feedback
- Successful scale to 20K users

---

## Appendices

### A. Glossary

- **Grain**: Orleans virtual actor representing a user
- **Silo**: Orleans runtime host for grains
- **SignalR**: Real-time bidirectional communication framework
- **SSE**: Server-Sent Events (current implementation)

### B. References

- [Microsoft Orleans Documentation](https://docs.microsoft.com/orleans)
- [SignalR Documentation](https://docs.microsoft.com/signalr)
- Internal Architecture Docs: `/docs/architecture/`

### C. Related Documents

- Current Architecture Analysis: `scratchpad/orleans-integration/codebase-research/`
- Detailed Requirements: `scratchpad/orleans-integration/detailed-requirements.md`
- Implementation Plan: `scratchpad/orleans-integration/phased-implementation-plan.md`

---

## Document Control

**Version**: 1.1  
**Status**: Final Draft  
**Author**: System Architect  
**Date**: 2025-09-07  
**Review**: Pending

### Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-08-29 | System Architect | Initial specification |
| 1.1 | 2025-09-07 | System Architect | Added FR-005: Orleans-First Message Processing requirement to ensure Orleans handles ALL chat scenarios including SSE streaming |

---

## Approval

| Role | Name | Signature | Date |
|------|------|-----------|------|
| Product Owner | | | |
| Technical Lead | | | |
| Architecture Team | | | |
| DevOps Lead | | | |
