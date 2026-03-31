/**
 * Tool Call Message Handler
 *
 * Handles the complete lifecycle of tool call messages:
 * - Initialization, streaming chunks, completion, and rendering
 */

import type {
	MessageTypeHandler,
	MessageRenderer,
	MessageSnapshot,
	HandlerEventListener
} from '../messageHandlers';
import type {
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	ToolCallUpdateStreamChunkPayload,
	ToolCallUpdate,
	ToolCallCompletePayload
} from '../sseEventTypes';
import { StreamChunkPayloadGuards, MessageCompletePayloadGuards } from '../sseEventTypes';
import type { MessageDto } from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';

/**
 * Interface for tool call data
 */
export interface ToolCallDto extends MessageDto {
	messageType: 'tool_call';
	toolCalls?: Array<{
		name: string;
		args: any;
		id?: string;
	}>;
}

/**
 * Renderer for tool call messages
 */
export class ToolCallMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		// Return Svelte component for streaming tool calls
		return null as any; // TODO: Import actual Svelte component when ready
	}

	getCompleteComponent() {
		// Return Svelte component for complete tool calls
		return null as any; // TODO: Import actual Svelte component when ready
	}

	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			toolCalls: snapshot.toolCalls || [],
			isStreaming: snapshot.isStreaming,
			messageId: snapshot.id
		};
	}

	getCompleteProps(dto: MessageDto): Record<string, any> {
		const toolCallDto = dto as ToolCallDto;
		return {
			toolCalls: toolCallDto.toolCalls || [],
			messageId: dto.id,
			timestamp: dto.timestamp
		};
	}
}

/**
 * Handler for tool call messages
 */
export class ToolCallMessageHandler extends BaseMessageHandler {
	private renderer = new ToolCallMessageRenderer();

	getMessageType(): string {
		return 'tools_call_update';
	}

	canHandle(messageType: string): boolean {
		// Handle both streaming updates and complete tool call messages
		return messageType === 'tools_call_update' || messageType === 'tools_call';
	}

	getRenderer(): MessageRenderer {
		return this.renderer;
	}

