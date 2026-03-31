# Agentic Loop Mocking - Implementation Tasks (Simplified)

## Overview

This document contains the minimal task list for implementing the simplified agentic loop mocking feature. The entire implementation requires **~100 lines of code** in a **single file** and can be completed in **1-2 hours**.

## Task Summary

- **Total Tasks**: 5
- **Total Effort**: 1-2 hours
- **Files to Modify**: 1 (`TestSseMessageHandler.cs`)
- **Lines of Code**: ~100
- **Complexity**: Low

---

## Implementation Tasks

### Task 1: Add Conversation Analysis Method
**Priority**: Critical  
**Estimated Time**: 30 minutes  
**Lines of Code**: ~40

#### Description
Add a new method to analyze the full conversation history, find the instruction chain, and count assistant responses.

#### Subtasks
- [ ] Create `AnalyzeConversation` method signature
- [ ] Implement backward scan for instruction chain
- [ ] Implement assistant response counting logic
- [ ] Extract instruction at calculated index

#### Implementation
```csharp
private static (InstructionPlan? plan, int assistantResponseCount) AnalyzeConversation(JsonElement root)
{
    if (!root.TryGetProperty("messages", out var messages)) 
        return (null, 0);
    
    InstructionPlan[]? chain = null;
    int chainMessageIndex = -1;
    int assistantCount = 0;
    
    // Find last instruction chain
    var messageArray = messages.EnumerateArray().ToList();
    for (int i = messageArray.Count - 1; i >= 0; i--)
    {
        // Check if user message with instruction tags
        // Extract chain if found
        // Break on first match
    }
    
    // Count assistant responses after chain
    if (chain != null && chainMessageIndex >= 0)
    {
        for (int i = chainMessageIndex + 1; i < messageArray.Count; i++)
        {
            // Count only assistant messages
        }
    }
    
    // Return instruction at index or null
    var instruction = (chain != null && assistantCount < chain.Length) 
        ? chain[assistantCount] 
        : null;
        
    return (instruction, assistantCount);
}
```

#### Requirements
- [ ] R1.1: Scan messages from newest to oldest
- [ ] R1.2: Use last (most recent) instruction chain found
- [ ] R2.1: Count only assistant responses (not user/tool)
- [ ] R3.1: Return instruction at index[count]

#### Tests
- [ ] Test 1: Finds instruction chain in conversation
- [ ] Test 2: Counts assistant responses correctly
- [ ] Test 3: Returns null when no chain found

---

### Task 2: Add Chain Extraction Method
**Priority**: Critical  
**Estimated Time**: 20 minutes  
**Lines of Code**: ~30

#### Description
Add a method to extract and parse instruction chains from user message content.

#### Subtasks
- [ ] Create `ExtractInstructionChain` method
- [ ] Parse JSON between instruction tags
- [ ] Convert to InstructionPlan array
- [ ] Handle backward compatibility

#### Implementation
```csharp
private static InstructionPlan[]? ExtractInstructionChain(string content)
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
            // Parse array of instructions
            var chain = new List<InstructionPlan>();
            foreach (var item in chainEl.EnumerateArray())
            {
                // Convert each to InstructionPlan
                // Reuse existing parsing logic
            }
            return chain.ToArray();
        }
        
        // Backward compatibility: single instruction
        return new[] { ParseSingleInstruction(root) };
    }
    catch (JsonException ex)
    {
        Logger?.LogError(ex, "Failed to parse instruction chain");
        throw; // Fail fast in test mode
    }
}
```

#### Requirements
- [ ] R1.3: Parse and validate instruction chain JSON
- [ ] R4.1: Use array format for chains
- [ ] R6.1: Throw exception on malformed JSON (fail-fast)
- [ ] R7.1: Support old single-instruction format

#### Tests
- [ ] Test 1: Parses valid instruction chain
- [ ] Test 2: Throws on malformed JSON
- [ ] Test 3: Handles single instruction (backward compat)

---

### Task 3: Modify SendAsync Method
**Priority**: Critical  
**Estimated Time**: 15 minutes  
**Lines of Code**: ~20

#### Description
Update the main request handler to use conversation analysis instead of just extracting the latest message.

#### Subtasks
- [ ] Replace latest message extraction with conversation analysis
- [ ] Add fallback for chain exhaustion
- [ ] Add detailed debug logging
- [ ] Maintain backward compatibility

#### Implementation
```csharp
protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
{
    // ... existing validation ...
    
    var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;
    
    // Analyze full conversation
    var (instruction, responseCount) = AnalyzeConversation(root);
    
    HttpContent content;
    if (instruction != null)
    {
        Logger?.LogInformation("Executing instruction {Index}: {Id}", 
            responseCount + 1, instruction.Id);
            
        content = new SseStreamHttpContent(
            instructionPlan: instruction,
            model: model,
            wordsPerChunk: WordsPerChunk,
            chunkDelayMs: ChunkDelayMs);
    }
    else
    {
        // Check for chain exhaustion vs no chain
        if (responseCount > 0)
        {
            Logger?.LogInformation("Chain exhausted after {Count} executions", responseCount);
            
            // Simple completion message
            var completion = new InstructionPlan(
                "completion", 
                null,
                new List<InstructionMessage> { InstructionMessage.ForText(5) }
            );
            
            content = new SseStreamHttpContent(
                instructionPlan: completion,
                model: model,
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);
        }
        else
        {
            // No chain found - use existing fallback logic
            var latest = ExtractLatestUserMessage(root) ?? string.Empty;
            var (plan, fallbackMessage) = TryParseInstructionPlan(latest);
            
            // ... existing single instruction logic ...
        }
    }
    
    // ... return response ...
}
```

