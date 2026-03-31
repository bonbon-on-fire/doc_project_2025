import { describe, it, expect, beforeEach } from 'vitest';
import { ToolCallMessageHandler } from './toolCallMessageHandler';
import type { StreamChunkEventEnvelope, ToolCallUpdateStreamChunkPayload } from '../sseEventTypes';

describe('ToolCallMessageHandler - ID Management', () => {
	let handler: ToolCallMessageHandler;

	beforeEach(() => {
		handler = new ToolCallMessageHandler();
	});

	it('should generate unique IDs for tool calls without IDs', () => {
		const messageId = 'msg_123';

		// First tool call
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					function_name: 'calculator',
					function_args: JSON.stringify({ a: 1, b: 2 }),
					index: 0
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);

		// Second tool call
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					function_name: 'calculator',
					function_args: JSON.stringify({ a: 3, b: 4 }),
					index: 1
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot = handler.processChunk(messageId, envelope2);

		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe(`${messageId}_tool_0`);
		expect(snapshot.toolCalls[1].id).toBe(`${messageId}_tool_1`);
	});

	it('should preserve provided tool call IDs', () => {
		const messageId = 'msg_123';

		// First tool call with ID
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_abc',
					function_name: 'calculator',
					function_args: JSON.stringify({ a: 1, b: 2 }),
					index: 0
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);

		// Second tool call with ID
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_def',
					function_name: 'search',
					function_args: JSON.stringify({ query: 'test' }),
					index: 1
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot = handler.processChunk(messageId, envelope2);

		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe('tool_abc');
		expect(snapshot.toolCalls[1].id).toBe('tool_def');
	});

	it('should not duplicate tool calls with same ID across multiple chunks', () => {
		const messageId = 'msg_123';
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_1',
					function_name: 'calculator',
					function_args: '{\"a\":',
					index: 0
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);

		// Send continuation of same tool call arguments
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_1',
					function_args: '1, \"b\": 2}',
					index: 0
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope2);

		// Add a different tool call
		const envelope3: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 3,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_2',
					function_name: 'search',
					function_args: JSON.stringify({ query: 'test' }),
					index: 1
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot = handler.processChunk(messageId, envelope3);

		// Should have 2 tool calls, not 3
		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe('tool_1');
		expect(snapshot.toolCalls[0].args).toEqual({ a: 1, b: 2 });
		expect(snapshot.toolCalls[1].id).toBe('tool_2');
	});

	it('should handle multiple tool calls in streaming chunks', () => {
		const messageId = 'msg_123';

		// First tool call
		const envelope1: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_1',
					function_name: 'calculator',
					function_args: JSON.stringify({ a: 1, b: 2 }),
					index: 0
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		handler.processChunk(messageId, envelope1);

		// Second tool call
		const envelope2: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 2,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_2',
					function_name: 'search',
					function_args: JSON.stringify({ query: 'test' }),
					index: 1
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot1 = handler.processChunk(messageId, envelope2);
		expect(snapshot1.toolCalls).toHaveLength(2);

		// Third tool call
		const envelope3: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 3,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call_update',
			payload: {
				delta: '',
				toolCallUpdate: {
					tool_call_id: 'tool_3',
					function_name: 'weather',
					function_args: JSON.stringify({ city: 'London' }),
					index: 2
				}
			} as ToolCallUpdateStreamChunkPayload
		};

		const snapshot2 = handler.processChunk(messageId, envelope3);
		expect(snapshot2.toolCalls).toHaveLength(3);
		expect(snapshot2.toolCalls[2].id).toBe('tool_3');
	});

	it('should handle complete tool call message', () => {
		const messageId = 'msg_123';

		// Send a complete tools_call message (not a streaming update)
		const envelope: StreamChunkEventEnvelope = {
			chatId: 'chat_1',
			messageId,
			sequenceId: 1,
			version: 1,
			ts: new Date().toISOString(),
			kind: 'tools_call', // Note: not tools_call_update
			payload: {
				toolCalls: [
					{
						tool_call_id: 'tool_1',
						function_name: 'calculator',
						function_args: JSON.stringify({ a: 1, b: 2 }),
						index: 0
					},
					{
						tool_call_id: 'tool_2',
						function_name: 'search',
						function_args: JSON.stringify({ query: 'test' }),
						index: 1
					}
				]
			}
		};

		const snapshot = handler.processChunk(messageId, envelope);

		expect(snapshot.toolCalls).toHaveLength(2);
		expect(snapshot.toolCalls[0].id).toBe('tool_1');
		expect(snapshot.toolCalls[0].name).toBe('calculator');
		expect(snapshot.toolCalls[1].id).toBe('tool_2');
		expect(snapshot.toolCalls[1].name).toBe('search');
	});
});
