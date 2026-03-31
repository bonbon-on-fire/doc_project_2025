# Rich Message Rendering - Implementation Tasks

## Overview

This document breaks down the Rich Message Rendering feature into specific, actionable tasks with clear acceptance criteria. Each task includes detailed requirements, testing criteria, and deliverables that developers can implement incrementally.

## Task Structure

Each task follows this format:
- **Task ID**: Unique identifier (e.g., RMR-P1-001)
- **Title**: Clear, concise task description
- **Priority**: Critical, High, Medium, Low
- **Estimated Effort**: Story points or time estimate
- **Dependencies**: Prerequisites that must be completed first
- **Acceptance Criteria**: Specific, testable requirements
- **Definition of Done**: What constitutes completion
- **Testing Requirements**: How to verify the task is complete

---

## Phase 1: Basic Expand/Collapse Foundation (Sprint 1-2)

### RMR-P1-001: Create Core TypeScript Interfaces ✅ COMPLETED
**Priority**: Critical  
**Estimated Effort**: 3 story points  
**Dependencies**: None  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Define the foundational TypeScript interfaces that all message renderers will implement. These interfaces establish the contract for the entire system.

#### Acceptance Criteria
1. **WHEN** `MessageRenderer<T>` interface is defined **THEN** it **MUST** include:
   - `messageType: string` property
   - `supportsStreaming: boolean` property
   - `supportsCollapse: boolean` property
   - `render(message: T, context: RenderContext): Promise<ComponentInstance>` method
   - Optional `updateStream?(message: T, delta: string): Promise<void>` method
   - Optional `onCollapse?(): void` method
   - Optional `onExpand?(): void` method

2. **WHEN** `StreamingHandler` interface is defined **THEN** it **MUST** include:
   - `bufferSize: number` property
   - `shouldBuffer: boolean` property
   - `processUpdate(content: string, delta: string): StreamingUpdate` method
   - `shouldRender(update: StreamingUpdate): boolean` method
   - `createRenderableContent(update: StreamingUpdate): string` method

3. **WHEN** `ExpandableComponent` interface is defined **THEN** it **MUST** include:
   - `isCollapsible: boolean` property
   - `defaultExpanded: boolean` property
   - `expand(): Promise<void>` method
   - `collapse(): Promise<void>` method
   - `toggle(): Promise<void>` method
   - Optional `onStateChange?(expanded: boolean): void` method

4. **WHEN** `CustomRenderer<T>` interface is defined **THEN** it **MUST** include:
   - `rendererName: string` property
   - `supportedTypes: string[]` property
   - `canRender(data: T): boolean` method
   - `render(data: T, context: RenderContext): Promise<ComponentInstance>` method
   - Optional `getPreviewContent?(data: T): string` method

#### Definition of Done
- [x] All interfaces defined in TypeScript files
- [x] JSDoc comments added for all properties and methods
- [x] Interfaces exported from main module
- [x] Type definitions compile without errors
- [x] Unit tests created for interface compliance checking

#### Testing Requirements
- Create mock implementations of each interface to verify contract compliance
- Verify TypeScript compilation passes with strict mode enabled

---

### RMR-P1-002: Implement RendererRegistry System ✅ COMPLETED
**Priority**: Critical  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P1-001  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Create a registry system that manages message renderers, allowing dynamic registration and retrieval of renderers based on message type.

#### Acceptance Criteria
1. **WHEN** `RendererRegistry` class is implemented **THEN** it **MUST**:
   - Maintain a `Map<string, MessageRenderer<any>>` for registered renderers
   - Provide `register<T>(messageType: string, renderer: MessageRenderer<T>): void` method
   - Provide `getRenderer(messageType: string): MessageRenderer<any>` method
   - Provide `unregister(messageType: string): boolean` method
   - Provide `listRenderers(): string[]` method

2. **WHEN** registering a renderer **THEN** it **MUST**:
   - Validate the renderer implements the `MessageRenderer` interface
   - Throw an error if messageType is already registered (unless force flag is provided)
   - Return void on successful registration

3. **WHEN** retrieving a renderer **THEN** it **MUST**:
   - Return the registered renderer for the given messageType
   - Return a default fallback renderer if messageType is not found
   - Never return null or undefined

4. **WHEN** a fallback renderer is used **THEN** it **MUST**:
   - Log a warning about the missing renderer
   - Render basic text content safely
   - Support all required interface methods

#### Definition of Done
- [x] `RendererRegistry` class implemented with all required methods
- [x] Default fallback renderer implemented
- [x] Error handling for invalid registrations
- [x] Comprehensive unit tests with 100% code coverage
- [x] Integration tests for renderer resolution

#### Testing Requirements
```typescript
// Example test cases
describe('RendererRegistry', () => {
  test('registers and retrieves renderers correctly', () => {
    const registry = new RendererRegistry();
    const mockRenderer = createMockRenderer('text');
    
    registry.register('text', mockRenderer);
    const retrieved = registry.getRenderer('text');
    
    expect(retrieved).toBe(mockRenderer);
  });
  
  test('returns fallback for unknown message types', () => {
    const registry = new RendererRegistry();
    const fallback = registry.getRenderer('unknown');
    
    expect(fallback).toBeDefined();
    expect(fallback.messageType).toBe('fallback');
  });
});
```

---

### RMR-P1-003: Create MessageRouter Svelte Component ✅ COMPLETED
**Priority**: Critical  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P1-002  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Implement the main routing component that receives messages and delegates rendering to the appropriate renderer based on message type.

