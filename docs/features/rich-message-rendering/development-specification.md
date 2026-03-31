# Development Specification: Rich Message Rendering

## Architecture Overview

The Rich Message Rendering system follows a **Component-Strategy-Observer** architectural pattern that enables incremental development while maintaining clean separation of concerns. The system is designed around core abstractions that allow each implementation phase to build upon previous work without requiring architectural refactoring.

### System Architecture Diagram

```mermaid
graph TB
    %% External Data Sources
    SSE[Server-Sent Events] --> MH[MessageHandler]
    LMD[LmDotnetTools] --> MH
    
    %% Core System
    MH --> MR[MessageRouter]
    MR --> RR[RendererRegistry]
    
    %% State Management
    MS[MessageState Store] --> MR
    MSC[MessageStateController] --> MS
    
    %% Renderer Components
    RR --> TR[TextRenderer]
    RR --> RER[ReasoningRenderer] 
    RR --> TCR[ToolCallRenderer]
    RR --> TRR[ToolResultRenderer]
    RR --> UR[UsageRenderer]
    
    %% Shared Components
    EC[ExpandableContainer] --> TR
    EC --> RER
    EC --> TCR
    EC --> TRR
    
    %% Streaming & Enhancement
    SH[StreamingHandler] --> TR
    MDP[Marked + DOMPurify] --> TR
    PJS[Prism.js] --> TR
    
    %% Custom Renderers
    CRP[Custom Renderer Plugins] --> TCR
    CRP --> TRR
    
    %% UI Output
    TR --> UI[User Interface]
    RER --> UI
    TCR --> UI
    TRR --> UI
    UR --> UI
    
    %% Styling
    CSS[CSS Themes] --> UI
    
    classDef core fill:#e1f5fe
    classDef renderer fill:#f3e5f5
    classDef shared fill:#e8f5e8
    classDef external fill:#fff3e0
    
    class MR,RR,MS,MSC core
    class TR,RER,TCR,TRR,UR renderer
    class EC,SH,MDP,PJS shared
    class SSE,LMD,CRP external
```

### Core Design Principles

1. **Interface Segregation**: Small, focused interfaces for each concern
2. **Progressive Enhancement**: Each phase adds capabilities without breaking existing functionality
3. **Composition Over Inheritance**: Components compose smaller, focused behaviors
4. **Dependency Inversion**: High-level modules depend on abstractions, not concretions
5. **Single Responsibility**: Each component has one clear purpose

## Core Abstractions

### Component Interaction Flow

```mermaid
sequenceDiagram
    participant U as User
    participant MR as MessageRouter
    participant RR as RendererRegistry
    participant MS as MessageState
    participant TR as TextRenderer
    participant EC as ExpandableContainer
    participant SH as StreamingHandler
    
    U->>MR: New Message Received
    MR->>RR: getRenderer(messageType)
    RR-->>MR: TextRenderer
    MR->>MS: updateMessageState(latest=true)
    MS-->>EC: auto-collapse previous messages
    MR->>TR: render(message, isLatest=true)
    
    alt Message is streaming
        TR->>SH: processUpdate(content, delta)
        SH-->>TR: bufferedContent
        TR->>TR: renderMarkdown(bufferedContent)
    else Message complete
        TR->>TR: renderFinalContent()
        TR->>TR: applySyntaxHighlighting()
    end
    
    TR-->>U: Rendered Component
    
    Note over U,SH: User interactions
    U->>EC: click expand/collapse
    EC->>MS: updateMessageState(expanded)
    MS-->>EC: state updated
```

### Interface Hierarchy

