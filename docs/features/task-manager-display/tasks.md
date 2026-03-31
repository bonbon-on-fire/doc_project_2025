# Task Manager Display - Implementation Tasks

## Overview

This document breaks down the Task Manager Display feature into specific, actionable tasks organized by implementation phase. Each task includes dependencies, estimated effort, and clear acceptance criteria.

## Task Structure Key
- **ID Format**: TMD-P{Phase}-{Number} (e.g., TMD-P1-001)
- **Effort Sizing**: S (1-2 hours), M (2-4 hours), L (4-8 hours)
- **Priority**: Critical, High, Medium, Low
- **Status**: Not Started, In Progress, Completed, Blocked

---

## Phase 1: Backend Infrastructure (Database & API)

### TMD-P1-001: Create Database Schema for Task Persistence
**Effort**: M  
**Priority**: Critical  
**Dependencies**: None  
**Status**: Completed  

**Description**: Create SQLite database tables and indexes for storing task data per chat.

**Acceptance Criteria**:
- [ ] Create `chat_tasks` table with columns: id, chat_id, task_data (JSON), version, created_at_utc, updated_at_utc
- [ ] Add foreign key constraint to chats table with CASCADE delete
- [ ] Create unique index on chat_id for efficient queries
- [ ] Create migration script for schema update
- [ ] Test foreign key cascade deletion works correctly

**Technical Notes**:
- Use EF Core migrations for schema management
- JSON column for flexible task structure
- Version column for optimistic concurrency control

**Testing Requirements**:
- [ ] Unit test: Verify table creation
- [ ] Unit test: Test cascade delete when chat is removed
- [ ] Unit test: Verify unique constraint on chat_id

---

### TMD-P1-002: Implement Task Storage Service
**Effort**: L  
**Priority**: Critical  
**Dependencies**: TMD-P1-001  
**Status**: Completed  

**Description**: Create ITaskStorage interface and SqliteTaskStorage implementation for task persistence.

**Acceptance Criteria**:
- [ ] Define ITaskStorage interface with methods: GetTasksAsync, SaveTasksAsync, DeleteTasksAsync
- [ ] Implement SqliteTaskStorage with proper error handling
- [ ] Add optimistic concurrency with version checking
- [ ] Implement JSON serialization/deserialization for task data
- [ ] Register service in dependency injection container

**Technical Notes**:
```csharp
public interface ITaskStorage
{
    Task<ChatTaskState?> GetTasksAsync(string chatId, CancellationToken ct);
    Task<ChatTaskState> SaveTasksAsync(string chatId, TaskItem[] tasks, int version, CancellationToken ct);
    Task DeleteTasksAsync(string chatId, CancellationToken ct);
}
```

**Testing Requirements**:
- [ ] Unit test: CRUD operations
- [ ] Unit test: Version conflict detection
- [ ] Unit test: Concurrent access handling
- [ ] Integration test: Database persistence

---

### TMD-P1-003: Create Task API Endpoints
**Effort**: L  
**Priority**: Critical  
**Dependencies**: TMD-P1-002  
**Status**: Completed  
**Note**: Only GET endpoint implemented as tasks are updated server-side via LLM tool calls only  

**Description**: Add REST API endpoints in ChatController for task operations.

**Acceptance Criteria**:
- [ ] Implement GET /api/chats/{chatId}/tasks endpoint
- [ ] Implement POST /api/chats/{chatId}/tasks endpoint with version checking
- [ ] Implement DELETE /api/chats/{chatId}/tasks endpoint
- [ ] Add proper authorization checks (user owns chat)
- [ ] Return appropriate HTTP status codes and error messages
- [ ] Add request/response DTOs with validation

**Technical Notes**:
- Reuse existing chat authorization logic
- Add TasksDto for API responses
- Include version in responses for optimistic concurrency

**Testing Requirements**:
- [ ] Unit test: Authorization validation
- [ ] Unit test: Version conflict handling
- [ ] Integration test: Full API flow
- [ ] E2E test: API endpoint accessibility