#### Acceptance Criteria
1. **WHEN** `MessageRouter.svelte` component is created **THEN** it **MUST**:
   - Accept `message: MessageDto` as a prop
   - Accept `isLatest: boolean` as a prop (default: false)
   - Accept `onStateChange: (messageId: string, state: MessageState) => void` as a prop
   - Use `RendererRegistry` to resolve the appropriate renderer
   - Dynamically render the resolved component using `<svelte:component>`

2. **WHEN** the component mounts **THEN** it **MUST**:
   - Register with the message state store
   - Initialize the message state as expanded if `isLatest` is true
   - Set up reactive statements for prop changes

3. **WHEN** `isLatest` prop changes **THEN** it **MUST**:
   - Update the message state accordingly
   - Trigger appropriate expand/collapse behavior
   - Call `onStateChange` callback with new state

4. **WHEN** an error occurs during rendering **THEN** it **MUST**:
   - Catch the error gracefully
   - Log the error with context information
   - Render the fallback component
   - Display user-friendly error message

#### Definition of Done
- [x] `MessageRouter.svelte` component implemented
- [x] Proper prop validation and TypeScript types
- [x] Error boundary implementation
- [x] Reactive state management
- [x] Component tests with Testing Library

#### Testing Requirements
```typescript
// Example test cases
describe('MessageRouter Component', () => {
  test('renders correct renderer for message type', async () => {
    const message = { messageType: 'text', content: 'Hello' };
    const { getByTestId } = render(MessageRouter, { message });
    
    expect(getByTestId('text-renderer')).toBeInTheDocument();
  });
  
  test('handles renderer errors gracefully', async () => {
    const message = { messageType: 'invalid', content: 'Test' };
    const { getByTestId } = render(MessageRouter, { message });
    
    expect(getByTestId('fallback-renderer')).toBeInTheDocument();
  });
});
```

---

### RMR-P1-004: Build ExpandableContainer Component ✅ COMPLETED
**Priority**: Critical  
**Estimated Effort**: 6 story points  
**Dependencies**: None  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Create a reusable container component that provides expand/collapse functionality with smooth animations and touch-optimized interactions.

#### Acceptance Criteria
1. **WHEN** `ExpandableContainer.svelte` component is created **THEN** it **MUST**:
   - Accept `expanded: boolean` prop (default: true)
   - Accept `collapsible: boolean` prop (default: true)
   - Accept `title: string` prop (default: '')
   - Accept `isLatest: boolean` prop (default: false)
   - Provide a default slot for content

2. **WHEN** `collapsible` is true **THEN** it **MUST**:
   - Display a clickable header with toggle icon
   - Show expand/collapse icon (▶ collapsed, ▼ expanded)
   - Apply appropriate ARIA attributes for accessibility
   - Support keyboard navigation (Enter and Space keys)

3. **WHEN** expand/collapse is triggered **THEN** it **MUST**:
   - Animate the transition smoothly (200ms duration)
   - Use appropriate easing function (quintOut)
   - Maintain 60fps performance during animation
   - Dispatch custom events for state changes

4. **WHEN** on touch devices **THEN** it **MUST**:
   - Provide minimum 44px touch target for header
   - Support touch interactions without requiring hover
   - Provide tactile feedback for interactions
   - Prevent text selection during rapid taps

5. **WHEN** `isLatest` is false and `collapsible` is true **THEN** it **MUST**:
   - Automatically collapse the container
   - Only collapse if currently expanded
   - Respect user's manual expansion preferences

#### Definition of Done
- [x] `ExpandableContainer.svelte` component implemented
- [x] Smooth animations with optimized performance
- [x] Full accessibility support (ARIA, keyboard navigation)
- [x] Touch-optimized interactions
- [x] Auto-collapse logic for non-latest messages
- [x] Comprehensive component tests
- [x] Visual regression tests for animations

#### Testing Requirements
```typescript
// Example test cases
describe('ExpandableContainer Component', () => {
  test('expands and collapses on click', async () => {
    const { getByRole, queryByTestId } = render(ExpandableContainer, {
      title: 'Test Container',
      collapsible: true
    });
    
    const button = getByRole('button');
    const content = queryByTestId('expandable-content');
    
    expect(content).toBeVisible();
    
    await fireEvent.click(button);
    expect(content).not.toBeVisible();
  });
  
  test('auto-collapses when not latest', async () => {
    const component = render(ExpandableContainer, {
      isLatest: true,
      expanded: true,
      collapsible: true
    });
    
    await component.rerender({ isLatest: false });
    expect(component.queryByTestId('expandable-content')).not.toBeVisible();
  });
  
  test('supports keyboard navigation', async () => {
    const { getByRole } = render(ExpandableContainer, {
      collapsible: true
    });
    
    const button = getByRole('button');
    await fireEvent.keyDown(button, { key: 'Enter' });
    
    // Verify state change
  });
});
```

---

### RMR-P1-005: Implement Message State Management ✅ COMPLETED
**Priority**: Critical  
**Estimated Effort**: 5 story points  
**Dependencies**: None  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Create Svelte stores and controllers for managing message expand/collapse state with auto-collapse logic for non-latest messages.

#### Acceptance Criteria
1. **WHEN** message state store is implemented **THEN** it **MUST**:
   - Use `writable<MessageStates>({})` for storing message states
   - Use `writable<string | null>(null)` for tracking latest message ID
   - Provide derived store for auto-collapse logic
   - Export action functions for state updates

