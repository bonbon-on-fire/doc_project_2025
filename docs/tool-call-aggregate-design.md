# FunctionCallMiddleware Enhancement Design Document

## Executive Summary

This document outlines the design for enhancing the FunctionCallMiddleware to support complete tool call lifecycle management, including streaming tool calls, executing functions, capturing results, and streaming results back to the client. The implementation will handle incremental updates, out-of-order result delivery, and proper message persistence while maintaining compatibility with the existing message rendering system.

## Current State Analysis

### LmDotnetTools Components

1. **FunctionCallMiddleware**: Already processes `ToolsCallMessage` and creates `ToolsCallAggregateMessage` containing both calls and results
2. **MessageUpdateJoinerMiddleware**: Joins streaming updates into complete messages
3. **ToolsCallAggregateMessage**: Existing message type that combines `ToolsCallMessage` and `ToolsCallResultMessage`

### Server Implementation

1. Currently persists `ToolsCallMessage` but not `ToolsCallAggregateMessage`
2. Streams `ToolsCallUpdateMessage` chunks to client
3. Lacks support for streaming tool results back to client

### Client Implementation  

1. Has handlers for tool call streaming updates
2. Can accumulate tool call chunks into complete messages
3. Lacks renderer for aggregate messages with results

## Design Goals

1. **Complete Tool Call Lifecycle**: Support tool call initiation, execution, and result streaming
2. **Incremental Updates**: Handle streaming tool calls and results that may arrive out of order
3. **Proper Persistence**: Store complete messages with both calls and results
4. **Client Rendering**: Display tool calls and their results in real-time
5. **Callback Mechanism**: Notify when tool results become available for streaming

## Proposed Architecture

### 1. FunctionCallMiddleware Enhancements

```csharp
public interface IToolResultCallback
{
    Task OnToolResultAvailable(string toolCallId, ToolCallResult result);
}

public class FunctionCallMiddleware : IStreamingMiddleware
{
    private IToolResultCallback? _resultCallback;
    
    public FunctionCallMiddleware WithResultCallback(IToolResultCallback callback)
    {
        _resultCallback = callback;
        return this;
    }
    
    // Enhanced ExecuteToolCallAsync to notify on completion
    private async Task<ToolCallResult> ExecuteToolCallAsync(ToolCall toolCall)
    {
        // ... existing execution logic ...
        
        var result = new ToolCallResult(toolCall.ToolCallId, resultContent);
        
        // Notify callback if registered
        if (_resultCallback != null)
        {
            await _resultCallback.OnToolResultAvailable(toolCall.ToolCallId, result);
        }
        
        return result;
    }
}
```

### 2. Server-Side Message DTOs

```csharp
// New DTO for tool results
public class ToolResultMessageDto : MessageDto
{
    [JsonPropertyName("toolResults")]
    public required ToolCallResult[] ToolResults { get; set; }
}

// New aggregate DTO combining calls and results
public class ToolsCallAggregateMessageDto : MessageDto  
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; set; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; set; }
}

// Update polymorphic serialization
[JsonDerivedType(typeof(ToolResultMessageDto), typeDiscriminator: "tool_result")]
[JsonDerivedType(typeof(ToolsCallAggregateMessageDto), typeDiscriminator: "tools_aggregate")]
```

### 3. Server Streaming Events

```csharp
// New event for streaming tool results
public record ToolResultStreamEvent : StreamChunkEvent
{
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }
    
    [JsonPropertyName("result")]
    public required string Result { get; init; }
    
    [JsonPropertyName("isError")]
    public bool IsError { get; init; }
}

// Event for complete aggregate message
public record ToolsAggregateEvent : MessageEvent
{
    [JsonPropertyName("toolCalls")]
    public required ToolCall[] ToolCalls { get; init; }
    
    [JsonPropertyName("toolResults")]
    public ToolCallResult[]? ToolResults { get; init; }
}
```

### 4. ChatService Integration

```csharp
public class ChatService : IChatService, IToolResultCallback
{
    private readonly Dictionary<string, TaskCompletionSource<ToolCallResult>> _pendingResults = new();
    
    public async Task OnToolResultAvailable(string toolCallId, ToolCallResult result)
    {
        // Stream result to client immediately
        if (StreamChunkReceived != null)
        {
            await StreamChunkReceived(new ToolResultStreamEvent
            {
                ChatId = _currentChatId,
                MessageId = $"{_currentMessageId}_result_{toolCallId}",
                Kind = "tool_result",
                ToolCallId = toolCallId,
                Result = result.Result,
                IsError = result.Result.StartsWith("Error"),
                Done = true,
                SequenceNumber = _nextSequence++
            });
        }
        
        // Mark result as available
        if (_pendingResults.TryGetValue(toolCallId, out var tcs))
        {
            tcs.SetResult(result);
        }
    }
    
    private async Task<int> PersistFullMessage(string chatId, IMessage message, string fullMessageId)
    {
        // ... existing cases ...
        
        ToolsCallAggregateMessage aggregate => new MessageRecord
        {
            Id = fullMessageId,
            ChatId = chatId,
            Role = aggregate.Role.ToString(),
            Kind = "tools_aggregate",
            TimestampUtc = timestamp,
            SequenceNumber = nextSequence,
            MessageJson = JsonSerializer.Serialize<MessageDto>(
                new ToolsCallAggregateMessageDto
                {
                    Id = fullMessageId,
                    ChatId = chatId,
                    Role = aggregate.Role.ToString(),
                    Timestamp = timestamp,
                    SequenceNumber = nextSequence,
                    ToolCalls = aggregate.ToolsCallMessage.ToolCalls.ToArray(),
                    ToolResults = aggregate.ToolsCallResult?.ToolCallResults?.ToArray()
                }, MessageSerializationOptions.Default)
        }
    }
}
```

