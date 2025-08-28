import { describe, test, expect } from 'vitest';
import { SSEStreamTestHarness } from './streamTestUtils';
import { SSE_FIXTURES } from './fixtures/sseStreamFixtures';
import { createFixtureTest, type FixtureTestOptions } from './fixtures/fixtureTestUtils';

/**
 * Tool Call Processing Tests
 *
 * Migrated from sseStreamProcessing.test.ts to use fixture-based testing approach.
 * Tests complex tool call streams including error handling and state progression.
 */

describe('Tool Call Processing - Complete Stream with Error Handling', () => {
	const testOptions: FixtureTestOptions = {
		verbose: process.env.VERBOSE === 'true',
		failFast: false
	};

	// Use the fixture for the basic test
	test(
		'validates message state progression through complex tool call stream',
		createFixtureTest(SSE_FIXTURES.TOOL_CALL_WITH_ERROR, testOptions)
	);

	// Additional specific tests that weren't covered by the fixture
	test('validates streaming snapshots are properly maintained', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TOOL_CALL_WITH_ERROR.streamContent);

		// Process through tool call streaming (event 8 is where JSON completes)
		await harness.processUntilEvent(8);
		let state = harness.getCurrentState();

		// Should have snapshots for both text and tools_aggregate messages
		const snapshots = Object.keys(state.streamingState.streamingSnapshots);
		expect(snapshots).toContain('gen-1756406547:text');
		expect(snapshots).toContain('gen-1756406547'); // tools_aggregate uses base ID

		// After message complete events (event 12 is tools aggregate complete)
		await harness.processUntilEvent(12);
		state = harness.getCurrentState();

		// Completed messages should have their snapshots marked as complete
		const textSnapshot = state.streamingState.streamingSnapshots['gen-1756406547:text'];
		if (textSnapshot) {
			expect(textSnapshot.isStreaming).toBe(false);
			expect(textSnapshot.phase).toBe('complete');
		}

		// After stream complete (event 16), all snapshots should be cleared
		await harness.processUntilEvent(16);
		state = harness.getCurrentState();
		expect(Object.keys(state.streamingState.streamingSnapshots)).toHaveLength(0);
	});

	test('validates message sequencing and IDs are correct', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TOOL_CALL_WITH_ERROR.streamContent);
		await harness.processAll();
		const finalState = harness.getCurrentState();

		const messages = finalState.chat!.messages;

		// Check sequence numbers
		expect(messages[0].sequenceNumber).toBe(0); // User message
		expect(messages[1].sequenceNumber).toBe(1); // First assistant message
		expect(messages[2].sequenceNumber).toBe(2); // Tool call message
		expect(messages[3].sequenceNumber).toBe(3); // Final text message

		// Check message IDs follow expected format
		expect(messages[1].id).toBe('gen-1756406547:text'); // Text messages have :text suffix
		expect(messages[2].id).toBe('gen-1756406547'); // tools_aggregate uses base ID
		expect(messages[3].id).toBe('gen-1756406552:text'); // Text messages have :text suffix
	});

	test('handles tool call error results correctly', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TOOL_CALL_WITH_ERROR.streamContent);

		// Process up to tool results (event 10 has the second tool result)
		await harness.processUntilEvent(10);
		const state = harness.getCurrentState();

		// Check that error result is properly stored
		const toolCallSnapshot = state.streamingState.streamingSnapshots['gen-1756406547']; // tools_aggregate uses base ID
		expect(toolCallSnapshot).toBeDefined();
		expect(toolCallSnapshot!.toolCallPairs).toBeDefined();
		expect(toolCallSnapshot!.toolCallPairs).toHaveLength(1);

		// Check if toolResult exists and has the expected properties
		const toolResult = toolCallSnapshot!.toolCallPairs![0].toolResult;
		expect(toolResult).toBeDefined();
		if (toolResult) {
			// The second tool result overwrites with isError: false (from event 10)
			expect(toolResult.isError).toBe(false);
			expect(toolResult.result).toContain("Function 'get_weather' is not available");
			expect(toolResult.toolCallId).toBe('f1ae8817-8108-43a6-b7c7-cd013e35f9ad');
		}
	});

	test('validates tool call argument accumulation through streaming', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TOOL_CALL_WITH_ERROR.streamContent);

		// Event 2: Tool call initiation
		await harness.processUntilEvent(2);
		let snapshot = harness.getCurrentState().streamingState.streamingSnapshots['gen-1756406547'];
		expect(snapshot?.toolCallPairs![0].toolCall.function_name).toBe('get_weather');
		expect(snapshot?.toolCallPairs![0].toolCall.function_args).toBe('');

		// Event 3: First JSON fragment
		await harness.processUntilEvent(3);
		snapshot = harness.getCurrentState().streamingState.streamingSnapshots['gen-1756406547'];
		expect(snapshot?.toolCallPairs![0].toolCall.function_args).toContain('{ "locatio');

		// Event 4: Location partial
		await harness.processUntilEvent(4);
		snapshot = harness.getCurrentState().streamingState.streamingSnapshots['gen-1756406547'];
		expect(snapshot?.toolCallPairs![0].toolCall.function_args).toContain('"San F');

		// Event 5: Location complete
		await harness.processUntilEvent(5);
		snapshot = harness.getCurrentState().streamingState.streamingSnapshots['gen-1756406547'];
		expect(snapshot?.toolCallPairs![0].toolCall.function_args).toContain('"San Francisco"');

		// Event 8: JSON complete
		await harness.processUntilEvent(8);
		snapshot = harness.getCurrentState().streamingState.streamingSnapshots['gen-1756406547'];
		const completeArgs = snapshot?.toolCallPairs![0].toolCall.function_args;
		expect(completeArgs).toBe('{ "location": "San Francisco", "units": "celsius" }');

		// Verify we can parse the accumulated JSON
		expect(() => JSON.parse(completeArgs!)).not.toThrow();
		const parsed = JSON.parse(completeArgs!);
		expect(parsed).toEqual({
			location: 'San Francisco',
			units: 'celsius'
		});
	});

	test('validates complete event flow with 17 total events', async () => {
		const harness = new SSEStreamTestHarness(SSE_FIXTURES.TOOL_CALL_WITH_ERROR.streamContent);
		const events = harness.getEvents();

		// Total event count validation
		expect(events).toHaveLength(17);

		// Process all events and validate final state
		await harness.processAll();
		const finalState = harness.getCurrentState();

		// Streaming should be complete
		expect(finalState.streamingState.isStreaming).toBe(false);
		expect(finalState.streamingState.currentMessageId).toBeNull();

		// All messages should be in final state
		expect(finalState.chat!.messages).toHaveLength(4);

		// Validate each message type
		const [userMsg, textMsg1, toolMsg, textMsg2] = finalState.chat!.messages;

		expect(userMsg.role).toBe('user');
		expect(textMsg1.messageType).toBe('text');
		expect(toolMsg.messageType).toBe('tools_aggregate');
		expect(textMsg2.messageType).toBe('text');
	});
});
