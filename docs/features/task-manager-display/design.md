# Task Manager Display - Technical Design Document

## Executive Summary

This document describes the technical design for integrating the LmDotNet TaskManager tool display into the chat application. The feature provides a dual-display system where task manager tool calls appear in the message stream and update a pinned task tracker above the message input area. Tasks are persisted per chat session and synchronized in real-time across all components.

## System Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Frontend (SvelteKit)                    │
├───────────────────────────────┬─────────────────────────────┤
│    Message Stream Renderer    │    Pinned Task Tracker      │
│  TaskManagerToolCallRenderer  │   PinnedTaskTracker.svelte  │
├───────────────────────────────┴─────────────────────────────┤
│                    Task Manager Store                        │
│              (taskManagerStore.ts + actions)                 │
├──────────────────────────────────────────────────────────────┤
│                  SSE/SignalR Event Handler                   │
│            (Extended toolsAggregateMessageHandler)           │
└────────────────────┬─────────────────────────────────────────┘
                     │
                     ↓ SSE/SignalR Events
┌────────────────────┴─────────────────────────────────────────┐
│                    Backend (ASP.NET 9.0)                     │
├──────────────────────────────────────────────────────────────┤
│                     Task Service Layer                       │
│                  ITaskService / TaskService                  │
├──────────────────────────────────────────────────────────────┤
│                    Task Storage Layer                        │
│                SqliteTaskStorage (EF Core)                   │
├──────────────────────────────────────────────────────────────┤
│                      SQLite Database                         │
│                    chat_tasks table                          │
└──────────────────────────────────────────────────────────────┘
```

### Component Relationships

```
ChatWindow.svelte
├── MessageList.svelte
│   └── MessageRouter.svelte
│       └── TaskManagerToolCallRenderer.svelte (NEW)
│           └── TaskListDisplay.svelte (NEW)
├── PinnedTaskTracker.svelte (NEW)
│   └── TaskListDisplay.svelte (NEW)
└── MessageInput.svelte
```

### Data Flow

1. **Tool Call Detection**:
   - AI calls task manager function
   - SSE event with tool call payload arrives
   - toolsAggregateMessageHandler detects task manager function

2. **State Update**:
   - Handler updates taskManagerStore with operation
   - Store triggers reactive updates to all subscribers
   - Store persists to backend via API

3. **UI Updates**:
   - Message stream shows tool call via TaskManagerToolCallRenderer
   - Pinned tracker updates via store subscription
   - Both components show synchronized state

4. **Persistence**:
   - Task state saved to database on each modification
   - Tasks loaded when chat is selected
   - Tasks deleted when chat is deleted

## Detailed Component Design

### TaskManagerToolCallRenderer.svelte

**Purpose**: Renders task manager tool calls in the message stream with collapsible details.

**Props Interface**:
```typescript
interface TaskManagerToolCallRendererProps {
  message: TaskManagerToolCallMessageDto;
  isStreaming: boolean;
  isExpanded?: boolean;
}

interface TaskManagerToolCallMessageDto extends MessageDto {
  toolCall: ToolCall;
  toolResult?: ToolCallResult;
  operation: TaskOperation;
  beforeState?: TaskState;
  afterState?: TaskState;
  messageType: 'task_manager_tool_call';
}

interface TaskOperation {
  type: 'add' | 'update' | 'delete' | 'list' | 'manage_notes';
  targetTask?: string;
  parameters: Record<string, any>;
}
```

**State Management**:
- Local expanded/collapsed state via messageState store
- Subscribes to taskManagerStore for context
- No direct task modifications (read-only)

**Event Handlers**:
- `toggleExpanded()`: Toggle collapsed/expanded view
- `copyOperation()`: Copy operation details to clipboard

**Rendering Logic**:
- Collapsed: Show operation summary (e.g., "Added task: Draft project plan")
- Expanded: Show full operation details, parameters, before/after state
- Use existing CollapsibleMessageRenderer patterns
- Apply task-specific styling and icons

### PinnedTaskTracker.svelte

**Purpose**: Persistent task display above message input, collapsible with real-time updates.

**Props Interface**:
```typescript
interface PinnedTaskTrackerProps {
  chatId: string;
}
```

**State Management**:
```typescript
// Local component state
let isExpanded = false;
let isLoading = false;
let error: string | null = null;

// Store subscriptions
$: tasks = $currentChatTasks;
$: taskStats = deriveTaskStats(tasks);

