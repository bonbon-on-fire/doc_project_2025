/**
 * SSE Stream Test Fixtures
 *
 * This module provides reusable test fixtures for SSE streaming scenarios.
 * Each fixture defines:
 * - The SSE event stream to test
 * - Expected state at each event
 * - Validation functions
 * - Debug helpers
 */

import { expect } from 'vitest';
import type { TestSnapshot } from '../streamTestUtils';

/**
 * Defines a test fixture with SSE events and expected state validations
 */
export interface SSETestFixture {
	/** Unique identifier for the test fixture */
	id: string;
	/** Human-readable description of what this fixture tests */
	description: string;
	/** The raw SSE stream content */
	streamContent: string;
	/** Tags for categorizing fixtures (e.g., 'bug', 'regression', 'feature') */
	tags?: string[];
	/** Expected states at each event, keyed by event index */
	expectations: {
		[eventIndex: number]: SSEEventExpectation;
	};
	/** Optional: Final validation after all events processed */
	finalValidation?: (state: TestSnapshot) => void;
	/** Optional: Setup function to run before processing events */
	setup?: () => void | Promise<void>;
	/** Optional: Teardown function to run after test completion */
	teardown?: () => void | Promise<void>;
}

/**
 * Defines expectations for a single SSE event
 */
export interface SSEEventExpectation {
	/** Expected event type at this index */
	eventType: string;
	/** Human-readable description of what's being validated */
	description?: string;
	/** Validation function to run on the state snapshot */
	validate: (state: TestSnapshot) => void;
	/** Optional: Additional assertions or logging */
	debug?: (state: TestSnapshot) => void;
	/** Optional: Skip this expectation based on conditions */
	skip?: boolean | ((state: TestSnapshot) => boolean);
}

/**
 * Base expectations that can be reused across fixtures
 */
export const BASE_EXPECTATIONS = {
	/**
	 * Validates that chat is initialized with a user message
	 */
	chatInitialized: (state: TestSnapshot) => {
		expect(state.chat).toBeTruthy();
		expect(state.chat!.messages).toHaveLength(1);
		// Note: User messages have messageType 'text' but role 'user'
		expect(state.chat!.messages[0].messageType).toBe('text');
		expect((state.chat!.messages[0] as any).role).toBe('user');
	},

	/**
	 * Validates that a text message exists with specific content
	 */
	textMessageExists: (messageId: string, expectedText: string) => (state: TestSnapshot) => {
		const message = state.chat!.messages.find((m) => m.id === messageId);
		expect(message).toBeTruthy();
		expect((message as any).text).toBe(expectedText);
	},

	/**
	 * Validates streaming snapshot for a message
	 */
	streamingSnapshotExists: (messageId: string, expectedDelta: string) => (state: TestSnapshot) => {
		const snapshot = state.streamingState.streamingSnapshots[messageId];
		expect(snapshot).toBeTruthy();
		expect(snapshot.textDelta).toBe(expectedDelta);
		expect(snapshot.isStreaming).toBe(true);
	},

	/**
	 * Validates that streaming is active for a specific message
	 */
	streamingActive: (messageId: string) => (state: TestSnapshot) => {
		expect(state.streamingState.isStreaming).toBe(true);
		expect(state.streamingState.currentMessageId).toBe(messageId);
	}
};

/**
 * Debug helpers for common logging scenarios
 */
export const DEBUG_HELPERS = {
	/**
	 * Logs message structure for debugging
	 */
	logMessageStructure: (messageId: string) => (state: TestSnapshot) => {
		const message = state.chat!.messages.find((m) => m.id === messageId);
		console.log(`\n=== Message Structure: ${messageId} ===`);
		console.log('Message:', JSON.stringify(message, null, 2));

		if (message) {
			console.log('Fields:', {
				id: message.id,
				type: message.messageType,
				hasText: 'text' in (message as any),
				textValue: (message as any).text
			});
		}
	},

	/**
	 * Logs streaming state for debugging
	 */
	logStreamingState: (state: TestSnapshot) => {
		console.log('\n=== Streaming State ===');
		console.log({
			isStreaming: state.streamingState.isStreaming,
			currentMessageId: state.streamingState.currentMessageId,
			snapshotCount: Object.keys(state.streamingState.streamingSnapshots).length,
			snapshots: Object.keys(state.streamingState.streamingSnapshots)
		});
	},

	/**
	 * Logs a warning if a condition is met
	 */
	warnIf:
		(condition: (state: TestSnapshot) => boolean, message: string) => (state: TestSnapshot) => {
			if (condition(state)) {
				console.warn(`⚠️  WARNING: ${message}`);
			}
		}
};