```mermaid
classDiagram
    class MessageRenderer~T~ {
        <<interface>>
        +messageType: string
        +supportsStreaming: boolean
        +supportsCollapse: boolean
        +render(message: T, context: RenderContext) Promise~ComponentInstance~
        +updateStream?(message: T, delta: string) Promise~void~
        +onCollapse?() void
        +onExpand?() void
    }
    
    class StreamingHandler {
        <<interface>>
        +bufferSize: number
        +shouldBuffer: boolean
        +processUpdate(content: string, delta: string) StreamingUpdate
        +shouldRender(update: StreamingUpdate) boolean
        +createRenderableContent(update: StreamingUpdate) string
    }
    
    class ExpandableComponent {
        <<interface>>
        +isCollapsible: boolean
        +defaultExpanded: boolean
        +expand() Promise~void~
        +collapse() Promise~void~
        +toggle() Promise~void~
        +onStateChange?(expanded: boolean) void
    }
    
    class CustomRenderer~T~ {
        <<interface>>
        +rendererName: string
        +supportedTypes: string[]
        +canRender(data: T) boolean
        +render(data: T, context: RenderContext) Promise~ComponentInstance~
        +getPreviewContent?(data: T) string
    }
    
    class TextRenderer {
        +messageType: "text"
        +supportsStreaming: true
        +supportsCollapse: false
        +render(message, context)
        +updateStream(message, delta)
    }
    
    class ReasoningRenderer {
        +messageType: "reasoning"
        +supportsStreaming: false
        +supportsCollapse: true
        +render(message, context)
        +onCollapse()
        +onExpand()
    }
    
    class ToolCallRenderer {
        +messageType: "tool_call"
        +supportsStreaming: false
        +supportsCollapse: true
        +render(message, context)
        +onCollapse()
        +onExpand()
    }
    
    MessageRenderer <|.. TextRenderer
    MessageRenderer <|.. ReasoningRenderer
    MessageRenderer <|.. ToolCallRenderer
    StreamingHandler <|.. TextStreamingHandler
    ExpandableComponent <|.. ExpandableContainer
    CustomRenderer <|.. FileRenderer
    CustomRenderer <|.. ImageRenderer
```

### 1. MessageRenderer Interface

The foundation interface that all message renderers implement:

```typescript
interface MessageRenderer<T extends MessageDto> {
  readonly messageType: string;
  readonly supportsStreaming: boolean;
  readonly supportsCollapse: boolean;
  
  render(message: T, context: RenderContext): Promise<ComponentInstance>;
  updateStream?(message: T, delta: string): Promise<void>;
  onCollapse?(): void;
  onExpand?(): void;
}
```

### 2. StreamingHandler Interface

Manages incremental content updates during streaming:

```typescript
interface StreamingHandler {
  readonly bufferSize: number;
  readonly shouldBuffer: boolean;
  
  processUpdate(content: string, delta: string): StreamingUpdate;
  shouldRender(update: StreamingUpdate): boolean;
  createRenderableContent(update: StreamingUpdate): string;
}
```

### 3. ExpandableComponent Interface

Handles expand/collapse behavior:

```typescript
interface ExpandableComponent {
  readonly isCollapsible: boolean;
  readonly defaultExpanded: boolean;
  
  expand(): Promise<void>;
  collapse(): Promise<void>;
  toggle(): Promise<void>;
  
  onStateChange?(expanded: boolean): void;
}
```

### 4. CustomRenderer Interface

Plugin interface for extensible rendering:

```typescript
interface CustomRenderer<T = any> {
  readonly rendererName: string;
  readonly supportedTypes: string[];
  
  canRender(data: T): boolean;
  render(data: T, context: RenderContext): Promise<ComponentInstance>;
  getPreviewContent?(data: T): string;
}
```

## Component Design

### MessageRouter Component

**Purpose**: Central routing component that delegates to appropriate renderers
**Phase**: 1 (MVP Foundation)

```svelte
<!-- MessageRouter.svelte -->
<script lang="ts">
  import { getRenderer } from './renderers/RendererRegistry';
  import type { MessageDto } from './types';
  
  export let message: MessageDto;
  export let isLatest: boolean = false;
  export let onStateChange: (messageId: string, state: MessageState) => void;
  
  $: renderer = getRenderer(message.messageType);
  $: Component = renderer.component;
</script>

<svelte:component 
  this={Component} 
  {message} 
  {isLatest}
  {onStateChange}
  renderer={renderer}
/>
```

**Key Features**:
- Dynamic component resolution
- State change propagation
- Renderer registry integration
- Clean prop passing

### ExpandableContainer Component

**Purpose**: Shared expand/collapse behavior across message types
**Phase**: 1 (MVP Foundation)

```svelte
<!-- ExpandableContainer.svelte -->
<script lang="ts">
  import { fly } from 'svelte/transition';
  import { quintOut } from 'svelte/easing';
  
  export let expanded: boolean = true;
  export let collapsible: boolean = true;
  export let title: string = '';
  export let isLatest: boolean = false;
  
  // Auto-collapse logic
  $: if (!isLatest && collapsible) {
    expanded = false;
  }
  
  function toggle() {
    if (collapsible) {
      expanded = !expanded;
    }
  }
</script>

<div class="expandable-container" class:expanded class:collapsible>
  {#if collapsible}
    <button 
      class="header" 
      on:click={toggle}
      aria-expanded={expanded}
      aria-controls="content"
    >
      <span class="toggle-icon" class:expanded>
        {expanded ? '▼' : '▶'}
      </span>
      <span class="title">{title}</span>
    </button>
  {/if}
  
  {#if expanded}
    <div 
      id="content"
      class="content"
      transition:fly="{{ y: -10, duration: 200, easing: quintOut }}"
    >
      <slot />
    </div>
  {/if}
</div>
```