interface TaskStats {
  total: number;
  completed: number;
  inProgress: number;
  notStarted: number;
}
```

**Event Handlers**:
- `toggleExpanded()`: Expand/collapse tracker
- `handleReload()`: Force reload tasks from server
- `handleClearTasks()`: Request AI to clear all tasks (via chat)

**Lifecycle Hooks**:
```typescript
onMount(() => {
  // Load tasks for current chat
  if (chatId) {
    taskManagerActions.loadTasks(chatId);
  }
  
  // Restore expanded state from localStorage
  const stored = localStorage.getItem(`task-tracker-expanded-${chatId}`);
  isExpanded = stored === 'true';
});

onDestroy(() => {
  // Save expanded state
  localStorage.setItem(`task-tracker-expanded-${chatId}`, String(isExpanded));
});
```

**Error Boundaries**:
- Try-catch around store operations
- Fallback UI for loading/error states
- Graceful degradation if store unavailable

### TaskListDisplay.svelte

**Purpose**: Reusable component for rendering hierarchical task lists.

**Props Interface**:
```typescript
interface TaskListDisplayProps {
  tasks: TaskItem[];
  variant: 'full' | 'compact';
  showNotes?: boolean;
  maxHeight?: string;
}

interface TaskItem {
  id: string;
  title: string;
  status: 'NotStarted' | 'InProgress' | 'Completed' | 'Removed';
  subtasks?: TaskItem[];
  notes?: string[];
  level: number;
}
```

**Rendering Features**:
- Hierarchical display with indentation
- Status icons matching TaskManager symbols
- Collapsible subtasks for complex lists
- Note tooltips on hover
- Virtual scrolling for large lists (>50 items)

## Data Models

### TypeScript Interfaces

```typescript
// Task state stored in database
interface ChatTaskState {
  chatId: string;
  tasks: TaskItem[];
  lastUpdated: Date;
  version: number; // For optimistic concurrency
}

// Task manager store state
interface TaskManagerState {
  tasks: Map<string, ChatTaskState>; // chatId -> state
  activeChat: string | null;
  isLoading: boolean;
  error: string | null;
  syncStatus: 'idle' | 'syncing' | 'error';
}

// SSE event payloads
interface TaskOperationEvent {
  eventType: 'task_operation';
  chatId: string;
  messageId: string;
  operation: {
    type: string;
    function: string;
    arguments: any;
    result?: any;
  };
  taskState: TaskItem[];
  timestamp: Date;
}

// API request/response types
interface GetTasksResponse {
  chatId: string;
  tasks: TaskItem[];
  version: number;
}

interface UpdateTasksRequest {
  chatId: string;
  tasks: TaskItem[];
  version: number; // For optimistic concurrency
}

interface UpdateTasksResponse {
  success: boolean;
  tasks: TaskItem[];
  version: number;
  error?: string;
}
```

### Store Schema

```typescript
// taskManagerStore.ts
import { writable, derived, get } from 'svelte/store';
import type { ChatTaskState, TaskManagerState, TaskItem } from '$lib/types/tasks';

// Main store
function createTaskManagerStore() {
  const { subscribe, update, set } = writable<TaskManagerState>({
    tasks: new Map(),
    activeChat: null,
    isLoading: false,
    error: null,
    syncStatus: 'idle'
  });

  return {
    subscribe,
    
    // Load tasks for a chat
    async loadTasks(chatId: string) {
      update(s => ({ ...s, isLoading: true, error: null }));
      
      try {
        const response = await fetch(`/api/chats/${chatId}/tasks`);
        const data: GetTasksResponse = await response.json();
        
        update(s => ({
          ...s,
          tasks: new Map(s.tasks).set(chatId, {
            chatId,
            tasks: data.tasks,
            lastUpdated: new Date(),
            version: data.version
          }),
          isLoading: false
        }));
      } catch (error) {
        update(s => ({
          ...s,
          isLoading: false,
          error: 'Failed to load tasks'
        }));
      }
    },
    
    // Update from tool call
    updateFromToolCall(chatId: string, operation: any, newState: TaskItem[]) {
      update(s => {
        const tasks = new Map(s.tasks);
        const current = tasks.get(chatId);
        
        tasks.set(chatId, {
          chatId,
          tasks: newState,
          lastUpdated: new Date(),
          version: (current?.version ?? 0) + 1
        });
        
        return { ...s, tasks, syncStatus: 'syncing' };
      });
      
      // Async persist to backend
      this.persistTasks(chatId);
    },
    
    // Persist to backend
    async persistTasks(chatId: string) {
      const state = get(this).tasks.get(chatId);
      if (!state) return;
      
      try {
        const response = await fetch(`/api/chats/${chatId}/tasks`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            tasks: state.tasks,
            version: state.version
          })
        });
        
        const data: UpdateTasksResponse = await response.json();
        
        if (data.success) {
          update(s => ({ ...s, syncStatus: 'idle' }));
        } else {
          // Handle version conflict
          if (data.version !== state.version) {
            // Reload from server
            this.loadTasks(chatId);
          }
        }
      } catch (error) {
        update(s => ({ ...s, syncStatus: 'error' }));
      }
    },
    
    // Clear tasks
    clearTasks(chatId: string) {
      update(s => {
        const tasks = new Map(s.tasks);
        tasks.delete(chatId);
        return { ...s, tasks };
      });
    },
    
    // Set active chat
    setActiveChat(chatId: string | null) {
      update(s => ({ ...s, activeChat: chatId }));
    }
  };
}

