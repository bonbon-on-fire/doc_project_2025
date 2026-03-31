import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createToolsAggregateMessageHandler } from './toolsAggregateMessageHandler';
import type { StreamChunkEventEnvelope, MessageCompleteEventEnvelope } from '../sseEventTypes';
import type { HandlerEventListener } from '../messageHandlers';
import type { ToolCallPair } from '$lib/types/chat';

describe('ToolsAggregateMessageHandler', () => {
	let handler: ReturnType<typeof createToolsAggregateMessageHandler>;
	let mockListener: HandlerEventListener;

	beforeEach(() => {
		mockListener = {
			onHandlerEvent: vi.fn((event) => {
				// Mock implementation that simulates the listener behavior
				if (event.type === 'message_started') {
					mockListener.onMessageStarted?.(event.messageId, event.chatId);
				} else if (event.type === 'message_chunk') {
					mockListener.onMessageChunk?.(event.messageId, event.chatId, event.data);
				} else if (event.type === 'message_completed') {
					mockListener.onMessageCompleted?.(event.messageId, event.chatId, event.data);
				}
			}),
			onMessageStarted: vi.fn(),
			onMessageChunk: vi.fn(),
			onMessageCompleted: vi.fn()
		};
		handler = createToolsAggregateMessageHandler(mockListener);
	});

	describe('getMessageType', () => {
		it('should return tools_aggregate', () => {
			expect(handler.getMessageType()).toBe('tools_aggregate');
		});
	});

	describe('canHandle', () => {
		it('should handle tools_aggregate messages', () => {
			expect(handler.canHandle('tools_aggregate')).toBe(true);
		});

		it('should handle tools_call_update messages', () => {
			expect(handler.canHandle('tools_call_update')).toBe(true);
		});

		it('should handle tool_result messages', () => {
			expect(handler.canHandle('tool_result')).toBe(true);
		});

		it('should not handle other message types', () => {
			expect(handler.canHandle('text')).toBe(false);
			expect(handler.canHandle('reasoning')).toBe(false);
		});
	});

	describe('processChunk - Tool Call Updates', () => {
		it('should initialize snapshot on first chunk', () => {
			const envelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'get_weather',
						index: 0
					}
				}
			};

			const snapshot = handler.processChunk('msg-1', envelope);

			expect(snapshot).toBeDefined();
			expect(snapshot.id).toBe('msg-1');
			expect(snapshot.chatId).toBe('chat-1');
			expect(snapshot.toolCallPairs).toBeDefined();
			expect(snapshot.toolCallPairs.length).toBe(1);
			expect(snapshot.toolCallPairs[0].toolCall.function_name).toBe('get_weather');
			expect(snapshot.toolCallPairs[0].toolResult).toBeUndefined();
		});

		it('should accumulate function arguments across multiple chunks', () => {
			const envelope1: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'calculate',
						function_args: '{"x":',
						index: 0
					}
				}
			};

			const envelope2: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tools_call_update',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_args: '10,"y":',
						index: 0
					}
				}
			};

			const envelope3: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:02Z',
				kind: 'tools_call_update',
				sequenceId: 3,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_args: '20}',
						index: 0
					}
				}
			};

			handler.processChunk('msg-1', envelope1);
			handler.processChunk('msg-1', envelope2);
			const snapshot = handler.processChunk('msg-1', envelope3);

			expect(snapshot.toolCallPairs).toHaveLength(1);
			expect(snapshot.toolCallPairs[0].toolCall.function_args).toBe('{"x":10,"y":20}');
			expect(snapshot.toolCallPairs[0].toolCall.args).toEqual({ x: 10, y: 20 });
		});

		it('should build args from JsonFragments when provided', () => {
			const envelope1: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-2',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-frag',
						function_name: 'compose',
						index: 0,
						json_update_fragments: [
							{ path: 'root', kind: 'StartObject' },
							{ path: 'root.title', kind: 'PartialString', textValue: 'He' }
						]
					}
				}
			};

			const envelope2: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-2',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tools_call_update',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-frag',
						index: 0,
						json_update_fragments: [
							{ path: 'root.title', kind: 'PartialString', textValue: 'llo' },
							{ path: 'root.title', kind: 'CompleteString', textValue: '"Hello"' }
						]
					}
				}
			};

			handler.processChunk('msg-2', envelope1);
			const snapshot = handler.processChunk('msg-2', envelope2);
			expect(snapshot.toolCallPairs).toHaveLength(1);
			expect(snapshot.toolCallPairs[0].toolCall.args).toEqual({ title: 'Hello' });
		});

		it('should handle multiple tool calls', () => {
			const envelope1: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'tool_one',
						index: 0
					}
				}
			};

			const envelope2: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tools_call_update',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-2',
						function_name: 'tool_two',
						index: 1
					}
				}
			};

			handler.processChunk('msg-1', envelope1);
			const snapshot = handler.processChunk('msg-1', envelope2);

			expect(snapshot.toolCallPairs).toHaveLength(2);
			expect(snapshot.toolCallPairs[0].toolCall.function_name).toBe('tool_one');
			expect(snapshot.toolCallPairs[1].toolCall.function_name).toBe('tool_two');
		});
	});

	describe('processChunk - Tool Results', () => {
		it('should ignore late fragments after tool result is completed', () => {
			// Create initial tool call with fragments
			const callEnvelope1: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-late',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-late',
						function_name: 'get_data',
						index: 0,
						json_update_fragments: [
							{ path: 'root', kind: 'StartObject' },
							{ path: 'root.status', kind: 'CompleteString', textValue: '"initial"' }
						]
					}
				}
			};

			// Process initial call
			let snapshot = handler.processChunk('msg-late', callEnvelope1);
			expect(snapshot.toolCallPairs[0].toolCall.args).toEqual({ status: 'initial' });

			// Tool result arrives (this should complete the document)
			const resultEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-late',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tool_result',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallId: 'call-late',
					result: 'Data retrieved successfully',
					isError: false
				}
			};

			snapshot = handler.processChunk('msg-late', resultEnvelope);
			expect(snapshot.toolCallPairs[0].toolResult).toBeDefined();
			expect(snapshot.toolCallPairs[0].toolResult!.result).toBe('Data retrieved successfully');

			// Late fragments arrive after tool result
			const lateFragmentEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-late',
				version: 1,
				ts: '2024-01-01T00:00:02Z',
				kind: 'tools_call_update',
				sequenceId: 3,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-late',
						index: 0,
						json_update_fragments: [
							{ path: 'root.status', kind: 'CompleteString', textValue: '"updated"' },
							{ path: 'root.extra', kind: 'CompleteNumber', textValue: '42' },
							{ path: 'root', kind: 'EndObject' }
						]
					}
				}
			};

			// Process late fragments - they should be ignored
			snapshot = handler.processChunk('msg-late', lateFragmentEnvelope);

			// Args should remain unchanged from before the tool result
			expect(snapshot.toolCallPairs[0].toolCall.args).toEqual({ status: 'initial' });
			expect(snapshot.toolCallPairs[0].toolCall.args).not.toHaveProperty('extra');

			// Tool result should still be present
			expect(snapshot.toolCallPairs[0].toolResult).toBeDefined();
			expect(snapshot.toolCallPairs[0].toolResult!.result).toBe('Data retrieved successfully');
		});

		it('should handle tool result updates', () => {
			// First create a tool call
			const callEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'get_weather',
						function_args: '{"city":"London"}',
						index: 0
					}
				}
			};

			// Then add the result
			const resultEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tool_result',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallId: 'call-1',
					result: 'Weather in London: Rainy, 15°C',
					isError: false
				}
			};

			handler.processChunk('msg-1', callEnvelope);
			const snapshot = handler.processChunk('msg-1', resultEnvelope);

			expect(snapshot.toolCallPairs).toHaveLength(1);
			expect(snapshot.toolCallPairs[0].toolCall.function_name).toBe('get_weather');
			expect(snapshot.toolCallPairs[0].toolResult).toBeDefined();
			expect(snapshot.toolCallPairs[0].toolResult!.toolCallId).toBe('call-1');
			expect(snapshot.toolCallPairs[0].toolResult!.result).toBe('Weather in London: Rainy, 15°C');
		});

		it('should handle out-of-order results', () => {
			// Add result before the tool call
			const resultEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tool_result',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallId: 'call-1',
					result: 'Result arrived early',
					isError: false
				}
			};

			// Then add the call
			const callEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tools_call_update',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'async_tool',
						index: 0
					}
				}
			};

			handler.processChunk('msg-1', resultEnvelope);
			const snapshot = handler.processChunk('msg-1', callEnvelope);

			expect(snapshot.toolCallPairs).toHaveLength(1);
			expect(snapshot.toolCallPairs[0].toolCall.function_name).toBe('async_tool');
			expect(snapshot.toolCallPairs[0].toolResult).toBeDefined();
			expect(snapshot.toolCallPairs[0].toolResult!.result).toBe('Result arrived early');
		});

		it('should handle error results', () => {
			const callEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'failing_tool',
						index: 0
					}
				}
			};

			const errorResultEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tool_result',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallId: 'call-1',
					result: 'Error: Tool execution failed',
					isError: true
				}
			};

			handler.processChunk('msg-1', callEnvelope);
			const snapshot = handler.processChunk('msg-1', errorResultEnvelope);

			expect(snapshot.toolCallPairs).toHaveLength(1);
			expect(snapshot.toolCallPairs[0].toolResult).toBeDefined();
			expect(snapshot.toolCallPairs[0].toolResult!.result).toContain('Error');
		});
	});

	describe('completeMessage', () => {
		it('should complete aggregate message and emit event', () => {
			// Setup initial chunks
			const callEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_call_update',
				sequenceId: 1,
				payload: {
					delta: '',
					toolCallUpdate: {
						tool_call_id: 'call-1',
						function_name: 'test_tool',
						function_args: '{"test":true}',
						index: 0
					}
				}
			};

			const resultEnvelope: StreamChunkEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:01Z',
				kind: 'tool_result',
				sequenceId: 2,
				payload: {
					delta: '',
					toolCallId: 'call-1',
					result: 'Test successful',
					isError: false
				}
			};

			handler.processChunk('msg-1', callEnvelope);
			handler.processChunk('msg-1', resultEnvelope);

			// Complete the message
			const completeEnvelope: MessageCompleteEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-1',
				version: 1,
				ts: '2024-01-01T00:00:02Z',
				kind: 'tools_aggregate',
				sequenceId: 3,
				payload: {}
			};

			const dto = handler.completeMessage('msg-1', completeEnvelope);

			expect(dto.messageType).toBe('tools_aggregate');
			expect(dto.id).toBe('msg-1');
			expect(dto.toolCallPairs).toHaveLength(1);
			expect(dto.toolCallPairs[0].toolCall).toBeDefined();
			expect(dto.toolCallPairs[0].toolResult).toBeDefined();
			expect(mockListener.onMessageCompleted).toHaveBeenCalledWith('msg-1', 'chat-1', dto);
		});

		it('should handle message completion without prior streaming', () => {
			const completeEnvelope: MessageCompleteEventEnvelope = {
				chatId: 'chat-1',
				messageId: 'msg-complete',
				version: 1,
				ts: '2024-01-01T00:00:00Z',
				kind: 'tools_aggregate',
				sequenceId: 1,
				payload: {
					toolCalls: [
						{
							id: 'call-1',
							tool_call_id: 'call-1',
							function_name: 'test_tool',
							function_args: '{"test":true}'
						}
					],
					toolResults: [
						{
							toolCallId: 'call-1',
							result: 'Test result'
						}
					]
				}
			};

			const dto = handler.completeMessage('msg-complete', completeEnvelope);
			expect(dto.messageType).toBe('tools_aggregate');
			expect(dto.toolCallPairs).toHaveLength(1);
			expect(dto.toolCallPairs[0].toolCall.function_name).toBe('test_tool');
			expect(dto.toolCallPairs[0].toolResult?.result).toBe('Test result');
		});
	});

	describe('getRenderer', () => {
		it('should return a renderer with correct components', () => {
			const renderer = handler.getRenderer();

			expect(renderer).toBeDefined();
			expect(renderer.getStreamingComponent()).toBeDefined();
			expect(renderer.getCompleteComponent()).toBeDefined();
		});

		it('should provide correct streaming props', () => {
			const renderer = handler.getRenderer();
			const snapshot = {
				id: 'msg-1',
				chatId: 'chat-1',
				role: 'assistant' as const,
				timestamp: new Date(),
				sequenceNumber: 1,
				isStreaming: true,
				toolCallPairs: [
					{
						toolCall: { function_name: 'test', tool_call_id: 'call-1', id: 'call-1' },
						toolResult: { toolCallId: 'call-1', result: 'test result' }
					}
				]
			};

			const props = renderer.getStreamingProps(snapshot);

			expect(props.message).toBeDefined();
			expect(props.message.toolCallPairs).toEqual(snapshot.toolCallPairs);
			expect(props.message.messageType).toBe('tools_aggregate');
			expect(props.isStreaming).toBe(true);
		});

		it('should provide correct complete props', () => {
			const renderer = handler.getRenderer();
			const dto = {
				id: 'msg-1',
				chatId: 'chat-1',
				role: 'assistant',
				timestamp: new Date(),
				sequenceNumber: 1,
				messageType: 'tools_aggregate',
				toolCallPairs: [
					{
						toolCall: { function_name: 'test', tool_call_id: 'call-1', id: 'call-1' },
						toolResult: { toolCallId: 'call-1', result: 'test result' }
					}
				]
			};

			const props = renderer.getCompleteProps(dto);

			expect(props.message).toEqual(dto);
			expect(props.isStreaming).toBe(false);
		});
	});
});