### TextRenderer Component

**Purpose**: Handles text messages with markdown and streaming
**Phase**: 2-3 (Enhanced Text + Syntax Highlighting)

```svelte
<!-- TextRenderer.svelte -->
<script lang="ts">
  import { marked } from 'marked';
  import DOMPurify from 'dompurify';
  import { createStreamingHandler } from '../streaming/TextStreamingHandler';
  import { onMount, afterUpdate } from 'svelte';
  import Prism from 'prismjs';
  
  export let message: TextMessageDto;
  export let isLatest: boolean = false;
  
  let contentElement: HTMLElement;
  let renderedContent: string = '';
  
  const streamingHandler = createStreamingHandler({
    bufferSize: 50, // ~sentence length
    shouldBuffer: true
  });
  
  $: {
    if (message.isStreaming && isLatest) {
      handleStreamingUpdate();
    } else {
      renderFinalContent();
    }
  }
  
  async function handleStreamingUpdate() {
    const update = streamingHandler.processUpdate(renderedContent, message.content);
    if (streamingHandler.shouldRender(update)) {
      renderedContent = DOMPurify.sanitize(
        marked.parse(streamingHandler.createRenderableContent(update))
      );
    }
  }
  
  async function renderFinalContent() {
    renderedContent = DOMPurify.sanitize(marked.parse(message.content));
  }
  
  afterUpdate(() => {
    if (contentElement && !message.isStreaming) {
      Prism.highlightAllUnder(contentElement);
    }
  });
</script>

<div class="text-message" class:streaming={message.isStreaming}>
  <div bind:this={contentElement} class="content prose">
    {@html renderedContent}
  </div>
</div>
```

### ReasoningRenderer Component

**Purpose**: Collapsible reasoning display with auto-collapse
**Phase**: 4 (Full Message Types)

```svelte
<!-- ReasoningRenderer.svelte -->
<script lang="ts">
  import ExpandableContainer from '../shared/ExpandableContainer.svelte';
  import type { ReasoningMessageDto } from '../types';
  
  export let message: ReasoningMessageDto;
  export let isLatest: boolean = false;
  
  $: expanded = isLatest;
</script>

<ExpandableContainer 
  {expanded}
  collapsible={true}
  title="Reasoning"
  {isLatest}
>
  <div class="reasoning-content">
    <pre class="reasoning-text">{message.content}</pre>
  </div>
</ExpandableContainer>

<style>
  .reasoning-content {
    max-height: 300px;
    overflow-y: auto;
    padding: 1rem;
    background: var(--surface-variant);
    border-radius: 0.5rem;
  }
  
  .reasoning-text {
    white-space: pre-wrap;
    font-family: var(--font-mono);
    font-size: 0.875rem;
    line-height: 1.5;
    margin: 0;
  }
</style>
```

## State Management Architecture

### Message State Flow

```mermaid
stateDiagram-v2
    [*] --> Initial: Message Created
    
    Initial --> Streaming: isStreaming = true
    Initial --> Complete: isStreaming = false
    
    Streaming --> StreamingExpanded: isLatest = true
    Streaming --> StreamingCollapsed: isLatest = false
    
    StreamingExpanded --> Enhanced: Streaming Complete
    StreamingCollapsed --> Enhanced: Streaming Complete
    
    Enhanced --> Complete: Enhancement Done
    
    Complete --> Expanded: isLatest = true
    Complete --> Collapsed: isLatest = false
    
    Expanded --> Collapsed: User Collapse / New Latest
    Collapsed --> Expanded: User Expand
    
    state Streaming {
        [*] --> BufferingContent
        BufferingContent --> RenderingChunk: Buffer Full
        RenderingChunk --> BufferingContent: More Content
        RenderingChunk --> [*]: Stream Complete
    }
    
    state Enhanced {
        [*] --> ApplyingMarkdown
        ApplyingMarkdown --> ApplyingSyntaxHighlighting
        ApplyingSyntaxHighlighting --> [*]
    }
```

### Expand/Collapse State Management