export const taskManager = createTaskManagerStore();

// Derived stores
export const currentChatTasks = derived(
  [taskManager],
  ([$taskManager]) => {
    if (!$taskManager.activeChat) return null;
    return $taskManager.tasks.get($taskManager.activeChat)?.tasks || [];
  }
);

export const taskStats = derived(
  currentChatTasks,
  ($tasks) => {
    if (!$tasks) return null;
    
    const stats = {
      total: 0,
      completed: 0,
      inProgress: 0,
      notStarted: 0
    };
    
    const countTasks = (tasks: TaskItem[]) => {
      for (const task of tasks) {
        stats.total++;
        switch (task.status) {
          case 'Completed': stats.completed++; break;
          case 'InProgress': stats.inProgress++; break;
          case 'NotStarted': stats.notStarted++; break;
        }
        if (task.subtasks) {
          countTasks(task.subtasks);
        }
      }
    };
    
    countTasks($tasks);
    return stats;
  }
);
```

## Integration Design

### Tool Call Detection and Routing

**Location**: `client/src/lib/chat/handlers/toolsAggregateMessageHandler.ts`

```typescript
// Extension to existing handler
class ExtendedToolsAggregateMessageHandler extends ToolsAggregateMessageHandler {
  private readonly TASK_MANAGER_FUNCTIONS = [
    'add-task', 'update-task', 'delete-task', 'get-task',
    'manage-notes', 'list-notes', 'list-tasks', 'search-tasks'
  ];
  
  isTaskManagerCall(toolCall: ToolCall): boolean {
    const functionName = toolCall.function_name || toolCall.name || '';
    return this.TASK_MANAGER_FUNCTIONS.includes(functionName);
  }
  
  processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
    const snapshot = super.processChunk(messageId, envelope);
    
    // Check if this is a task manager tool call
    if (StreamChunkPayloadGuards.isToolCallUpdate(envelope.payload)) {
      const toolCall = envelope.payload.toolCall;
      if (this.isTaskManagerCall(toolCall)) {
        // Mark for special rendering
        snapshot.messageType = 'task_manager_tool_call';
        snapshot.metadata = {
          ...snapshot.metadata,
          isTaskManager: true,
          operation: this.extractOperation(toolCall)
        };
      }
    }
    
    return snapshot;
  }
  
  processComplete(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
    const dto = super.processComplete(messageId, envelope);
    
    // If this was a task manager operation, update the store
    if (dto.metadata?.isTaskManager && dto.toolResult) {
      const result = JSON.parse(dto.toolResult.result);
      if (result.tasks) {
        taskManager.updateFromToolCall(
          dto.chatId,
          dto.metadata.operation,
          result.tasks
        );
      }
    }
    
    return dto;
  }
}
```

### Chat Window Integration

**Location**: `client/src/lib/components/ChatWindow.svelte`

```svelte
<!-- Modified section before MessageInput -->
<div class="border-t border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
  <!-- Pinned Task Tracker -->
  {#if $currentChatId && $currentChatTasks && $currentChatTasks.length > 0}
    <PinnedTaskTracker chatId={$currentChatId} />
  {/if}
  
  <!-- Message Input -->
  <MessageInput
    on:send={handleSend}
    disabled={isSending && !$isStreaming}
    placeholder={$isStreaming ? 'AI is responding...' : 'Type a message...'}
  />
</div>
```

### Renderer Registration

**Location**: `client/src/lib/renderers/index.ts`

```typescript
import { rendererRegistry } from './RendererRegistry';
import TaskManagerToolCallRenderer from './TaskManagerToolCallRenderer.svelte';

// Register task manager renderer
rendererRegistry.register('task_manager_tool_call', {
  messageType: 'task_manager_tool_call',
  component: TaskManagerToolCallRenderer,
  supportsStreaming: true,
  supportsCollapse: true
});
```

## Database Design

### Server-Side Schema (SQLite)

```sql
-- New table for task persistence
CREATE TABLE chat_tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id TEXT NOT NULL,
  task_data TEXT NOT NULL, -- JSON blob of TaskItem[]
  version INTEGER NOT NULL DEFAULT 1,
  created_at_utc TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL,
  FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
);

