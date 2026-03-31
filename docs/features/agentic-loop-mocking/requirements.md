# Feature Specification: Agentic Loop Mocking Enhancement (Simplified)

## High-Level Overview

This specification describes an enhancement to the existing LLM mocking system to support autonomous agentic loops through a simplified conversation-aware approach. The enhancement enables the test mocking layer to simulate multi-step agent workflows by analyzing conversation history and executing instructions sequentially based on the count of assistant responses.

## Problem Statement

### Current Limitations
The existing test mocking system (`TestSseMessageHandler`) can only process single instruction sets per message. This limitation prevents testing of:
- Multi-step agent workflows that require sequential operations
- Autonomous agents that make decisions based on previous outputs
- Complex testing scenarios involving tool chains and iterative refinement
- End-to-end testing of agentic behaviors without actual LLM calls

### Need for Enhancement
Modern AI applications increasingly rely on autonomous agents that:
- Execute multi-step workflows independently
- Chain multiple tool calls and reasoning steps
- Iterate based on intermediate results
- Continue processing until task completion

Testing these scenarios requires a mocking system that can simulate the entire autonomous loop, not just individual LLM calls.

## Solution: Conversation-Aware Instruction Execution

### Core Innovation
Instead of embedding instructions in responses and extracting them (complex), we leverage the fact that the full conversation history is sent with each LLM request. This allows us to:
1. Find the instruction chain in the conversation history
2. Count the number of assistant responses since the instruction chain
3. Use this count to index into the instruction array
4. Execute the appropriate instruction without any embedding or extraction

## High-Level Requirements

1. **Conversation Analysis**: Analyze full conversation history to find instruction chains
2. **Turn Counting**: Count assistant responses to determine instruction index
3. **Sequential Execution**: Process instructions in order based on response count
4. **Natural Termination**: Stop when instruction index exceeds chain length
5. **Stateless Processing**: Each request independently analyzes conversation
6. **Backward Compatibility**: Maintain support for existing single-instruction format

## Existing Solutions

### Current Implementation
- **TestSseMessageHandler.cs**: Intercepts LLM calls and extracts instructions from user messages
- **SseStreamHttpContent.cs**: Generates SSE streams based on instruction plans
- **Instruction Tags**: Uses `<|instruction_start|>` and `<|instruction_end|>` markers
- **Support for**: Text messages, reasoning, and tool calls with configurable lengths

### Industry Patterns (2024-2025)
- **Conversation-Aware Testing**: Using conversation history for test state
- **Index-Based Execution**: Simple counting instead of complex state machines
- **Deterministic Replay**: Predictable behavior based on conversation position

## Detailed Requirements

### Requirement 1: Instruction Chain Discovery
**User Story**: As a test system, I need to find the active instruction chain by scanning the conversation history.

#### Acceptance Criteria:
1. [ ] WHEN processing a request THEN SHALL scan messages array from newest to oldest
2. [ ] WHEN multiple instruction chains exist THEN SHALL use the LAST (most recent) one found
3. [ ] WHEN instruction tags found in user message THEN SHALL parse and validate the instruction chain
4. [ ] WHEN no instruction chain found THEN SHALL fall back to default behavior

#### Example:
```json
{
  "messages": [
    {
      "role": "user",
      "content": "Previous interaction..."
    },
    {
      "role": "assistant",
      "content": "Previous response..."
    },
    {
      "role": "user", 
      "content": "Start workflow\n<|instruction_start|>\n{\"instruction_chain\": [{\"id\": \"step-1\", ...}, {\"id\": \"step-2\", ...}]}\n<|instruction_end|>"
    },
    {
      "role": "assistant",
      "content": "First response"
    }
  ]
}
```

### Requirement 2: Assistant Response Counting
**User Story**: As a test system, I need to count assistant responses after the instruction chain to determine which instruction to execute.

#### Acceptance Criteria:
1. [ ] WHEN instruction chain found THEN SHALL count assistant messages after it
2. [ ] WHEN assistant sends multiple tool calls in one response THEN SHALL count as ONE turn
3. [ ] WHEN counting responses THEN SHALL only count role="assistant" messages
4. [ ] WHEN count equals N THEN SHALL execute instruction at index N in the chain