	/**
	 * Process tool call streaming chunk
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		console.log('[ToolCallMessageHandler] processChunk called:', {
			messageId,
			envelopeKind: envelope.kind,
			payload: envelope.payload
		});

		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist
		if (!snapshot) {
			console.log('[ToolCallMessageHandler] Creating new snapshot for message:', messageId);
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);

			// Initialize empty tool calls array
			snapshot = this.updateSnapshot(messageId, {
				toolCalls: [],
				messageType: 'tool_call'
			});
		}

		// Check if this is a complete tool call message or a streaming update
		if (envelope.kind === 'tools_call') {
			// Handle complete tool call message
			console.log('[ToolCallMessageHandler] Processing complete tool call message');
			const payload = envelope.payload as any;
			if (payload.toolCalls && Array.isArray(payload.toolCalls)) {
				// Set complete tool calls directly
				snapshot = this.updateSnapshot(messageId, {
					toolCalls: payload.toolCalls.map((tc: any) => ({
						id: tc.tool_call_id || tc.id || `${messageId}_tool_${tc.index || 0}`,
						name: tc.function_name || tc.name || '',
						args: tc.function_args ? JSON.parse(tc.function_args) : tc.args || {},
						argsJson: tc.function_args || JSON.stringify(tc.args || {})
					})),
					messageType: 'tool_call'
				});
				console.log('[ToolCallMessageHandler] Set complete tool calls:', {
					count: payload.toolCalls.length,
					toolCalls: snapshot.toolCalls
				});
				return snapshot;
			}
		}

		// Validate payload type for streaming updates
		if (!StreamChunkPayloadGuards.isToolCallUpdateStreamChunk(envelope.payload)) {
			console.error(
				'[ToolCallMessageHandler] Invalid payload for tool call message:',
				envelope.messageId,
				envelope.payload
			);
			throw new Error(`Invalid payload for tool call message: ${envelope.messageId}`);
		}

		const toolCallPayload = envelope.payload as ToolCallUpdateStreamChunkPayload;

		// Process single tool call update from payload (server sends one per chunk)
		if (toolCallPayload.toolCallUpdate) {
			const toolCallUpdate: ToolCallUpdate = toolCallPayload.toolCallUpdate;
			console.log('[ToolCallMessageHandler] Processing tool call update:', {
				toolCallUpdate,
				hasToolCallId: !!toolCallUpdate.tool_call_id,
				hasFunctionName: !!toolCallUpdate.function_name,
				hasFunctionArgs: !!toolCallUpdate.function_args,
				hasIndex: toolCallUpdate.index !== undefined
			});
			const currentToolCalls = snapshot.toolCalls || [];
			const toolCallsMap = new Map<string, any>();

			// First, add existing tool calls to the map
			currentToolCalls.forEach((tc: any) => {
				toolCallsMap.set(tc.id, tc);
			});

			// Process the tool call update - resolve tool call ID correctly
			let toolCallId: string;
			let existingToolCall: any;

			if (toolCallUpdate.tool_call_id) {
				// Use server-provided tool_call_id when available
				toolCallId = toolCallUpdate.tool_call_id;
				existingToolCall = toolCallsMap.get(toolCallId);
			} else {
				// No tool_call_id provided, find existing tool call by index
				const targetIndex = toolCallUpdate.index || 0;
				existingToolCall = currentToolCalls.find((tc: any) => tc.index === targetIndex);

				if (existingToolCall) {
					// Reuse existing tool call's ID to maintain consistency
					toolCallId = existingToolCall.id;
				} else {
					// Create new fallback ID only if no existing tool call found
					toolCallId = `${messageId}_tool_${targetIndex}`;
				}
			}

			// Create new tool call if none exists
			if (!existingToolCall) {
				existingToolCall = {
					id: toolCallId,
					name: '',
					args: {},
					argsJson: '',
					index: toolCallUpdate.index || 0
				};
			}

			// Update with new data from this chunk
			if (toolCallUpdate.function_name) {
				existingToolCall.name = toolCallUpdate.function_name;
				existingToolCall.function_name = toolCallUpdate.function_name;
			}

			// Ensure tool_call_id and index are set
			if (toolCallUpdate.tool_call_id && !existingToolCall.tool_call_id) {
				existingToolCall.tool_call_id = toolCallUpdate.tool_call_id;
			}
			if (toolCallUpdate.index !== undefined && existingToolCall.index === undefined) {
				existingToolCall.index = toolCallUpdate.index;
			}

			// Accumulate function arguments (streaming JSON fragments)
			if (toolCallUpdate.function_args) {
				existingToolCall.argsJson =
					(existingToolCall.argsJson || '') + toolCallUpdate.function_args;
				try {
					// Try to parse accumulated JSON
					existingToolCall.args = JSON.parse(existingToolCall.argsJson);
				} catch {
					// Keep as partial JSON string if not yet complete
					existingToolCall.args = { partial: existingToolCall.argsJson };
				}
			}

			toolCallsMap.set(toolCallId, existingToolCall);

			// Convert map back to array
			const newToolCalls = Array.from(toolCallsMap.values());

			console.log('[ToolCallMessageHandler] Updated tool calls:', {
				count: newToolCalls.length,
				toolCalls: newToolCalls.map((tc) => ({
					id: tc.id,
					name: tc.name,
					hasArgs: !!tc.args,
					argsJson: tc.argsJson?.substring(0, 50) + '...'
				}))
			});

			return this.updateSnapshot(messageId, {
				toolCalls: newToolCalls,
				isStreaming: true
			});
		}

		// If no tool call data, keep current snapshot
		return snapshot;
	}

	/**
	 * Complete tool call message with final content
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		console.log('[ToolCallMessageHandler] Completing message:', {
			messageId,
			kind: envelope.kind,
			payload: envelope.payload
		});

		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist - this handles cases where:
		// 1. Tool call messages arrive directly as complete messages (cached responses, etc.)
		// 2. Messages that don't go through streaming phase
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}

		// Validate payload type
		if (!MessageCompletePayloadGuards.isToolCallComplete(envelope.payload)) {
			throw new Error(`Invalid completion payload for tool call message: ${messageId}`);
		}

		const toolCallPayload = envelope.payload as ToolCallCompletePayload;

		console.log('[ToolCallMessageHandler] Final tool calls from completion:', {
			count: toolCallPayload.toolCalls?.length || 0,
			toolCalls: toolCallPayload.toolCalls
		});

		// Update snapshot with final tool calls
		const finalSnapshot = this.updateSnapshot(messageId, {
			toolCalls: toolCallPayload.toolCalls,
			isStreaming: false,
			isComplete: true
		});

		// Convert to DTO
		const dto = this.snapshotToDto(finalSnapshot);
		console.log('[ToolCallMessageHandler] Final DTO:', {
			id: dto.id,
			messageType: dto.messageType,
			toolCallsCount: dto.toolCalls?.length || 0,
			toolCalls: dto.toolCalls
		});

		this.emitEvent('message_completed', messageId, envelope.chatId, dto);

		return dto;
	}

	/**
	 * Convert tool call snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ToolCallDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			toolCalls: snapshot.toolCalls || [],
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'tool_call'
		};
	}
}

/**
 * Factory function for creating tool call message handler
 */
export function createToolCallMessageHandler(
	eventListener?: HandlerEventListener
): ToolCallMessageHandler {
	return new ToolCallMessageHandler(eventListener);
}