// =============================================================================
// FIXTURE DEFINITIONS
// =============================================================================

/**
 * Text content preservation when tool messages start streaming
 */
export const TEXT_THEN_TOOLS_FIXTURE: SSETestFixture = {
	id: 'text-then-tools',
	description: 'Text message content should be preserved when tools message starts streaming',
	tags: ['bug', 'regression', 'text-preservation'],
	streamContent: `id: 780ed82f-5d21-45db-a9c2-b09996065f42<|>3e174638-a803-45a0-bf91-8061d144719c
event: init
data: {"payload":{"userMessageId":"3e174638-a803-45a0-bf91-8061d144719c","userTimestamp":"2025-08-28T18:42:26.7140377Z","userSequenceNumber":0},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:26.7177326Z","kind":"meta"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A364F4F0-001:1:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A364F4F0-001","sequenceId":1,"payload":{"done":false,"delta":"test-weather"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:27.8964852Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3B1AE3B-002:2:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3B1AE3B-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_name":"get_weather","function_args":""},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:28.3993626Z","kind":"tools_call_update"}
`,
	expectations: {
		0: {
			eventType: 'init',
			description: 'Chat initialization with user message',
			validate: BASE_EXPECTATIONS.chatInitialized
		},
		1: {
			eventType: 'messageupdate',
			description: 'Text message streaming with content',
			validate: (state) => {
				const messageId = 'gen-1756406547:text';

				// Validate streaming is active
				BASE_EXPECTATIONS.streamingActive(messageId)(state);

				// Validate streaming snapshot
				BASE_EXPECTATIONS.streamingSnapshotExists(messageId, 'test-weather')(state);

				// Validate message has text content
				BASE_EXPECTATIONS.textMessageExists(messageId, 'test-weather')(state);
			},
			debug: DEBUG_HELPERS.logMessageStructure('gen-1756406547:text')
		},
		2: {
			eventType: 'messageupdate',
			description: 'Tool call starts - text content should be preserved',
			validate: (state) => {
				// CRITICAL: Text message content must still exist
				BASE_EXPECTATIONS.textMessageExists('gen-1756406547:text', 'test-weather')(state);

				// Verify we now have a tools message too
				// Note: Tools messages use just the base ID without ':tools' suffix
				const toolsMessage = state.chat!.messages.find((m) => m.id === 'gen-1756406547');
				expect(toolsMessage).toBeTruthy();
				expect(toolsMessage?.messageType).toBe('tools_aggregate');
			},
			debug: (state) => {
				DEBUG_HELPERS.logMessageStructure('gen-1756406547:text')(state);
				DEBUG_HELPERS.logStreamingState(state);

				// Specific bug detection
				const textMessage = state.chat!.messages.find((m) => m.id === 'gen-1756406547:text');
				if (!textMessage || !(textMessage as any).text) {
					console.error('🐛 BUG DETECTED: Text message lost its content!');
					console.error('Expected: text = "test-weather"');
					console.error('Actual:', textMessage);
				}
			}
		}
	}
};

/**
 * Multiple text messages preservation
 */
