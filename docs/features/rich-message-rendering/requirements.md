# Feature Specification: Rich Message Rendering

## High-Level Overview

The Rich Message Rendering feature enhances the chat interface to support sophisticated AI interactions including reasoning/thinking displays, tool calls with custom renderers, and improved text rendering with markdown support. This feature transforms the current basic text-only chat into a full agentic AI interface capable of displaying complex message types with appropriate expand/collapse behaviors and mobile-optimized interactions.

## High Level Requirements

1. **Enhanced Message Type Support**: Render multiple message types including text, reasoning, tool calls, tool results, and usage information
2. **Intelligent Expand/Collapse**: Latest messages expanded by default, auto-collapse when superseded, user control for manual expansion
3. **Streaming-Optimized Rendering**: Real-time responsiveness during streaming with incremental markdown rendering
4. **Mobile-First Experience**: Touch-optimized interaction patterns for expand/collapse functionality
5. **Extensible Architecture**: Support for custom renderers and message type plugins
6. **Performance Optimized**: Balanced approach - fast during streaming, polished after completion

## Existing Solutions

### External References

- **ChatGPT Web Interface**: Collapsible thinking sections, syntax highlighting, copy buttons
- **Claude Web Interface**: Expandable tool use sections, markdown rendering
- **GitHub Copilot Chat**: Inline code rendering, expandable details
- **VS Code Chat**: Integrated markdown rendering with syntax highlighting

### Current Implementation Analysis

- **Basic text messages** with simple `content` field processing
- **Svelte component architecture** in `client/src/routes/chat/[chatId]/+page.svelte`
- **LmDotnetTools integration** with comprehensive message type support
- **Server-Sent Events (SSE)** for real-time streaming
- **Existing message types**: TextMessage, ReasoningMessage, ToolsCallMessage, etc.

## Detailed Requirements

### Requirement 1: Message Type Architecture

- **User Story**: As a user, I want to see different types of AI messages rendered appropriately so I can understand the AI's reasoning and tool usage.

#### Acceptance Criteria:

1. **WHEN** the system receives a TextMessage **THEN** it **SHALL** render with markdown formatting including code blocks with syntax highlighting
2. **WHEN** the system receives a ReasoningMessage **THEN** it **SHALL** display as expandable content with "Reasoning" header
3. **WHEN** the system receives a ToolsCallMessage **THEN** it **SHALL** show tool name with expandable arguments section
4. **WHEN** the system receives a ToolsCallResultMessage **THEN** it **SHALL** render results with custom renderer support and height constraints
5. **WHEN** the system receives a UsageMessage **THEN** it **SHALL** display as a small token usage pill

### Requirement 2: Streaming-Optimized Text Rendering

- **User Story**: As a user, I want to see messages update smoothly during streaming without jarring visual changes.

#### Acceptance Criteria:

1. **WHEN** text updates are streaming **THEN** the system **SHALL** buffer updates into sentence-level chunks before applying markdown
2. **WHEN** a text message is being streamed **THEN** the system **SHALL** provide real-time responsiveness without blocking the UI
3. **WHEN** a text message completes streaming **THEN** the system **SHALL** apply full polished rendering including syntax highlighting
4. **WHEN** markdown rendering is applied **THEN** it **SHALL** NOT cause jarring visual transitions by turning on only at the end

### Requirement 3: Intelligent Expand/Collapse Behavior

- **User Story**: As a user, I want to see the latest AI activity expanded while keeping previous messages organized and accessible.

#### Acceptance Criteria:

1. **WHEN** a new message starts streaming **THEN** the latest message **SHALL** be in expanded state
2. **WHEN** a subsequent message begins **THEN** previous messages **SHALL** auto-collapse to conserve space
3. **WHEN** a user clicks on a collapsed message **THEN** it **SHALL** expand to show full content
4. **WHEN** a message type opts out of collapsibility (TextMessage, custom renderers) **THEN** it **SHALL** remain fully visible
5. **WHEN** reasoning messages are collapsed **THEN** they **SHALL** show as single line with "Reasoning" label

### Requirement 4: Mobile-Optimized Interactions

- **User Story**: As a mobile user, I want touch-friendly expand/collapse interactions that work intuitively on my device.