2. **WHEN** `updateMessageState(messageId, updates)` is called **THEN** it **MUST**:
   - Merge updates with existing state for the message
   - Create new state object if message doesn't exist
   - Trigger reactive updates for subscribers
   - Validate state updates for correctness

3. **WHEN** `setLatestMessage(messageId)` is called **THEN** it **MUST**:
   - Update the latest message ID
   - Auto-collapse all other expanded messages
   - Ensure the latest message is expanded
   - Preserve user's manual collapse preference for latest message

4. **WHEN** state changes occur **THEN** it **MUST**:
   - Notify all subscribed components
   - Batch updates to prevent unnecessary re-renders
   - Maintain immutable state updates
   - Log state changes for debugging (dev mode only)

5. **WHEN** cleanup is needed **THEN** it **MUST**:
   - Remove state for destroyed messages
   - Prevent memory leaks from old message states
   - Clean up event listeners and subscriptions

#### Definition of Done
- [x] Message state stores implemented with proper TypeScript types
- [x] Action functions for state management
- [x] Auto-collapse logic working correctly
- [ ] State persistence (optional: localStorage)
- [x] Unit tests for all store operations
- [x] Integration tests with components

#### Testing Requirements
```typescript
// Example test cases
describe('Message State Management', () => {
  test('updates message state correctly', () => {
    updateMessageState('msg-1', { expanded: true });
    
    const state = get(messageStates)['msg-1'];
    expect(state.expanded).toBe(true);
  });
  
  test('auto-collapses when new latest message set', () => {
    // Set up initial state
    updateMessageState('msg-1', { expanded: true });
    updateMessageState('msg-2', { expanded: true });
    setLatestMessage('msg-1');
    
    // Set new latest message
    setLatestMessage('msg-2');
    
    const states = get(messageStates);
    expect(states['msg-1'].expanded).toBe(false);
    expect(states['msg-2'].expanded).toBe(true);
  });
});
```

---

### RMR-P1-006: Create Basic TextRenderer Component ✅ COMPLETED
**Priority**: High  
**Estimated Effort**: 3 story points  
**Dependencies**: RMR-P1-004  
**Status**: ✅ Completed on August 7, 2025  

#### Description
Implement a basic text renderer that displays plain text content without markdown parsing. This serves as the foundation for enhanced text rendering in Phase 2.

