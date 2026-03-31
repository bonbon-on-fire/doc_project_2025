/**
 * Server-Side Event (SSE) Types
 *
 * This module contains TypeScript types that exactly match the server's
 * SSE event envelope structures defined in AIChat.Server.Models.SSE.
 *
 * These types ensure type safety when parsing SSE events from the server
 * and prevent mismatches between client and server event structures.
 */

// ============================================================================
// Base SSE Event Envelope Types
// ============================================================================

/**
 * Base interface for all SSE event envelopes
 * Matches: AIChat.Server.Models.SSE.SSEEventEnvelope
 */
export interface SSEEventEnvelope {
	chatId: string;
	version: number;
	ts: string; // ISO timestamp from server
	kind: string;
}

// ============================================================================
// Initialization Events
// ============================================================================

/**
 * Payload for initialization events
 * Matches: AIChat.Server.Models.SSE.InitPayload
 */
export interface InitPayload {
	userMessageId: string;
	userTimestamp: string; // ISO timestamp
	userSequenceNumber: number;
}

/**
 * Envelope for initialization events
 * Matches: AIChat.Server.Models.SSE.InitEventEnvelope
 */
export interface InitEventEnvelope extends SSEEventEnvelope {
	kind: 'meta';
	payload: InitPayload;
}

// ============================================================================
// Stream Chunk Events (Delta Updates)
// ============================================================================

/**
 * Base payload for streaming chunk events
 * Matches: AIChat.Server.Models.SSE.StreamChunkPayload
 */
export interface StreamChunkPayload {
	delta: string;
}

/**
 * Payload for text streaming chunks
 * Matches: AIChat.Server.Models.SSE.TextStreamChunkPayload
 */
export interface TextStreamChunkPayload extends StreamChunkPayload {
	done: boolean;
}

/**
 * Payload for reasoning streaming chunks
 * Matches: AIChat.Server.Models.SSE.ReasoningStreamChunkPayload
 */
export interface ReasoningStreamChunkPayload extends StreamChunkPayload {
	visibility?: string; // 'plain' | 'summary' | 'encrypted'
}

/**
 * Tool call update structure from server
 * Matches: AchieveAi.LmDotnetTools.LmCore.Messages.ToolCallUpdate
 */
export interface ToolCallUpdate {
	tool_call_id?: string;
	index?: number;
	function_name?: string;
	function_args?: string;
	// Incremental structured updates derived from server-side JsonFragmentUpdateMiddleware
	json_update_fragments?: JsonFragmentUpdate[];
}

// JsonFragments: client-side mirror of server fragment updates
export type JsonFragmentKind =
	| 'StartObject'
	| 'EndObject'
	| 'StartArray'
	| 'EndArray'
	| 'StartString'
	| 'PartialString'
	| 'CompleteString'
	| 'Key'
	| 'CompleteNumber'
	| 'CompleteBoolean'
	| 'CompleteNull'
	| 'JsonComplete';

export interface JsonFragmentUpdate {
	path: string;
	kind: JsonFragmentKind;
	textValue?: string;
	value?: unknown;
}

/**
 * Payload for tool call update streaming chunks
 * Matches: AIChat.Server.Services.ToolsCallUpdateStreamEvent
 */
export interface ToolCallUpdateStreamChunkPayload extends StreamChunkPayload {
	toolCallUpdate?: ToolCallUpdate; // Single tool call update object from server
}

/**
 * Payload for tool result streaming chunks
 * Matches: AIChat.Server.Services.ToolResultStreamEvent
 */
export interface ToolResultStreamChunkPayload extends StreamChunkPayload {
	toolCallId: string;
	result: string;
	isError?: boolean;
}

/**
 * Payload for task update streaming chunks
 * Matches: AIChat.Server.Models.SSE.TaskUpdateStreamChunkPayload
 */
export interface TaskUpdateStreamChunkPayload extends StreamChunkPayload {
	taskState: any; // JsonElement from server
	operationType: string; // 'sync', 'add', 'update', 'delete', etc.
}

/**
 * Envelope for streaming chunk events (delta updates)
 * Matches: AIChat.Server.Models.SSE.StreamChunkEventEnvelope
 */