```mermaid
graph LR
    subgraph "Message State Store"
        MS[MessageStates]
        LM[LatestMessageId]
        AC[AutoCollapse Logic]
    end
    
    subgraph "Message Components"
        M1[Message 1<br/>Expanded]
        M2[Message 2<br/>Collapsed]
        M3[Message 3<br/>Latest/Expanded]
    end
    
    subgraph "User Actions"
        UA[User Clicks]
        NM[New Message]
    end
    
    MS --> M1
    MS --> M2
    MS --> M3
    
    LM --> AC
    AC --> MS
    
    UA --> MS
    NM --> LM
    
    style M3 fill:#e8f5e8
    style M1 fill:#f3e5f5
    style M2 fill:#f3e5f5
```

### MessageState Store

Centralized state management for message rendering states:

```typescript
// stores/messageState.ts
import { writable, derived } from 'svelte/store';

interface MessageState {
  expanded: boolean;
  isLatest: boolean;
  isStreaming: boolean;
  renderPhase: 'initial' | 'streaming' | 'enhanced' | 'complete';
}

interface MessageStates {
  [messageId: string]: MessageState;
}

export const messageStates = writable<MessageStates>({});

export const latestMessageId = writable<string | null>(null);

// Derived store for auto-collapse logic
export const shouldAutoCollapse = derived(
  [messageStates, latestMessageId],
  ([$states, $latestId]) => {
    return (messageId: string) => {
      const state = $states[messageId];
      return state && messageId !== $latestId && state.expanded;
    };
  }
);

// Actions
export function updateMessageState(messageId: string, updates: Partial<MessageState>) {
  messageStates.update(states => ({
    ...states,
    [messageId]: { ...states[messageId], ...updates }
  }));
}

export function setLatestMessage(messageId: string) {
  latestMessageId.set(messageId);
  
  // Auto-collapse previous messages
  messageStates.update(states => {
    const newStates = { ...states };
    Object.keys(newStates).forEach(id => {
      if (id !== messageId && newStates[id].expanded) {
        newStates[id] = { ...newStates[id], expanded: false, isLatest: false };
      }
    });
    
    // Ensure latest message is expanded
    if (newStates[messageId]) {
      newStates[messageId] = { ...newStates[messageId], expanded: true, isLatest: true };
    }
    
    return newStates;
  });
}
```

## Phase Implementation Plans

### Implementation Timeline Overview

```mermaid
gantt
    title Rich Message Rendering Implementation Timeline
    dateFormat  YYYY-MM-DD
    section Phase 1: Foundation
    Core Abstractions       :p1-1, 2025-08-07, 2d
    MessageRouter          :p1-2, after p1-1, 1d
    ExpandableContainer    :p1-3, after p1-2, 2d
    State Management       :p1-4, after p1-3, 2d
    Basic Text Renderer    :p1-5, after p1-4, 1d
    
    section Phase 2: Enhanced Text
    Marked.js Integration  :p2-1, after p1-5, 2d
    Streaming Handler      :p2-2, after p2-1, 3d
    Enhanced TextRenderer  :p2-3, after p2-2, 2d
    Mobile Optimizations   :p2-4, after p2-3, 1d
    
    section Phase 3: Syntax Highlighting
    Prism.js Integration   :p3-1, after p2-4, 2d
    Copy-to-Clipboard      :p3-2, after p3-1, 1d
    Performance Optimization :p3-3, after p3-2, 2d
    
    section Phase 4: Message Types
    ReasoningRenderer      :p4-1, after p3-3, 2d
    ToolCallRenderer       :p4-2, after p4-1, 2d
    ToolResultRenderer     :p4-3, after p4-2, 2d
    UsageRenderer          :p4-4, after p4-3, 1d
    
    section Phase 5: Advanced Features
    Plugin System          :p5-1, after p4-4, 3d
    Advanced Animations    :p5-2, after p5-1, 2d
    Performance Tuning     :p5-3, after p5-2, 2d
```

### Streaming Data Flow

```mermaid
flowchart TD
    SSE[Server-Sent Events] --> |Raw Delta| MH[MessageHandler]
    MH --> |Message + Delta| SH[StreamingHandler]
    
    subgraph "Streaming Processing"
        SH --> |Check Buffer| BF{Buffer Full?}
        BF -->|No| AB[Add to Buffer]
        BF -->|Yes| PC[Process Chunk]
        AB --> BF
        PC --> |Buffered Content| MR[Markdown Renderer]
    end
    
    MR --> |Safe HTML| DP[DOMPurify]
    DP --> |Sanitized HTML| UI[Update UI]
    
    subgraph "Performance Optimization"
        UI --> |Check Frame Budget| FB{<16ms Available?}
        FB -->|Yes| RN[Render Now]
        FB -->|No| DF[Defer to Next Frame]
        DF --> RN
    end
    
    RN --> |Visual Update| USER[User Sees Content]
    
    style SH fill:#e1f5fe
    style MR fill:#f3e5f5
    style DP fill:#e8f5e8
    style USER fill:#fff3e0
```