#### Acceptance Criteria
1. **WHEN** `TextRenderer.svelte` component is created **THEN** it **MUST**:
   - Accept `message: TextMessageDto` prop
   - Accept `isLatest: boolean` prop
   - Implement `MessageRenderer<TextMessageDto>` interface
   - Set `supportsStreaming = false` (for Phase 1)
   - Set `supportsCollapse = false` (text messages don't collapse)

2. **WHEN** rendering text content **THEN** it **MUST**:
   - Display message content as plain text
   - Properly escape HTML entities to prevent XSS
   - Apply appropriate CSS classes for styling
   - Handle empty or null content gracefully

3. **WHEN** long text content is provided **THEN** it **MUST**:
   - Apply word wrapping for readability
   - Maintain proper line spacing
   - Support right-to-left (RTL) languages
   - Handle special characters correctly

4. **WHEN** the component is used **THEN** it **MUST**:
   - Integrate seamlessly with MessageRouter
   - Provide consistent styling with other renderers
   - Support theming through CSS custom properties
   - Be accessible to screen readers

#### Definition of Done
- [x] `TextRenderer.svelte` component implemented
- [x] XSS protection through proper escaping
- [x] CSS styling with theme support
- [x] RTL language support
- [x] Component tests for various text scenarios
- [x] Accessibility tests

#### Testing Requirements
```typescript
// Example test cases
describe('TextRenderer Component', () => {
  test('renders plain text correctly', () => {
    const message = { content: 'Hello, world!' };
    const { getByText } = render(TextRenderer, { message });
    
    expect(getByText('Hello, world!')).toBeInTheDocument();
  });
  
  test('escapes HTML entities', () => {
    const message = { content: '<script>alert("xss")</script>' };
    const { container } = render(TextRenderer, { message });
    
    expect(container.innerHTML).not.toContain('<script>');
    expect(container.textContent).toContain('<script>alert("xss")</script>');
  });
  
  test('handles empty content', () => {
    const message = { content: '' };
    const { container } = render(TextRenderer, { message });
    
    expect(container).toBeInTheDocument();
  });
});
```

---

## Phase 1 Integration Task

### RMR-P1-007: Phase 1 Integration and Testing ✅ COMPLETED

**Priority**: High  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P1-001 through RMR-P1-006  
**Status**: ✅ Completed on August 7, 2025  

#### Description

Integrate all Phase 1 components and ensure they work together correctly with comprehensive end-to-end testing.

#### Acceptance Criteria

1. **WHEN** all Phase 1 components are integrated **THEN** the system **MUST**:

   - Successfully route text messages to TextRenderer
   - Display expand/collapse UI for collapsible message types
   - Maintain correct state when latest message changes
   - Handle errors gracefully with fallback rendering

2. **WHEN** user interactions occur **THEN** the system **MUST**:

   - Respond to expand/collapse clicks within 100ms
   - Maintain smooth 60fps animations
   - Provide proper keyboard navigation
   - Work correctly on touch devices

3. **WHEN** multiple messages are present **THEN** the system **MUST**:

   - Auto-collapse previous messages when new message arrives
   - Allow manual expansion of any collapsed message
   - Maintain performance with 50+ messages
   - Preserve user preferences during session

#### Definition of Done

- [x] End-to-end tests passing for all Phase 1 functionality (9/9 integration tests)
- [x] Performance benchmarks meeting targets (<100ms init for 50 messages; smooth 60fps animations)
- [x] Accessibility audit completed and issues resolved (ARIA + keyboard + RTL)
- [x] Cross-browser testing completed (desktop Chromium-based + mobile simulation)
- [x] Mobile device testing completed (touch interaction + expand/collapse behavior)
- [x] Documentation updated with integration examples (checklist + completion summary + completion-status)

#### Completion Notes

- All Phase 1 components (P1-001 → P1-006) integrated successfully
- 195/195 unit + component tests passing (100%) after removal of obsolete page test
- Integration coverage validates routing, state management, error fallback, performance, and interaction flows
- Foundation ready for Phase 2 (markdown & streaming enhancements)

---

## Phase 2: Enhanced Text Rendering (Sprint 3-4)

### RMR-P2-001: Integrate Marked.js and DOMPurify

**Status**: In Progress (core parsing + sanitization + security tests completed; extended coverage & perf pending)

**Priority**: Critical  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P1-007  

#### Description

Add markdown parsing capabilities to the TextRenderer using Marked.js for parsing and DOMPurify for security sanitization.

#### Acceptance Criteria

1. **WHEN** Marked.js is integrated **THEN** it **MUST**:

    - Parse CommonMark-compliant markdown syntax
    - Support code blocks, lists, headers, emphasis, and links
    - Handle malformed markdown gracefully
    - Provide consistent output across different inputs

2. **WHEN** DOMPurify is integrated **THEN** it **MUST**:

    - Sanitize all HTML output to prevent XSS attacks
    - Allow safe HTML elements (p, strong, em, ul, ol, li, h1-h6, code, pre)
    - Remove dangerous attributes (onclick, onerror, etc.)
    - Preserve markdown-generated HTML structure

3. **WHEN** markdown is rendered **THEN** it **MUST**:

    - Apply appropriate CSS classes for styling
    - Support syntax highlighting preparation (class="language-*")
    - Maintain accessibility with proper semantic HTML
    - Handle nested markdown elements correctly

4. **WHEN** security is tested **THEN** it **MUST**:

    - Block all XSS attack vectors
    - Sanitize user-generated content safely
    - Log and report sanitization events (dev mode)
    - Pass OWASP security guidelines

#### Definition of Done

- [x] Marked.js integration with proper configuration (GFM, breaks disabled, language-* preserved)
- [x] DOMPurify sanitization working correctly (DOM + jsdom fallback + defensive regex layer)
- [x] Security testing passed for XSS prevention (script, onerror, javascript: href blocked)
- [x] Markdown rendering tests for baseline syntax (headings, emphasis, lists, fenced code, malformed input) — extended nested/semantic tests pending
- [ ] Performance testing for large markdown documents (to add; needs timing assertion + large sample)

#### Progress Notes

- Implemented `parseMarkdown` utility combining marked + DOMPurify + fallback sanitation for SSR/test environments.
- Added jsdom dev dependency for server-side tests.
- TextRenderer now renders sanitized markdown; previous plain-text path replaced.
- Security tests pass after sanitation hardening (added protocol filtering + regex fallback).
- Remaining work: add broader semantic/nested test coverage, performance benchmark, documentation of OWASP mapping.

#### Testing Requirements

```typescript
// Example test cases
describe('Markdown Integration', () => {
   test('renders markdown syntax correctly', () => {
      const message = { content: '# Hello\n\n**Bold** and *italic* text' };
      const { container } = render(TextRenderer, { message });
    
      expect(container.querySelector('h1')).toHaveTextContent('Hello');
      expect(container.querySelector('strong')).toHaveTextContent('Bold');
      expect(container.querySelector('em')).toHaveTextContent('italic');
   });
  
   test('prevents XSS attacks', () => {
      const message = { content: '[Click here](<javascript:alert("xss")>)' };
      const { container } = render(TextRenderer, { message });
    
      const link = container.querySelector('a');
      expect(link?.getAttribute('href')).not.toContain('javascript:');
   });
  
   test('sanitizes dangerous HTML', () => {
      const message = { content: '<img src="x" onerror="alert(\'xss\')">' };
      const { container } = render(TextRenderer, { message });
    
      expect(container.innerHTML).not.toContain('onerror');
   });
});
```

---

### RMR-P2-002: Implement Streaming Handler for Text
**Priority**: Critical  
**Estimated Effort**: 6 story points  
**Dependencies**: RMR-P2-001  

#### Description
Create a streaming handler that buffers text updates and applies incremental markdown rendering without jarring visual transitions.

#### Acceptance Criteria
1. **WHEN** `TextStreamingHandler` is implemented **THEN** it **MUST**:
   - Buffer content updates until sentence boundaries (. ! ?)
   - Maintain a configurable buffer size (default: 50 characters)
   - Process updates in chunks to maintain 60fps performance
   - Provide smooth visual transitions between updates

2. **WHEN** processing streaming updates **THEN** it **MUST**:
   - Detect sentence boundaries accurately
   - Buffer partial sentences to avoid rendering incomplete thoughts
   - Apply markdown parsing to complete sentences only
   - Preserve formatting across update boundaries

3. **WHEN** content is being streamed **THEN** it **MUST**:
   - Never apply markdown rendering only at the very end
   - Maintain cursor position during updates
   - Preserve scroll position for user experience
   - Update within 16ms budget for 60fps

4. **WHEN** streaming completes **THEN** it **MUST**:
   - Apply final markdown rendering to entire content
   - Clean up any partial formatting artifacts
   - Trigger completion callbacks
   - Free memory from streaming buffers

#### Definition of Done
- [ ] `TextStreamingHandler` class implemented
- [ ] Sentence boundary detection working correctly
- [ ] Performance testing showing <16ms update times
- [ ] Visual transition testing for smoothness
- [ ] Memory leak testing for long streams

#### Testing Requirements
```typescript
// Example test cases
describe('TextStreamingHandler', () => {
  test('buffers content until sentence boundary', () => {
    const handler = new TextStreamingHandler();
    
    let update1 = handler.processUpdate('', 'Hello world');
    expect(update1.shouldRender).toBe(false);
    
    let update2 = handler.processUpdate('Hello world', '. How are you?');
    expect(update2.shouldRender).toBe(true);
    expect(update2.content).toContain('Hello world.');
  });
  
  test('maintains performance under 16ms', () => {
    const handler = new TextStreamingHandler();
    const largeContent = 'A'.repeat(10000);
    
    const start = performance.now();
    handler.processUpdate('', largeContent);
    const duration = performance.now() - start;
    
    expect(duration).toBeLessThan(16);
  });
});
```

---

### RMR-P2-003: Enhanced TextRenderer with Streaming
**Priority**: Critical  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P2-002  

#### Description
Upgrade the TextRenderer component to support streaming updates with incremental markdown rendering and smooth visual transitions.

#### Acceptance Criteria
1. **WHEN** TextRenderer supports streaming **THEN** it **MUST**:
   - Set `supportsStreaming = true` in renderer interface
   - Implement `updateStream(message, delta)` method
   - Use TextStreamingHandler for processing updates
   - Maintain state between streaming updates

2. **WHEN** streaming updates arrive **THEN** it **MUST**:
   - Process updates through streaming handler
   - Apply incremental markdown rendering for ready content
   - Maintain smooth visual flow without jarring changes
   - Show streaming indicator when appropriate

3. **WHEN** markdown is applied incrementally **THEN** it **MUST**:
   - Preserve existing DOM structure where possible
   - Apply updates without full re-render when possible
   - Maintain focus and selection states
   - Handle cursor positioning correctly

4. **WHEN** streaming completes **THEN** it **MUST**:
   - Apply final markdown rendering pass
   - Remove streaming indicators
   - Trigger completion events
   - Prepare for syntax highlighting (Phase 3)

#### Definition of Done
- [ ] Enhanced TextRenderer with streaming support
- [ ] Incremental markdown rendering working
- [ ] Smooth visual transitions verified
- [ ] Integration tests with streaming scenarios
- [ ] Performance tests for streaming updates

#### Testing Requirements
```typescript
// Example test cases
describe('Enhanced TextRenderer', () => {
  test('handles streaming updates correctly', async () => {
    const message = { content: 'Hello', isStreaming: true };
    const { rerender } = render(TextRenderer, { message });
    
    await rerender({ 
      message: { content: 'Hello world.', isStreaming: true }
    });
    
    // Verify incremental update
    expect(screen.getByText('Hello world.')).toBeInTheDocument();
  });
  
  test('applies final rendering on completion', async () => {
    const message = { content: '# Title\n\nContent', isStreaming: false };
    const { container } = render(TextRenderer, { message });
    
    expect(container.querySelector('h1')).toHaveTextContent('Title');
    expect(container.querySelector('p')).toHaveTextContent('Content');
  });
});
```

---

### RMR-P2-004: Mobile Interaction Optimizations
**Priority**: High  
**Estimated Effort**: 3 story points  
**Dependencies**: RMR-P2-003  

#### Description
Optimize touch interactions and responsive design for mobile devices, ensuring smooth expand/collapse and reading experience.

#### Acceptance Criteria
1. **WHEN** on mobile devices **THEN** expand/collapse **MUST**:
   - Provide minimum 44px touch targets
   - Support touch without requiring hover states
   - Provide visual feedback for touch interactions
   - Prevent text selection during rapid taps

2. **WHEN** viewing content on small screens **THEN** it **MUST**:
   - Apply responsive typography (16px minimum)
   - Optimize line length for readability (45-75 characters)
   - Provide adequate spacing between interactive elements
   - Support portrait and landscape orientations

3. **WHEN** scrolling on mobile **THEN** it **MUST**:
   - Maintain smooth 60fps scrolling performance
   - Preserve scroll position during expand/collapse
   - Handle momentum scrolling correctly
   - Support native mobile scrolling behaviors

4. **WHEN** animations run on mobile **THEN** they **MUST**:
   - Respect user's reduced motion preferences
   - Use hardware acceleration where possible
   - Complete within 300ms for touch responsiveness
   - Degrade gracefully on low-end devices

#### Definition of Done
- [ ] Touch target optimization completed
- [ ] Responsive design testing on multiple devices
- [ ] Performance testing on low-end mobile devices
- [ ] Accessibility testing with mobile screen readers
- [ ] Cross-platform mobile testing (iOS/Android)

---

## Phase 3: Syntax Highlighting (Sprint 5)

### RMR-P3-001: Integrate Prism.js for Code Highlighting
**Priority**: High  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P2-004  

#### Description
Add syntax highlighting for code blocks using Prism.js with language detection and theme support.

#### Acceptance Criteria
1. **WHEN** Prism.js is integrated **THEN** it **MUST**:
   - Support 20+ common programming languages
   - Automatically detect language from code fence syntax
   - Apply highlighting only to `<code>` elements with language classes
   - Load language definitions on demand for performance

2. **WHEN** code blocks are highlighted **THEN** they **MUST**:
   - Apply syntax highlighting within 50ms of rendering
   - Support popular languages (JavaScript, Python, TypeScript, etc.)
   - Fall back gracefully for unsupported languages
   - Maintain accessibility with proper ARIA labels

3. **WHEN** themes are applied **THEN** they **MUST**:
   - Support light and dark theme variants
   - Respect user's OS theme preference
   - Provide consistent contrast ratios (WCAG AA)
   - Allow customization through CSS custom properties

4. **WHEN** large code blocks are processed **THEN** they **MUST**:
   - Maintain performance with 1000+ line files
   - Use efficient highlighting algorithms
   - Support lazy loading for performance
   - Provide loading indicators for large files

#### Definition of Done
- [ ] Prism.js integration with core languages
- [ ] Theme support with light/dark variants
- [ ] Performance testing with large code blocks
- [ ] Language detection working correctly
- [ ] Accessibility compliance for highlighted code

#### Testing Requirements
```typescript
// Example test cases
describe('Prism.js Integration', () => {
  test('highlights JavaScript code correctly', () => {
    const message = { 
      content: '```javascript\nconst hello = "world";\n```' 
    };
    const { container } = render(TextRenderer, { message });
    
    const codeBlock = container.querySelector('code.language-javascript');
    expect(codeBlock).toBeInTheDocument();
    expect(codeBlock?.querySelector('.token.keyword')).toHaveTextContent('const');
  });
  
  test('handles unknown languages gracefully', () => {
    const message = { 
      content: '```unknown\nsome code\n```' 
    };
    const { container } = render(TextRenderer, { message });
    
    const codeBlock = container.querySelector('code');
    expect(codeBlock).toHaveTextContent('some code');
  });
});
```

---

### RMR-P3-002: Add Copy-to-Clipboard Functionality
**Priority**: Medium  
**Estimated Effort**: 3 story points  
**Dependencies**: RMR-P3-001  

#### Description
Add copy-to-clipboard buttons for code blocks with visual feedback and keyboard shortcuts.

#### Acceptance Criteria
1. **WHEN** code blocks are rendered **THEN** they **MUST**:
   - Display a copy button in the top-right corner
   - Show copy button on hover/focus (desktop) or always (mobile)
   - Provide keyboard access (Tab navigation)
   - Include appropriate ARIA labels for accessibility

2. **WHEN** copy button is clicked **THEN** it **MUST**:
   - Copy raw code content to clipboard (not HTML)
   - Show success feedback for 2 seconds
   - Handle clipboard API permissions correctly
   - Fall back to selection method if clipboard API unavailable

3. **WHEN** keyboard shortcuts are used **THEN** they **MUST**:
   - Support Ctrl+C when code block is focused
   - Provide visual indication of focused code block
   - Work with screen readers and keyboard navigation
   - Not interfere with other page shortcuts

4. **WHEN** copy operation fails **THEN** it **MUST**:
   - Show error message to user
   - Log error details for debugging
   - Provide alternative (select text) option
   - Maintain consistent UI state

#### Definition of Done
- [ ] Copy buttons added to all code blocks
- [ ] Clipboard API integration working
- [ ] Keyboard shortcut support
- [ ] Visual feedback for copy operations
- [ ] Cross-browser testing for clipboard functionality

---

### RMR-P3-003: Performance Optimization for Large Content
**Priority**: Medium  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P3-002  

#### Description
Optimize performance for large markdown documents and multiple code blocks through lazy loading and efficient rendering.

#### Acceptance Criteria
1. **WHEN** large documents are rendered **THEN** performance **MUST**:
   - Maintain <100ms initial render time for 10,000 character documents
   - Use virtual scrolling for documents >50KB
   - Lazy load syntax highlighting for off-screen code blocks
   - Debounce highlighting updates during rapid scrolling

2. **WHEN** multiple code blocks exist **THEN** highlighting **MUST**:
   - Process visible code blocks first
   - Queue off-screen blocks for later processing
   - Cancel queued work when component unmounts
   - Use Web Workers for heavy highlighting tasks

3. **WHEN** memory usage is monitored **THEN** it **MUST**:
   - Clean up highlighted code when scrolled out of view
   - Prevent memory leaks from Prism.js workers
   - Limit concurrent highlighting operations to 3
   - Garbage collect unused language definitions

4. **WHEN** performance degrades **THEN** the system **MUST**:
   - Fall back to plain text rendering
   - Show performance warning to user
   - Log performance metrics for monitoring
   - Provide option to disable highlighting

#### Definition of Done
- [ ] Performance benchmarks meeting targets
- [ ] Lazy loading implementation working
- [ ] Memory usage optimization verified
- [ ] Fallback mechanisms tested
- [ ] Performance monitoring integration

---

## Phase 4: Full Message Type Support (Sprint 6-7)

### RMR-P4-001: Implement ReasoningRenderer Component
**Priority**: High  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P3-003  

#### Description
Create a renderer for AI reasoning/thinking messages that displays in a collapsible container with height constraints and scroll functionality.

#### Acceptance Criteria
1. **WHEN** `ReasoningRenderer.svelte` is implemented **THEN** it **MUST**:
   - Implement `MessageRenderer<ReasoningMessageDto>` interface
   - Set `supportsStreaming = false` and `supportsCollapse = true`
   - Use `ExpandableContainer` with title "Reasoning"
   - Display content in a constrained height container (300px max)

2. **WHEN** reasoning content is displayed **THEN** it **MUST**:
   - Show content in monospace font for readability
   - Provide vertical scrolling when content exceeds height
   - Scroll to bottom when new content is added
   - Preserve formatting and line breaks

3. **WHEN** expand/collapse occurs **THEN** it **MUST**:
   - Auto-collapse when no longer the latest message
   - Allow manual expansion by user click
   - Show "Reasoning" title when collapsed
   - Animate transitions smoothly

4. **WHEN** accessibility is tested **THEN** it **MUST**:
   - Provide screen reader announcements for state changes
   - Support keyboard navigation for expand/collapse
   - Include proper ARIA labels and descriptions
   - Work with high contrast mode

#### Definition of Done
- [ ] ReasoningRenderer component implemented
- [ ] Height constraints and scrolling working
- [ ] Auto-collapse behavior verified
- [ ] Accessibility compliance tested
- [ ] Integration with RendererRegistry completed

#### Testing Requirements
```typescript
// Example test cases
describe('ReasoningRenderer', () => {
  test('displays reasoning content with height constraints', () => {
    const message = { 
      content: 'Let me think through this step by step...',
      messageType: 'reasoning'
    };
    const { container } = render(ReasoningRenderer, { message });
    
    const contentArea = container.querySelector('.reasoning-content');
    expect(contentArea).toHaveStyle('max-height: 300px');
    expect(contentArea).toHaveStyle('overflow-y: auto');
  });
  
  test('auto-collapses when not latest', async () => {
    const { rerender } = render(ReasoningRenderer, { 
      message: { content: 'Thinking...' },
      isLatest: true 
    });
    
    await rerender({ isLatest: false });
    
    expect(screen.queryByText('Thinking...')).not.toBeVisible();
    expect(screen.getByText('Reasoning')).toBeVisible();
  });
});
```

---

### RMR-P4-002: Create ToolCallRenderer Component
**Priority**: High  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P4-001  

#### Description
Implement a renderer for tool call messages that shows tool name and expandable arguments with support for custom renderers.

#### Acceptance Criteria
1. **WHEN** `ToolCallRenderer.svelte` is implemented **THEN** it **MUST**:
   - Implement `MessageRenderer<ToolsCallMessageDto>` interface
   - Set `supportsStreaming = false` and `supportsCollapse = true`
   - Display tool name prominently in header
   - Show expandable arguments section

2. **WHEN** tool calls are displayed **THEN** they **MUST**:
   - Show tool name in header (e.g., "search_web")
   - Display collapsed by default unless latest message
   - Show argument count in header (e.g., "3 arguments")
   - Format arguments as key-value pairs when expanded

3. **WHEN** custom renderers are available **THEN** they **MUST**:
   - Check for tool-specific custom renderer first
   - Fall back to default argument display if no custom renderer
   - Allow custom renderers to override display completely
   - Pass tool metadata to custom renderers

4. **WHEN** arguments are displayed **THEN** they **MUST**:
   - Show argument names and values clearly
   - Handle complex argument types (objects, arrays)
   - Provide syntax highlighting for code arguments
   - Support copy-to-clipboard for argument values

#### Definition of Done
- [ ] ToolCallRenderer component implemented
- [ ] Custom renderer plugin system integrated
- [ ] Argument formatting working correctly
- [ ] Copy functionality for arguments
- [ ] Tool-specific renderer examples created

#### Testing Requirements
```typescript
// Example test cases
describe('ToolCallRenderer', () => {
  test('displays tool name and argument count', () => {
    const message = {
      toolName: 'search_web',
      arguments: { query: 'test', limit: 5 },
      messageType: 'tool_call'
    };
    const { getByText } = render(ToolCallRenderer, { message });
    
    expect(getByText('search_web')).toBeInTheDocument();
    expect(getByText(/2 arguments/)).toBeInTheDocument();
  });
  
  test('uses custom renderer when available', () => {
    const customRenderer = {
      canRender: () => true,
      render: () => Promise.resolve({ component: MockCustomComponent })
    };
    
    registerCustomRenderer('search_web', customRenderer);
    
    const message = { toolName: 'search_web', arguments: {} };
    render(ToolCallRenderer, { message });
    
    expect(screen.getByTestId('custom-renderer')).toBeInTheDocument();
  });
});
```

---

### RMR-P4-003: Build ToolResultRenderer Component
**Priority**: High  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P4-002  

#### Description
Create a renderer for tool result messages that displays results with custom rendering support and height constraints.

#### Acceptance Criteria
1. **WHEN** `ToolResultRenderer.svelte` is implemented **THEN** it **MUST**:
   - Implement `MessageRenderer<ToolsCallResultMessageDto>` interface
   - Set `supportsStreaming = false` and `supportsCollapse = true`
   - Display results with appropriate formatting
   - Support custom result renderers

2. **WHEN** tool results are displayed **THEN** they **MUST**:
   - Show result status (success, error, warning)
   - Display result data with proper formatting
   - Apply height constraints (400px max) with scrolling
   - Show result metadata (execution time, etc.)

3. **WHEN** custom result renderers exist **THEN** they **MUST**:
   - Check for result-type-specific renderer
   - Support file, image, table, and chart renderers
   - Fall back to JSON formatting for unknown types
   - Allow renderer plugins to override display

4. **WHEN** errors occur in results **THEN** they **MUST**:
   - Display error messages clearly
   - Show stack traces in expandable sections
   - Provide troubleshooting suggestions
   - Allow reporting of tool errors

#### Definition of Done
- [ ] ToolResultRenderer component implemented
- [ ] Custom result renderer system working
- [ ] Error handling and display completed
- [ ] Height constraints and scrolling functional
- [ ] Result type detection implemented

---

### RMR-P4-004: Create UsageRenderer Component
**Priority**: Medium  
**Estimated Effort**: 2 story points  
**Dependencies**: RMR-P4-003  

#### Description
Implement a small pill component that displays token usage information for the conversation turn.

#### Acceptance Criteria
1. **WHEN** `UsageRenderer.svelte` is implemented **THEN** it **MUST**:
   - Implement `MessageRenderer<UsageMessageDto>` interface
   - Set `supportsStreaming = false` and `supportsCollapse = false`
   - Display as small, unobtrusive pill component
   - Show aggregated token usage for the turn

2. **WHEN** usage information is displayed **THEN** it **MUST**:
   - Show input and output token counts
   - Display total cost if available
   - Use compact, readable format (e.g., "1.2K tokens")
   - Include model information in tooltip

3. **WHEN** styling is applied **THEN** it **MUST**:
   - Use minimal visual footprint
   - Integrate with conversation theme
   - Support light and dark modes
   - Remain readable at small sizes

4. **WHEN** interaction occurs **THEN** it **MUST**:
   - Show detailed breakdown on hover/click
   - Display cost calculation methodology
   - Support copying usage data
   - Provide link to billing information

#### Definition of Done
- [ ] UsageRenderer component implemented
- [ ] Compact pill design completed
- [ ] Detailed breakdown functionality working
- [ ] Theme integration verified
- [ ] Usage calculation accuracy tested

---

## Phase 5: Advanced Features (Sprint 8)

### RMR-P5-001: Custom Renderer Plugin System
**Priority**: Medium  
**Estimated Effort**: 6 story points  
**Dependencies**: RMR-P4-004  

#### Description
Create a comprehensive plugin system that allows registration of custom renderers for specific tool types and result formats.

#### Acceptance Criteria
1. **WHEN** plugin system is implemented **THEN** it **MUST**:
   - Support dynamic registration of custom renderers
   - Provide clear plugin development API
   - Include plugin lifecycle management
   - Support plugin dependencies and versioning

2. **WHEN** plugins are registered **THEN** they **MUST**:
   - Validate plugin interface compliance
   - Handle registration conflicts gracefully
   - Support plugin priority ordering
   - Allow plugin unregistration

3. **WHEN** custom renderers are used **THEN** they **MUST**:
   - Receive proper context and data
   - Support all standard message features
   - Handle errors without breaking the system
   - Provide fallback to default renderers

4. **WHEN** plugin examples are provided **THEN** they **MUST**:
   - Include file viewer plugin
   - Include image gallery plugin
   - Include data table plugin
   - Include chart visualization plugin

#### Definition of Done
- [ ] Plugin system architecture implemented
- [ ] Plugin development documentation created
- [ ] Example plugins working correctly
- [ ] Plugin testing framework created
- [ ] Plugin marketplace integration ready

---

### RMR-P5-002: Advanced Animations and Transitions
**Priority**: Low  
**Estimated Effort**: 4 story points  
**Dependencies**: RMR-P5-001  

#### Description
Polish the user experience with advanced animations, micro-interactions, and smooth transitions.

#### Acceptance Criteria
1. **WHEN** animations are enhanced **THEN** they **MUST**:
   - Use spring-based animations for natural feel
   - Support reduced motion preferences
   - Maintain 60fps performance
   - Provide consistent timing across interactions

2. **WHEN** micro-interactions are added **THEN** they **MUST**:
   - Provide feedback for all user actions
   - Use subtle hover and focus effects
   - Include loading states for async operations
   - Support haptic feedback on supported devices

3. **WHEN** transitions occur **THEN** they **MUST**:
   - Maintain spatial relationships
   - Use appropriate easing functions
   - Complete within optimal timing windows
   - Respect accessibility guidelines

#### Definition of Done
- [ ] Animation library integration completed
- [ ] Micro-interactions implemented throughout
- [ ] Performance optimization verified
- [ ] Accessibility compliance maintained

---

### RMR-P5-003: Final Performance Optimization
**Priority**: High  
**Estimated Effort**: 5 story points  
**Dependencies**: RMR-P5-002  

#### Description
Final optimization pass to ensure excellent performance with large datasets and complex conversations.

#### Acceptance Criteria
1. **WHEN** performance is optimized **THEN** the system **MUST**:
   - Handle 200+ messages without degradation
   - Maintain <100ms response times for interactions
   - Use <50MB memory for typical conversations
   - Support efficient virtual scrolling

2. **WHEN** monitoring is implemented **THEN** it **MUST**:
   - Track key performance metrics
   - Provide performance analytics dashboard
   - Alert on performance regressions
   - Support performance debugging tools

#### Definition of Done
- [ ] Performance targets achieved and verified
- [ ] Monitoring system implemented
- [ ] Optimization documentation completed
- [ ] Performance testing automated

---

## Conclusion

This task breakdown provides clear, actionable items with specific acceptance criteria that development teams can implement incrementally. Each task includes:

- **Clear scope and boundaries**
- **Specific, testable acceptance criteria** 
- **Comprehensive testing requirements**
- **Performance and accessibility considerations**
- **Dependencies and integration points**

The tasks are designed to be completed in order, with each building upon the previous work while maintaining system integrity throughout the development process.
