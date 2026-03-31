/**
 * Text Message Handler
 *
 * Handles the complete lifecycle of text messages:
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
	TextStreamChunkPayload,
	TextCompletePayload
} from '../sseEventTypes';
import { StreamChunkPayloadGuards, MessageCompletePayloadGuards } from '../sseEventTypes';
import type { MessageDto, TextMessageDto } from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';

/**
 * Renderer for text messages
 */
export class TextMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		// Return Svelte component for streaming text
		// NOTE: Placeholder renderer; not wired into UI yet. Callers should guard.
		return null as any; // TODO: Import actual Svelte component when ready
	}

	getCompleteComponent() {
		// Return Svelte component for complete text
		// NOTE: Placeholder renderer; not wired into UI yet. Callers should guard.
		return null as any; // TODO: Import actual Svelte component when ready
	}

	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			content: snapshot.textDelta || '',
			isStreaming: snapshot.isStreaming,
			messageId: snapshot.id
		};
	}

	getCompleteProps(dto: MessageDto): Record<string, any> {
		const textDto = dto as TextMessageDto;
		return {
			content: textDto.text,
			messageId: dto.id,
			timestamp: dto.timestamp
		};
	}
}

/**
 * Handler for text messages
 */
export class TextMessageHandler extends BaseMessageHandler {
	private renderer = new TextMessageRenderer();

	getMessageType(): string {
		return 'text';
	}

	canHandle(messageType: string): boolean {
		return messageType === 'text';
	}

	getRenderer(): MessageRenderer {
		return this.renderer;
	}

	/**
	 * Process text streaming chunk
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		console.log('[TextMessageHandler] processChunk called:', {
			messageId,
			envelopeKind: envelope.kind,
			payload: envelope.payload
		});

		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist - this handles cases where:
		// 1. First chunk for a new message
		// 2. Server streams multiple messages sequentially (reasoning -> text)
		// 3. Late completion events arrive after next message starts chunking
		if (!snapshot) {
			console.log('[TextMessageHandler] Creating new snapshot for message:', messageId);
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}

		// Validate payload type (gracefully handle missing 'done' as text)
		if (!StreamChunkPayloadGuards.isTextStreamChunk(envelope.payload)) {
			console.error(
				'[TextMessageHandler] Invalid payload for text message:',
				messageId,
				envelope.payload
			);
			throw new Error(`Invalid payload for text message: ${messageId}`);
		}
		const textPayload =
			envelope.payload as Partial<TextStreamChunkPayload> as TextStreamChunkPayload;

		// Accumulate text delta if present; tolerate missing/empty
		const delta = typeof textPayload.delta === 'string' ? textPayload.delta : '';
		console.log('[TextMessageHandler] Processing delta:', {
			delta,
			currentTextDelta: snapshot.textDelta
		});

		if (delta.length === 0) {
			// Nothing to append; keep current snapshot and streaming state
			console.log('[TextMessageHandler] Empty delta, keeping current state');
			return this.updateSnapshot(messageId, {
				textDelta: snapshot.textDelta || '',
				isStreaming: textPayload.done === true ? false : true
			});
		}

		const newTextDelta = (snapshot.textDelta || '') + delta;
		console.log('[TextMessageHandler] New textDelta:', newTextDelta);

		return this.updateSnapshot(messageId, {
			textDelta: newTextDelta,
			isStreaming: textPayload.done === true ? false : true
		});
	}

	/**
	 * Complete text message with final content
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist - this handles cases where:
		// 1. Messages arrive directly as complete messages (cached responses, etc.)
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
		if (!MessageCompletePayloadGuards.isTextComplete(envelope.payload)) {
			throw new Error(`Invalid completion payload for text message: ${messageId}`);
		}

		const textPayload = envelope.payload as TextCompletePayload;

		// Update snapshot with final content
		const finalSnapshot = this.updateSnapshot(messageId, {
			text: textPayload.text,
			textDelta: '', // Clear delta since we have final content
			isStreaming: false,
			isComplete: true
		});

		// Convert to DTO
		const dto = this.snapshotToDto(finalSnapshot);
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);

		return dto;
	}

	/**
	 * Convert text snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): TextMessageDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			text: snapshot.text || snapshot.textDelta || '',
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'text'
		};
	}
}

/**
 * Factory function for creating text message handler
 */
export function createTextMessageHandler(eventListener?: HandlerEventListener): TextMessageHandler {
	return new TextMessageHandler(eventListener);
}
