# Online Research - Existing Solutions and Design Patterns

## AI Tool Use Interface Patterns (Anthropic Claude)

### Key Insights:
- **Tool Use Workflow**: Standard pattern is Request → AI Decision → Tool Execution → Result Processing → Final Response
- **Two Types of Tools**:
  - Client tools: Execute on user's system, require implementation
  - Server tools: Execute on AI provider's servers, automatic integration
- **UI Considerations**: Need to handle tool_use requests, tool_result responses, and streaming completions

### Relevant Design Patterns:
- Tool use requests have specific structure: tool name, input schema, descriptions
- Results can be complex and may need custom rendering
- Token usage tracking is important for cost awareness

## UI Design Patterns for Complex Content (Nielsen Norman Group)

### Key Insights for Accordions/Expandable Content:
- **When to Use Accordions**: 
  - Users need only a few key pieces of content
  - Content is in very small spaces (mobile)
  - Information is loosely related
- **When NOT to Use Accordions**:
  - Users need most/all content to answer questions
  - Content is highly related and relevant
  - Better to show all content and let users scroll

### Design Principles:
- **Interaction Cost**: Every click has a cost - must be worthwhile
- **Progressive Disclosure**: Show overview, allow expansion when needed
- **Mental Models**: Users expect consistent behavior
- **Accessibility**: Hidden content must be properly coded for screen readers

### Best Practices for Our Use Case:
- Allow multiple sections to be open simultaneously
- Maintain state (opened/closed) until user changes it
- Provide clear headings that indicate content value
- Consider default expanded state for important content (like reasoning in debugging scenarios)

## Implications for Rich Message Rendering:

### Text Messages:
- Markdown rendering is standard
- Streaming/incremental updates should be smooth

### Reasoning/Thinking Messages:
- Default collapsed makes sense (follows progressive disclosure)
- Max height with scroll is good compromise
- Auto-collapse when new content streams (reduces cognitive load)

### Tool Call Messages:
- Show tool name by default (good overview)
- Expandable for details follows accordion best practices
- Extensible components allow custom rendering per tool type

### Tool Results:
- Max height + expandable follows established patterns
- Custom renderers provide flexibility for different data types

### Usage Messages:
- Pill format is non-intrusive
- Provides valuable cost awareness without cognitive overhead

## Technology Considerations:
- Component-based architecture supports extensibility
- State management for expand/collapse preferences
- Accessibility considerations for screen readers
- Print-friendly considerations for expanded content