#### Counting Rules:
- Start counting from 0 after the instruction chain message
- Each assistant response increments count by 1
- Tool responses (role="tool") do NOT increment count
- User messages do NOT increment count

### Requirement 3: Instruction Execution Based on Count
**User Story**: As a test system, I need to execute the correct instruction based on the assistant response count.

#### Acceptance Criteria:
1. [ ] WHEN count is 0 THEN SHALL execute first instruction (index 0)
2. [ ] WHEN count is N THEN SHALL execute instruction at index N
3. [ ] WHEN count >= chain length THEN SHALL use fallback behavior
4. [ ] WHEN executing instruction THEN SHALL generate response according to instruction spec

#### Example Flow:
```
Conversation State:          Count:  Executes:
User (with chain [A,B,C])    -       -
Assistant responds           0       Instruction A
User/Tool responds           0       -
Assistant responds           1       Instruction B
User/Tool responds           1       -
Assistant responds           2       Instruction C
User/Tool responds           2       -
Assistant responds           3       Fallback (no more instructions)
```

### Requirement 4: Instruction Chain Format
**User Story**: As a test author, I want to define instruction chains that will be executed sequentially.

#### Acceptance Criteria:
1. [ ] WHEN defining instructions THEN SHALL use array format
2. [ ] WHEN chain is defined THEN SHALL maintain order throughout execution
3. [ ] WHEN instruction lacks required fields THEN SHALL skip with warning
4. [ ] WHEN chain is empty THEN SHALL use fallback behavior

#### Data Structure:
```json
{
  "instruction_chain": [
    {
      "id": "step-1",
      "id_message": "initial-analysis",
      "messages": [
        {
          "text_message": { "length": 50 }
        }
      ]
    },
    {
      "id": "step-2",
      "id_message": "tool-execution",
      "messages": [
        {
          "tool_call": [
            {
              "name": "analyze_data",
              "args": { "data": "sample" }
            }
          ]
        }
      ]
    },
    {
      "id": "step-3",
      "id_message": "summary",
      "messages": [
        {
          "text_message": { "length": 100 }
        }
      ]
    }
  ]
}
```

### Requirement 5: Natural Loop Termination
**User Story**: As a test system, I need to gracefully handle when all instructions have been executed.

#### Acceptance Criteria:
1. [ ] WHEN response count exceeds chain length THEN SHALL use fallback message generation
2. [ ] WHEN no instruction at index THEN SHALL generate default response
3. [ ] WHEN termination occurs THEN SHALL NOT throw errors
4. [ ] WHEN terminated THEN SHALL continue responding to further messages with fallback

### Requirement 6: Error Handling
**User Story**: As a test system, I need robust error handling for malformed or invalid instruction chains.

#### Acceptance Criteria:
1. [ ] WHEN instruction JSON is malformed THEN SHALL log error and use fallback
2. [ ] WHEN chain parsing fails THEN SHALL continue with default behavior
3. [ ] WHEN response counting fails THEN SHALL default to index 0
4. [ ] WHEN unexpected message structure THEN SHALL handle gracefully

### Requirement 7: Backward Compatibility
**User Story**: As an existing test user, I want my current tests to continue working without modification.

#### Acceptance Criteria:
1. [ ] WHEN no instruction_chain property THEN SHALL use existing single instruction format
2. [ ] WHEN using old format THEN SHALL execute as single instruction (no counting)
3. [ ] WHEN mixing formats THEN SHALL prefer chain format if present
4. [ ] WHEN both formats present THEN SHALL use instruction_chain

## Implementation Approach

### Phase 1: Conversation Analysis
1. Modify `TestSseMessageHandler` to scan full message history
2. Implement logic to find last instruction chain in conversation
3. Add response counting logic after instruction chain message

### Phase 2: Index-Based Execution
1. Calculate assistant response count since instruction chain
2. Use count to index into instruction array
3. Pass selected instruction to `SseStreamHttpContent`

