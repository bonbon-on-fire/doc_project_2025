# Feature Specification: Task Manager Display

## High-Level Overview

The Task Manager Display feature provides a visual interface for the LmDotNet TaskManager tool within the chat application. It renders task manager tool calls in the message stream while maintaining a pinned, read-only task tracker component above the message input box. This allows users to track AI progress on tasks throughout their conversation, with tasks persisted per chat session.

## High Level Requirements

- **Dual Display System**: Task manager tool calls appear both in the message stream AND update a pinned task tracker
- **Chat-Scoped Persistence**: Each chat maintains its own task list, persisted with the chat
- **Read-Only Tracking**: Pinned component is view-only for tracking LLM progress
- **AI-Driven Updates**: All task modifications happen through AI chat interactions
- **Visual Consistency**: Design integrates seamlessly with existing application style
- **Real-Time Synchronization**: Immediate updates as tool calls are processed

## Existing Solutions

### Current TaskManager Implementation
The TaskManager tool in LmDotNet (`submodules/LmDotnetTools/src/Misc/Utils/TaskManager.cs`) provides:
- Hierarchical task management (main tasks and subtasks)
- Status tracking (NotStarted, InProgress, Completed, Removed)
- Note management for context persistence
- Markdown output generation
- Function-based interface for LLM integration

### Current Message Rendering System
- **Renderer Registry**: Dynamic component registration based on message type
- **Message Router**: Routes messages to appropriate renderers with fallback support
- **Tool Call Rendering**: Existing patterns for displaying tool calls with YAML-formatted arguments
- **Collapsible Components**: Established patterns for expandable/collapsible message content

## Current Implementation

The application currently renders tool calls as individual messages in the stream using:
- `ToolCallRenderer.svelte` for individual tool calls
- `ToolsCallAggregateRenderer.svelte` for paired tool calls and results
- `CollapsibleMessageRenderer.svelte` as the base for collapsible messages

There is no current implementation of:
- Task-specific rendering
- Pinned task tracker component
- Task persistence per chat
- Task state synchronization

## Detailed Requirements

### Requirement 1: Task Manager Tool Call Rendering
**User Story**: As a user, I want to see task manager tool calls rendered in the message stream so I can understand what task operations the AI is performing.

#### Acceptance Criteria:
1. [ ] WHEN the AI calls a task manager function THEN it SHALL appear in the message stream as a specialized task manager message
2. [ ] WHEN rendering task manager tool calls THEN they SHALL display the function name, arguments, and result in a clear format
3. [ ] WHEN task manager messages are displayed THEN they SHALL be collapsible like other tool call messages
4. [ ] WHEN expanded THEN the message SHALL show full task operation details including task hierarchy and status changes
5. [ ] WHEN collapsed THEN the message SHALL show a summary (e.g., "Added task: Draft project plan")

### Requirement 2: Pinned Task Tracker Component
**User Story**: As a user, I want a persistent task tracker above the input box so I can always see the current state of my tasks.

#### Acceptance Criteria:
1. [ ] WHEN a chat has tasks THEN a pinned task tracker SHALL appear above the message input area
2. [ ] WHEN no tasks exist THEN the pinned component SHALL be hidden or show an empty state
3. [ ] WHEN collapsed THEN the tracker SHALL show task statistics (e.g., "3 tasks: 1 completed, 2 in progress")
4. [ ] WHEN expanded THEN the tracker SHALL display the full task hierarchy with status indicators
5. [ ] WHEN tasks are updated THEN the pinned tracker SHALL update in real-time without page refresh
6. [ ] WHEN switching between chats THEN the tracker SHALL show tasks for the current chat only

### Requirement 3: Task Persistence and Synchronization
**User Story**: As a user, I want my tasks to be saved with each chat so I can return to them later.

#### Acceptance Criteria:
1. [ ] WHEN tasks are created/modified THEN they SHALL be persisted to the database linked to the current chat
2. [ ] WHEN loading a chat THEN existing tasks SHALL be retrieved and displayed in the pinned tracker
3. [ ] WHEN the AI modifies tasks THEN changes SHALL be synchronized across all components immediately
4. [ ] WHEN multiple task operations occur in sequence THEN they SHALL be processed in order
5. [ ] WHEN a chat is deleted THEN its associated tasks SHALL also be removed

### Requirement 4: Real-Time Updates via SSE/SignalR
**User Story**: As a user, I want to see task updates happen in real-time as the AI processes them.