### 5. Client-Side Type Definitions

```typescript
// New DTOs matching server
export interface ToolResultMessageDto extends MessageDto {
    toolResults: ToolCallResult[];
    messageType?: 'tool_result';
}

export interface ToolsCallAggregateMessageDto extends MessageDto {
    toolCalls: ToolCall[];
    toolResults?: ToolCallResult[];
    messageType?: 'tools_aggregate';
}

export interface ToolCallResult {
    toolCallId: string;
    result: string;
}
```

### 6. Client Message Handler

```typescript
export class ToolsAggregateMessageHandler extends BaseMessageHandler {
    private toolCalls: Map<string, ToolCall> = new Map();
    private toolResults: Map<string, ToolCallResult> = new Map();
    
    processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
        let snapshot = this.getSnapshot(messageId);
        
        // Handle tool call updates
        if (envelope.kind === 'tools_call_update') {
            const update = envelope.payload.toolCallUpdate;
            this.updateToolCall(messageId, update);
        }
        
        // Handle tool result updates
        else if (envelope.kind === 'tool_result') {
            const result = envelope.payload as ToolResultStreamPayload;
            this.toolResults.set(result.toolCallId, {
                toolCallId: result.toolCallId,
                result: result.result
            });
            
            // Update snapshot with new result
            snapshot = this.updateSnapshot(messageId, {
                toolResults: Array.from(this.toolResults.values())
            });
        }
        
        return snapshot;
    }
    
    private updateToolCall(messageId: string, update: ToolCallUpdate) {
        const toolCallId = update.tool_call_id || `${messageId}_tool_${update.index}`;
        
        let toolCall = this.toolCalls.get(toolCallId) || {
            id: toolCallId,
            name: '',
            args: {}
        };
        
        // Accumulate updates
        if (update.function_name) {
            toolCall.name = update.function_name;
        }
        
        if (update.function_args) {
            toolCall.argsJson = (toolCall.argsJson || '') + update.function_args;
            try {
                toolCall.args = JSON.parse(toolCall.argsJson);
            } catch {
                // Keep partial JSON
            }
        }
        
        this.toolCalls.set(toolCallId, toolCall);
        
        // Update snapshot
        this.updateSnapshot(messageId, {
            toolCalls: Array.from(this.toolCalls.values())
        });
    }
}
```

### 7. Client Renderer Component

```svelte
<!-- ToolsCallAggregateRenderer.svelte -->
<script lang="ts">
    import type { ToolsCallAggregateMessageDto } from '$lib/types/chat';
    import { writable } from 'svelte/store';
    
    export let message: ToolsCallAggregateMessageDto;
    export let isStreaming = false;
    
    // Track which results have been received
    const resultMap = writable(new Map<string, any>());
    
    $: {
        if (message.toolResults) {
            const map = new Map();
            message.toolResults.forEach(result => {
                map.set(result.toolCallId, result);
            });
            resultMap.set(map);
        }
    }
</script>

<div class="tools-aggregate">
    <div class="tool-calls">
        <h4>Tool Calls</h4>
        {#each message.toolCalls as toolCall}
            <div class="tool-call" data-id={toolCall.id}>
                <div class="tool-name">{toolCall.name}</div>
                <pre class="tool-args">{JSON.stringify(toolCall.args, null, 2)}</pre>
                
                {#if $resultMap.has(toolCall.id)}
                    <div class="tool-result">
                        <h5>Result:</h5>
                        <pre>{$resultMap.get(toolCall.id).result}</pre>
                    </div>
                {:else if isStreaming}
                    <div class="pending">Executing...</div>
                {/if}
            </div>
        {/each}
    </div>
</div>

<style>
    .tools-aggregate {
        padding: 1rem;
        border: 1px solid var(--border-color);
        border-radius: 0.5rem;
    }
    
    .tool-call {
        margin: 0.5rem 0;
        padding: 0.5rem;
        background: var(--bg-secondary);
        border-radius: 0.25rem;
    }
    
    .tool-result {
        margin-top: 0.5rem;
        padding: 0.5rem;
        background: var(--bg-success);
        border-radius: 0.25rem;
    }
    
    .pending {
        color: var(--text-muted);
        font-style: italic;
    }
</style>
```