#### Requirements
- [ ] R2.4: Execute instruction at index N when count equals N
- [ ] R3.3: Use fallback when count >= chain length
- [ ] R5.1: Generate completion message when exhausted
- [ ] R7.2: Maintain backward compatibility

#### Tests
- [ ] Test 1: Uses conversation analysis for chains
- [ ] Test 2: Falls back to single instruction when no chain
- [ ] Test 3: Generates completion when chain exhausted

---

### Task 4: Add Logging and Error Handling
**Priority**: High  
**Estimated Time**: 10 minutes  
**Lines of Code**: ~10

#### Description
Add comprehensive logging and fail-fast error handling as specified.

#### Subtasks
- [ ] Add chain discovery logging
- [ ] Add response counting logging
- [ ] Add instruction execution logging
- [ ] Add error logging with fail-fast behavior

#### Implementation
```csharp
// In AnalyzeConversation
Logger?.LogInformation("Found instruction chain with {Count} instructions at index {Index}", 
    chain.Length, chainMessageIndex);
Logger?.LogDebug("Counted {Count} assistant responses since chain", assistantCount);

// In SendAsync  
Logger?.LogInformation("Executing instruction {Index}/{Total}: {Id}", 
    responseCount + 1, chain?.Length ?? 0, instruction?.Id);
Logger?.LogInformation("Chain exhausted after {Count} executions, using fallback", 
    responseCount);

// In ExtractInstructionChain
catch (JsonException ex)
{
    Logger?.LogError(ex, "Failed to parse instruction chain");
    throw new InvalidOperationException("Malformed instruction chain", ex);
}
```

#### Requirements
- [ ] R6.2: Log errors and throw exceptions (fail-fast)
- [ ] Debug logging for chain discovery
- [ ] Info logging for instruction execution
- [ ] Error logging for parsing failures

#### Tests
- [ ] Test 1: Verify logging output in test scenarios
- [ ] Test 2: Verify exceptions thrown on errors

---

### Task 5: Write Tests
**Priority**: High  
**Estimated Time**: 25 minutes  

#### Description
Write unit and integration tests to verify the implementation.

#### Subtasks
- [ ] Write unit test for chain finding
- [ ] Write unit test for response counting
- [ ] Write unit test for instruction execution
- [ ] Write integration test for multi-step workflow

#### Test Implementations
```csharp
[Test]
public void Should_Find_Last_Instruction_Chain()
{
    // Given: Conversation with multiple chains
    var json = @"{
        ""messages"": [
            {""role"": ""user"", ""content"": ""old chain...""},
            {""role"": ""user"", ""content"": ""Start\n<|instruction_start|>{...}<|instruction_end|>""}
        ]
    }";
    
    // When: Analyzing conversation
    var (instruction, count) = AnalyzeConversation(JsonDocument.Parse(json).RootElement);
    
    // Then: Returns last chain
    Assert.NotNull(instruction);
}

[Test]
public void Should_Count_Only_Assistant_Responses()
{
    // Given: Mixed message types after chain
    // When: Counting responses
    // Then: Only assistant messages counted
}

[Test]
public async Task Should_Execute_Multi_Step_Workflow()
{
    // Given: 3-step instruction chain
    // When: Making sequential requests
    // Then: Each executes correct instruction
}
```

#### Requirements
- [ ] 100% code coverage for new methods
- [ ] Test all error scenarios
- [ ] Test backward compatibility
- [ ] Test multi-step execution flow

#### Tests
- [ ] Test 1: Chain discovery works correctly
- [ ] Test 2: Response counting is accurate
- [ ] Test 3: Instruction execution by index
- [ ] Test 4: Integration test passes

---

## Definition of Done

### Overall Completion Criteria
- [ ] All 5 tasks completed
- [ ] ~100 lines of code added/modified
- [ ] All tests passing
- [ ] Backward compatibility verified
- [ ] Logging output verified
- [ ] Performance < 5ms overhead

### Quality Checklist
- [ ] Code follows existing patterns
- [ ] No memory leaks or allocations
- [ ] Error handling is robust
- [ ] Logging is informative
- [ ] Tests provide good coverage

## Timeline

### Estimated Schedule (2 hours total)
- **Hour 1**: Tasks 1-3 (Core implementation)
- **Hour 2**: Tasks 4-5 (Logging, error handling, tests)

### Actual Time Tracking
- Task 1: _____ minutes
- Task 2: _____ minutes  
- Task 3: _____ minutes
- Task 4: _____ minutes
- Task 5: _____ minutes
- **Total**: _____ minutes

## Notes

This simplified implementation dramatically reduces complexity compared to the original design:
- No instruction embedding or extraction
- No state management between requests
- No new classes or dependencies
- Single file modification
- Simple counting algorithm

The entire feature can be implemented by a single developer in one focused session.