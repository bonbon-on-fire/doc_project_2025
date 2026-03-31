# Revised Design: Simplified Agentic Loop Implementation

## Overview
This design document details the implementation of the simplified conversation-aware approach for agentic loop mocking. The key innovation is using conversation history analysis instead of instruction embedding.

## Core Components

### 1. TestSseMessageHandler Modifications

#### Current Implementation
```csharp
// Current: Only looks at latest user message
private static string? ExtractLatestUserMessage(JsonElement root)
{
    // ... finds last user message ...
    return latest;
}

// Current: Extracts single instruction from latest message
private static (InstructionPlan? plan, string fallback) TryParseInstructionPlan(string userMessage)
{
    // ... extracts instruction between tags ...
    return (plan, fallback);
}
```

#### New Implementation
```csharp
// New: Analyze full conversation history
private static (InstructionChain? chain, int assistantResponseCount) AnalyzeConversation(JsonElement root)
{
    if (!root.TryGetProperty("messages", out var messages)) 
        return (null, 0);
    
    InstructionChain? lastChain = null;
    int chainMessageIndex = -1;
    int assistantCount = 0;
    
    // Step 1: Find the last instruction chain
    var messageArray = messages.EnumerateArray().ToList();
    for (int i = messageArray.Count - 1; i >= 0; i--)
    {
        var msg = messageArray[i];
        if (IsUserMessage(msg))
        {
            var chain = ExtractInstructionChain(GetContent(msg));
            if (chain != null)
            {
                lastChain = chain;
                chainMessageIndex = i;
                break;
            }
        }
    }
    
    // Step 2: Count assistant responses after the chain
    if (lastChain != null && chainMessageIndex >= 0)
    {
        for (int i = chainMessageIndex + 1; i < messageArray.Count; i++)
        {
            var msg = messageArray[i];
            if (IsAssistantMessage(msg))
            {
                assistantCount++;
            }
        }
    }
    
    return (lastChain, assistantCount);
}

// New: Extract instruction chain (not just single instruction)
private static InstructionChain? ExtractInstructionChain(string content)
{
    const string startTag = "<|instruction_start|>";
    const string endTag = "<|instruction_end|>";
    
    var start = content.IndexOf(startTag);
    var end = content.IndexOf(endTag);
    
    if (start < 0 || end <= start) return null;
    
    var json = content.Substring(start + startTag.Length, end - start - startTag.Length);
    
    try
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        if (root.TryGetProperty("instruction_chain", out var chainEl))
        {
            return ParseInstructionChain(chainEl);
        }
        
        // Backward compatibility: single instruction becomes chain of one
        return new InstructionChain { Instructions = new[] { ParseSingleInstruction(root) } };
    }
    catch
    {
        return null;
    }
}
```

### 2. Updated SendAsync Method

```csharp
protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, 
    CancellationToken cancellationToken)
{
    // ... existing validation ...
    
    var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;
    
    // Analyze conversation history
    var (chain, responseCount) = AnalyzeConversation(root);
    
    HttpContent content;
    if (chain != null)
    {
        // Get instruction based on response count
        InstructionPlan? instruction = null;
        if (responseCount < chain.Instructions.Length)
        {
            instruction = chain.Instructions[responseCount];
        }
        
        if (instruction != null)
        {
            // Execute the instruction at current index
            content = new SseStreamHttpContent(
                instructionPlan: instruction,
                model: model,
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);
        }
        else
        {
            // Chain exhausted, use fallback
            content = new SseStreamHttpContent(
                userMessage: "Chain completed - using fallback response",
                model: model,
                reasoningFirst: false,
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);
        }
    }
    else
    {
        // No chain found, use existing fallback logic
        var latest = ExtractLatestUserMessage(root) ?? string.Empty;
        var (plan, fallbackMessage) = TryParseInstructionPlan(latest);
        
        if (plan != null)
        {
            // Backward compatibility: single instruction
            content = new SseStreamHttpContent(
                instructionPlan: plan,
                model: model,
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);
        }
        else
        {
            // Default fallback
            content = new SseStreamHttpContent(
                userMessage: fallbackMessage,
                model: model,
                reasoningFirst: fallbackMessage.Contains("\nReason:"),
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);
        }
    }
    
    // ... return response ...
}
```

### 3. New Data Structures

```csharp
public class InstructionChain
{
    public InstructionPlan[] Instructions { get; set; } = Array.Empty<InstructionPlan>();
}

// InstructionPlan remains unchanged
public class InstructionPlan
{
    public string Id { get; set; } = string.Empty;
    public string IdMessage { get; set; } = string.Empty;
    public int? ReasoningLength { get; set; }
    public List<InstructionMessage> Messages { get; set; } = new();
}
```