-- Index for efficient chat-based queries
CREATE UNIQUE INDEX idx_chat_tasks_chat_id ON chat_tasks(chat_id);

-- Audit log for task operations (optional)
CREATE TABLE task_operations_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id TEXT NOT NULL,
  message_id TEXT NOT NULL,
  operation_type TEXT NOT NULL,
  operation_data TEXT NOT NULL, -- JSON
  timestamp_utc TEXT NOT NULL,
  FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
);

CREATE INDEX idx_task_operations_chat ON task_operations_log(chat_id, timestamp_utc DESC);
```

### Migration Strategy

1. Add tables via EF Core migration
2. Seed existing chats with empty task state
3. Version column for optimistic concurrency
4. JSON serialization for flexible schema evolution

## API Design

### Endpoints

#### GET /api/chats/{chatId}/tasks
**Purpose**: Retrieve tasks for a specific chat

**Response**:
```json
{
  "chatId": "chat-123",
  "tasks": [
    {
      "id": "1",
      "title": "Draft project plan",
      "status": "InProgress",
      "subtasks": [
        {
          "id": "1.1",
          "title": "Outline sections",
          "status": "Completed"
        }
      ],
      "notes": ["Remember to include timeline"]
    }
  ],
  "version": 3
}
```

#### POST /api/chats/{chatId}/tasks
**Purpose**: Update task state for a chat

**Request**:
```json
{
  "tasks": [...],
  "version": 3
}
```

**Response**:
```json
{
  "success": true,
  "tasks": [...],
  "version": 4
}
```

#### DELETE /api/chats/{chatId}/tasks
**Purpose**: Clear all tasks for a chat

**Response**:
```json
{
  "success": true
}
```

### Error Responses

```json
{
  "error": {
    "code": "VERSION_CONFLICT",
    "message": "Task state has been modified",
    "currentVersion": 5
  }
}
```

## SSE/SignalR Integration

### Event Types

```typescript
// Task operation started
{
  "eventType": "task_operation_start",
  "chatId": "chat-123",
  "messageId": "msg-456",
  "operation": {
    "type": "add",
    "function": "add-task",
    "arguments": {
      "title": "Draft project plan"
    }
  }
}

// Task operation completed
{
  "eventType": "task_operation_complete",
  "chatId": "chat-123",
  "messageId": "msg-456",
  "operation": {
    "type": "add",
    "function": "add-task",
    "result": {
      "success": true,
      "taskId": "1",
      "tasks": [...]
    }
  }
}

// Task state synchronization
{
  "eventType": "task_state_sync",
  "chatId": "chat-123",
  "tasks": [...],
  "version": 4
}
```

### Event Processing

```typescript
// In SSE event handler
function handleTaskEvents(event: SSEEvent) {
  switch (event.eventType) {
    case 'task_operation_start':
      // Show loading indicator in pinned tracker
      taskManager.setLoading(event.chatId, true);
      break;
      
    case 'task_operation_complete':
      // Update task state from result
      if (event.operation.result?.tasks) {
        taskManager.updateFromToolCall(
          event.chatId,
          event.operation,
          event.operation.result.tasks
        );
      }
      taskManager.setLoading(event.chatId, false);
      break;
      
    case 'task_state_sync':
      // Force sync with server state
      taskManager.syncState(event.chatId, event.tasks, event.version);
      break;
  }
}
```

## Testing Strategy

### Unit Tests

**Component Tests**:
```typescript
// TaskManagerToolCallRenderer.test.ts
describe('TaskManagerToolCallRenderer', () => {
  test('renders collapsed view with operation summary', () => {
    const message = createMockTaskMessage('add', 'Draft project plan');
    const { getByText } = render(TaskManagerToolCallRenderer, {
      props: { message, isExpanded: false }
    });
    expect(getByText('Added task: Draft project plan')).toBeInTheDocument();
  });
  
  test('expands to show full operation details', async () => {
    const message = createMockTaskMessage('add', 'Draft project plan');
    const { getByRole, getByText } = render(TaskManagerToolCallRenderer, {
      props: { message, isExpanded: false }
    });
    
    await fireEvent.click(getByRole('button', { name: /expand/i }));
    expect(getByText(/function_name: add-task/)).toBeInTheDocument();
  });
});