---

### TMD-P1-004: Extend SSE Event Types for Tasks
**Effort**: M  
**Priority**: High  
**Dependencies**: TMD-P1-003  
**Status**: Completed  

**Description**: Add task-specific SSE event types and payload structures.

**Acceptance Criteria**:
- [ ] Define TaskOperationEvent class with appropriate properties
- [ ] Add task_operation_start, task_operation_complete, task_state_sync event types
- [ ] Update SSEEventEnvelope to support task events
- [ ] Implement task event emission in ChatService
- [ ] Add event serialization with proper JSON formatting

**Technical Notes**:
```csharp
public class TaskOperationEvent : ISSEPayload
{
    public string EventType => "task_operation";
    public string ChatId { get; set; }
    public string MessageId { get; set; }
    public TaskOperation Operation { get; set; }
    public TaskItem[] TaskState { get; set; }
}
```

**Testing Requirements**:
- [ ] Unit test: Event serialization
- [ ] Unit test: Event type routing
- [ ] Integration test: Event emission via SSE

---

## Phase 2: Core Frontend Components

### TMD-P2-001: Create TaskItem TypeScript Interfaces
**Effort**: S  
**Priority**: Critical  
**Dependencies**: None  
**Status**: Completed  

**Description**: Define TypeScript interfaces for task data structures.

**Acceptance Criteria**:
- [ ] Create TaskItem interface with id, title, status, subtasks, notes, level
- [ ] Create TaskStatus enum matching C# TaskManager
- [ ] Create ChatTaskState interface for storage
- [ ] Create TaskOperation interface for operations
- [ ] Export from shared types module

**Technical Notes**:
```typescript
// In shared/types/tasks.ts
export interface TaskItem {
  id: string;
  title: string;
  status: 'NotStarted' | 'InProgress' | 'Completed' | 'Removed';
  subtasks?: TaskItem[];
  notes?: string[];
  level: number;
}
```

**Testing Requirements**:
- [ ] TypeScript compilation check
- [ ] Type compatibility with backend DTOs

---

### TMD-P2-002: Implement TaskListDisplay Component
**Effort**: L  
**Priority**: High  
**Dependencies**: TMD-P2-001  
**Status**: Completed  

**Description**: Create reusable component for rendering hierarchical task lists.

**Acceptance Criteria**:
- [ ] Display tasks with proper indentation for hierarchy
- [ ] Show status icons matching TaskManager symbols (□, ◻, ☑, ☒)
- [ ] Support full and compact display variants
- [ ] Implement collapsible subtasks for complex lists
- [ ] Add hover tooltips for task notes
- [ ] Handle empty state gracefully

**Technical Notes**:
- Use recursive rendering for subtasks
- Implement virtual scrolling for >50 items
- Use CSS grid for alignment

**Testing Requirements**:
- [ ] Unit test: Renders task hierarchy correctly
- [ ] Unit test: Status icons display properly
- [ ] Unit test: Handles empty task list
- [ ] Visual test: Screenshot comparison

---

### TMD-P2-003: Create TaskManagerToolCallRenderer
**Effort**: L  
**Priority**: High  
**Dependencies**: TMD-P2-002  
**Status**: Completed  

**Description**: Build renderer for task manager tool calls in message stream.

**Acceptance Criteria**:
- [ ] Extend CollapsibleMessageRenderer base component
- [ ] Display operation summary when collapsed (e.g., "Added task: Draft plan")
- [ ] Show full operation details when expanded
- [ ] Include before/after task state comparison
- [ ] Apply task-specific styling and icons
- [ ] Handle streaming state with loading indicators

**Technical Notes**:
- Parse tool call arguments for operation details
- Format YAML-like display for parameters
- Use existing collapse/expand patterns

**Testing Requirements**:
- [ ] Unit test: Collapsed view shows summary
- [ ] Unit test: Expanded view shows details
- [ ] Unit test: Handles missing data gracefully
- [ ] Integration test: Works with message router

