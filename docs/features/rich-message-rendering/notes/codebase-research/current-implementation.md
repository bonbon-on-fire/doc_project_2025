# Current Implementation Analysis

## Current Message Handling

### Backend (Server)
- **ChatService.cs**: Uses `AchieveAi.LmDotnetTools.LmCore.Messages` namespace
- **Current message types being handled**:
  - `TextUpdateMessage` - already being processed in streaming (line 495, 569)
  - `TextMessage` - used in conversion (line 813)
- **Streaming Architecture**: Server-Sent Events (SSE) with event types:
  - `init`: Chat/message initialization
  - `chunk`: Streaming content updates
  - `complete`: Final message completion
  - `error`: Error handling

### Frontend (Client)
- **Current Components**:
  - `MessageBubble.svelte`: Basic message rendering with simple markdown-like formatting
  - `MessageList.svelte`: Container for message list
  - `StreamingMessage.svelte`: Dedicated streaming message component
- **Current Message Interface** (`MessageDto`):
  ```typescript
  interface MessageDto {
    id: string;
    chatId: string;
    role: 'user' | 'assistant' | 'system';
    content: string;        // Currently just a string
    timestamp: Date;
    sequenceNumber: number;
  }
  ```

### Current Limitations
1. **Single Content Type**: Messages only have a `content` string field
2. **Basic Formatting**: Only simple markdown-like replacements
3. **No Message Type Differentiation**: All messages treated as text
4. **No Extensible Rendering**: Fixed component structure
5. **No Tool Call Support**: No handling of tool calls or results
6. **No Usage Tracking**: No token consumption display

## LmDotnetTools Integration
- **Already Integrated**: The server already uses LmDotnetTools for AI interactions
- **Message Types Available**: The library likely provides:
  - `TextMessage` / `TextUpdateMessage`
  - `ReasoningMessage` / `ReasoningUpdateMessage`  
  - `ToolsCallMessage` / `ToolsCallUpdateMessage`
  - `ToolsCallResponseMessage`
  - `UsageMessage`

## Current Architecture Strengths
1. **Streaming Support**: Already handles real-time message updates
2. **SSE Infrastructure**: Event-based communication established
3. **Component Architecture**: Svelte components for easy extension
4. **Type Safety**: TypeScript interfaces for message handling
5. **State Management**: Svelte stores for real-time updates

## Required Changes for Rich Message Support

### Backend Changes Needed
1. **Expand Message Model**: Support different message types/content structures
2. **Enhanced SSE Events**: Add events for different message types
3. **Message Type Detection**: Handle different LmDotnetTools message types
4. **Serialization**: Convert complex message types to JSON for client

### Frontend Changes Needed
1. **Enhanced MessageDto**: Support different content types
2. **Component System**: Extensible rendering based on message type
3. **New Components**: 
   - `ReasoningMessage.svelte`
   - `ToolCallMessage.svelte`
   - `ToolResultMessage.svelte`
   - `UsageMessage.svelte`
4. **Message Type Router**: Route to appropriate component based on type
5. **Expand/Collapse Logic**: State management for expandable content

## Implementation Strategy
1. **Phase 1**: Extend backend message handling to support LmDotnetTools message types
2. **Phase 2**: Create new frontend message components
3. **Phase 3**: Implement message type routing and rendering
4. **Phase 4**: Add expand/collapse functionality and custom renderers
5. **Phase 5**: Integrate streaming updates for all message types