export const MULTIPLE_TEXT_MESSAGES_FIXTURE: SSETestFixture = {
	id: 'multiple-text-messages',
	description: 'Multiple sequential text messages should each preserve their content',
	tags: ['text-preservation', 'sequential-messages'],
	streamContent: `id: test-chat-001<|>user-msg-001
event: init
data: {"payload":{"userMessageId":"user-msg-001","userTimestamp":"2025-08-28T10:00:00.000Z","userSequenceNumber":0},"chatId":"test-chat-001","version":1,"ts":"2025-08-28T10:00:00.100Z","kind":"meta"}

id: test-chat-001:gen-msg-001:1:1
event: messageupdate
data: {"messageId":"gen-msg-001","sequenceId":1,"payload":{"done":false,"delta":"First message"},"chatId":"test-chat-001","version":1,"ts":"2025-08-28T10:00:01.000Z","kind":"text"}

id: test-chat-001:gen-msg-001:1:2
event: messageupdate
data: {"messageId":"gen-msg-001","sequenceId":1,"payload":{"done":true,"delta":" content"},"chatId":"test-chat-001","version":1,"ts":"2025-08-28T10:00:02.000Z","kind":"text"}

id: test-chat-001:gen-msg-002:2:1
event: messageupdate
data: {"messageId":"gen-msg-002","sequenceId":2,"payload":{"done":false,"delta":"Second message"},"chatId":"test-chat-001","version":1,"ts":"2025-08-28T10:00:03.000Z","kind":"text"}

id: test-chat-001:gen-msg-002:2:2
event: messageupdate
data: {"messageId":"gen-msg-002","sequenceId":2,"payload":{"done":true,"delta":" content"},"chatId":"test-chat-001","version":1,"ts":"2025-08-28T10:00:04.000Z","kind":"text"}
`,
	expectations: {
		0: {
			eventType: 'init',
			description: 'Chat initialization',
			validate: BASE_EXPECTATIONS.chatInitialized
		},
		1: {
			eventType: 'messageupdate',
			description: 'First text message starts',
			validate: (state) => {
				BASE_EXPECTATIONS.textMessageExists('gen-msg:text', 'First message')(state);
			}
		},
		2: {
			eventType: 'messageupdate',
			description: 'First text message completes',
			validate: (state) => {
				BASE_EXPECTATIONS.textMessageExists('gen-msg:text', 'First message content')(state);
			}
		},
		3: {
			eventType: 'messageupdate',
			description: 'Second text message starts - first should be preserved',
			validate: (state) => {
				// NOTE: These message IDs match the expected pattern from the SSE stream
				// The handler converts gen-msg-001 to gen-msg:text for the first text message
				BASE_EXPECTATIONS.textMessageExists('gen-msg:text', 'First message content')(state);

				// Second message should be starting
				const secondMessage = state.chat!.messages.find((m) => m.id === 'gen-msg-002:text');
				expect(secondMessage).toBeTruthy();
			}
		},
		4: {
			eventType: 'messageupdate',
			description: 'Both messages should have their content preserved',
			validate: (state) => {
				BASE_EXPECTATIONS.textMessageExists('gen-msg:text', 'First message content')(state);
				BASE_EXPECTATIONS.textMessageExists('gen-msg-002:text', 'Second message content')(state);
			}
		}
	}
};

/**
 * Complete tool call with error stream fixture
 * Migrated from sseStreamProcessing.test.ts for comprehensive tool call testing
 */