---

### TMD-P2-004: Register Task Manager Renderer
**Effort**: S  
**Priority**: High  
**Dependencies**: TMD-P2-003  
**Status**: Completed  

**Description**: Register TaskManagerToolCallRenderer in the renderer registry.

**Acceptance Criteria**:
- [ ] Register renderer for 'task_manager_tool_call' message type
- [ ] Configure streaming and collapse support flags
- [ ] Update MessageRouter to handle new type
- [ ] Ensure fallback behavior for unknown types
- [ ] Add to renderer initialization sequence

**Technical Notes**:
```typescript
rendererRegistry.register('task_manager_tool_call', {
  messageType: 'task_manager_tool_call',
  component: TaskManagerToolCallRenderer,
  supportsStreaming: true,
  supportsCollapse: true
});
```

**Testing Requirements**:
- [ ] Unit test: Renderer registration successful
- [ ] Unit test: Message router finds renderer
- [ ] Integration test: Renders in message stream

---

## Phase 3: State Management & Pinned Tracker

### TMD-P3-001: Implement Task Manager Store
**Effort**: L  
**Priority**: Critical  
**Dependencies**: TMD-P2-001  
**Status**: Completed  

**Description**: Create Svelte store for managing task state across components.

**Acceptance Criteria**:
- [ ] Create taskManagerStore with Map of chatId to tasks
- [ ] Implement loadTasks, updateFromToolCall, persistTasks, clearTasks actions
- [ ] Add optimistic updates with rollback on failure
- [ ] Create derived stores for currentChatTasks and taskStats
- [ ] Handle loading and error states
- [ ] Implement version-based concurrency control

**Technical Notes**:
- Use writable store with custom methods
- Debounce persistence to backend
- Cache tasks in memory for performance

**Testing Requirements**:
- [ ] Unit test: Store initialization
- [ ] Unit test: Task CRUD operations
- [ ] Unit test: Optimistic updates and rollback
- [ ] Unit test: Derived store calculations
- [ ] Integration test: API communication

---

### TMD-P3-002: Build PinnedTaskTracker Component
**Effort**: L  
**Priority**: Critical  
**Dependencies**: TMD-P3-001, TMD-P2-002  
**Status**: Completed  

**Description**: Create the main pinned task tracker component.

**Acceptance Criteria**:
- [ ] Display above message input when tasks exist
- [ ] Show task statistics when collapsed (e.g., "3 tasks: 1 completed")
- [ ] Expand to show full task list using TaskListDisplay
- [ ] Subscribe to task store for real-time updates
- [ ] Persist expanded/collapsed state in localStorage
- [ ] Handle loading and error states with appropriate UI
- [ ] Include smooth expand/collapse animation

**Technical Notes**:
- Position with CSS sticky or fixed positioning
- Use Svelte transitions for animations
- Implement proper cleanup in onDestroy

**Testing Requirements**:
- [ ] Unit test: Shows/hides based on task presence
- [ ] Unit test: Collapsed view shows correct stats
- [ ] Unit test: Expanded view renders task list
- [ ] Unit test: Real-time updates work
- [ ] E2E test: Persists state across sessions

---

### TMD-P3-003: Integrate Tracker in ChatWindow
**Effort**: M  
**Priority**: Critical  
**Dependencies**: TMD-P3-002  
**Status**: Completed  

**Description**: Add PinnedTaskTracker to ChatWindow component.

**Acceptance Criteria**:
- [ ] Add PinnedTaskTracker above MessageInput component
- [ ] Pass current chatId as prop
- [ ] Show only when current chat has tasks
- [ ] Maintain proper layout and spacing
- [ ] Ensure responsive behavior on mobile
- [ ] Handle chat switching correctly

**Technical Notes**:
```svelte
{#if $currentChatId && $currentChatTasks?.length > 0}
  <PinnedTaskTracker chatId={$currentChatId} />
{/if}
```