### 8. Renderer Registry Update

```typescript
// client/src/lib/renderers/registry.ts
import ToolsCallAggregateRenderer from './ToolsCallAggregateRenderer.svelte';

export const messageRenderers = {
    'text': TextRenderer,
    'reasoning': ReasoningRenderer,
    'tool_call': ToolCallRenderer,
    'tool_result': ToolResultRenderer,
    'tools_aggregate': ToolsCallAggregateRenderer, // New renderer
    'usage': UsageRenderer
};
```

## Message Flow Sequence

### Streaming Flow

1. **Tool Call Initiation**
   - LLM generates tool calls via streaming
   - FunctionCallMiddleware builds `ToolsCallMessage` from updates
   - Server streams `ToolsCallUpdateMessage` chunks to client
   - Client accumulates chunks into tool call display

2. **Tool Execution** 
   - FunctionCallMiddleware executes tool calls as they complete
   - Callback notifies ChatService of each result
   - ChatService streams `ToolResultStreamEvent` to client
   - Client updates display with results as they arrive

3. **Message Completion**
   - FunctionCallMiddleware creates `ToolsCallAggregateMessage`
   - ChatService persists complete aggregate message
   - Client receives final `ToolsAggregateEvent`
   - Client renders complete message with all calls and results

### Out-of-Order Handling

Results may arrive before or after tool calls complete streaming:

1. **Early Results**: Store results in map, display when tool call arrives
2. **Late Results**: Update existing tool call display with result
3. **Missing Results**: Show timeout/error state after threshold

## Database Schema

```sql
-- Message storage with new 'tools_aggregate' kind
CREATE TABLE messages (
    id TEXT PRIMARY KEY,
    chat_id TEXT NOT NULL,
    role TEXT NOT NULL,
    kind TEXT NOT NULL, -- 'text', 'reasoning', 'tools_call', 'tools_aggregate'
    timestamp_utc DATETIME NOT NULL,
    sequence_number INTEGER NOT NULL,
    message_json TEXT NOT NULL -- Polymorphic JSON with type discriminator
);
```

## Migration Strategy

1. **Phase 1**: Add new DTOs and streaming events without breaking existing flow
2. **Phase 2**: Update FunctionCallMiddleware to use callbacks
3. **Phase 3**: Integrate ChatService with callback mechanism
4. **Phase 4**: Add client handlers and renderers
5. **Phase 5**: Enable full aggregate message flow

## Testing Strategy

### Unit Tests

1. **FunctionCallMiddleware**: Test callback invocation on result
2. **ChatService**: Test result streaming and persistence
3. **Client Handlers**: Test accumulation of calls and results
4. **Renderers**: Test display of partial and complete states

### Integration Tests

1. **End-to-End Flow**: Test complete tool call lifecycle
2. **Out-of-Order**: Test results arriving before calls complete
3. **Error Handling**: Test failed tool executions
4. **Persistence**: Verify aggregate messages stored correctly

### Performance Tests

1. **Concurrent Tools**: Test multiple tool calls executing in parallel
2. **Large Results**: Test streaming of large tool results
3. **Network Interruption**: Test resilience to connection issues

## Security Considerations

1. **Tool Result Validation**: Sanitize tool results before display
2. **Rate Limiting**: Limit concurrent tool executions per chat
3. **Timeout Protection**: Cancel long-running tool executions
4. **Error Isolation**: Prevent one tool failure from affecting others

## Future Enhancements

1. **Tool Progress Updates**: Stream intermediate progress for long-running tools
2. **Result Caching**: Cache tool results for identical calls
3. **Retry Logic**: Automatic retry for transient tool failures
4. **Tool Chaining**: Support tools that depend on other tool results
5. **Parallel Execution Groups**: Define tool execution dependencies

## Implementation Checklist

- [ ] Create new DTOs on server (`ToolResultMessageDto`, `ToolsCallAggregateMessageDto`)
- [ ] Add IToolResultCallback interface to FunctionCallMiddleware
- [ ] Implement callback mechanism in FunctionCallMiddleware
- [ ] Update ChatService to implement IToolResultCallback
- [ ] Add streaming events for tool results
- [ ] Update message persistence for aggregate messages
- [ ] Create client type definitions for new DTOs
- [ ] Implement ToolsAggregateMessageHandler
- [ ] Create ToolsCallAggregateRenderer component
- [ ] Update renderer registry
- [ ] Add unit tests for all components
- [ ] Add integration tests for complete flow
- [ ] Update documentation

## Conclusion

This design provides a comprehensive solution for handling the complete tool call lifecycle, from initiation through execution to result display. The architecture supports incremental updates, out-of-order delivery, and proper persistence while maintaining compatibility with the existing message system. The implementation can be rolled out in phases to minimize risk and ensure stability.