// PinnedTaskTracker.test.ts
describe('PinnedTaskTracker', () => {
  test('shows task statistics when collapsed', () => {
    const tasks = createMockTasks(5, 2, 2, 1); // 5 total, 2 complete, 2 in progress, 1 not started
    mockTaskStore.set({ tasks: new Map([['chat-1', { tasks }]]) });
    
    const { getByText } = render(PinnedTaskTracker, {
      props: { chatId: 'chat-1' }
    });
    
    expect(getByText('Tasks (2/5 completed)')).toBeInTheDocument();
  });
  
  test('updates in real-time when tasks change', async () => {
    const { rerender } = render(PinnedTaskTracker, {
      props: { chatId: 'chat-1' }
    });
    
    // Simulate task update via store
    taskManager.updateFromToolCall('chat-1', mockOperation, updatedTasks);
    
    await tick();
    expect(screen.getByText('Tasks (3/5 completed)')).toBeInTheDocument();
  });
});
```

**Store Tests**:
```typescript
// taskManagerStore.test.ts
describe('taskManagerStore', () => {
  test('loads tasks from API', async () => {
    fetchMock.mockResponseOnce(JSON.stringify({
      tasks: mockTasks,
      version: 1
    }));
    
    await taskManager.loadTasks('chat-1');
    
    const state = get(taskManager);
    expect(state.tasks.get('chat-1')?.tasks).toEqual(mockTasks);
  });
  
  test('handles optimistic concurrency conflicts', async () => {
    // Set initial state
    taskManager.updateFromToolCall('chat-1', op1, tasks1);
    
    // Simulate version conflict response
    fetchMock.mockResponseOnce(JSON.stringify({
      success: false,
      error: 'VERSION_CONFLICT',
      version: 3
    }));
    
    await taskManager.persistTasks('chat-1');
    
    // Should trigger reload
    expect(fetchMock).toHaveBeenCalledWith('/api/chats/chat-1/tasks');
  });
});
```

### Integration Tests

```typescript
// E2E test for task creation flow
test('creates and displays task via AI interaction', async ({ page }) => {
  // Navigate to chat
  await page.goto('/chat');
  
  // Send message to create task
  await page.fill('[data-testid="message-input"]', 'Create a task to draft project plan');
  await page.click('[data-testid="send-button"]');
  
  // Wait for AI response with tool call
  await page.waitForSelector('[data-testid="task-manager-tool-call"]');
  
  // Verify task appears in pinned tracker
  const tracker = page.locator('[data-testid="pinned-task-tracker"]');
  await expect(tracker).toContainText('Draft project plan');
  
  // Verify task persists after page reload
  await page.reload();
  await expect(tracker).toContainText('Draft project plan');
});