### Component Lifecycle

```mermaid
sequenceDiagram
    participant LC as LifecycleController
    participant MC as MessageComponent
    participant SH as StreamingHandler
    participant MR as MarkdownRenderer
    participant PH as PrismHighlighter
    
    Note over LC,PH: Component Creation
    LC->>MC: onMount()
    MC->>SH: initialize()
    MC->>MR: initialize()
    
    Note over LC,PH: Streaming Phase
    loop While Streaming
        LC->>MC: updateContent(delta)
        MC->>SH: processUpdate(delta)
        SH-->>MC: shouldRender?
        alt Should Render
            MC->>MR: renderMarkdown(buffered)
            MR-->>MC: safeHTML
            MC->>MC: updateDOM()
        end
    end
    
    Note over LC,PH: Enhancement Phase
    LC->>MC: onStreamComplete()
    MC->>MR: renderFinal()
    MC->>PH: highlightCode()
    PH-->>MC: highlighted
    
    Note over LC,PH: Cleanup
    LC->>MC: onDestroy()
    MC->>SH: cleanup()
    MC->>MR: cleanup()
    MC->>PH: cleanup()
```

### Phase 1: Basic Expand/Collapse Foundation
**Timeline**: Week 1-2  
**Goal**: Core architecture with basic expand/collapse

#### Tasks:
1. **Create Core Abstractions** (2 days)
   - Define TypeScript interfaces
   - Create base component contracts
   - Set up renderer registry

2. **Implement MessageRouter** (1 day)
   - Dynamic component resolution
   - Basic prop passing
   - Renderer registration

3. **Build ExpandableContainer** (2 days)
   - Expand/collapse animations
   - Touch-optimized interactions
   - Accessibility support (ARIA attributes)

4. **Create State Management** (2 days)
   - Svelte stores for message states
   - Auto-collapse logic
   - State persistence

5. **Basic Text Renderer** (1 day)
   - Simple text display
   - No markdown (plain text only)
   - Integration with ExpandableContainer

#### Acceptance Criteria:
- ✅ Messages can expand/collapse manually
- ✅ Latest message auto-expands
- ✅ Previous messages auto-collapse
- ✅ Touch-friendly interactions work
- ✅ Basic message routing functions

### Phase 2: Enhanced Text Rendering
**Timeline**: Week 3-4  
**Goal**: Markdown parsing with streaming optimization

#### Tasks:
1. **Integrate Marked.js + DOMPurify** (2 days)
   - Safe markdown parsing
   - XSS prevention
   - Performance optimization

2. **Implement Streaming Handler** (3 days)
   - Sentence-level buffering
   - Incremental markdown rendering
   - Smooth visual transitions

3. **Enhanced TextRenderer** (2 days)
   - Replace plain text with markdown
   - Streaming vs final render modes
   - Performance monitoring

4. **Mobile Optimizations** (1 day)
   - Touch interaction refinements
   - Responsive design improvements
   - Performance testing on devices

#### Acceptance Criteria:
- ✅ Markdown renders correctly and safely
- ✅ Streaming updates smooth without jarring transitions
- ✅ Sentence-level buffering works
- ✅ Mobile interactions optimized

### Phase 3: Syntax Highlighting
**Timeline**: Week 5  
**Goal**: Code block highlighting with copy functionality

#### Tasks:
1. **Integrate Prism.js** (2 days)
   - Language detection
   - Syntax highlighting
   - Theme support

2. **Copy-to-Clipboard** (1 day)
   - Copy button for code blocks
   - Visual feedback
   - Keyboard shortcuts

3. **Performance Optimization** (2 days)
   - Lazy loading of language packs
   - Debounced highlighting
   - Memory management

#### Acceptance Criteria:
- ✅ Code blocks highlight correctly
- ✅ Copy functionality works
- ✅ Performance remains smooth
- ✅ Multiple languages supported

### Phase 4: Full Message Type Support
**Timeline**: Week 6-7  
**Goal**: All message types with custom renderers

#### Tasks:
1. **ReasoningRenderer** (2 days)
   - Expandable reasoning display
   - Height constraints with scrolling
   - Auto-collapse integration

