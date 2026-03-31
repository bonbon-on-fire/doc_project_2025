# Feature Specification: Collapsible Message State System

## High-Level Overview

The Collapsible Message State System provides an intelligent and user-friendly way to manage the display of AI agent conversations that involve multiple intermediate activities (reasoning, tool calls, tool responses) between user input and final agent response. The system automatically collapses verbose intermediate content while preserving access to detailed information when needed, creating a cleaner and more focused chat experience.

## High-Level Requirements

1. **Automatic State Management**: The system shall automatically manage expansion/collapse states based on message position, type, and activity status
2. **Informative Collapsed Views**: Each message type shall display meaningful preview information when collapsed
3. **Consistent User Experience**: All collapsible message types shall follow consistent interaction patterns
4. **Performance Optimization**: The system shall minimize re-renders and maintain smooth animations
5. **Accessibility Compliance**: All interactive elements shall be keyboard navigable and screen reader accessible
6. **Extensibility**: The system shall support easy addition of new message types without breaking existing functionality

## Existing Solutions

### Current Implementation
- **State Store**: Centralized `messageState.ts` manages expansion states and render phases
- **Message Router**: Dynamic component loading based on message type
- **Collapsible Renderer**: Reusable component providing collapse/expand UI
- **Type-Specific Renderers**: Individual components for text, reasoning, and tool calls

### Industry Patterns
- **Slack**: Accordion-style collapsible sections with chevron indicators
- **ChatGPT**: Thinking/reasoning states shown inline with ability to expand
- **Claude**: Artifact system with separate panels for code/documents
- **IDE Consoles**: Collapsible log entries with severity indicators

## Current Implementation Analysis

### Strengths
- Centralized state management through Svelte stores
- Reusable `CollapsibleMessageRenderer` component
- Dynamic renderer loading via registry pattern
- Keyboard accessibility support

### Weaknesses
- Inconsistent collapsed preview logic across message types
- No standardized preview format for tool calls
- Missing batch operation handling for multiple tool calls
- Unclear rules for auto-expand/collapse behavior
- Limited visual hierarchy in collapsed states

## Detailed Requirements

### Requirement 1: Unified Collapsed State Display
**User Story**: As a user, I want to quickly scan through a conversation and understand what happened in each collapsed message without expanding them.

#### Acceptance Criteria:
1. [ ] WHEN a message is collapsed THEN it SHALL display a type indicator icon
2. [ ] WHEN a message is collapsed THEN it SHALL show a one-line preview of content
3. [ ] WHEN a reasoning message is collapsed THEN it SHALL show first 60 characters of reasoning
4. [ ] WHEN a tool call is collapsed THEN it SHALL show "Called {tool_name}" or "Calling {n} tools"
5. [ ] WHEN a tool result is collapsed THEN it SHALL show "Result: {status}" or "{n} results"
6. [ ] WHEN preview text exceeds available width THEN it SHALL truncate with ellipsis

### Requirement 2: Intelligent Auto-Expansion Rules
**User Story**: As a user, I want the system to automatically show me relevant content while hiding verbose details that I can access on demand.

#### Acceptance Criteria:
1. [ ] WHEN a new message arrives THEN it SHALL be expanded by default
2. [ ] WHEN a message starts streaming THEN it SHALL expand automatically
3. [ ] WHEN streaming completes for reasoning messages THEN they SHALL collapse automatically
4. [ ] WHEN streaming completes for text messages THEN they SHALL remain expanded
5. [ ] WHEN a new message becomes latest THEN previous messages SHALL collapse
6. [ ] WHEN user manually toggles a message THEN auto-behavior SHALL be disabled for that message

### Requirement 3: Tool Call Aggregate Handling
**User Story**: As a user, I want to see multiple tool calls grouped together with clear visual separation and individual collapse control.

#### Acceptance Criteria:
1. [ ] WHEN multiple tool calls occur together THEN they SHALL be displayed in a single aggregate container
2. [ ] WHEN the aggregate is collapsed THEN it SHALL show "Executed {n} tools" with tool names
3. [ ] WHEN the aggregate is expanded THEN each tool call SHALL be individually collapsible
4. [ ] WHEN a tool is still executing THEN it SHALL show a loading indicator
5. [ ] WHEN a tool fails THEN it SHALL show error state in both collapsed and expanded views
6. [ ] WHEN results arrive asynchronously THEN they SHALL update without losing collapse state

### Requirement 4: Visual Hierarchy and Indicators
**User Story**: As a user, I want clear visual cues that help me understand message types, states, and available actions at a glance.

