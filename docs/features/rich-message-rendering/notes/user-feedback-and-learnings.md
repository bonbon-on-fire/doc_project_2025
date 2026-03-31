# User Feedback and Learnings

## Initial Requirements (August 6, 2025)

### Message Update Types to Support:
1. **TextUpdateMessage** - incremental markdown rendering
2. **ReasoningUpdateMessage** - text with scroll box, collapsible 
3. **ToolsCallUpdateMessage** - tool name with expandable arguments
4. **ToolsCallResponseMessage** - custom component for results

### Completed Message Types to Support:
1. **TextMessage** - markdown rendering
2. **ReasoningMessage** - collapsible thinking/reasoning display
3. **ToolsCallMessage** - expandable tool calls with custom components
4. **ToolsCallAggregateMessage** - tool results with custom renderers
5. **UsageMessage** - token usage pill

### Detailed Requirements by Message Type:

#### TextMessages (and updates)
- Render in markdown format
- Updates should be incremental (streaming)

#### Reasoning/Thinking Messages  
- Render as text with max height constraint
- Scroll to bottom within constrained box
- User can expand if desired
- When next message starts streaming, collapse to single line ("Reasoning")

#### ToolsCallMessage
- Default: render tool name in single line with expand capability
- When expanded: show argument names
- **Key requirement**: Extensible element - can switch components based on tool type
- Component decides if collapsed or expanded by default

#### ToolsCallAggregate/Result Message
- Display tool call results
- Default: limited max height, expandable
- **Key requirement**: Result may have custom renderer

#### Usage Message
- Small pill showing token consumption
- Shows tokens consumed in all previous messages in a single turn

### High-Level Goals (inferred):
- Enhance chat experience with rich message types
- Support complex AI interactions (reasoning, tool use)
- Provide extensible/customizable rendering system
- Maintain clean, organized UI with expand/collapse functionality

## Performance & Interaction Priority: Balanced Approach

**User's Response (August 7, 2025):**
> "It's a balanced approach, which we're streaming a message we can be realtime responsive, but once the message completed, then we need to be polished (even if it's a bit slow).
>
> Most importantly, what should not happen is markdown is turned on at very end. It's OK to club few updates together (a sentence worth), before incrementally updating it with markdown based rendering.
>
> Note above is only true for markdown based rendering"

#### Key Technical Requirements
1. **Real-time responsiveness** during streaming
2. **Incremental markdown rendering** - club updates together (sentence worth) before applying markdown
3. **No jarring visual changes** - markdown should NOT be turned on only at the very end
4. **Polished rendering** after message completion (even if slightly slower)

#### Technical Implementation Implications
- Need buffering mechanism for text updates (sentence-level chunks)
- Incremental markdown parsing during streaming
- Different rendering strategies for different message types
- Post-completion enhancement phase

## Expand/Collapse Behavior

**User's Response (August 7, 2025):**
> "On thinking, we want the latest message to always be in expanded state, and when next message starts streaming it could collapse. This allows the user to see what's going on. But user can always go back and expand any message. The exception to this rule is 'TextMessage' or custom tool renderings, if they decide not to be collapsible (both call and result)."

### Key Interaction Rules

#### Default State Logic
1. **Latest message**: Always in expanded state (user can see what's happening)
2. **Previous messages**: Auto-collapse when next message starts streaming
3. **User control**: Can always manually expand any collapsed message
4. **Exceptions**: TextMessage and custom tool renderings may opt out of collapsibility

#### Message Type Behaviors
- **ReasoningMessage**: Expanded when latest → auto-collapse when superseded → user can re-expand
- **ToolsCallMessage**: Expanded when latest → auto-collapse when superseded → user can re-expand
- **ToolsCallResultMessage**: Expanded when latest → auto-collapse when superseded → user can re-expand (unless custom renderer opts out)
- **TextMessage**: Not collapsible (always fully visible)
- **UsageMessage**: Not collapsible (small pill format)

#### Technical Requirements
- State management for expand/collapse per message
- Auto-collapse trigger when new message starts streaming
- User interaction handlers for manual expand/collapse
- Opt-out mechanism for custom renderers

## Accessibility & Mobile Experience

**User's Response (August 7, 2025):**
> "I'd just prefer 'A'" (Mobile experience priority)

### Focus Area: Mobile Experience
- Primary concern is touch device interaction patterns
- Expand/collapse should work well on mobile vs desktop
- May need different interaction patterns for touch vs mouse
- Mobile-first approach for expand/collapse functionality

## Implementation Timeline

**User's Response (August 7, 2025):**
> "C" (MVP First approach)

### Implementation Strategy: MVP First
- **Phase 1**: Get basic expand/collapse working with simple rendering
- **Phase 2**: Enhance with markdown rendering for text messages
- **Phase 3**: Add syntax highlighting with Prism.js
- **Phase 4**: Implement full message type support (reasoning, tool calls, etc.)
- **Phase 5**: Add custom renderers and advanced features

### Benefits of MVP Approach
- Faster time to initial value
- Early user feedback on core interactions
- Reduced risk through incremental delivery
- Allows refinement of UX patterns before full complexity