#### Acceptance Criteria:
1. [ ] WHEN the AI streams a task operation THEN the pinned tracker SHALL update progressively
2. [ ] WHEN task tool calls are in progress THEN a loading indicator SHALL be shown
3. [ ] WHEN task operations complete THEN the final state SHALL be reflected immediately
4. [ ] WHEN connection is lost THEN the tracker SHALL show the last known state
5. [ ] WHEN connection is restored THEN the tracker SHALL synchronize with the server state

## Technical Requirements

### Message Type and Routing
- **New Message Type**: `task_manager` for task-specific tool calls
- **Renderer Registration**: Register `TaskManagerRenderer` and `TaskManagerToolCallRenderer` in the renderer registry
- **Message Detection**: Identify task manager tool calls by function name prefix pattern
- **Routing Logic**: Update MessageRouter to route task manager messages appropriately

### State Management
- **Task Store**: New Svelte store for managing task state per chat
  ```typescript
  interface TaskState {
    chatId: string;
    tasks: TaskItem[];
    isLoading: boolean;
    lastUpdated: Date;
  }
  ```
- **Store Actions**: `loadTasks`, `updateTask`, `addTask`, `deleteTask`, `clearTasks`
- **Persistence Layer**: Sync with backend on each modification
- **Optimistic Updates**: Update UI immediately, rollback on server error

### Database Schema Updates
```sql
-- New table for task persistence
CREATE TABLE chat_tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  chat_id TEXT NOT NULL,
  task_data TEXT NOT NULL, -- JSON blob of task hierarchy
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  FOREIGN KEY (chat_id) REFERENCES chats(id) ON DELETE CASCADE
);

-- Index for efficient chat-based queries
CREATE INDEX idx_chat_tasks_chat_id ON chat_tasks(chat_id);
```

### API Endpoints
```typescript
// GET /api/chats/{chatId}/tasks - Retrieve tasks for a chat
// POST /api/chats/{chatId}/tasks - Update task state
// DELETE /api/chats/{chatId}/tasks - Clear all tasks
```

### SignalR/SSE Integration
- **Event Types**: 
  - `task_operation_start`
  - `task_operation_update`
  - `task_operation_complete`
  - `task_state_sync`
- **Payload Structure**:
  ```typescript
  interface TaskOperationEvent {
    chatId: string;
    operationType: 'add' | 'update' | 'delete' | 'list';
    taskData: any;
    timestamp: Date;
  }
  ```

## UI/UX Requirements

### Visual Design
- **Consistent Styling**: Match existing application design language
- **Color Coding**: 
  - Not Started: Gray/neutral
  - In Progress: Blue/active
  - Completed: Green/success
  - Removed: Red/strikethrough
- **Icons**: 
  - Task manager operations: Clipboard/checklist icon
  - Status indicators: Checkbox states matching TaskManager symbols
- **Typography**: Use existing font hierarchy and sizing

### Pinned Component Layout
```
┌─────────────────────────────────────────┐
│ Chat Header                             │
├─────────────────────────────────────────┤
│                                         │
│         Messages Area                   │
│                                         │
├─────────────────────────────────────────┤
│ ▼ Tasks (2/5 completed)     [Collapse]  │ <- Pinned Tracker (collapsed)
├─────────────────────────────────────────┤
│ Message Input Box                       │
└─────────────────────────────────────────┘

When expanded:
├─────────────────────────────────────────┤
│ ▼ Tasks                      [Collapse] │
│ ┌───────────────────────────────────┐   │
│ │ □ 1. Draft project plan           │   │
│ │   └─ ☑ 1.1 Outline sections      │   │
│ │ ☑ 2. Review requirements         │   │
│ │ ◻ 3. Implementation planning     │   │
│ └───────────────────────────────────┘   │
├─────────────────────────────────────────┤
│ Message Input Box                       │
└─────────────────────────────────────────┘
```

### Interaction Patterns
- **Collapse/Expand**: Click header or chevron to toggle
- **Hover Effects**: Subtle highlight on interactive elements
- **Keyboard Navigation**: Support Tab/Enter/Space for accessibility
- **Auto-Collapse**: Optionally collapse when >5 tasks to save space
- **Smooth Transitions**: Animate expand/collapse with slide effect

### Responsive Behavior
- **Mobile**: Stack tasks vertically, reduce padding
- **Tablet**: Maintain layout with adjusted spacing
- **Desktop**: Full layout with optimal spacing
- **Height Constraints**: Max height with internal scroll for many tasks

## Implementation Details

### New Components Required

#### 1. TaskManagerToolCallRenderer.svelte
```svelte
<!-- Renders task manager tool calls in the message stream -->
- Extends CollapsibleMessageRenderer
- Displays operation type, parameters, and result
- Shows before/after task state for context
- Integrates with existing tool call rendering patterns
```

