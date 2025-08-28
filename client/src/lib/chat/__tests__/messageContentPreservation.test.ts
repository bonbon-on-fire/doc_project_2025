import { describe, test, expect, beforeEach } from 'vitest';
import { SSEStreamTestHarness } from './streamTestUtils';

describe('Message Content Preservation During Transitions', () => {
	describe('Text to Tools Transition', () => {
		const TEXT_TO_TOOLS_STREAM = `id: chat-123<|>user-msg-1
event: init
data: {"payload":{"userMessageId":"user-msg-1","userTimestamp":"2025-08-28T18:00:00.000Z","userSequenceNumber":0},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:00.000Z","kind":"meta"}

id: chat-123:gen-100-001:1:1
event: messageupdate
data: {"messageId":"gen-100-001","sequenceId":1,"payload":{"done":false,"delta":"Hello, I'll check the weather"},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:01.000Z","kind":"text"}

id: chat-123:gen-100-002:2:1
event: messageupdate
data: {"messageId":"gen-100-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"tool-1","index":0,"function_name":"get_weather","function_args":"{\\"location\\":\\""},"delta":""},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:02.000Z","kind":"tools_call_update"}

id: chat-123:gen-100-002:2:2
event: messageupdate
data: {"messageId":"gen-100-002","sequenceId":2,"payload":{"toolCallUpdate":{"tool_call_id":"tool-1","index":0,"function_args":"NYC\\"}"},"delta":""},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:03.000Z","kind":"tools_call_update"}

id: chat-123:gen-100-001:1
event: message
data: {"messageId":"gen-100-001","sequenceId":1,"payload":{"text":"Hello, I'll check the weather"},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:04.000Z","kind":"text"}

id: chat-123:gen-100-002:2
event: message
data: {"messageId":"gen-100-002","sequenceId":2,"payload":{"toolCalls":[{"function_name":"get_weather","function_args":"{\\"location\\":\\"NYC\\"}","tool_call_id":"tool-1"}],"toolResults":[]},"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:05.000Z","kind":"tools_aggregate"}

id: chat-123<|>user-msg-1
event: complete
data: {"chatId":"chat-123","version":1,"ts":"2025-08-28T18:00:06.000Z","kind":"complete"}
`;

		let harness: SSEStreamTestHarness;

		beforeEach(() => {
			harness = new SSEStreamTestHarness(TEXT_TO_TOOLS_STREAM);
		});

		test('preserves text message content throughout the entire stream', async () => {
			// Process init
			await harness.processUntilEvent(0);

			// Process text streaming
			let state = await harness.processUntilEvent(1);
			let textMessage = state.chat!.messages.find((m) => m.id === 'gen-100:text');
			expect(textMessage).toBeTruthy();
			expect((textMessage as any).text).toBe("Hello, I'll check the weather");

			// Process first tool update - text should remain
			state = await harness.processUntilEvent(2);
			textMessage = state.chat!.messages.find((m) => m.id === 'gen-100:text');
			expect(textMessage).toBeTruthy();
			expect((textMessage as any).text).toBe("Hello, I'll check the weather");

			// Process second tool update - text should still remain
			state = await harness.processUntilEvent(3);
			textMessage = state.chat!.messages.find((m) => m.id === 'gen-100:text');
			expect(textMessage).toBeTruthy();
			expect((textMessage as any).text).toBe("Hello, I'll check the weather");

			// Process text completion
			state = await harness.processUntilEvent(4);
			textMessage = state.chat!.messages.find((m) => m.id === 'gen-100:text');
			expect(textMessage).toBeTruthy();
			expect((textMessage as any).text).toBe("Hello, I'll check the weather");

			// Process tools completion
			state = await harness.processUntilEvent(5);
			textMessage = state.chat!.messages.find((m) => m.id === 'gen-100:text');
			const toolsMessage = state.chat!.messages.find((m) => m.id === 'gen-100');
			expect(textMessage).toBeTruthy();
			expect(toolsMessage).toBeTruthy();
			expect((textMessage as any).text).toBe("Hello, I'll check the weather");
			expect((toolsMessage as any).toolCallPairs).toHaveLength(1);
		});
	});

	describe('Reasoning to Text Transition', () => {
		const REASONING_TO_TEXT_STREAM = `id: chat-456<|>user-msg-2
event: init
data: {"payload":{"userMessageId":"user-msg-2","userTimestamp":"2025-08-28T19:00:00.000Z","userSequenceNumber":0},"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:00.000Z","kind":"meta"}

id: chat-456:gen-200-001:1:1
event: messageupdate
data: {"messageId":"gen-200-001","sequenceId":1,"payload":{"delta":"Let me think about this","visibility":"plain"},"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:01.000Z","kind":"reasoning"}

id: chat-456:gen-200-002:2:1
event: messageupdate
data: {"messageId":"gen-200-002","sequenceId":2,"payload":{"done":false,"delta":"Based on my analysis"},"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:02.000Z","kind":"text"}

id: chat-456:gen-200-001:1
event: message
data: {"messageId":"gen-200-001","sequenceId":1,"payload":{"reasoning":"Let me think about this","visibility":"Plain"},"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:03.000Z","kind":"reasoning"}

id: chat-456:gen-200-002:2
event: message
data: {"messageId":"gen-200-002","sequenceId":2,"payload":{"text":"Based on my analysis"},"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:04.000Z","kind":"text"}

id: chat-456<|>user-msg-2
event: complete
data: {"chatId":"chat-456","version":1,"ts":"2025-08-28T19:00:05.000Z","kind":"complete"}
`;

		let harness: SSEStreamTestHarness;

		beforeEach(() => {
			harness = new SSEStreamTestHarness(REASONING_TO_TEXT_STREAM);
		});

		test('preserves reasoning message content when text starts streaming', async () => {
			// Process init
			await harness.processUntilEvent(0);

			// Process reasoning streaming
			let state = await harness.processUntilEvent(1);
			let reasoningMessage = state.chat!.messages.find((m) => m.id === 'gen-200:reasoning');
			expect(reasoningMessage).toBeTruthy();
			expect((reasoningMessage as any).reasoning).toBe('Let me think about this');
			expect((reasoningMessage as any).visibility).toBe('Plain');

			// Process text streaming - reasoning should remain
			state = await harness.processUntilEvent(2);
			reasoningMessage = state.chat!.messages.find((m) => m.id === 'gen-200:reasoning');
			const textMessage = state.chat!.messages.find((m) => m.id === 'gen-200:text');
			expect(reasoningMessage).toBeTruthy();
			expect(textMessage).toBeTruthy();
			expect((reasoningMessage as any).reasoning).toBe('Let me think about this');
			expect((textMessage as any).text).toBe('Based on my analysis');

			// After all completions, both should have their content
			await harness.processAll();
			state = harness.getCurrentState();
			reasoningMessage = state.chat!.messages.find((m) => m.id === 'gen-200:reasoning');
			const finalTextMessage = state.chat!.messages.find((m) => m.id === 'gen-200:text');
			expect((reasoningMessage as any).reasoning).toBe('Let me think about this');
			expect((finalTextMessage as any).text).toBe('Based on my analysis');
		});
	});

	describe('Multiple Message Types Interleaved', () => {
		const COMPLEX_STREAM = `id: chat-789<|>user-msg-3
event: init
data: {"payload":{"userMessageId":"user-msg-3","userTimestamp":"2025-08-28T20:00:00.000Z","userSequenceNumber":0},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:00.000Z","kind":"meta"}

id: chat-789:gen-300-001:1:1
event: messageupdate
data: {"messageId":"gen-300-001","sequenceId":1,"payload":{"delta":"Thinking...","visibility":"plain"},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:01.000Z","kind":"reasoning"}

id: chat-789:gen-300-002:2:1
event: messageupdate
data: {"messageId":"gen-300-002","sequenceId":2,"payload":{"done":false,"delta":"I need to"},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:02.000Z","kind":"text"}

id: chat-789:gen-300-002:2:2
event: messageupdate
data: {"messageId":"gen-300-002","sequenceId":2,"payload":{"done":false,"delta":" check something"},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:03.000Z","kind":"text"}

id: chat-789:gen-300-003:3:1
event: messageupdate
data: {"messageId":"gen-300-003","sequenceId":3,"payload":{"toolCallUpdate":{"tool_call_id":"t1","index":0,"function_name":"search","function_args":""},"delta":""},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:04.000Z","kind":"tools_call_update"}

id: chat-789:gen-300-001:1
event: message
data: {"messageId":"gen-300-001","sequenceId":1,"payload":{"reasoning":"Thinking...","visibility":"Plain"},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:05.000Z","kind":"reasoning"}

id: chat-789:gen-300-002:2
event: message
data: {"messageId":"gen-300-002","sequenceId":2,"payload":{"text":"I need to check something"},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:06.000Z","kind":"text"}

id: chat-789:gen-300-003:3
event: message
data: {"messageId":"gen-300-003","sequenceId":3,"payload":{"toolCalls":[{"function_name":"search","function_args":"","tool_call_id":"t1"}],"toolResults":[]},"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:07.000Z","kind":"tools_aggregate"}

id: chat-789<|>user-msg-3
event: complete
data: {"chatId":"chat-789","version":1,"ts":"2025-08-28T20:00:08.000Z","kind":"complete"}
`;

		let harness: SSEStreamTestHarness;

		beforeEach(() => {
			harness = new SSEStreamTestHarness(COMPLEX_STREAM);
		});

		test('preserves all message content during complex interleaved streaming', async () => {
			// Process through all streaming events
			let state = await harness.processUntilEvent(4);

			// Check all messages exist with content during streaming
			const reasoning = state.chat!.messages.find((m) => m.id === 'gen-300:reasoning');
			const text = state.chat!.messages.find((m) => m.id === 'gen-300:text');
			const tools = state.chat!.messages.find((m) => m.id === 'gen-300');

			expect(reasoning).toBeTruthy();
			expect((reasoning as any).reasoning).toBe('Thinking...');

			expect(text).toBeTruthy();
			expect((text as any).text).toBe('I need to check something');

			expect(tools).toBeTruthy();
			expect((tools as any).toolCallPairs).toBeDefined();

			// After all completions, everything should still be preserved
			await harness.processAll();
			state = harness.getCurrentState();

			const finalReasoning = state.chat!.messages.find((m) => m.id === 'gen-300:reasoning');
			const finalText = state.chat!.messages.find((m) => m.id === 'gen-300:text');
			const finalTools = state.chat!.messages.find((m) => m.id === 'gen-300');

			expect((finalReasoning as any).reasoning).toBe('Thinking...');
			expect((finalText as any).text).toBe('I need to check something');
			expect((finalTools as any).toolCallPairs).toHaveLength(1);

			// Verify message order
			const messages = state.chat!.messages.filter((m) => m.role === 'assistant');
			expect(messages).toHaveLength(3);
			expect(messages[0].sequenceNumber).toBe(1);
			expect(messages[1].sequenceNumber).toBe(2);
			expect(messages[2].sequenceNumber).toBe(3);
		});
	});
});
