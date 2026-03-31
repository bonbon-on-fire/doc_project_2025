# Migration Notes: Tool Call to ToolsCallAggregateMessage

## Current State
- Server can send both `tool_call` messages and `ToolsCallAggregateMessage`
- Client handlers support both message types
- E2E tests expect `tool_call` messages

## Migration Strategy

### Phase 1: Dual Support (Current)
✅ Client supports both message types:
- `toolCallMessageHandler` - handles individual tool calls
- `toolsAggregateMessageHandler` - handles aggregate messages with results

### Phase 2: Server Migration
When server starts sending ToolsCallAggregateMessage:
1. Update server to send aggregate messages for tool interactions
2. Tool calls and results will be combined in single message type

### Phase 3: Test Updates Required

#### E2E Tests to Update:
- `tool-calls.test.ts` - expects `.tool-call-container`
- `tool-calls-persistence.test.ts` - expects tool call UI elements
- Any test looking for tool call specific selectors

#### Changes Needed:
```typescript
// Old selector
await page.locator('.tool-call-container').first()

// New selector  
await page.locator('.tools-aggregate-container').first()
```

#### UI Elements Mapping:
- `.tool-call-container` → `.tools-aggregate-container`
- `.tool-name` → stays same
- `.tool-arguments` → `.tool-args`
- New: `.tool-result` (wasn't present before)
- New: `.status-badge` (shows pending/complete/error)

### Phase 4: Backward Compatibility
Consider keeping both handlers active:
- Old messages continue to work
- New aggregate messages provide enhanced functionality
- Gradual migration without breaking changes

## Testing Strategy

### For E2E Tests:
1. **Don't create new E2E tests** for aggregate messages until server sends them
2. **Keep existing tests** working with current message format
3. **When server switches**, update tests to match new UI

### For Unit Tests:
✅ Already complete - handlers tested independently

## Notes
- The client is ready for aggregate messages
- Server needs to be updated to use IToolResultCallback
- E2E tests should match what server actually sends
- No need to test features that aren't live yet