#### 2. PinnedTaskTracker.svelte
```svelte
<!-- Pinned component above message input -->
- Collapsible header with task statistics
- Hierarchical task display when expanded
- Real-time updates via store subscription
- Empty state handling
- Loading/error states
```

#### 3. TaskListDisplay.svelte
```svelte
<!-- Reusable task list visualization -->
- Renders task hierarchy with status indicators
- Supports both full and compact views
- Note display for tasks with context
- Visual status indicators matching TaskManager
```

### Store Updates

#### taskManagerStore.ts
```typescript
import { writable, derived } from 'svelte/store';

interface TaskManagerState {
  tasks: Map<string, ChatTasks>; // chatId -> tasks
  activeChat: string | null;
  isLoading: boolean;
  error: string | null;
}

// Main store
export const taskManager = writable<TaskManagerState>({
  tasks: new Map(),
  activeChat: null,
  isLoading: false,
  error: null
});

// Derived store for current chat tasks
export const currentChatTasks = derived(
  [taskManager, currentChatId],
  ([$taskManager, $chatId]) => {
    return $taskManager.tasks.get($chatId) || null;
  }
);

// Actions
export const taskManagerActions = {
  loadTasks: async (chatId: string) => { /* ... */ },
  updateFromToolCall: (chatId: string, operation: any) => { /* ... */ },
  clearTasks: (chatId: string) => { /* ... */ }
};
```

### Integration Points

#### 1. Message Stream Integration
- Hook into `toolsAggregateMessageHandler.ts`
- Detect task manager functions by name pattern
- Route to specialized renderer
- Extract task state from tool results

#### 2. Chat Window Integration
```svelte
<!-- In ChatWindow.svelte -->
<div class="border-t border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
  {#if $currentChatTasks && $currentChatTasks.length > 0}
    <PinnedTaskTracker tasks={$currentChatTasks} chatId={$currentChatId} />
  {/if}
  <MessageInput ... />
</div>
```

#### 3. SSE Handler Updates
```typescript
// In SSE event processing
if (isTaskManagerToolCall(event)) {
  // Update task store
  taskManagerActions.updateFromToolCall(chatId, event.payload);
  // Continue normal tool call processing
  processToolCall(event);
}
```

#### 4. Backend Integration
- Add task persistence service
- Update ChatController with task endpoints
- Include task state in chat export/import
- Add task state to SignalR hub broadcasts

### Error Handling

- **Network Failures**: Show last known state with retry option
- **Invalid Operations**: Display error in message stream, maintain tracker state
- **Sync Conflicts**: Server state wins, notify user of changes
- **Storage Limits**: Implement task count limits per chat (e.g., max 100 tasks)

### Performance Considerations

- **Lazy Loading**: Load tasks only when chat is selected
- **Debounced Updates**: Batch rapid task changes
- **Virtual Scrolling**: For task lists with >50 items
- **Memoization**: Cache rendered task trees
- **Optimistic UI**: Update immediately, reconcile with server

### Testing Requirements

#### Unit Tests
- Task store operations
- Task state transformations
- Renderer component logic
- SSE event processing

#### Integration Tests
- End-to-end task creation flow
- Task persistence across sessions
- Real-time synchronization
- Chat switching with tasks

#### E2E Tests
- User creates tasks via AI
- Tasks persist after page reload
- Multiple users see synchronized tasks
- Task tracker interactions

## Migration and Rollout

### Phase 1: Backend Support
- Deploy database schema updates
- Add API endpoints
- Update SSE/SignalR events

### Phase 2: Frontend Components
- Deploy task manager renderers
- Add pinned tracker (hidden by default)
- Enable for beta users

### Phase 3: Full Release
- Enable for all users
- Add user preferences for tracker behavior
- Documentation and examples

## Future Enhancements

1. **Task Templates**: Pre-defined task structures for common workflows
2. **Task Export**: Export tasks to external formats (Markdown, JSON, CSV)
3. **Task Analytics**: Visualize task completion rates and patterns
4. **Collaborative Tasks**: Share task lists between users
5. **Task Notifications**: Alerts for task status changes
6. **External Integrations**: Sync with external task management tools
7. **Custom Task Types**: User-defined task categories and workflows

## Dependencies

- LmDotNet TaskManager tool (existing)
- Svelte stores and components
- SQLite database with Drizzle ORM
- SignalR/SSE infrastructure
- Existing renderer registry system

## Success Criteria

1. Task manager tool calls render correctly in message stream
2. Pinned tracker updates in real-time with tool calls
3. Tasks persist per chat across sessions
4. No performance degradation with up to 100 tasks
5. Mobile and desktop responsive design
6. Accessibility standards met (WCAG 2.1 AA)
7. 100% backward compatibility with existing chats