### Phase 3: Testing & Validation
1. Add unit tests for conversation analysis
2. Test response counting with various message patterns
3. Validate backward compatibility with existing tests

## Key Simplifications from Previous Design

### Removed Complexity
- ❌ No instruction embedding in responses
- ❌ No extraction of instructions from generated content
- ❌ No parsing of assistant responses
- ❌ No hidden markers in output
- ❌ No state management between requests

### Added Simplicity
- ✅ Simple conversation history analysis
- ✅ Basic counting of assistant responses
- ✅ Direct indexing into instruction array
- ✅ Clean, unmodified responses
- ✅ Easier debugging and testing

## Example Scenarios

### Scenario 1: Three-Step Analysis Workflow
```json
{
  "instruction_chain": [
    {
      "id": "analyze",
      "messages": [
        { "text_message": { "length": 30 } }
      ]
    },
    {
      "id": "process", 
      "messages": [
        { "tool_call": [{ "name": "process_data", "args": {} }] }
      ]
    },
    {
      "id": "summarize",
      "messages": [
        { "text_message": { "length": 50 } }
      ]
    }
  ]
}
```

**Execution Flow:**
1. User sends message with instruction chain
2. Assistant response #0 → Execute "analyze" (text message)
3. User/tool responds
4. Assistant response #1 → Execute "process" (tool call)
5. Tool responds with data
6. Assistant response #2 → Execute "summarize" (text message)
7. User responds
8. Assistant response #3 → Fallback (chain exhausted)

### Scenario 2: Tool Chain with Reasoning
```json
{
  "instruction_chain": [
    {
      "id": "fetch",
      "reasoning": { "length": 20 },
      "messages": [
        { "tool_call": [{ "name": "fetch_data", "args": {} }] }
      ]
    },
    {
      "id": "validate",
      "messages": [
        { "tool_call": [{ "name": "validate", "args": {} }] }
      ]
    },
    {
      "id": "report",
      "messages": [
        { "text_message": { "length": 100 } }
      ]
    }
  ]
}
```

## Testing Considerations

1. **Unit Tests**: Test conversation scanning, response counting, index calculation
2. **Integration Tests**: Verify complete multi-step execution flows
3. **Edge Cases**: Empty chains, missing messages, malformed JSON
4. **Performance**: Ensure minimal overhead for conversation analysis
5. **Debugging**: Clear logging of chain discovery and response counting

## Success Metrics

1. Support for unlimited instruction chain length
2. Zero impact on existing single-instruction tests
3. Clear error messages for invalid chains
4. Deterministic execution based on conversation position
5. Simplified implementation compared to embedding approach

## Implementation Notes

### Code Location
- Primary changes in `TestSseMessageHandler.cs`
- No changes needed to `SseStreamHttpContent.cs`
- Minimal changes to `InstructionPlan` class

### Key Algorithm
```csharp
// Pseudocode for the core logic
1. Extract all messages from request
2. Find last user message with instruction_start tags
3. If found:
   a. Parse instruction_chain array
   b. Count assistant messages after instruction message
   c. Get instruction at index[count]
   d. Execute instruction or use fallback if index out of bounds
4. If not found:
   - Use existing single instruction logic
```

## Migration Path

1. Deploy with backward compatibility enabled
2. Update test documentation with new chain format
3. Provide examples of multi-step test scenarios
4. Gradual adoption by test authors
5. No breaking changes to existing tests

## Dependencies

- Existing `TestSseMessageHandler` and `SseStreamHttpContent` classes
- Current instruction parsing infrastructure
- SSE streaming mechanism
- Test mode configuration

## Advantages of This Approach

1. **Simplicity**: No complex embedding or extraction logic
2. **Transparency**: Responses are clean, instructions visible in conversation
3. **Debuggability**: Easy to see instruction chain and count position
4. **Reliability**: No parsing of generated content that could fail
5. **Maintainability**: Fewer moving parts, clearer flow
6. **Testability**: Easier to unit test counting logic vs extraction logic