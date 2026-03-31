# Technology Stack Research

## Markdown Rendering

### Marked.js
- **Stars**: 35.3k GitHub stars, very mature and widely used
- **Features**: 
  - Built for speed (⚡ low-level compiler)
  - Lightweight while implementing all markdown features
  - Works in browser, server, or CLI
  - Supports extensibility
- **Security**: ⚠️ Does NOT sanitize output HTML - need DOMPurify
- **Usage**: Simple `marked.parse(markdownText)` API
- **Perfect for**: Our text message rendering needs

### Alternative: DOMPurify
- **Purpose**: HTML sanitization to prevent XSS attacks
- **Integration**: `DOMPurify.sanitize(marked.parse(markdownText))`
- **Essential**: Required when using marked.js for security

## Syntax Highlighting

### Prism.js
- **Stars**: 35.3k GitHub stars, industry standard
- **Features**:
  - Only 2KB core, languages add ~300-500 bytes each
  - 297 supported languages
  - Extensible plugin architecture
  - Web Workers support for performance
  - Easy CSS styling with semantic class names
- **Usage Patterns**:
  - Automatic: Add classes to `<code>` elements
  - Manual: Use `Prism.highlight(code, grammar, language)` API
- **Plugin Ecosystem**: Copy-to-clipboard, line numbers, toolbar, etc.

## Svelte Component Patterns

### Component Composition
- **Dynamic Components**: Use `<svelte:component this={ComponentType} />`
- **Component Props**: Spread props with `{...props}` syntax
- **Slot-based Architecture**: `<slot name="content" />` for extensible layouts
- **Conditional Rendering**: `{#if messageType === 'tool'}{/if}` blocks

### State Management
- **Stores**: Reactive state with `writable()` and `derived()`
- **Context API**: `setContext()` and `getContext()` for provider patterns
- **Event Dispatch**: `createEventDispatcher()` for component communication

## Implementation Strategy

### Phase 1: Enhanced Message Types
```typescript
interface RichMessageDto {
  id: string;
  chatId: string;
  role: 'user' | 'assistant' | 'system' | 'tool';
  messageType: 'text' | 'reasoning' | 'tool_call' | 'tool_result' | 'usage';
  content?: string;              // For text/reasoning
  toolCalls?: ToolCallDto[];     // For tool calls
  toolResults?: ToolResultDto[]; // For tool results
  reasoning?: string;            // For reasoning content
  usage?: UsageDto;             // For usage information
  metadata?: Record<string, any>; // For extensibility
  timestamp: Date;
  sequenceNumber: number;
}
```

### Phase 2: Component Architecture
```svelte
<!-- MessageRenderer.svelte - Main router -->
<script>
  import TextMessage from './TextMessage.svelte';
  import ReasoningMessage from './ReasoningMessage.svelte';
  import ToolCallMessage from './ToolCallMessage.svelte';
  import ToolResultMessage from './ToolResultMessage.svelte';
  import UsageMessage from './UsageMessage.svelte';
  
  export let message;
  
  const componentMap = {
    text: TextMessage,
    reasoning: ReasoningMessage,
    tool_call: ToolCallMessage,
    tool_result: ToolResultMessage,
    usage: UsageMessage
  };
  
  $: Component = componentMap[message.messageType] || TextMessage;
</script>

<svelte:component this={Component} {message} />
```

### Phase 3: Enhanced Text Rendering
```svelte
<!-- TextMessage.svelte -->
<script>
  import { marked } from 'marked';
  import DOMPurify from 'dompurify';
  import Prism from 'prismjs';
  
  export let message;
  
  $: renderedContent = DOMPurify.sanitize(marked.parse(message.content));
  
  // Auto-highlight code blocks after render
  function highlightCode(node) {
    Prism.highlightAllUnder(node);
  }
</script>

<div class="prose" use:highlightCode>
  {@html renderedContent}
</div>
```

### Phase 4: Expandable Components
```svelte
<!-- ExpandableContent.svelte -->
<script>
  export let title;
  export let expanded = false;
  export let maxHeight = '200px';
  
  function toggle() {
    expanded = !expanded;
  }
</script>

<div class="expandable-content">
  <button class="header" on:click={toggle}>
    <span class="icon" class:expanded>{expanded ? '▼' : '▶'}</span>
    <span class="title">{title}</span>
  </button>
  
  {#if expanded}
    <div class="content" style="max-height: {maxHeight}">
      <slot />
    </div>
  {/if}
</div>
```

## Technology Integration Benefits

### Marked.js + DOMPurify
- **Security**: Safe HTML rendering from markdown
- **Performance**: Fast parsing and rendering
- **Features**: Full CommonMark compliance
- **Extensibility**: Plugin system for custom syntax

### Prism.js Integration
- **Performance**: Automatic detection and highlighting
- **Maintainability**: Standard CSS classes for theming
- **Extensibility**: Plugin system (copy button, line numbers)
- **Accessibility**: Proper semantic markup

### Svelte Component System
- **Flexibility**: Easy to add new message types
- **Performance**: Compile-time optimizations
- **Maintainability**: Clear separation of concerns
- **Extensibility**: Plugin-like architecture for custom renderers

## Security Considerations

### Content Sanitization
- **Always use DOMPurify** for HTML content
- **Validate tool inputs** before rendering
- **Escape user content** in tool call arguments
- **CSP headers** to prevent XSS attacks

### Performance Considerations
- **Lazy load** syntax highlighting for performance
- **Virtual scrolling** for large conversation history
- **Debounce** real-time rendering updates
- **Code splitting** for language packs