export interface StreamChunkEventEnvelope extends SSEEventEnvelope {
	messageId: string;
	sequenceId: number;
	payload:
		| TextStreamChunkPayload
		| ReasoningStreamChunkPayload
		| ToolCallUpdateStreamChunkPayload
		| ToolResultStreamChunkPayload
		| TaskUpdateStreamChunkPayload;
}

// ============================================================================
// Message Complete Events
// ============================================================================

/**
 * Base payload for message completion events
 * Matches: AIChat.Server.Models.SSE.MessageCompletePayload
 */
export interface MessageCompletePayload {
	// Base class - no additional properties
}

/**
 * Payload for completed text messages
 * Matches: AIChat.Server.Models.SSE.TextCompletePayload
 */
export interface TextCompletePayload extends MessageCompletePayload {
	text: string;
}

/**
 * Payload for completed reasoning messages
 * Matches: AIChat.Server.Models.SSE.ReasoningCompletePayload
 */
export interface ReasoningCompletePayload extends MessageCompletePayload {
	reasoning: string;
	visibility?: string; // 'plain' | 'summary' | 'encrypted'
}

/**
 * Payload for completed tool call messages
 * Matches: AIChat.Server.Models.SSE.ToolCallCompletePayload
 */
export interface ToolCallCompletePayload extends MessageCompletePayload {
	toolCalls: any[]; // ToolCall[] from server
}

/**
 * Payload for usage information
 * Matches: AIChat.Server.Models.SSE.UsageCompletePayload
 */
export interface UsageCompletePayload extends MessageCompletePayload {
	usage: Record<string, unknown>;
}

/**
 * Envelope for complete message events
 * Matches: AIChat.Server.Models.SSE.MessageCompleteEventEnvelope
 */
export interface MessageCompleteEventEnvelope extends SSEEventEnvelope {
	messageId: string;
	sequenceId: number;
	payload:
		| TextCompletePayload
		| ReasoningCompletePayload
		| ToolCallCompletePayload
		| UsageCompletePayload;
}

// ============================================================================
// Stream Completion Events
// ============================================================================

/**
 * Envelope for stream completion events
 * Matches: AIChat.Server.Models.SSE.StreamCompleteEventEnvelope
 */
export interface StreamCompleteEventEnvelope extends SSEEventEnvelope {
	kind: 'complete';
	// No additional properties beyond base
}

// ============================================================================
// Task Operation Events
// ============================================================================

/**
 * Represents a task management operation
 * Matches: AIChat.Server.Models.SSE.TaskOperation
 */
export interface TaskOperation {
	type: string; // "add", "update", "delete", "list", "manage_notes"
	function: string;
	arguments?: any;
	result?: any;
}

/**
 * Payload for task operation events
 * Matches: AIChat.Server.Models.SSE.TaskOperationPayload
 */
export interface TaskOperationPayload {
	operationType: 'start' | 'complete' | 'sync';
	operation?: TaskOperation;
	taskState?: any; // Task state as JSON
	version?: number;
}

/**
 * Envelope for task operation events
 * Matches: AIChat.Server.Models.SSE.TaskOperationEventEnvelope
 */
export interface TaskOperationEventEnvelope extends SSEEventEnvelope {
	kind: 'task_operation';
	messageId?: string;
	payload: TaskOperationPayload;
}

// ============================================================================
// Error Events
// ============================================================================

/**
 * Payload for error events
 * Matches: AIChat.Server.Models.SSE.ErrorPayload
 */
export interface ErrorPayload {
	message: string;
	code?: string;
}

/**
 * Envelope for error events
 * Matches: AIChat.Server.Models.SSE.ErrorEventEnvelope
 */
export interface ErrorEventEnvelope extends SSEEventEnvelope {
	messageId?: string;
	sequenceId?: number;
	kind: 'error';
	payload: ErrorPayload;
}

// ============================================================================
// Union Types for Type-Safe Event Processing
// ============================================================================

/**
 * Union type for all SSE event envelopes
 * Enables type-safe parsing of different SSE event types
 */
export type SSEEventEnvelopeUnion =
	| InitEventEnvelope
	| StreamChunkEventEnvelope
	| MessageCompleteEventEnvelope
	| StreamCompleteEventEnvelope
	| TaskOperationEventEnvelope
	| ErrorEventEnvelope;

/**
 * Type guards for SSE event envelopes
 */
