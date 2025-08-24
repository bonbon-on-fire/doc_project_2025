/**
 * Event Interfaces
 *
 * Proper TypeScript interfaces for SSE event payloads to replace 'any' types.
 * These interfaces ensure type safety throughout the message handling system.
 */

import type { MessageVisibility } from '$lib/constants/ui';

/**
 * Base event data structure
 */
export interface EventData {
	messageId?: string;
	chatId?: string;
	timestamp?: string;
	sequenceId?: number;
}

/**
 * Text message event data
 */
export interface TextEventData extends EventData {
	text?: string;
	textDelta?: string;
	isComplete?: boolean;
}

/**
 * Reasoning message event data
 */
export interface ReasoningEventData extends EventData {
	reasoning?: string;
	reasoningDelta?: string;
	visibility?: MessageVisibility | string;
	isComplete?: boolean;
}

/**
 * Tool call event data
 */
export interface ToolCallEventData extends EventData {
	toolCallId?: string;
	functionName?: string;
	functionArgs?: string;
	args?: any; // Parsed arguments object
	index?: number;
	isComplete?: boolean;
}

/**
 * Tool result event data
 */
export interface ToolResultEventData extends EventData {
	toolCallId: string;
	result: string | any;
	error?: string;
	isComplete?: boolean;
}

/**
 * Usage event data
 */
export interface UsageEventData extends EventData {
	promptTokens?: number;
	completionTokens?: number;
	totalTokens?: number;
	modelName?: string;
	cost?: number;
}

/**
 * Error event data
 */
export interface ErrorEventData extends EventData {
	error: string;
	errorType?: string;
	errorCode?: string;
	details?: any;
}

/**
 * Stream chunk event data union type
 */
export type StreamEventData =
	| TextEventData
	| ReasoningEventData
	| ToolCallEventData
	| ToolResultEventData
	| UsageEventData
	| ErrorEventData;

/**
 * Type guards for event data
 */
export const EventDataGuards = {
	isTextEvent(data: any): data is TextEventData {
		return (
			data != null &&
			typeof data === 'object' &&
			(typeof data.text === 'string' || typeof data.textDelta === 'string')
		);
	},

	isReasoningEvent(data: any): data is ReasoningEventData {
		return (
			data != null &&
			typeof data === 'object' &&
			(typeof data.reasoning === 'string' || typeof data.reasoningDelta === 'string')
		);
	},

	isToolCallEvent(data: any): data is ToolCallEventData {
		return (
			data != null &&
			typeof data === 'object' &&
			(typeof data.functionName === 'string' || typeof data.functionArgs === 'string')
		);
	},

	isToolResultEvent(data: any): data is ToolResultEventData {
		return (
			data != null &&
			typeof data === 'object' &&
			typeof data.toolCallId === 'string' &&
			data.result !== undefined
		);
	},

	isUsageEvent(data: any): data is UsageEventData {
		return (
			data != null &&
			typeof data === 'object' &&
			(typeof data.promptTokens === 'number' ||
				typeof data.completionTokens === 'number' ||
				typeof data.totalTokens === 'number')
		);
	},

	isErrorEvent(data: any): data is ErrorEventData {
		return data != null && typeof data === 'object' && typeof data.error === 'string';
	}
};

/**
 * Parse raw event data with type safety
 */
export function parseEventData(raw: string): StreamEventData | null {
	try {
		const parsed = JSON.parse(raw);

		// Validate and return typed data
		if (EventDataGuards.isTextEvent(parsed)) return parsed;
		if (EventDataGuards.isReasoningEvent(parsed)) return parsed;
		if (EventDataGuards.isToolCallEvent(parsed)) return parsed;
		if (EventDataGuards.isToolResultEvent(parsed)) return parsed;
		if (EventDataGuards.isUsageEvent(parsed)) return parsed;
		if (EventDataGuards.isErrorEvent(parsed)) return parsed;

		// Unknown event type - return as generic EventData
		return parsed as EventData;
	} catch {
		return null;
	}
}
