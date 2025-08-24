import { describe, it, expect } from 'vitest';
import {
	EventDataGuards,
	parseEventData,
	type TextEventData,
	type ReasoningEventData,
	type ToolCallEventData,
	type ToolResultEventData,
	type UsageEventData,
	type ErrorEventData
} from './eventInterfaces';

describe('Event Interfaces', () => {
	describe('EventDataGuards', () => {
		describe('isTextEvent', () => {
			it('should identify text events', () => {
				const textEvent: TextEventData = {
					text: 'Hello world',
					messageId: '123'
				};
				expect(EventDataGuards.isTextEvent(textEvent)).toBe(true);
			});

			it('should identify text delta events', () => {
				const textDeltaEvent: TextEventData = {
					textDelta: 'streaming text',
					messageId: '123'
				};
				expect(EventDataGuards.isTextEvent(textDeltaEvent)).toBe(true);
			});

			it('should reject non-text events', () => {
				expect(EventDataGuards.isTextEvent({ reasoning: 'test' })).toBe(false);
				expect(EventDataGuards.isTextEvent({ functionName: 'test' })).toBe(false);
				expect(EventDataGuards.isTextEvent(null)).toBe(false);
				expect(EventDataGuards.isTextEvent(undefined)).toBe(false);
			});
		});

		describe('isReasoningEvent', () => {
			it('should identify reasoning events', () => {
				const reasoningEvent: ReasoningEventData = {
					reasoning: 'Thinking about the problem',
					visibility: 'Plain'
				};
				expect(EventDataGuards.isReasoningEvent(reasoningEvent)).toBe(true);
			});

			it('should identify reasoning delta events', () => {
				const reasoningDeltaEvent: ReasoningEventData = {
					reasoningDelta: 'streaming reasoning',
					visibility: 'Summary'
				};
				expect(EventDataGuards.isReasoningEvent(reasoningDeltaEvent)).toBe(true);
			});

			it('should reject non-reasoning events', () => {
				expect(EventDataGuards.isReasoningEvent({ text: 'test' })).toBe(false);
				expect(EventDataGuards.isReasoningEvent({ functionName: 'test' })).toBe(false);
				expect(EventDataGuards.isReasoningEvent(null)).toBe(false);
			});
		});

		describe('isToolCallEvent', () => {
			it('should identify tool call events', () => {
				const toolCallEvent: ToolCallEventData = {
					functionName: 'calculate',
					functionArgs: '{"a": 1, "b": 2}',
					toolCallId: 'tool_123'
				};
				expect(EventDataGuards.isToolCallEvent(toolCallEvent)).toBe(true);
			});

			it('should reject non-tool call events', () => {
				expect(EventDataGuards.isToolCallEvent({ text: 'test' })).toBe(false);
				expect(EventDataGuards.isToolCallEvent({ reasoning: 'test' })).toBe(false);
				expect(EventDataGuards.isToolCallEvent(null)).toBe(false);
			});
		});

		describe('isToolResultEvent', () => {
			it('should identify tool result events', () => {
				const toolResultEvent: ToolResultEventData = {
					toolCallId: 'tool_123',
					result: '3'
				};
				expect(EventDataGuards.isToolResultEvent(toolResultEvent)).toBe(true);
			});

			it('should identify tool result events with object results', () => {
				const toolResultEvent: ToolResultEventData = {
					toolCallId: 'tool_123',
					result: { value: 42, status: 'success' }
				};
				expect(EventDataGuards.isToolResultEvent(toolResultEvent)).toBe(true);
			});

			it('should reject events without required fields', () => {
				expect(EventDataGuards.isToolResultEvent({ result: '3' })).toBe(false);
				expect(EventDataGuards.isToolResultEvent({ toolCallId: 'test' })).toBe(false);
				expect(EventDataGuards.isToolResultEvent({ text: 'test' })).toBe(false);
			});
		});

		describe('isUsageEvent', () => {
			it('should identify usage events', () => {
				const usageEvent: UsageEventData = {
					promptTokens: 100,
					completionTokens: 50,
					totalTokens: 150
				};
				expect(EventDataGuards.isUsageEvent(usageEvent)).toBe(true);
			});

			it('should identify partial usage events', () => {
				expect(EventDataGuards.isUsageEvent({ promptTokens: 100 })).toBe(true);
				expect(EventDataGuards.isUsageEvent({ completionTokens: 50 })).toBe(true);
				expect(EventDataGuards.isUsageEvent({ totalTokens: 150 })).toBe(true);
			});

			it('should reject non-usage events', () => {
				expect(EventDataGuards.isUsageEvent({ text: 'test' })).toBe(false);
				expect(EventDataGuards.isUsageEvent({ tokens: '100' })).toBe(false);
				expect(EventDataGuards.isUsageEvent(null)).toBe(false);
			});
		});

		describe('isErrorEvent', () => {
			it('should identify error events', () => {
				const errorEvent: ErrorEventData = {
					error: 'Something went wrong',
					errorType: 'ValidationError',
					errorCode: 'ERR_001'
				};
				expect(EventDataGuards.isErrorEvent(errorEvent)).toBe(true);
			});

			it('should identify minimal error events', () => {
				const errorEvent: ErrorEventData = {
					error: 'Error occurred'
				};
				expect(EventDataGuards.isErrorEvent(errorEvent)).toBe(true);
			});

			it('should reject non-error events', () => {
				expect(EventDataGuards.isErrorEvent({ message: 'test' })).toBe(false);
				expect(EventDataGuards.isErrorEvent({ errorCode: 'ERR_001' })).toBe(false);
				expect(EventDataGuards.isErrorEvent(null)).toBe(false);
			});
		});
	});

	describe('parseEventData', () => {
		it('should parse valid text event JSON', () => {
			const json = JSON.stringify({ text: 'Hello', messageId: '123' });
			const result = parseEventData(json);
			expect(result).toEqual({ text: 'Hello', messageId: '123' });
			expect(EventDataGuards.isTextEvent(result)).toBe(true);
		});

		it('should parse valid reasoning event JSON', () => {
			const json = JSON.stringify({
				reasoning: 'Thinking...',
				visibility: 'Plain'
			});
			const result = parseEventData(json);
			expect(result).toEqual({ reasoning: 'Thinking...', visibility: 'Plain' });
			expect(EventDataGuards.isReasoningEvent(result)).toBe(true);
		});

		it('should parse valid tool call event JSON', () => {
			const json = JSON.stringify({
				functionName: 'calculate',
				functionArgs: '{"a": 1}',
				toolCallId: 'tool_123'
			});
			const result = parseEventData(json);
			expect(result?.functionName).toBe('calculate');
			expect(EventDataGuards.isToolCallEvent(result)).toBe(true);
		});

		it('should parse valid tool result event JSON', () => {
			const json = JSON.stringify({
				toolCallId: 'tool_123',
				result: '42'
			});
			const result = parseEventData(json);
			expect(result?.toolCallId).toBe('tool_123');
			expect(result?.result).toBe('42');
			expect(EventDataGuards.isToolResultEvent(result)).toBe(true);
		});

		it('should parse valid usage event JSON', () => {
			const json = JSON.stringify({
				promptTokens: 100,
				completionTokens: 50,
				totalTokens: 150
			});
			const result = parseEventData(json);
			expect(result?.promptTokens).toBe(100);
			expect(EventDataGuards.isUsageEvent(result)).toBe(true);
		});

		it('should parse valid error event JSON', () => {
			const json = JSON.stringify({
				error: 'Something failed',
				errorType: 'NetworkError'
			});
			const result = parseEventData(json);
			expect(result?.error).toBe('Something failed');
			expect(EventDataGuards.isErrorEvent(result)).toBe(true);
		});

		it('should return generic EventData for unknown event types', () => {
			const json = JSON.stringify({
				unknownField: 'value',
				messageId: '123'
			});
			const result = parseEventData(json);
			expect(result).toEqual({
				unknownField: 'value',
				messageId: '123'
			});
		});

		it('should return null for invalid JSON', () => {
			expect(parseEventData('invalid json')).toBeNull();
			expect(parseEventData('')).toBeNull();
			expect(parseEventData('{')).toBeNull();
		});
	});
});