#### Acceptance Criteria:

1. **WHEN** using touch devices **THEN** expand/collapse interactions **SHALL** use touch-optimized hit targets (minimum 44px)
2. **WHEN** expanding content on mobile **THEN** the system **SHALL** provide smooth animations and appropriate scroll behavior
3. **WHEN** viewing on small screens **THEN** collapsed messages **SHALL** show essential information clearly
4. **WHEN** interacting with expandable content **THEN** the system **SHALL** prevent accidental expansion/collapse

### Requirement 5: Custom Renderer Extensibility

- **User Story**: As a developer, I want to create custom renderers for specific tool types so users get optimized displays for different data types.

#### Acceptance Criteria:

1. **WHEN** a ToolsCallMessage specifies a custom renderer **THEN** the system **SHALL** use the appropriate component
2. **WHEN** a custom renderer is not available **THEN** the system **SHALL** fall back to default rendering
3. **WHEN** custom renderers are implemented **THEN** they **SHALL** support expand/collapse behaviors unless opted out
4. **WHEN** tool results have custom renderers **THEN** they **SHALL** handle data formatting and display optimization

### Requirement 6: Security and Performance

- **User Story**: As a user, I want fast, secure rendering of content without security vulnerabilities or performance issues.

#### Acceptance Criteria:

1. **WHEN** rendering markdown content **THEN** the system **SHALL** sanitize HTML output using DOMPurify to prevent XSS
2. **WHEN** highlighting code blocks **THEN** the system **SHALL** use Prism.js with appropriate language detection
3. **WHEN** processing large amounts of content **THEN** the system **SHALL** maintain smooth scrolling and interaction performance
4. **WHEN** multiple messages are expanded **THEN** the system **SHALL** handle memory usage efficiently

## Implementation Phases (MVP First Approach)

### Phase 1: Basic Expand/Collapse Foundation

- Implement expand/collapse state management
- Create basic collapsible components
- Add auto-collapse behavior for new messages
- Simple text rendering without markdown

### Phase 2: Enhanced Text Rendering

- Integrate Marked.js for markdown parsing
- Add DOMPurify for HTML sanitization
- Implement incremental markdown rendering during streaming
- Sentence-level buffering for smooth updates

### Phase 3: Syntax Highlighting

- Integrate Prism.js for code block highlighting
- Language detection and highlighting
- Copy-to-clipboard functionality for code blocks
- Theme support for syntax highlighting

### Phase 4: Full Message Type Support

- Implement ReasoningMessage components
- Add ToolsCallMessage with expandable arguments
- Create ToolsCallResultMessage with height constraints
- Add UsageMessage pill display

### Phase 5: Advanced Features

- Custom renderer plugin system
- Mobile interaction optimizations
- Advanced animations and transitions
- Performance optimizations and lazy loading

## Technical Architecture

### Component Structure
```
MessageRenderer.svelte (Router)
├── TextMessage.svelte (Markdown + Syntax highlighting)
├── ReasoningMessage.svelte (Expandable reasoning display)
├── ToolCallMessage.svelte (Tool name + expandable args)
├── ToolResultMessage.svelte (Results with custom renderers)
├── UsageMessage.svelte (Token usage pill)
└── ExpandableContent.svelte (Shared expand/collapse logic)
```

### Technology Stack

- **Markdown**: Marked.js + DOMPurify for security
- **Syntax Highlighting**: Prism.js with language detection
- **State Management**: Svelte stores for expand/collapse state
- **Streaming**: Enhanced SSE handling for incremental updates
- **Mobile**: Touch-optimized CSS and interaction patterns

### Message Flow

1. **Receive message** via SSE from LmDotnetTools
2. **Route to appropriate component** based on message type
3. **Apply streaming optimizations** for text content
4. **Manage expand/collapse state** based on message position
5. **Render with appropriate formatting** and custom renderers

## References

- [LmDotnetTools Message Types Analysis](notes/codebase-research/lmdotnettools-message-types.md)
- [Technology Stack Research](notes/online-research/technology-stack.md)
- [User Feedback and Learnings](notes/user-feedback-and-learnings.md)
- [Current Implementation Analysis](notes/codebase-research/current-implementation.md)
