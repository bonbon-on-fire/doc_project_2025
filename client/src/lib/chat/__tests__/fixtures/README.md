# SSE Stream Fixture-Based Testing

## Overview

This directory contains a fixture-based testing framework for SSE (Server-Sent Events) stream processing. The framework allows for declarative, reusable test scenarios that validate complex streaming behaviors.

## Architecture

### Core Components

1. **Test Fixtures** (`sseStreamFixtures.ts`)
   - Defines reusable SSE stream scenarios
   - Contains validation functions for each event in a stream
   - Provides base expectations and debug helpers

2. **Test Utilities** (`fixtureTestUtils.ts`)
   - Implements the fixture runner
   - Handles test execution options (parallel, sequential, verbose)
   - Provides assertion helpers and reporting

3. **Stream Test Harness** (`../streamTestUtils.ts`)
   - Processes SSE events through message handlers
   - Captures state snapshots at each event
   - Manages stores and streaming state

## Usage

### Creating a New Fixture

```typescript
export const MY_NEW_FIXTURE: SSETestFixture = {
  id: 'my-test-scenario',
  description: 'What this test validates',
  tags: ['feature', 'regression'],
  streamContent: `...SSE event stream...`,
  expectations: {
    0: {
      eventType: 'init',
      description: 'Initial state',
      validate: (state) => {
        expect(state.chat).toBeTruthy();
      }
    },
    1: {
      eventType: 'messageupdate',
      description: 'Message streaming',
      validate: (state) => {
        // Your validations here
      },
      debug: (state) => {
        // Optional debug logging
      }
    }
  }
};
```

### Writing Tests with Fixtures

```typescript
import { createFixtureTest } from './fixtures/fixtureTestUtils';
import { SSE_FIXTURES } from './fixtures/sseStreamFixtures';

describe('My Feature Tests', () => {
  test(
    'should handle my scenario',
    createFixtureTest(SSE_FIXTURES.MY_FIXTURE, {
      verbose: true,    // Enable detailed logging
      failFast: false   // Continue on failures
    })
  );
});
```

### Batch Testing

Run multiple fixtures as a suite:

```typescript
import { createBatchFixtureTest, getFixturesByTag } from './fixtures';

test(
  'all regression tests should pass',
  createBatchFixtureTest(
    getFixturesByTag('regression'),
    { failFast: false }
  )
);
```

## Fixture Structure

### SSETestFixture Interface

- `id`: Unique identifier for the fixture
- `description`: Human-readable description
- `streamContent`: Raw SSE event stream
- `tags`: Array of tags for categorization
- `expectations`: Object mapping event indices to validations
- `finalValidation`: Optional final state validation
- `setup/teardown`: Optional lifecycle hooks

### SSEEventExpectation Interface

- `eventType`: Expected SSE event type
- `description`: What's being validated
- `validate`: Function to validate state
- `debug`: Optional debug output
- `skip`: Optional skip condition

## Best Practices

### 1. Use Base Expectations

Leverage reusable validation functions:

```typescript
BASE_EXPECTATIONS.chatInitialized(state);
BASE_EXPECTATIONS.textMessageExists('msg-id', 'expected text')(state);
BASE_EXPECTATIONS.streamingActive('msg-id')(state);
```

### 2. Use Debug Helpers

For troubleshooting:

```typescript
DEBUG_HELPERS.logMessageStructure('msg-id')(state);
DEBUG_HELPERS.logStreamingState(state);
DEBUG_HELPERS.warnIf(condition, 'Warning message')(state);
```

### 3. Tag Fixtures Appropriately

Use consistent tags for easy filtering:
- `bug`: Known bug reproductions
- `regression`: Regression tests
- `feature`: Feature validation
- `edge-case`: Edge case handling

### 4. Document Complex Validations

Add clear descriptions to expectations:

```typescript
expectations: {
  2: {
    eventType: 'messageupdate',
    description: 'Critical: Text content must be preserved when tools start',
    validate: (state) => {
      // Detailed comment explaining the validation
      const message = state.chat!.messages.find(/*...*/);
      expect(message.text).toBeDefined();
    }
  }
}
```

## Extending the Framework

### Adding New Base Expectations

In `sseStreamFixtures.ts`:

```typescript
export const BASE_EXPECTATIONS = {
  // ... existing expectations
  
  myNewExpectation: (param: string) => (state: TestSnapshot) => {
    // Your reusable validation logic
    expect(state.something).toBe(param);
  }
};
```

### Adding New Debug Helpers

```typescript
export const DEBUG_HELPERS = {
  // ... existing helpers
  
  logCustomState: (state: TestSnapshot) => {
    console.log('Custom State:', {
      // Your custom logging
    });
  }
};
```

### Creating Parameterized Tests

```typescript
const createScenarioFixture = (messageCount: number) => ({
  id: `scenario-${messageCount}`,
  description: `Test with ${messageCount} messages`,
  streamContent: generateStream(messageCount),
  expectations: generateExpectations(messageCount)
});

const scenarios = [1, 5, 10].map(count => ({
  name: `${count} messages`,
  params: count
}));

const tests = createParameterizedFixtureTest(
  createScenarioFixture,
  scenarios
);
```

## Debugging Tips

1. **Enable Verbose Mode**: Set `verbose: true` in test options
2. **Use Debug Functions**: Add debug callbacks to expectations
3. **Check Snapshots**: Examine state snapshots at each event
4. **Use Single Event Tests**: Test specific events with `only: [eventIndex]`
5. **Review Handler Logs**: Check console output from message handlers

## Common Issues

### Issue: Text content lost during streaming
- Ensure message IDs match expected patterns
- Verify handlers preserve existing message properties
- Check streaming snapshot management

### Issue: Message ordering problems
- Validate sequence numbers in expectations
- Check timestamp ordering
- Verify handler message insertion logic

### Issue: Failed validations
- Enable verbose logging to see actual state
- Use debug helpers to log message structure
- Check for timing-related issues in async operations