// Test real-time synchronization
test('synchronizes tasks across multiple clients', async ({ page, context }) => {
  // Open two browser tabs
  const page2 = await context.newPage();
  
  // Both navigate to same chat
  await page.goto('/chat/chat-123');
  await page2.goto('/chat/chat-123');
  
  // Create task in first tab
  await page.fill('[data-testid="message-input"]', 'Add task: Review code');
  await page.click('[data-testid="send-button"]');
  
  // Verify task appears in second tab
  const tracker2 = page2.locator('[data-testid="pinned-task-tracker"]');
  await expect(tracker2).toContainText('Review code', { timeout: 5000 });
});
```

### Performance Tests

```typescript
describe('Performance', () => {
  test('handles large task lists efficiently', async () => {
    const largeTasks = generateMockTasks(100);
    
    const start = performance.now();
    render(TaskListDisplay, {
      props: { tasks: largeTasks, variant: 'full' }
    });
    const renderTime = performance.now() - start;
    
    expect(renderTime).toBeLessThan(100); // Should render in <100ms
  });
  
  test('virtual scrolling activates for >50 tasks', () => {
    const manyTasks = generateMockTasks(60);
    const { container } = render(TaskListDisplay, {
      props: { tasks: manyTasks }
    });
    
    const virtualScroller = container.querySelector('[data-virtual-scroll]');
    expect(virtualScroller).toBeInTheDocument();
  });
});
```

## Error Handling

### Network Failures
- Store last known state in localStorage
- Show stale indicator with retry button
- Queue operations for retry when connection restored

### Invalid Operations
- Display error in message stream
- Maintain current tracker state
- Log error details for debugging

### Sync Conflicts
- Server state wins on version conflicts
- Notify user of external changes
- Automatic reload with visual indication

### Storage Limits
- Maximum 100 tasks per chat
- Warn at 80 tasks
- Prevent adding beyond limit
- Archive old completed tasks

## Performance Considerations

### Optimization Strategies

1. **Lazy Loading**:
   - Load tasks only when chat selected
   - Defer tracker rendering until tasks exist
   - Use intersection observer for visibility

2. **Debounced Updates**:
   ```typescript
   const debouncedPersist = debounce(
     (chatId: string) => taskManager.persistTasks(chatId),
     1000
   );
   ```

3. **Virtual Scrolling**:
   - Activate for lists >50 items
   - Render only visible items
   - Maintain scroll position on updates

4. **Memoization**:
   ```typescript
   const memoizedTaskTree = useMemo(
     () => buildTaskTree(tasks),
     [tasks]
   );
   ```

5. **Optimistic UI**:
   - Update UI immediately
   - Reconcile with server response
   - Rollback on failure

### Performance Metrics

- Initial render: <50ms
- Task update: <20ms
- Large list (100 items): <100ms render
- Memory usage: <5MB for 100 tasks
- Network payload: <10KB per sync

## Security Considerations

### Input Validation
- Sanitize task titles and notes
- Validate task structure on server
- Prevent XSS in rendered content

### Authorization
- Verify chat ownership before task access
- Rate limit task operations
- Audit log sensitive operations

### Data Privacy
- Tasks scoped to individual chats
- No cross-chat task access
- Secure deletion with chat

## Accessibility

### WCAG 2.1 AA Compliance

1. **Keyboard Navigation**:
   - Tab through interactive elements
   - Enter/Space to expand/collapse
   - Escape to close expanded views

2. **Screen Reader Support**:
   ```svelte
   <button
     aria-expanded={isExpanded}
     aria-controls="task-list"
     aria-label={`Tasks: ${completed} of ${total} completed`}
   >
   ```

3. **Focus Management**:
   - Restore focus after operations
   - Trap focus in modals
   - Visual focus indicators

4. **Color Contrast**:
   - Minimum 4.5:1 for normal text
   - 3:1 for large text
   - Don't rely solely on color

## Implementation Phases

### Phase 1: Backend Infrastructure (Sprint 1)
- Database schema and migrations
- Task storage service
- API endpoints
- Basic SSE events

### Phase 2: Core Components (Sprint 2)
- TaskManagerToolCallRenderer
- TaskListDisplay component
- Renderer registration
- Message stream integration

### Phase 3: Pinned Tracker (Sprint 3)
- PinnedTaskTracker component
- Task store implementation
- Chat window integration
- Real-time updates

### Phase 4: Polish & Testing (Sprint 4)
- Error handling
- Performance optimization
- Comprehensive testing
- Documentation

## Rollback Plan

### Feature Flags
```typescript
const FEATURE_FLAGS = {
  TASK_MANAGER_DISPLAY: process.env.ENABLE_TASK_MANAGER === 'true'
};
```

### Gradual Rollout
1. Internal testing (1 week)
2. Beta users (5%)
3. Staged rollout (25%, 50%, 100%)
4. Monitor metrics at each stage

### Rollback Triggers
- Error rate >1%
- Performance degradation >20%
- User complaints >10
- Data corruption detected

## Monitoring & Metrics

### Key Metrics
- Task operation success rate
- Average response time
- Task sync conflicts
- User engagement with tracker
- Task completion rates

### Logging
```typescript
logger.info('Task operation', {
  chatId,
  operation: operationType,
  taskCount: tasks.length,
  duration: endTime - startTime
});
```

### Alerts
- Task sync failures >10/min
- API response time >1s
- Database connection errors
- Memory usage >100MB

## Conclusion

This design provides a robust, scalable implementation of the Task Manager Display feature. It leverages existing patterns in the codebase while introducing minimal complexity. The phased approach allows for incremental delivery with clear milestones and rollback capabilities.

Key success factors:
- Reuse of existing renderer infrastructure
- Clean separation of concerns
- Real-time synchronization
- Comprehensive error handling
- Performance optimization
- Accessibility compliance