export namespace SSEEventGuards {
	export function isInitEvent(envelope: SSEEventEnvelope): envelope is InitEventEnvelope {
		return envelope.kind === 'meta' && 'payload' in envelope;
	}

	export function isStreamChunkEvent(
		envelope: SSEEventEnvelope
	): envelope is StreamChunkEventEnvelope {
		if (!('messageId' in envelope) || !('sequenceId' in envelope) || !('payload' in envelope))
			return false;
		const payload: any = (envelope as any).payload;
		// Check for different types of streaming chunks:
		// - Text/reasoning chunks have 'delta' property
		// - Tool call updates have 'toolCallUpdate' property
		// - Task updates have 'taskState' property
		return (
			typeof payload === 'object' &&
			payload != null &&
			// Text or reasoning chunk
			(('delta' in payload &&
				('done' in payload || 'visibility' in payload || Object.keys(payload).length === 1)) || // delta-only
				// Tool call update chunk
				'toolCallUpdate' in payload ||
				// Tool result chunk
				'toolCallId' in payload ||
				// Task update chunk
				('taskState' in payload && 'operationType' in payload))
		);
	}

	export function isMessageCompleteEvent(
		envelope: SSEEventEnvelope
	): envelope is MessageCompleteEventEnvelope {
		if (!('messageId' in envelope) || !('sequenceId' in envelope) || !('payload' in envelope))
			return false;
		const payload: any = (envelope as any).payload;
		// Completion payloads have final fields like text, reasoning, usage, or toolCalls
		return (
			typeof payload === 'object' &&
			payload != null &&
			('text' in payload || 'reasoning' in payload || 'usage' in payload || 'toolCalls' in payload)
		);
	}

	export function isStreamCompleteEvent(
		envelope: SSEEventEnvelope
	): envelope is StreamCompleteEventEnvelope {
		return envelope.kind === 'complete';
	}

	export function isTaskOperationEvent(
		envelope: SSEEventEnvelope
	): envelope is TaskOperationEventEnvelope {
		return envelope.kind === 'task_operation' && 'payload' in envelope;
	}

	export function isErrorEvent(envelope: SSEEventEnvelope): envelope is ErrorEventEnvelope {
		return envelope.kind === 'error' && 'payload' in envelope;
	}
}

/**
 * Payload type guards for streaming chunks
 */
export namespace StreamChunkPayloadGuards {
	export function isTextStreamChunk(
		payload: StreamChunkPayload
	): payload is TextStreamChunkPayload {
		// Treat as text when it has no reasoning-specific field.
		// Some backends may omit 'done' on intermediate chunks; default to text.
		return 'done' in payload || !('visibility' in (payload as any));
	}

	export function isReasoningStreamChunk(
		payload: StreamChunkPayload
	): payload is ReasoningStreamChunkPayload {
		return 'visibility' in (payload as any);
	}

	export function isToolCallUpdateStreamChunk(
		payload: StreamChunkPayload
	): payload is ToolCallUpdateStreamChunkPayload {
		return 'toolCallUpdate' in (payload as any);
	}

	export function isToolResultStreamChunk(
		payload: StreamChunkPayload
	): payload is ToolResultStreamChunkPayload {
		return 'toolCallId' in (payload as any) && 'result' in (payload as any);
	}

	/**
	 * Type guard for TaskUpdateStreamChunkPayload
	 */
	export function isTaskUpdateStreamChunk(
		payload: StreamChunkPayload
	): payload is TaskUpdateStreamChunkPayload {
		return 'taskState' in (payload as any) && 'operationType' in (payload as any);
	}
}

/**
 * Payload type guards for message completion
 */
export namespace MessageCompletePayloadGuards {
	export function isTextComplete(payload: MessageCompletePayload): payload is TextCompletePayload {
		return 'text' in payload;
	}

	export function isReasoningComplete(
		payload: MessageCompletePayload
	): payload is ReasoningCompletePayload {
		return 'reasoning' in payload;
	}

	export function isToolCallComplete(
		payload: MessageCompletePayload
	): payload is ToolCallCompletePayload {
		return 'toolCalls' in payload;
	}

	export function isUsageComplete(
		payload: MessageCompletePayload
	): payload is UsageCompletePayload {
		return 'usage' in payload;
	}
}
