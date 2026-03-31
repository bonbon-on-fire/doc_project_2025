/**
 * Message Type Handler Architecture
 *
 * This module defines a clean, extensible architecture where each message type
 * owns its complete lifecycle: initialization, streaming, completion, and rendering.
 *
 * Benefits:
 * - Single Responsibility: Each handler owns one message type completely
 * - Extensibility: New message types = new handler registration
 * - Cohesive Rendering: Each handler owns its rendering logic
 * - Isolated State: Each handler manages its own message snapshots
 * - No Legacy Cruft: Clean slate design
 */

import type { SvelteComponent } from 'svelte';
import type {
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	SSEEventEnvelopeUnion
} from './sseEventTypes';
import type { MessageDto } from '$lib/types/chat';

// ============================================================================
// Core Handler Interfaces
// ============================================================================

/**
 * Snapshot of a message during streaming (working state)
 */
export interface MessageSnapshot {
	id: string;
	chatId: string;
	role: 'user' | 'assistant';
	messageType: string;
	timestamp: Date;
	sequenceNumber: number;
	isStreaming: boolean;
	isComplete: boolean;

	// Content fields (different handlers use different fields)
	textDelta?: string;
	text?: string;
	reasoningDelta?: string;
	reasoning?: string;
	visibility?: 'Plain' | 'Summary' | 'Encrypted';
	usage?: Record<string, unknown>;

	// Tool call fields
	toolCalls?: Array<{
		name: string;
		args: any;
		argsJson?: string; // For accumulating streamed JSON
		id?: string;
	}>;

	// Tool results for aggregate messages
	toolResults?: Array<{
		toolCallId: string;
		result: string;
	}>;

	// Paired structure for aggregate messages
	toolCallPairs?: Array<{
		toolCall: any;
		toolResult?: any;
	}>;
}

/**
 * Rendering interface for message types
 */
export interface MessageRenderer {
	/**
	 * Get component for rendering streaming state
	 */
	getStreamingComponent(): typeof SvelteComponent;

	/**
	 * Get component for rendering complete state
	 */
	getCompleteComponent(): typeof SvelteComponent;

	/**
	 * Get props for streaming component
	 */
	getStreamingProps(snapshot: MessageSnapshot): Record<string, any>;

	/**
	 * Get props for complete component
	 */
	getCompleteProps(dto: MessageDto): Record<string, any>;
}

/**
 * Core interface for message type handlers
 * Each handler owns the complete lifecycle for one message type
 */
export interface MessageTypeHandler {
	/**
	 * Initialize a new message from an assistant message creation event
	 */
	initializeMessage(
		messageId: string,
		chatId: string,
		timestamp: Date,
		sequenceNumber: number
	): MessageSnapshot;

	/**
	 * Process a streaming chunk update
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot;

	/**
	 * Process message completion with final content
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto;

	/**
	 * Get current snapshot for a message
	 */
	getSnapshot(messageId: string): MessageSnapshot | null;

	/**
	 * Finalize a message and convert to DTO (for stream completion)
	 */
	finalizeMessage(messageId: string): MessageDto | null;

	/**
	 * Clear message state
	 */
	clearMessage(messageId: string): void;

	/**
	 * Get renderer for this message type
	 */
	getRenderer(): MessageRenderer;

	/**
	 * Get the message type this handler manages
	 */
	getMessageType(): string;

	/**
	 * Check if this handler can handle the given message type
	 */
	canHandle(messageType: string): boolean;
}

// ============================================================================
// Handler Registry
// ============================================================================

/**
 * Registry for all message type handlers
 * Provides centralized access to handlers by message type
 */
export interface MessageHandlerRegistry {
	/**
	 * Register a new message type handler
	 */
	register(handler: MessageTypeHandler): void;

	/**
	 * Get handler for a specific message type
	 */
	getHandler(messageType: string): MessageTypeHandler | null;

	/**
	 * Get all registered handlers
	 */
	getAllHandlers(): MessageTypeHandler[];

	/**
	 * Check if a message type is supported
	 */
	isSupported(messageType: string): boolean;

	/**
	 * Clear all handlers
	 */
	clear(): void;
}

// ============================================================================
// Events for Handler Communication
// ============================================================================

/**
 * Events that handlers can emit to communicate with the chat system
 */
export interface HandlerEvent {
	type: 'message_created' | 'message_updated' | 'message_completed' | 'error';
	messageId: string;
	chatId: string;
	data?: any;
}

/**
 * Handler event listener interface
 */
export interface HandlerEventListener {
	onHandlerEvent(event: HandlerEvent): void;
}

// ============================================================================
// Base Handler Implementation
// ============================================================================

/**
 * Abstract base class for message type handlers
 * Provides common functionality and state management
 */
export abstract class BaseMessageHandler implements MessageTypeHandler {
	protected snapshots = new Map<string, MessageSnapshot>();
	protected eventListener?: HandlerEventListener;

	constructor(eventListener?: HandlerEventListener) {
		this.eventListener = eventListener;
	}

	abstract getMessageType(): string;
	abstract getRenderer(): MessageRenderer;
	abstract canHandle(messageType: string): boolean;

	/**
	 * Template method for processing chunks - subclasses implement the specifics
	 */
	abstract processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot;

	/**
	 * Template method for completing messages - subclasses implement the specifics
	 */
	abstract completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto;

	/**
	 * Common initialization logic
	 */
	initializeMessage(
		messageId: string,
		chatId: string,
		timestamp: Date,
		sequenceNumber: number
	): MessageSnapshot {
		const snapshot: MessageSnapshot = {
			id: messageId,
			chatId,
			role: 'assistant',
			messageType: this.getMessageType(),
			timestamp,
			sequenceNumber,
			isStreaming: true,
			isComplete: false
		};

		this.snapshots.set(messageId, snapshot);
		this.emitEvent('message_created', messageId, chatId, snapshot);
		return snapshot;
	}

	/**
	 * Get current snapshot
	 */
	getSnapshot(messageId: string): MessageSnapshot | null {
		return this.snapshots.get(messageId) || null;
	}

	/**
	 * Update snapshot with new data
	 */
	protected updateSnapshot(messageId: string, updates: Partial<MessageSnapshot>): MessageSnapshot {
		const existing = this.snapshots.get(messageId);
		if (!existing) {
			throw new Error(`No snapshot found for message ${messageId}`);
		}

		const updated = { ...existing, ...updates };
		this.snapshots.set(messageId, updated);
		this.emitEvent('message_updated', messageId, updated.chatId, updated);
		return updated;
	}

	/**
	 * Finalize message - default implementation uses complete message
	 */
	finalizeMessage(messageId: string): MessageDto | null {
		const snapshot = this.snapshots.get(messageId);
		if (!snapshot) return null;

		// Mark as complete if not already
		if (!snapshot.isComplete) {
			this.updateSnapshot(messageId, { isComplete: true, isStreaming: false });
		}

		// Convert to DTO - subclasses should override if they need special logic
		return this.snapshotToDto(snapshot);
	}

	/**
	 * Convert snapshot to DTO - subclasses can override
	 */
	protected abstract snapshotToDto(snapshot: MessageSnapshot): MessageDto;

	/**
	 * Clear message state
	 */
	clearMessage(messageId: string): void {
		this.snapshots.delete(messageId);
	}

	/**
	 * Emit event to listener
	 */
	protected emitEvent(
		type: HandlerEvent['type'],
		messageId: string,
		chatId: string,
		data?: any
	): void {
		if (this.eventListener) {
			this.eventListener.onHandlerEvent({ type, messageId, chatId, data });
		}
	}
}