#### Acceptance Criteria:
1. [ ] WHEN a message is collapsible THEN it SHALL show a chevron indicator
2. [ ] WHEN hovering over collapsed content THEN it SHALL show hover state
3. [ ] WHEN a message type has a specific icon THEN it SHALL be displayed consistently
4. [ ] WHEN content is collapsed THEN background SHALL be subtly different from expanded
5. [ ] WHEN keyboard focus is on collapsible element THEN it SHALL show focus ring
6. [ ] WHEN message has error state THEN it SHALL show red indicator regardless of collapse state

### Requirement 5: Performance and Optimization
**User Story**: As a user, I want smooth animations and responsive interactions even with many messages in the conversation.

#### Acceptance Criteria:
1. [ ] WHEN toggling collapse state THEN animation SHALL complete within 200-300ms
2. [ ] WHEN multiple messages update THEN only affected components SHALL re-render
3. [ ] WHEN scrolling through messages THEN collapsed messages SHALL not trigger layout shifts
4. [ ] WHEN state updates occur THEN they SHALL be batched to minimize store updates
5. [ ] WHEN messages are removed from view THEN their state SHALL be cleaned up
6. [ ] WHEN navigating between chats THEN previous chat states SHALL be cleared

### Requirement 6: Accessibility and Keyboard Navigation
**User Story**: As a user with accessibility needs, I want to navigate and interact with collapsible messages using keyboard and screen readers.

#### Acceptance Criteria:
1. [ ] WHEN pressing Enter or Space on collapsed message THEN it SHALL expand
2. [ ] WHEN using screen reader THEN it SHALL announce "collapsed" or "expanded" state
3. [ ] WHEN navigating with Tab key THEN focus SHALL move to next collapsible element
4. [ ] WHEN element has focus THEN it SHALL show visible focus indicator
5. [ ] WHEN state changes THEN screen reader SHALL announce the change
6. [ ] WHEN content is collapsed THEN aria-expanded SHALL be "false"

## Technical Architecture

### Component Hierarchy
```
MessageRouter
├── TextRenderer (non-collapsible)
├── CollapsibleMessageRenderer (wrapper)
│   ├── ReasoningRenderer
│   ├── ToolsCallAggregateRenderer
│   │   └── ToolCallRouter (per tool)
│   └── CustomRenderers (future)
└── MessageBubble (fallback)
```

### State Management Architecture
```typescript
interface MessageState {
  expanded: boolean;
  renderPhase: 'initial' | 'streaming' | 'enhanced' | 'complete';
  hasManualOverride?: boolean; // Track user manual toggles
  collapsedPreview?: string;   // Cached preview text
}

interface CollapsibleConfig {
  messageType: string;
  autoCollapseOnComplete: boolean;
  autoExpandOnStream: boolean;
  previewLength: number;
  previewFormatter: (message: any) => string;
}
```

### Message Type Registry
```typescript
const collapsibleConfigs: Map<string, CollapsibleConfig> = new Map([
  ['reasoning', {
    messageType: 'reasoning',
    autoCollapseOnComplete: true,
    autoExpandOnStream: true,
    previewLength: 60,
    previewFormatter: (msg) => msg.reasoning?.substring(0, 60) + '...'
  }],
  ['tools_aggregate', {
    messageType: 'tools_aggregate',
    autoCollapseOnComplete: false,
    autoExpandOnStream: true,
    previewLength: 100,
    previewFormatter: (msg) => {
      const count = msg.toolCallPairs?.length || 0;
      const names = msg.toolCallPairs?.map(p => p.toolCall.name).join(', ');
      return `Executed ${count} tool${count !== 1 ? 's' : ''}: ${names}`;
    }
  }],
  ['tool_call', {
    messageType: 'tool_call',
    autoCollapseOnComplete: false,
    autoExpandOnStream: true,
    previewLength: 80,
    previewFormatter: (msg) => `Called ${msg.toolCall?.name || 'tool'}`
  }]
]);
```

## API/Interface Definitions

### Store API Extensions
```typescript
// Enhanced message state functions
export function updateMessageStateWithOverride(
  messageId: string, 
  updates: Partial<MessageState>,
  isManualAction: boolean = false
): void;

export function batchUpdateMessageStates(
  updates: Map<string, Partial<MessageState>>
): void;

export function getCollapsibleConfig(
  messageType: string
): CollapsibleConfig | undefined;

export function generatePreview(
  message: RichMessageDto
): string;
```