**Testing Requirements**:
- [ ] Visual test: Layout looks correct
- [ ] Unit test: Conditional rendering works
- [ ] E2E test: Tracker appears when tasks exist
- [ ] E2E test: Tracker updates on chat switch

---

### TMD-P3-004: Extend Tool Call Handler for Tasks
**Effort**: M  
**Priority**: High  
**Dependencies**: TMD-P3-001  
**Status**: Completed  

**Description**: Modify toolsAggregateMessageHandler to detect and process task manager calls.

**Acceptance Criteria**:
- [ ] Detect task manager functions by name pattern
- [ ] Mark task manager messages with special type
- [ ] Extract operation details from tool calls
- [ ] Update task store when operations complete
- [ ] Parse task state from tool results
- [ ] Handle errors gracefully

**Technical Notes**:
- List of task functions: add-task, update-task, delete-task, etc.
- Parse JSON results for task state
- Trigger store updates asynchronously

**Testing Requirements**:
- [ ] Unit test: Correctly identifies task functions
- [ ] Unit test: Extracts operation details
- [ ] Unit test: Updates store on completion
- [ ] Integration test: Full flow from SSE to store

---

## Phase 4: Real-time Synchronization

### TMD-P4-001: Implement SSE Task Event Processing
**Effort**: M  
**Priority**: High  
**Dependencies**: TMD-P3-001, TMD-P1-004  
**Status**: Not Started  

**Description**: Add SSE event handlers for task-specific events.

**Acceptance Criteria**:
- [ ] Handle task_operation_start events
- [ ] Handle task_operation_complete events
- [ ] Handle task_state_sync events
- [ ] Update loading states during operations
- [ ] Sync task state from server events
- [ ] Handle connection loss gracefully

**Technical Notes**:
```typescript
function handleTaskEvents(event: SSEEvent) {
  switch (event.eventType) {
    case 'task_operation_start':
      taskManager.setLoading(event.chatId, true);
      break;
    case 'task_operation_complete':
      taskManager.updateFromToolCall(...);
      break;
  }
}
```

**Testing Requirements**:
- [ ] Unit test: Event type routing
- [ ] Unit test: State updates from events
- [ ] Integration test: Real-time updates
- [ ] E2E test: Multi-client synchronization

---

### TMD-P4-002: Add SignalR Task Broadcasts
**Effort**: M  
**Priority**: Medium  
**Dependencies**: TMD-P1-004  
**Status**: Not Started  

**Description**: Extend SignalR hub to broadcast task state changes.

**Acceptance Criteria**:
- [ ] Add TaskStateChanged hub method
- [ ] Broadcast on task operations
- [ ] Include chat ID and task state
- [ ] Handle client subscriptions
- [ ] Ensure proper authorization

**Technical Notes**:
- Reuse existing hub infrastructure
- Broadcast to chat participants only
- Include version for conflict detection

**Testing Requirements**:
- [ ] Unit test: Hub method works
- [ ] Integration test: Broadcasts received
- [ ] E2E test: Multi-user synchronization

---

## Phase 5: Error Handling & Optimization

### TMD-P5-001: Implement Error Recovery
**Effort**: M  
**Priority**: High  
**Dependencies**: TMD-P3-001  
**Status**: Not Started  

**Description**: Add comprehensive error handling and recovery mechanisms.

**Acceptance Criteria**:
- [ ] Cache task state in localStorage for recovery
- [ ] Show stale data indicator when offline
- [ ] Queue failed operations for retry
- [ ] Display user-friendly error messages
- [ ] Add manual refresh capability
- [ ] Log errors for debugging

**Technical Notes**:
- Use exponential backoff for retries
- Limit localStorage to 5MB
- Clear old cached data periodically

**Testing Requirements**:
- [ ] Unit test: Error state handling
- [ ] Unit test: Offline mode works
- [ ] Unit test: Retry logic
- [ ] E2E test: Recovery after connection loss

---