2. **ToolCallRenderer** (2 days)
   - Tool name display
   - Expandable arguments
   - Custom renderer hooks

3. **ToolResultRenderer** (2 days)
   - Result display with constraints
   - Custom renderer support
   - Data formatting

4. **UsageRenderer** (1 day)
   - Token usage pill
   - Aggregated display
   - Visual design

#### Acceptance Criteria:
- ✅ All message types render correctly
- ✅ Custom renderers work
- ✅ Height constraints respected
- ✅ Auto-collapse works for all types

### Phase 5: Advanced Features
**Timeline**: Week 8  
**Goal**: Polish and performance optimization

#### Tasks:
1. **Custom Renderer Plugin System** (3 days)
   - Plugin registration
   - Dynamic loading
   - Fallback mechanisms

2. **Advanced Animations** (2 days)
   - Smooth transitions
   - Loading states
   - Micro-interactions

3. **Performance Optimization** (2 days)
   - Virtual scrolling for long conversations
   - Lazy loading optimizations
   - Memory leak prevention

#### Acceptance Criteria:
- ✅ Plugin system functional
- ✅ Smooth animations throughout
- ✅ Performance excellent with large datasets
- ✅ Memory usage optimized

## API Contracts

### Renderer Registry

```typescript
class RendererRegistry {
  private renderers = new Map<string, MessageRenderer<any>>();
  
  register<T extends MessageDto>(
    messageType: string, 
    renderer: MessageRenderer<T>
  ): void;
  
  getRenderer(messageType: string): MessageRenderer<any>;
  unregister(messageType: string): boolean;
  listRenderers(): string[];
}
```

### Streaming Update Contract

```typescript
interface StreamingUpdate {
  readonly content: string;
  readonly delta: string;
  readonly shouldRender: boolean;
  readonly bufferedContent: string;
  readonly isComplete: boolean;
}
```

### Custom Renderer Plugin Contract

```typescript
interface RendererPlugin {
  readonly name: string;
  readonly version: string;
  readonly supportedTypes: string[];
  
  initialize(context: PluginContext): Promise<void>;
  createRenderer(type: string): CustomRenderer;
  cleanup?(): Promise<void>;
}
```

## Testing Strategy

### Testing Pyramid

```mermaid
graph TD
    subgraph "Testing Levels"
        E2E[End-to-End Tests<br/>Playwright<br/>User Scenarios]
        INT[Integration Tests<br/>Component Integration<br/>Message Flow]
        UNIT[Unit Tests<br/>Jest + Testing Library<br/>Individual Components]
        PERF[Performance Tests<br/>Benchmarks<br/>Memory & Speed]
    end
    
    subgraph "Test Coverage Areas"
        UC[User Interactions<br/>Click, Touch, Keyboard]
        ST[Streaming Behavior<br/>Real-time Updates]
        SEC[Security Testing<br/>XSS Prevention]
        ACC[Accessibility<br/>ARIA, Screen Readers]
        MOB[Mobile Testing<br/>Touch, Responsive]
    end
    
    E2E --> UC
    E2E --> ST
    INT --> SEC
    INT --> ACC
    UNIT --> MOB
    PERF --> ST
    
    style E2E fill:#e8f5e8
    style INT fill:#e1f5fe
    style UNIT fill:#f3e5f5
    style PERF fill:#fff3e0
```

### Test Data Flow

```mermaid
sequenceDiagram
    participant TF as Test Framework
    participant MC as Mock Components
    participant TR as Text Renderer
    participant SH as Streaming Handler
    participant DOM as DOM Testing
    
    Note over TF,DOM: Unit Test Flow
    TF->>MC: Create Mock Message
    MC->>TR: Render Component
    TR->>SH: Process Mock Stream
    SH-->>TR: Buffered Content
    TR-->>DOM: Update Component
    DOM-->>TF: Assert Expectations
    
    Note over TF,DOM: Integration Test Flow
    TF->>MC: Create Full Message Flow
    MC->>TR: Stream Multiple Updates
    loop For Each Update
        TR->>SH: Process Delta
        SH-->>TR: Render Decision
        TR-->>DOM: Update UI
    end
    DOM-->>TF: Verify Final State
    
    Note over TF,DOM: E2E Test Flow
    TF->>DOM: Navigate to Chat
    DOM->>MC: Send Real Message
    MC->>TR: Real Streaming
    TR->>DOM: Real UI Updates
    DOM-->>TF: User Experience Validation
```

### Unit Testing (Jest + Testing Library)