### Component Props Interface
```typescript
interface CollapsibleRendererProps {
  message: RichMessageDto;
  isLatest: boolean;
  expanded: boolean;
  renderPhase: RenderPhase;
  config?: CollapsibleConfig;
  onToggle?: (expanded: boolean, isManual: boolean) => void;
  onStateChange?: (state: Partial<MessageState>) => void;
}
```

### Event System
```typescript
// Custom events for collapse state changes
interface CollapseEvents {
  'collapse:toggle': { 
    messageId: string; 
    expanded: boolean; 
    isManual: boolean; 
  };
  'collapse:auto': { 
    messageId: string; 
    reason: 'streaming' | 'complete' | 'new-latest'; 
  };
  'collapse:batch': { 
    messageIds: string[]; 
    expanded: boolean; 
  };
}
```

## Edge Cases and Error Handling

### Edge Case 1: Rapid Message Updates
- **Scenario**: Multiple messages arrive in quick succession
- **Solution**: Debounce state updates with 50ms delay
- **Fallback**: Queue updates and process in batches

### Edge Case 2: Missing Renderer
- **Scenario**: Unknown message type received
- **Solution**: Fall back to MessageBubble with full content
- **Error Display**: Show warning icon with "Unknown message type"

### Edge Case 3: Streaming Interruption
- **Scenario**: Connection lost during streaming
- **Solution**: Maintain last known state, show reconnection indicator
- **Recovery**: Resume with expanded state when connection restored

### Edge Case 4: Very Long Content
- **Scenario**: Message content exceeds reasonable display limits
- **Solution**: Implement virtual scrolling for expanded content
- **Limit**: Cap preview at 200 characters, full content at 10,000

### Edge Case 5: Circular Tool Calls
- **Scenario**: Tool calls reference each other in a loop
- **Solution**: Track call depth, show warning after 5 levels
- **Display**: Flatten display with indentation levels

## Testing Strategy

### Unit Tests
```typescript
describe('MessageState Store', () => {
  test('should auto-collapse previous messages when new latest arrives');
  test('should preserve manual override through auto-operations');
  test('should batch multiple state updates efficiently');
  test('should generate appropriate previews for each message type');
});

describe('CollapsibleMessageRenderer', () => {
  test('should display correct preview when collapsed');
  test('should animate smoothly between states');
  test('should handle keyboard navigation correctly');
  test('should announce state changes to screen readers');
});
```

### Integration Tests
```typescript
describe('Message Flow Integration', () => {
  test('should handle complete conversation flow with mixed message types');
  test('should maintain state consistency during streaming');
  test('should recover gracefully from connection interruptions');
  test('should handle rapid successive messages without flickering');
});
```

### E2E Tests
```typescript
describe('Collapsible Message UX', () => {
  test('user can manually expand/collapse any message');
  test('reasoning auto-collapses after streaming completes');
  test('tool calls display appropriate loading states');
  test('keyboard navigation works throughout conversation');
  test('collapsed previews provide sufficient context');
});
```

### Performance Tests
- Measure render time with 100+ messages
- Verify smooth animations at 60fps
- Check memory usage with extended conversations
- Validate state update batching effectiveness

## Implementation Phases

### Phase 1: Core Infrastructure (Week 1)
- Enhance state store with new APIs
- Implement preview generation system
- Add manual override tracking

### Phase 2: Message Type Support (Week 2)
- Update reasoning renderer with new preview logic
- Implement tool call aggregate handling
- Standardize collapsed preview displays

### Phase 3: Polish and Optimization (Week 3)
- Add smooth animations
- Implement keyboard navigation
- Optimize performance with batching
- Add comprehensive error handling

### Phase 4: Testing and Documentation (Week 4)
- Complete unit and integration tests
- Perform accessibility audit
- Update user documentation
- Create developer guidelines

## Success Metrics

1. **User Engagement**: 80% of users interact with collapse/expand features
2. **Performance**: All animations complete within 300ms
3. **Accessibility**: WCAG 2.1 AA compliance achieved
4. **Error Rate**: Less than 0.1% of messages fail to render correctly
5. **Developer Satisfaction**: New message types can be added in under 1 hour

## Future Enhancements

1. **Bulk Operations**: "Collapse All" / "Expand All" buttons
2. **Persistence**: Remember user preferences across sessions
3. **Smart Previews**: AI-generated summaries for long content
4. **Nested Collapsibles**: Support for hierarchical message structures
5. **Custom Themes**: User-defined colors and animations
6. **Export Views**: Generate clean exports with collapsed state preserved