### TMD-P5-002: Optimize Performance
**Effort**: L  
**Priority**: Medium  
**Dependencies**: TMD-P2-002, TMD-P3-002  
**Status**: Not Started  

**Description**: Implement performance optimizations for large task lists.

**Acceptance Criteria**:
- [ ] Implement virtual scrolling for >50 tasks
- [ ] Add debouncing for rapid updates
- [ ] Memoize expensive computations
- [ ] Lazy load task details on demand
- [ ] Optimize re-renders with proper keys
- [ ] Profile and fix performance bottlenecks

**Technical Notes**:
- Use svelte-virtual-list for scrolling
- Debounce persistence by 1 second
- Use $inspect for performance profiling

**Testing Requirements**:
- [ ] Performance test: 100 tasks render <100ms
- [ ] Performance test: Updates <20ms
- [ ] Memory test: <5MB for 100 tasks
- [ ] Visual test: Smooth scrolling

---

### TMD-P5-003: Add Accessibility Features
**Effort**: M  
**Priority**: High  
**Dependencies**: TMD-P3-002  
**Status**: Not Started  

**Description**: Ensure WCAG 2.1 AA compliance for all components.

**Acceptance Criteria**:
- [ ] Add proper ARIA labels and roles
- [ ] Implement keyboard navigation (Tab, Enter, Space, Escape)
- [ ] Ensure proper focus management
- [ ] Meet color contrast requirements (4.5:1)
- [ ] Add screen reader announcements
- [ ] Test with accessibility tools

**Technical Notes**:
```svelte
<button
  aria-expanded={isExpanded}
  aria-controls="task-list"
  aria-label={`Tasks: ${completed} of ${total} completed`}
>
```

**Testing Requirements**:
- [ ] Automated accessibility tests (axe-core)
- [ ] Manual screen reader testing
- [ ] Keyboard navigation testing
- [ ] Color contrast validation

---

## Phase 6: Testing & Documentation

### TMD-P6-001: Write Unit Tests
**Effort**: L  
**Priority**: High  
**Dependencies**: All implementation tasks  
**Status**: Not Started  

**Description**: Create comprehensive unit test suite.

**Acceptance Criteria**:
- [ ] Test all component props and events
- [ ] Test store operations and state changes
- [ ] Test error conditions and edge cases
- [ ] Test API request/response handling
- [ ] Achieve >80% code coverage
- [ ] All tests pass in CI pipeline

**Technical Notes**:
- Use Vitest for component tests
- Use Testing Library utilities
- Mock API calls and stores

**Testing Requirements**:
- [ ] Components: 20+ test cases
- [ ] Store: 15+ test cases
- [ ] Handlers: 10+ test cases
- [ ] Coverage report generated

---

### TMD-P6-002: Create E2E Tests
**Effort**: L  
**Priority**: High  
**Dependencies**: All implementation tasks  
**Status**: Not Started  

**Description**: Build end-to-end test scenarios.

**Acceptance Criteria**:
- [ ] Test task creation via AI chat
- [ ] Test task persistence across sessions
- [ ] Test real-time synchronization
- [ ] Test error recovery flows
- [ ] Test mobile responsiveness
- [ ] Test accessibility compliance

**Technical Notes**:
- Use Playwright for E2E tests
- Test against test database
- Include visual regression tests

**Testing Requirements**:
- [ ] 10+ E2E test scenarios
- [ ] Cross-browser testing (Chrome, Firefox, Safari)
- [ ] Mobile device testing
- [ ] Performance benchmarks included

---

### TMD-P6-003: Write Developer Documentation
**Effort**: M  
**Priority**: Medium  
**Dependencies**: All implementation tasks  
**Status**: Not Started  

**Description**: Create documentation for developers.

**Acceptance Criteria**:
- [ ] Document component APIs and props
- [ ] Document store structure and actions
- [ ] Provide integration examples
- [ ] Include troubleshooting guide
- [ ] Add inline code comments
- [ ] Update main README if needed