export const TOOL_CALL_WITH_ERROR_FIXTURE: SSETestFixture = {
	id: 'tool-call-with-error',
	description: 'Complete tool call stream with error handling and multiple messages',
	tags: ['tools', 'error-handling', 'complex-stream'],
	streamContent: `id: 780ed82f-5d21-45db-a9c2-b09996065f42<|>3e174638-a803-45a0-bf91-8061d144719c
event: init
data: {"payload":{"userMessageId":"3e174638-a803-45a0-bf91-8061d144719c","userTimestamp":"2025-08-28T18:42:26.7140377Z","userSequenceNumber":0},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:26.7177326Z","kind":"meta"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A364F4F0-001:1:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A364F4F0-001","sequenceId":1,"payload":{"done":false,"delta":"test-weather"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:27.8964852Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3B1AE3B-002:2:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3B1AE3B-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_name":"get_weather","function_args":""},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:28.3993626Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3FF5B90-002:2:2
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3FF5B90-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":"{ \\"locatio","json_update_fragments":[{"path":"root","kind":"startObject","textValue":"{"}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:28.9085408Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A44D0471-002:2:3
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A44D0471-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":"n\\": \\"San F","json_update_fragments":[{"path":"root","kind":"key","textValue":"\\"location\\"","value":"\\"location\\""},{"path":"root.location","kind":"startString","textValue":"\\""},{"path":"root.location","kind":"partialString","textValue":"San F","value":"San F"}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:29.4173557Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A49AED68-002:2:4
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A49AED68-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":"rancisco\\",","json_update_fragments":[{"path":"root.location","kind":"partialString","textValue":"rancisco","value":"rancisco"},{"path":"root.location","kind":"completeString","textValue":"\\"San Francisco\\"","value":"\\"San Francisco\\""}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:29.9279498Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A4E8A4AD-002:2:5
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A4E8A4AD-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":" \\"units\\": ","json_update_fragments":[{"path":"root","kind":"key","textValue":"\\"units\\"","value":"\\"units\\""}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:30.4372638Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A536020A-002:2:6
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A536020A-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":"\\"celsius\\" ","json_update_fragments":[{"path":"root.units","kind":"startString","textValue":"\\""},{"path":"root.units","kind":"partialString","textValue":"celsius","value":"celsius"},{"path":"root.units","kind":"completeString","textValue":"\\"celsius\\"","value":"\\"celsius\\""}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:30.9442753Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A5845512-002:2:7
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A5845512-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_args":"}","json_update_fragments":[{"path":"root","kind":"endObject","textValue":"}"},{"path":"root","kind":"jsonComplete","textValue":"{ \\"location\\": \\"San Francisco\\", \\"units\\": \\"celsius\\" }"}]},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:31.4576189Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3B1AE3B-002:2:0
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3B1AE3B-002","sequenceId":2,"payload":{"toolCallId":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","result":"Error: Function 'get_weather' is not available. Available functions: ","isError":true,"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.4811155Z","kind":"tool_result"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3B1AE3B-002:2:0
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3B1AE3B-002","sequenceId":2,"payload":{"toolCallId":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","result":"Function 'get_weather' is not available. Available functions: ","isError":false,"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.4811736Z","kind":"tool_result"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A6208D3E-001:1
event: message
data: {"messageId":"gen-1756406547-d-8DDE662A6208D3E-001","sequenceId":1,"payload":{"text":"test-weather"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.485485Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A62133A6-002:2
event: message
data: {"messageId":"gen-1756406547-d-8DDE662A62133A6-002","sequenceId":2,"payload":{"toolCalls":[{"function_name":"get_weather","function_args":"{ \"location\": \"San Francisco\", \"units\": \"celsius\" }","index":0,"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad"}],"toolResults":[{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","result":"Function 'get_weather' is not available. Available functions: "}]},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.4900477Z","kind":"tools_aggregate"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406552-4-8DDE662A622FC27-003:3:1
event: messageupdate
data: {"messageId":"gen-1756406552-4-8DDE662A622FC27-003","sequenceId":3,"payload":{"done":false,"delta":"completion"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.497318Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406552-4-8DDE662A670F173-003:3:2
event: messageupdate
data: {"messageId":"gen-1756406552-4-8DDE662A670F173-003","sequenceId":3,"payload":{"done":false,"delta":"lorem ipsum dolor sit amet"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:33.0082241Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406552-4-8DDE662A70C1394-003:3
event: message
data: {"messageId":"gen-1756406552-4-8DDE662A70C1394-003","sequenceId":3,"payload":{"text":"completionlorem ipsum dolor sit amet"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:34.029074Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42<|>3e174638-a803-45a0-bf91-8061d144719c
event: complete
data: {"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:34.0321278Z","kind":"complete"}
`,
	expectations: {
		// We'll add full expectations after migration
	},
	finalValidation: (state) => {
		// Final state validation
		expect(state.chat).toBeTruthy();
		if (!state.chat) {
			throw new Error('Chat not initialized in test');
		}
		expect(state.chat.messages).toHaveLength(4); // User + 3 assistant messages
		const assistantMessages = state.chat.messages.filter((m) => m.role === 'assistant');
		expect(assistantMessages).toHaveLength(3);

		// Verify message types and content
		expect(assistantMessages[0].id).toBe('gen-1756406547:text');
		expect((assistantMessages[0] as any).text).toBe('test-weather');

		expect(assistantMessages[1].id).toBe('gen-1756406547');
		expect((assistantMessages[1] as any).toolCallPairs).toBeDefined();

		expect(assistantMessages[2].id).toBe('gen-1756406552:text');
		expect((assistantMessages[2] as any).text).toBe('completionlorem ipsum dolor sit amet');
	}
};

/**
 * Collection of all fixtures for easy access
 */
export const SSE_FIXTURES = {
	TEXT_THEN_TOOLS: TEXT_THEN_TOOLS_FIXTURE,
	MULTIPLE_TEXT_MESSAGES: MULTIPLE_TEXT_MESSAGES_FIXTURE,
	TOOL_CALL_WITH_ERROR: TOOL_CALL_WITH_ERROR_FIXTURE
	// Add more fixtures here as needed
} as const;

/**
 * Get fixtures by tag
 */
export function getFixturesByTag(tag: string): SSETestFixture[] {
	return Object.values(SSE_FIXTURES).filter((fixture) => fixture.tags?.includes(tag));
}

/**
 * Get fixture by ID
 */
export function getFixtureById(id: string): SSETestFixture | undefined {
	return Object.values(SSE_FIXTURES).find((fixture) => fixture.id === id);
}