## Execution Flow Diagram

```
┌─────────────────────────────────────┐
│  LLM Request with Full Conversation │
└─────────────────┬───────────────────┘
                  │
                  ▼
        ┌─────────────────────┐
        │ Scan for Last Chain │
        └─────────┬───────────┘
                  │
        ┌─────────▼──────────┐
        │ Chain Found?       │
        └──┬──────────────┬──┘
           │ Yes          │ No
           ▼              ▼
   ┌───────────────┐  ┌──────────────┐
   │Count Assistant│  │Use Single    │
   │Responses      │  │Instruction   │
   └───────┬───────┘  │or Fallback   │
           │          └──────────────┘
           ▼
   ┌───────────────┐
   │Get Instruction│
   │at Index[count]│
   └───────┬───────┘
           │
   ┌───────▼────────┐
   │Index Valid?    │
   └──┬─────────┬───┘
      │ Yes     │ No
      ▼         ▼
┌──────────┐ ┌──────────┐
│Execute   │ │Use       │
│Instruction│ │Fallback  │
└──────────┘ └──────────┘
```

## Example Conversation Analysis

### Input Conversation:
```json
{
  "messages": [
    {"role": "user", "content": "Hello"},
    {"role": "assistant", "content": "Hi there!"},
    {"role": "user", "content": "Start test\n<|instruction_start|>{\"instruction_chain\":[...]}<|instruction_end|>"},
    {"role": "assistant", "content": "First response"},     // Count: 0
    {"role": "tool", "content": "Tool output"},
    {"role": "assistant", "content": "Second response"},    // Count: 1
    {"role": "user", "content": "Continue"},
    {"role": "assistant", "content": "Third response"}      // Count: 2
  ]
}
```

### Analysis Result:
- Chain found at index 2
- Assistant responses after chain: 3
- Current request will execute instruction at index 3 (or fallback if chain has only 3 items)

## Benefits Comparison

### Old Approach (Embedding)
```
Complexity: HIGH
- Parse response for embedded instructions
- Extract instructions from text/tool responses
- Handle malformed embeddings
- Complex state management
```

### New Approach (Conversation Analysis)
```
Complexity: LOW
- Count messages in array
- Simple array indexing
- No response parsing needed
- Stateless operation
```

## Testing Strategy

### Unit Tests
```csharp
[Test]
public void Should_Find_Last_Instruction_Chain()
{
    // Given: Multiple instruction chains in conversation
    // When: Analyzing conversation
    // Then: Returns the last chain found
}

[Test]
public void Should_Count_Only_Assistant_Responses()
{
    // Given: Mixed message types after chain
    // When: Counting responses
    // Then: Only assistant messages increment count
}

[Test]
public void Should_Handle_Chain_Exhaustion()
{
    // Given: Response count >= chain length
    // When: Getting instruction
    // Then: Uses fallback behavior
}
```

### Integration Tests
```csharp
[Test]
public async Task Should_Execute_Multi_Step_Workflow()
{
    // Given: 3-step instruction chain
    // When: Making sequential requests
    // Then: Each request executes correct instruction
}
```

## Performance Considerations

### Complexity Analysis
- **Finding chain**: O(n) where n = number of messages
- **Counting responses**: O(n) worst case
- **Overall**: O(n) linear with conversation length
- **Memory**: O(1) additional memory (no copying)

### Optimization Opportunities
1. Cache chain location if messages haven't changed
2. Start scan from a recent index if conversation is very long
3. Use binary search if messages have timestamps

## Error Handling

### Graceful Degradation
```csharp
try
{
    var (chain, count) = AnalyzeConversation(root);
    // ... normal flow ...
}
catch (Exception ex)
{
    Logger?.LogError(ex, "Failed to analyze conversation");
    // Fall back to single instruction mode
    return HandleSingleInstruction(root);
}
```

### Edge Cases Handled
1. Empty conversation → Use fallback
2. No instruction chain → Check for single instruction
3. Malformed JSON → Log and use fallback
4. Index out of bounds → Use fallback
5. Null/missing fields → Safe navigation with defaults

## Backward Compatibility

### Supported Formats
1. **New format**: `instruction_chain` array
2. **Old format**: Single instruction between tags
3. **No instructions**: Generate default response

### Migration Path
- No changes required to existing tests
- New tests can use chain format
- Both formats coexist peacefully

## Summary

This simplified design dramatically reduces complexity while maintaining all required functionality. The key insight is that conversation history already contains all the state we need - we just need to count and index, not embed and extract.