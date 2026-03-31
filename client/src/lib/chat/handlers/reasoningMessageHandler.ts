/**
 * Reasoning Message Handler
 *
 * Handles the complete lifecycle of reasoning messages:
 * - Initialization, streaming chunks, completion, and rendering
 * - Includes visibility control (plain, summary, encrypted)
 */

import type { MessageRenderer, MessageSnapshot, HandlerEventListener } from '../messageHandlers';
import type {
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	ReasoningStreamChunkPayload,
	ReasoningCompletePayload
} from '../sseEventTypes';
import { StreamChunkPayloadGuards, MessageCompletePayloadGuards } from '../sseEventTypes';
import type { MessageDto, ReasoningMessageDto } from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';
import ReasoningRenderer from '$lib/components/ReasoningRenderer.svelte';

/**
 * Renderer for reasoning messages
 */
export class ReasoningMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		return ReasoningRenderer;
	}

	getCompleteComponent() {
		return ReasoningRenderer;
	}

	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			content: snapshot.reasoningDelta || '',
			isStreaming: snapshot.isStreaming,
			visibility: snapshot.visibility,
			messageId: snapshot.id
		};
	}

	getCompleteProps(dto: MessageDto): Record<string, any> {
		const reasoningDto = dto as ReasoningMessageDto;
		return {
			content: reasoningDto.reasoning,
			visibility: reasoningDto.visibility,
			messageId: dto.id,
			timestamp: dto.timestamp
		};
	}
}

/**
 * Handler for reasoning messages
 */
export class ReasoningMessageHandler extends BaseMessageHandler {
	private renderer = new ReasoningMessageRenderer();

	getMessageType(): string {
		return 'reasoning';
	}

	canHandle(messageType: string): boolean {
		return messageType === 'reasoning';
	}

	getRenderer(): MessageRenderer {
		return this.renderer;
	}

	/**
	 * Process reasoning streaming chunk
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist - this handles cases where:
		// 1. First chunk for a new message
		// 2. Server streams multiple messages sequentially (reasoning -> text)
		// 3. Late completion events arrive after next message starts chunking
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}

		// Validate payload type
		if (!StreamChunkPayloadGuards.isReasoningStreamChunk(envelope.payload)) {
			throw new Error(`Invalid payload for reasoning message: ${messageId}`);
		}

		const reasoningPayload = envelope.payload as ReasoningStreamChunkPayload;

		// Accumulate reasoning delta
		const newReasoningDelta = (snapshot.reasoningDelta || '') + reasoningPayload.delta;

		const updates: Partial<MessageSnapshot> = {
			reasoningDelta: newReasoningDelta,
			isStreaming: true // Reasoning messages don't have a "done" flag in chunks
		};

		// Update visibility if provided
		if (reasoningPayload.visibility) {
			// Convert server casing (lowercase) to client casing (Pascal)
			const visibility = (reasoningPayload.visibility.charAt(0).toUpperCase() +
				reasoningPayload.visibility.slice(1)) as 'Plain' | 'Summary' | 'Encrypted';
			updates.visibility = visibility;
		}

		return this.updateSnapshot(messageId, updates);
	}

	/**
	 * Complete reasoning message with final content
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist - this handles cases where:
		// 1. Encrypted reasoning messages arrive directly as complete messages
		// 2. Messages that don't go through streaming phase (e.g., cached responses)
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}

		// Validate payload type
		if (!MessageCompletePayloadGuards.isReasoningComplete(envelope.payload)) {
			throw new Error(`Invalid completion payload for reasoning message: ${messageId}`);
		}

		const reasoningPayload = envelope.payload as ReasoningCompletePayload;

		const updates: Partial<MessageSnapshot> = {
			reasoning: reasoningPayload.reasoning,
			reasoningDelta: '', // Clear delta since we have final content
			isStreaming: false,
			isComplete: true
		};

		// Update visibility if provided
		if (reasoningPayload.visibility) {
			// Convert server casing (lowercase) to client casing (Pascal)
			const visibility = (reasoningPayload.visibility.charAt(0).toUpperCase() +
				reasoningPayload.visibility.slice(1)) as 'Plain' | 'Summary' | 'Encrypted';
			updates.visibility = visibility;
		}

		// Update snapshot with final content
		const finalSnapshot = this.updateSnapshot(messageId, updates);

		// Convert to DTO
		const dto = this.snapshotToDto(finalSnapshot);
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);

		return dto;
	}

	/**
	 * Convert reasoning snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ReasoningMessageDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			reasoning: snapshot.reasoning || snapshot.reasoningDelta || '',
			visibility: snapshot.visibility,
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'reasoning'
		};
	}
}

/**
 * Factory function for creating reasoning message handler
 */
export function createReasoningMessageHandler(
	eventListener?: HandlerEventListener
): ReasoningMessageHandler {
	return new ReasoningMessageHandler(eventListener);
}