```typescript
// Example: ExpandableContainer.test.ts
describe('ExpandableContainer', () => {
  test('expands and collapses on click', async () => {
    const { getByRole, queryByText } = render(ExpandableContainer, {
      title: 'Test Content',
      collapsible: true
    });
    
    const button = getByRole('button');
    const content = queryByText('Content inside');
    
    expect(content).toBeInTheDocument();
    
    await fireEvent.click(button);
    expect(content).not.toBeInTheDocument();
  });
  
  test('auto-collapses when not latest', async () => {
    const component = render(ExpandableContainer, {
      isLatest: true,
      expanded: true
    });
    
    await component.rerender({ isLatest: false });
    expect(component.queryByText('Content')).not.toBeInTheDocument();
  });
});
```

### Integration Testing (Playwright)

```typescript
// Example: message-rendering.spec.ts
test('message rendering flow', async ({ page }) => {
  await page.goto('/chat/test-chat');
  
  // Test streaming text message
  await page.waitForSelector('[data-testid="message-0"]');
  
  // Verify markdown rendering
  const codeBlock = page.locator('pre code');
  await expect(codeBlock).toHaveClass(/language-javascript/);
  
  // Test expand/collapse
  await page.click('[data-testid="reasoning-toggle"]');
  await expect(page.locator('[data-testid="reasoning-content"]')).toBeVisible();
  
  // Test auto-collapse when new message arrives
  await page.click('[data-testid="send-button"]');
  await expect(page.locator('[data-testid="reasoning-content"]')).toBeHidden();
});
```

### Performance Testing

```typescript
// Performance benchmarks
describe('Performance', () => {
  test('renders 100 messages under 500ms', async () => {
    const start = performance.now();
    
    const messages = Array.from({ length: 100 }, (_, i) => 
      createMockMessage(`Message ${i}`)
    );
    
    render(ChatView, { messages });
    
    const duration = performance.now() - start;
    expect(duration).toBeLessThan(500);
  });
  
  test('streaming updates under 16ms (60fps)', async () => {
    const { component } = render(TextRenderer, {
      message: createStreamingMessage()
    });
    
    const start = performance.now();
    await component.$set({ 
      message: { ...message, content: message.content + ' new content' }
    });
    const duration = performance.now() - start;
    
    expect(duration).toBeLessThan(16);
  });
});
```

## Performance Considerations

### Performance Monitoring Architecture

```mermaid
graph TB
    subgraph "Performance Metrics"
        RT[Render Time<br/>< 16ms target]
        MT[Memory Usage<br/>Leak Detection]
        FPS[Frame Rate<br/>60fps target]
        TTI[Time to Interactive<br/>< 100ms]
    end
    
    subgraph "Monitoring Points"
        SU[Streaming Updates]
        CR[Component Render]
        SH[Syntax Highlighting]
        EC[Expand/Collapse]
    end
    
    subgraph "Optimization Strategies"
        VD[Virtual DOM Diffing]
        LL[Lazy Loading]
        DB[Debouncing]
        WM[Web Workers]
        MC[Memory Cleanup]
    end
    
    SU --> RT
    CR --> FPS
    SH --> TTI
    EC --> MT
    
    RT --> VD
    FPS --> LL
    TTI --> DB
    MT --> MC
    
    VD --> WM
    LL --> WM
    
    style RT fill:#e8f5e8
    style MT fill:#e1f5fe
    style FPS fill:#f3e5f5
    style TTI fill:#fff3e0
```

### Performance Optimization Flow

```mermaid
flowchart TD
    START[Component Update] --> CHECK{Performance Budget available?}
    
    CHECK -->|Yes| RENDER[Immediate Render]
    CHECK -->|No| DEFER[Defer to Next Frame]
    
    RENDER --> MEASURE[Measure Render Time]
    DEFER --> RAF[RequestAnimationFrame]
    RAF --> RENDER
    
    MEASURE --> FAST{< 16ms?}
    FAST -->|Yes| CONTINUE[Continue Normal Flow]
    FAST -->|No| OPTIMIZE[Apply Optimizations]
    
    OPTIMIZE --> LAZY[Lazy Load Non-Critical]
    LAZY --> DEBOUNCE[Debounce Updates]
    DEBOUNCE --> WORKER[Move to Web Worker]
    WORKER --> CONTINUE
    
    CONTINUE --> CLEANUP[Memory Cleanup]
    CLEANUP --> END[Complete]
    
    style CHECK fill:#e1f5fe
    style FAST fill:#e8f5e8
    style OPTIMIZE fill:#fff3e0
```

### Memory Management

Cleanup strategy for components:

```typescript
// Cleanup strategy for components
class MessageComponentManager {
  private activeComponents = new Set<ComponentInstance>();
  private observerPool = new Map<string, IntersectionObserver>();
  
  registerComponent(component: ComponentInstance) {
    this.activeComponents.add(component);
    this.setupIntersectionObserver(component);
  }
  
  private setupIntersectionObserver(component: ComponentInstance) {
    const observer = new IntersectionObserver((entries) => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) {
          // Cleanup non-visible components
          this.cleanupComponent(component);
        }
      });
    }, { threshold: 0 });
    
    observer.observe(component.element);
    this.observerPool.set(component.id, observer);
  }
  
  private cleanupComponent(component: ComponentInstance) {
    // Remove event listeners
    // Clear timers
    // Release references
    this.activeComponents.delete(component);
  }
}
```

### Lazy Loading Strategy

```typescript
// Dynamic imports for performance
export async function loadRenderer(messageType: string): Promise<MessageRenderer> {
  switch (messageType) {
    case 'text':
      return (await import('./renderers/TextRenderer.svelte')).default;
    case 'reasoning':
      return (await import('./renderers/ReasoningRenderer.svelte')).default;
    case 'tool_call':
      return (await import('./renderers/ToolCallRenderer.svelte')).default;
    default:
      return (await import('./renderers/DefaultRenderer.svelte')).default;
  }
}
```

## Error Handling & Fallbacks

## Error Recovery & Resilience Architecture

### Error Recovery Architecture

```mermaid
flowchart TD
    START[Component Render] --> TRY{Try Render}
    
    TRY -->|Success| RENDER[Normal Render]
    TRY -->|Error| CATCH[Catch Error]
    
    CATCH --> LOG[Log Error Details]
    LOG --> TYPE{Error Type?}
    
    TYPE -->|Syntax Error| FALLBACK1[Plain Text Fallback]
    TYPE -->|Network Error| FALLBACK2[Cached Content]
    TYPE -->|Memory Error| FALLBACK3[Simplified Render]
    TYPE -->|Unknown Error| FALLBACK4[Generic Fallback]
    
    FALLBACK1 --> NOTIFY[Notify User]
    FALLBACK2 --> NOTIFY
    FALLBACK3 --> NOTIFY
    FALLBACK4 --> NOTIFY
    
    NOTIFY --> RECOVERY[Recovery Options]
    RECOVERY --> RETRY[Retry Button]
    RECOVERY --> REPORT[Report Issue]
    RECOVERY --> CONTINUE[Continue with Fallback]
    
    RETRY --> TRY
    REPORT --> CONTINUE
    CONTINUE --> END[Complete]
    RENDER --> END
    
    style CATCH fill:#ffebee
    style FALLBACK1 fill:#e8f5e8
    style FALLBACK2 fill:#e8f5e8
    style FALLBACK3 fill:#e8f5e8
    style FALLBACK4 fill:#e8f5e8
```

### Graceful Degradation Strategy

```mermaid
graph LR
    subgraph "Feature Levels"
        FULL[Full Experience<br/>All Features Working]
        ENHANCED[Enhanced Experience<br/>Basic + Markdown]
        BASIC[Basic Experience<br/>Plain Text Only]
        MINIMAL[Minimal Experience<br/>Error State]
    end
    
    subgraph "Fallback Triggers"
        JS[JavaScript Error]
        NET[Network Issue] 
        MEM[Memory Limit]
        PERF[Performance Issue]
    end
    
    FULL -->|JS Error| ENHANCED
    ENHANCED -->|Markdown Error| BASIC
    BASIC -->|Render Error| MINIMAL
    
    NET --> ENHANCED
    MEM --> BASIC
    PERF --> BASIC
    
    style FULL fill:#e8f5e8
    style ENHANCED fill:#e1f5fe
    style BASIC fill:#f3e5f5
    style MINIMAL fill:#ffebee
```

```typescript
// Graceful degradation strategy
class RenderingErrorHandler {
  static handleRenderError(error: Error, message: MessageDto): ComponentInstance {
    console.error('Rendering failed:', error);
    
    // Fallback to plain text
    return this.createFallbackRenderer(message);
  }
  
  private static createFallbackRenderer(message: MessageDto): ComponentInstance {
    return {
      render: () => `<div class="error-fallback">${message.content || 'Message unavailable'}</div>`
    };
  }
}
```

This development specification provides a comprehensive, elegant architecture that enables incremental implementation while maintaining clean separation of concerns and high code quality throughout all phases of development.
