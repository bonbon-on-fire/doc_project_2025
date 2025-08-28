import { describe, test, expect, beforeEach } from 'vitest';
import { SSEStreamTestHarness } from './streamTestUtils';

// Test SSE stream that reproduces the text content loss bug
const TEXT_TO_TOOLS_TRANSITION_STREAM = `id: 780ed82f-5d21-45db-a9c2-b09996065f42<|>3e174638-a803-45a0-bf91-8061d144719c
event: init
data: {"payload":{"userMessageId":"3e174638-a803-45a0-bf91-8061d144719c","userTimestamp":"2025-08-28T18:42:26.7140377Z","userSequenceNumber":0},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:26.7177326Z","kind":"meta"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A364F4F0-001:1:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A364F4F0-001","sequenceId":1,"payload":{"done":false,"delta":"test-weather"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:27.8964852Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A3B1AE3B-002:2:1
event: messageupdate
data: {"messageId":"gen-1756406547-d-8DDE662A3B1AE3B-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad","index":0,"function_name":"get_weather","function_args":""},"delta":""},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:28.3993626Z","kind":"tools_call_update"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A6208D3E-001:1
event: message
data: {"messageId":"gen-1756406547-d-8DDE662A6208D3E-001","sequenceId":1,"payload":{"text":"test-weather"},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.485485Z","kind":"text"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42:gen-1756406547-d-8DDE662A62133A6-002:2
event: message
data: {"messageId":"gen-1756406547-d-8DDE662A62133A6-002","sequenceId":2,"payload":{"toolCalls":[{"function_name":"get_weather","function_args":"","index":0,"tool_call_id":"f1ae8817-8108-43a6-b7c7-cd013e35f9ad"}],"toolResults":[]},"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:32.4900477Z","kind":"tools_aggregate"}

id: 780ed82f-5d21-45db-a9c2-b09996065f42<|>3e174638-a803-45a0-bf91-8061d144719c
event: complete
data: {"chatId":"780ed82f-5d21-45db-a9c2-b09996065f42","version":1,"ts":"2025-08-28T18:42:34.0321278Z","kind":"complete"}
`;

describe('Text Message Content Preservation', () => {
	let harness: SSEStreamTestHarness;

	beforeEach(() => {
		harness = new SSEStreamTestHarness(TEXT_TO_TOOLS_TRANSITION_STREAM);
	});

	test('preserves text message content when transitioning to tool messages', async () => {
		// Process init event
		let state = await harness.processUntilEvent(0);
		expect(state.chat).toBeTruthy();
		expect(state.chat!.messages).toHaveLength(1); // User message

		// Process text message streaming
		state = await harness.processUntilEvent(1);
		expect(state.eventType).toBe('messageupdate');
		expect(state.streamingState.currentMessageId).toBe('gen-1756406547:text');

		// Verify text is accumulated in the streaming snapshot
		const textSnapshot = state.streamingState.streamingSnapshots['gen-1756406547:text'];
		expect(textSnapshot).toBeTruthy();
		expect(textSnapshot.textDelta).toBe('test-weather');
		expect(textSnapshot.isStreaming).toBe(true);

		// Process tool call update - this should NOT clear the text snapshot
		state = await harness.processUntilEvent(2);
		expect(state.eventType).toBe('messageupdate');
		expect(state.streamingState.currentMessageId).toBe('gen-1756406547'); // tools_aggregate

		// Text snapshot should still exist but marked as not streaming
		const textSnapshotAfterTool = state.streamingState.streamingSnapshots['gen-1756406547:text'];
		expect(textSnapshotAfterTool).toBeTruthy();
		expect(textSnapshotAfterTool.textDelta).toBe('test-weather'); // Content preserved
		expect(textSnapshotAfterTool.isStreaming).toBe(false); // No longer active

		// Process text message completion - this should preserve the content
		state = await harness.processUntilEvent(3);
		expect(state.eventType).toBe('message');

		// Find the text message in chat
		const textMessage = state.chat!.messages.find((m) => m.id === 'gen-1756406547:text');
		expect(textMessage).toBeTruthy();
		expect(textMessage!.role).toBe('assistant');
		expect((textMessage as any).text).toBe('test-weather'); // CRITICAL: Text content preserved
		expect(textMessage!.messageType).toBe('text');

		// Process tool message completion
		state = await harness.processUntilEvent(4);
		expect(state.eventType).toBe('message');

		// Verify both messages exist with correct content
		const messages = state.chat!.messages.filter((m) => m.role === 'assistant');
		expect(messages).toHaveLength(2);

		// Text message should have its content
		const finalTextMessage = messages.find((m) => m.id === 'gen-1756406547:text');
		expect(finalTextMessage).toBeTruthy();
		expect((finalTextMessage as any).text).toBe('test-weather');

		// Tool message should exist
		const toolMessage = messages.find((m) => m.id === 'gen-1756406547');
		expect(toolMessage).toBeTruthy();
		expect(toolMessage!.messageType).toBe('tools_aggregate');
	});

	test('handles text content in snapshots even when completion arrives late', async () => {
		// Process through tool call streaming
		await harness.processUntilEvent(2);
		let state = harness.getCurrentState();

		// Both snapshots should exist
		expect(state.streamingState.streamingSnapshots['gen-1756406547:text']).toBeTruthy();
		expect(state.streamingState.streamingSnapshots['gen-1756406547']).toBeTruthy();

		// Text snapshot should have accumulated content
		const textSnapshot = state.streamingState.streamingSnapshots['gen-1756406547:text'];
		expect(textSnapshot.textDelta).toBe('test-weather');

		// Process the late text completion
		await harness.processUntilEvent(3);
		state = harness.getCurrentState();

		// Text message should be properly completed with content
		const textMessage = state.chat!.messages.find((m) => m.id === 'gen-1756406547:text');
		expect(textMessage).toBeTruthy();
		expect((textMessage as any).text).toBe('test-weather');
	});
});
