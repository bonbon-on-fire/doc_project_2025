# LmDotnetTools Message Types Analysis

## Available Message Types in LmDotnetTools

Based on the submodule code analysis, here are the message types available:

### Update Messages (Streaming)
1. **TextUpdateMessage** - Incremental text updates during streaming
2. **ReasoningUpdateMessage** - Incremental reasoning/thinking updates  
3. **ToolsCallUpdateMessage** - Incremental tool call construction

### Complete Messages
1. **TextMessage** - Complete text messages with `GetText()` method
2. **ReasoningMessage** - Reasoning/thinking content (may be in metadata)
3. **ToolsCallMessage** - Complete tool call messages with `ToolCalls` property
4. **ToolsCallResultMessage** - Tool execution results with `ToolCallResults` property
5. **ToolsCallAggregateMessage** - Combines ToolsCallMessage + ToolsCallResultMessage
6. **UsageMessage** - Token usage information with `Usage` property
7. **ImageMessage** - Image content messages

### Key Properties and Interfaces

#### IMessage (Base Interface)
- `Role` (User, Assistant, System, Tool)
- `FromAgent` (string)
- `GenerationId` (string)
- `Metadata` (ImmutableDictionary<string, object>)

#### ICanGetText Interface
- `GetText()` method for text content
- Used by TextMessage and other text-containing messages

#### ICanGetToolCalls Interface  
- `GetToolCalls()` method
- Used by ToolsCallMessage

#### ToolCall Structure
- `ToolCallId` (string)
- `FunctionName` (string) 
- `FunctionArgs` (string, JSON)

#### ToolCallResult Structure
- `ToolCallId` (string)
- `Result` (string)

#### Usage Structure (in UsageMessage)
- Token consumption data
- Typically includes input/output tokens, cost information

### Reasoning Content Handling
From the OpenAI provider code, reasoning can be stored in:
- `message.Metadata["reasoning"]` as string
- `message.Metadata["reasoning_details"]` as List<ReasoningDetail>
- Some messages may have dedicated reasoning content

### Current Server Integration Points

#### Already Handled in ChatService.cs
```csharp
// Line 495, 569 - TextUpdateMessage processing
if (message is TextUpdateMessage textMessage)
{
    var content = textMessage.Text;
    // ... streaming logic
}

// Line 813 - TextMessage creation
return new TextMessage
{
    Role = role,
    Text = message.Content,
    Metadata = ImmutableDictionary<string, object>.Empty
};
```

### Middleware Processing
The LmDotnetTools already has sophisticated middleware:

1. **MessageUpdateJoinerMiddleware** - Joins update messages into complete messages
2. **FunctionCallMiddleware** - Handles tool execution and aggregation
3. **UsageAccumulator** - Consolidates usage data across messages

### JSON Serialization Support
- Full type discrimination for serialization/deserialization
- Type discriminators: "text", "image", "tools_call", "tools_call_result", etc.
- Round-trip preservation of all message data

### Stream Processing Architecture
The middleware processes streams like this:
```
TextUpdateMessage → (accumulate) → TextMessage
ToolsCallUpdateMessage → (accumulate) → ToolsCallMessage → (execute) → ToolsCallAggregateMessage
ReasoningUpdateMessage → (accumulate) → ReasoningMessage
UsageMessage → (pass through + accumulate)
```

## Implementation Strategy for Rich Message Support

### Phase 1: Extend Server Message Processing
1. **Modify ChatService to handle all LmDotnetTools message types**
   - Update streaming logic to detect and handle different update types
   - Create proper SSE events for each message type
   - Serialize complex message structures for client

### Phase 2: Enhance Client Message Interface
1. **Extend MessageDto to support rich content**
   ```typescript
   interface MessageDto {
     id: string;
     chatId: string;
     role: 'user' | 'assistant' | 'system' | 'tool';
     messageType: 'text' | 'reasoning' | 'tool_call' | 'tool_result' | 'usage';
     content: string;              // For text content
     toolCalls?: ToolCallDto[];    // For tool calls
     toolResults?: ToolResultDto[]; // For tool results  
     reasoning?: string;           // For reasoning content
     usage?: UsageDto;            // For usage information
     timestamp: Date;
     sequenceNumber: number;
   }
   ```

### Phase 3: Create Message Type Components
1. **TextMessage Component** - Enhanced markdown rendering
2. **ReasoningMessage Component** - Collapsible thinking display
3. **ToolCallMessage Component** - Expandable tool calls with custom renderers
4. **ToolResultMessage Component** - Tool execution results display
5. **UsageMessage Component** - Token usage pill

### Phase 4: Message Router
Create intelligent routing based on messageType to appropriate components.

### Phase 5: Streaming Updates
Update SSE handling to support all message types with appropriate UI updates.