**Technical Notes**:
- Use JSDoc/TSDoc format
- Include code examples
- Document known limitations

**Testing Requirements**:
- [ ] Documentation builds without errors
- [ ] Examples are executable
- [ ] Links are valid

---

## Task Dependencies Graph

```
Phase 1 (Backend)
├── TMD-P1-001: Database Schema
├── TMD-P1-002: Storage Service ──────────┐
├── TMD-P1-003: API Endpoints ────────────┤
└── TMD-P1-004: SSE Events ───────────────┤
                                          │
Phase 2 (Components)                      │
├── TMD-P2-001: TypeScript Interfaces     │
├── TMD-P2-002: TaskListDisplay ──────────┤
├── TMD-P2-003: ToolCallRenderer ─────────┤
└── TMD-P2-004: Register Renderer ────────┤
                                          │
Phase 3 (State & Tracker)                 │
├── TMD-P3-001: Task Store ←──────────────┘
├── TMD-P3-002: PinnedTracker
├── TMD-P3-003: ChatWindow Integration
└── TMD-P3-004: Tool Handler Extension

Phase 4 (Real-time)
├── TMD-P4-001: SSE Processing
└── TMD-P4-002: SignalR Broadcasts

Phase 5 (Polish)
├── TMD-P5-001: Error Recovery
├── TMD-P5-002: Performance
└── TMD-P5-003: Accessibility

Phase 6 (Testing)
├── TMD-P6-001: Unit Tests
├── TMD-P6-002: E2E Tests
└── TMD-P6-003: Documentation
```

## Implementation Schedule

### Sprint 1 (Week 1-2): Foundation
- Phase 1: Backend Infrastructure (TMD-P1-001 to TMD-P1-004)
- Phase 2: Core Components (TMD-P2-001 to TMD-P2-002)

### Sprint 2 (Week 3-4): Core Features
- Phase 2: Complete Components (TMD-P2-003 to TMD-P2-004)
- Phase 3: State & Tracker (TMD-P3-001 to TMD-P3-004)

### Sprint 3 (Week 5-6): Integration
- Phase 4: Real-time Sync (TMD-P4-001 to TMD-P4-002)
- Phase 5: Error & Performance (TMD-P5-001 to TMD-P5-002)

### Sprint 4 (Week 7-8): Polish
- Phase 5: Accessibility (TMD-P5-003)
- Phase 6: Testing & Docs (TMD-P6-001 to TMD-P6-003)

## Risk Mitigation

### Technical Risks
1. **Performance with large task lists**
   - Mitigation: Virtual scrolling, pagination
   - Fallback: Limit tasks to 100 per chat

2. **Real-time sync conflicts**
   - Mitigation: Version-based concurrency
   - Fallback: Server state wins, user notification

3. **Browser compatibility**
   - Mitigation: Progressive enhancement
   - Fallback: Basic HTML rendering

### Schedule Risks
1. **Backend delays**
   - Mitigation: Mock API for frontend development
   - Fallback: Local storage only in v1

2. **Complex state management**
   - Mitigation: Incremental store development
   - Fallback: Simplified state model

## Success Metrics

### Technical Metrics
- [ ] All unit tests passing (>80% coverage)
- [ ] All E2E tests passing
- [ ] Performance benchmarks met (<100ms render)
- [ ] Zero critical bugs in production

### User Metrics
- [ ] Task tracker used by >50% of active users
- [ ] <1% error rate for task operations
- [ ] Positive user feedback (>4.0 rating)
- [ ] No significant performance degradation

## Notes for Developers

1. **Start with Phase 1**: Backend must be ready before frontend
2. **Test continuously**: Write tests alongside implementation
3. **Use existing patterns**: Follow codebase conventions
4. **Document as you go**: Don't leave docs for the end
5. **Communicate blockers**: Raise issues early
6. **Consider mobile**: Test on mobile devices regularly
7. **Profile performance**: Use browser dev tools
8. **Review accessibility**: Test with screen readers