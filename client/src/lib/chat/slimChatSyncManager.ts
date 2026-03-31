/**
 * Slim Chat Synchronization Manager
 *
 * A lightweight orchestrator that routes SSE events to appropriate message type handlers.
 * No longer handles message specifics - just coordinates and manages chat-level state.
 */

import { writable, get, type Writable } from 'svelte/store';
import type { ChatDto, MessageDto } from '$lib/types/chat';
import type { StreamingUIState, InitEvent, ErrorEvent } from './types';
import type {
	SSEEventEnvelopeUnion,
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope,
	ErrorEventEnvelope
} from './sseEventTypes';
import { SSEEventGuards, StreamChunkPayloadGuards } from './sseEventTypes';
import type { MessageHandlerRegistry, HandlerEvent, HandlerEventListener } from './messageHandlers';
import { taskManager } from '$lib/stores/taskManager';

/**
 * Slim chat sync manager - just routes events to handlers
 */
export class SlimChatSyncManager implements HandlerEventListener {
	private handlerRegistry: MessageHandlerRegistry;

	// Svelte stores for reactive state management
	private currentChatStore: Writable<ChatDto | null>;
	private chatsStore: Writable<ChatDto[]>;
	private streamingStateStore: Writable<StreamingUIState>;
	private currentChatIdStore?: Writable<string | null>;
	// Provisional sequence numbers assigned per streaming message
	private provisionalSeqByMessageId: Map<string, number> = new Map();
	// Final sequence numbers provided by server on completion
	private finalSeqByMessageId: Map<string, number> = new Map();

	// Current state tracking
	private currentChatId: string | null = null;
	private currentUserMessage: string = '';

	/** Map server event id + kind to a stable, UI-unique message id */
	private toDisplayId(kind: string, messageId: string): string {
		/*
		* Ignore defensive programming...
		// For tool-related events, map them all to the same aggregate message ID
		// This ensures tool_call_update and tool_result go to the same message
		if (kind === 'tools_call_update'
			|| kind === 'tool_result'
			|| kind === 'tools_aggregate') {
			return messageId;
		}
		// Ensure reasoning/text/tools do not collide if server reuses ids
		// Include all known message types to prevent ID collisions
		if (kind === 'text' || kind === 'reasoning' || 
		    kind === 'tool_call' || kind === 'usage') {
			return messageId;
		}
		*/
		return messageId;
	}

	constructor(
		handlerRegistry: MessageHandlerRegistry,
		currentChatStore: Writable<ChatDto | null>,
		chatsStore: Writable<ChatDto[]>,
		streamingStateStore: Writable<StreamingUIState>,
		currentChatIdStore?: Writable<string | null>
	) {
		this.handlerRegistry = handlerRegistry;
		this.currentChatStore = currentChatStore;
		this.chatsStore = chatsStore;
		this.streamingStateStore = streamingStateStore;
		this.currentChatIdStore = currentChatIdStore;
	}

	/**
	 * Process a strongly-typed SSE event
	 */
	processSSEEvent(envelope: SSEEventEnvelopeUnion): void {
		try {
			if (SSEEventGuards.isInitEvent(envelope)) {
				this.handleInitEvent(envelope);
			} else if (SSEEventGuards.isStreamChunkEvent(envelope)) {
				this.handleStreamChunkEvent(envelope);
			} else if (SSEEventGuards.isMessageCompleteEvent(envelope)) {
				this.handleMessageCompleteEvent(envelope);
			} else if (SSEEventGuards.isStreamCompleteEvent(envelope)) {
				this.handleStreamCompleteEvent(envelope);
			} else if (SSEEventGuards.isErrorEvent(envelope)) {
				this.handleErrorEvent(envelope);
			} else {
				console.warn('Unknown SSE event type:', envelope);
			}
		} catch (error) {
			console.error('Error processing SSE event:', error, envelope);
			this.handleError({
				chatId: envelope.chatId,
				messageId: 'messageId' in envelope ? envelope.messageId : undefined,
				message: error instanceof Error ? error.message : 'Unknown error',
				kind: 'error'
			});
		}
	}

	/**
	 * Handle initialization event - create user message and prepare for streaming
	 */
	private handleInitEvent(envelope: InitEventEnvelope): void {
		this.currentChatId = envelope.chatId;

		// Convert to legacy InitEvent format for compatibility
		const initEvent: InitEvent = {
			chatId: envelope.chatId,
			userMessageId: envelope.payload.userMessageId,
			userTimestamp: envelope.payload.userTimestamp,
			userSequenceNumber: envelope.payload.userSequenceNumber
		};

		this.initializeChat(initEvent, this.currentUserMessage);
	}

	/**
	 * Handle stream chunk event - route to appropriate message handler
	 */
	private handleStreamChunkEvent(envelope: StreamChunkEventEnvelope): void {
		console.log(
			`[SlimChatSyncManager] Handling stream chunk: kind=${envelope.kind}, messageId=${envelope.messageId}`
		);

		// Handle task updates separately - they're not messages
		if (envelope.kind === 'task_update') {
			this.handleTaskUpdateEvent(envelope);
			return;
		}

		// Special logging for tool calls
		if (envelope.kind === 'tools_call_update') {
			console.log('[SlimChatSyncManager] Tool call chunk received:', {
				messageId: envelope.messageId,
				payload: envelope.payload,
				payloadKeys: Object.keys(envelope.payload || {})
			});
		}

		// Route tool_call_update and tool_result to the aggregate handler
		let handlerType = envelope.kind;
		if (envelope.kind === 'tools_call_update' || envelope.kind === 'tool_result') {
			handlerType = 'tools_aggregate';
		}

		const handler = this.handlerRegistry.getHandler(handlerType);
		if (!handler) {
			console.warn(`No handler found for message type: ${handlerType}`);
			return;
		}

		try {
			// Ensure a stable, message-level sequence number exists on the envelope so
			// handlers use the same sequence across chunks and completion.
			const displayId = this.toDisplayId(envelope.kind, envelope.messageId);
			const seq = this.getOrAssignProvisionalSequence(envelope.chatId, displayId);
			const fixedEnvelope: StreamChunkEventEnvelope = {
				...envelope,
				messageId: displayId,
				sequenceId: seq
			} as StreamChunkEventEnvelope;
			const snapshot = handler.processChunk(displayId, fixedEnvelope);

			// Log tool call snapshots
			if (envelope.kind === 'tools_call_update') {
				console.log('[SlimChatSyncManager] Tool call snapshot after processing:', {
					displayId,
					toolCalls: snapshot.toolCalls,
					messageType: snapshot.messageType
				});
			}

			// Update streaming UI state based on the updated snapshot
			this.updateStreamingState(displayId, snapshot.messageType, snapshot);
		} catch (error) {
			console.error(`Error processing chunk for ${envelope.kind}:`, error);
		}
	}

	/**
	 * Handle task update event - update task store with new state
	 */
	private handleTaskUpdateEvent(envelope: StreamChunkEventEnvelope): void {
		console.log('[SlimChatSyncManager] Handling task update event:', {
			chatId: envelope.chatId,
			messageId: envelope.messageId,
			payload: envelope.payload
		});

		if (StreamChunkPayloadGuards.isTaskUpdateStreamChunk(envelope.payload)) {
			const { taskState, operationType } = envelope.payload;

			console.log('[SlimChatSyncManager] Updating task store with:', {
				chatId: envelope.chatId,
				operationType,
				taskState
			});

			// Update the task store with the new state from server
			taskManager.updateFromSSE(envelope.chatId, taskState);
		}
	}

	/**
	 * Handle message complete event - route to appropriate message handler
	 */
	private handleMessageCompleteEvent(envelope: MessageCompleteEventEnvelope): void {
		// Route tool-related completions to the aggregate handler
		let handlerType = envelope.kind;
		if (
			envelope.kind === 'tools_call' ||
			envelope.kind === 'tool_result' ||
			envelope.kind === 'tools_aggregate'
		) {
			handlerType = 'tools_aggregate';
		}

		const handler = this.handlerRegistry.getHandler(handlerType);
		if (!handler) {
			console.warn(`No handler found for message type: ${handlerType}`);
			return;
		}

		try {
			// Cache authoritative sequence number from server for this message
			const displayId = this.toDisplayId(envelope.kind, envelope.messageId);
			this.finalSeqByMessageId.set(displayId, envelope.sequenceId);
			// Pass through completion envelope so handlers receive the final server sequenceId
			const fixedEnv: MessageCompleteEventEnvelope = {
				...envelope,
				messageId: displayId
			} as MessageCompleteEventEnvelope;
			const dto = handler.completeMessage(displayId, fixedEnv);

			// Message completion is handled by the handler event listener
			// The handler will emit a 'message_completed' event that we'll handle
		} catch (error) {
			console.error(`Error completing message for ${envelope.kind}:`, error);
		}
	}

	/**
	 * Handle stream complete event - finalize all messages and clear state
	 */
	private handleStreamCompleteEvent(envelope: StreamCompleteEventEnvelope): void {
		console.log(`Stream completed for chat ${envelope.chatId}`);

		// Finalize any remaining messages that haven't been explicitly completed
		this.finalizeAllPendingMessages();
		// Clear sequence caches for next turn
		this.provisionalSeqByMessageId.clear();
		this.finalSeqByMessageId.clear();

		// Clear streaming state
		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		}));

		this.currentUserMessage = '';
	}

	/**
	 * Handle error event
	 */
	private handleErrorEvent(envelope: ErrorEventEnvelope): void {
		this.handleError({
			chatId: envelope.chatId,
			messageId: envelope.messageId,
			message: envelope.payload.message,
			kind: 'error'
		});
	}

	/**
	 * Handler event listener - receives events from message handlers
	 */
	onHandlerEvent(event: HandlerEvent): void {
		switch (event.type) {
			case 'message_created':
				this.onMessageCreated(event.messageId, event.chatId, event.data);
				break;
			case 'message_updated':
				this.onMessageUpdated(event.messageId, event.chatId, event.data);
				break;
			case 'message_completed':
				this.onMessageCompleted(event.messageId, event.chatId, event.data);
				break;
			case 'error':
				this.handleError({
					chatId: event.chatId,
					messageId: event.messageId,
					message: event.data?.message || 'Handler error',
					kind: 'error'
				});
				break;
		}
	}

	/**
	 * Handle message created by handler
	 */
	private onMessageCreated(messageId: string, chatId: string, snapshot: any): void {
		console.log(
			`Creating placeholder for message ${messageId} with sequence ${snapshot.sequenceNumber}`
		);

		// Map handler message types to proper DTO message types
		let dtoMessageType = snapshot.messageType;
		if (dtoMessageType === 'tools_call_update') {
			dtoMessageType = 'tool_call';
		}

		// Create placeholder message DTO and add to chat
		const messageDto: MessageDto = {
			id: messageId,
			chatId: chatId,
			role: 'assistant',
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: dtoMessageType
		} as MessageDto;

		// Include type-specific data from snapshot
		if (dtoMessageType === 'tool_call' && snapshot.toolCalls) {
			(messageDto as any).toolCalls = snapshot.toolCalls;
		} else if (dtoMessageType === 'tools_aggregate' && snapshot.toolCallPairs) {
			// Use paired structure for aggregate messages
			(messageDto as any).toolCallPairs = snapshot.toolCallPairs || [];
		} else if (dtoMessageType === 'text' && snapshot.textDelta) {
			(messageDto as any).text = snapshot.textDelta;
		} else if (dtoMessageType === 'reasoning' && snapshot.reasoningDelta) {
			(messageDto as any).reasoning = snapshot.reasoningDelta;
		}

		this.addMessageToChat(messageDto);
	}

	/**
	 * Handle message updated by handler (streaming)
	 */
	private onMessageUpdated(messageId: string, chatId: string, snapshot: any): void {
		// For tool call messages, update the placeholder with current tool calls
		if (snapshot.messageType === 'tool_call' && snapshot.toolCalls) {
			const currentChat = get(this.currentChatStore);
			const existingMessage = currentChat?.messages.find((m) => m.id === messageId);

			if (existingMessage && existingMessage.messageType === 'tool_call') {
				// Update the existing message with current tool calls
				const updatedDto = {
					...existingMessage,
					toolCalls: snapshot.toolCalls
				};
				this.updateMessageInChat(messageId, updatedDto);
			}
		}
		// For tools aggregate messages, update with paired structure
		else if (snapshot.messageType === 'tools_aggregate' && snapshot.toolCallPairs) {
			const currentChat = get(this.currentChatStore);
			const existingMessage = currentChat?.messages.find((m) => m.id === messageId);

			if (existingMessage && existingMessage.messageType === 'tools_aggregate') {
				// Update the existing message with paired structure
				const updatedDto = {
					...existingMessage,
					toolCallPairs: snapshot.toolCallPairs || []
				};
				this.updateMessageInChat(messageId, updatedDto);
			}
		}
		// Other message types handled by streaming state
	}

	/**
	 * Handle message completed by handler
	 */
	private onMessageCompleted(messageId: string, chatId: string, dto: MessageDto): void {
		console.log(
			`[SlimChatSyncManager] Completing message ${messageId} with sequence ${dto.sequenceNumber}, messageType: ${dto.messageType}`
		);

		// Log tool call specific data
		if (dto.messageType === 'tool_call') {
			console.log('[SlimChatSyncManager] Tool call message completion:', {
				messageId: dto.id,
				toolCalls: (dto as any).toolCalls,
				toolCallsCount: (dto as any).toolCalls?.length || 0
			});
		}

		// Preserve original messageType before any modifications
		const originalMessageType = dto.messageType;

		// Overwrite with final server-provided sequence number if available
		const finalSeq = this.finalSeqByMessageId.get(messageId);
		if (typeof finalSeq === 'number') {
			console.log(
				`Overriding sequence ${dto.sequenceNumber} -> ${finalSeq} for message ${messageId}`
			);
			(dto as any).sequenceNumber = finalSeq;
			// Ensure messageType is preserved after sequence override
			(dto as any).messageType = originalMessageType;
			this.finalSeqByMessageId.delete(messageId);
		}

		// Double-check that messageType is still set
		if (!dto.messageType) {
			console.warn(
				`MessageType missing for ${messageId}, attempting to restore from completion envelope`
			);
			// Try to determine messageType from the completion envelope kind
			// This is a fallback that shouldn't normally be needed
			(dto as any).messageType = originalMessageType || 'text';
		}

		console.log(`[SlimChatSyncManager] Final DTO for message ${messageId}:`, {
			id: dto.id,
			messageType: dto.messageType,
			role: dto.role,
			sequenceNumber: dto.sequenceNumber,
			hasToolCalls: this.hasToolCalls(dto)
		});

		// Check if this message already exists in chat (from placeholder creation)
		const currentChat = get(this.currentChatStore);
		const existingMessage = currentChat?.messages.find((m) => m.id === messageId);

		if (existingMessage) {
			console.log(`[SlimChatSyncManager] Replacing existing placeholder for message ${messageId}`, {
				oldMessageType: existingMessage.messageType,
				newMessageType: dto.messageType
			});
			// Replace the placeholder with the final DTO
			this.updateMessageInChat(messageId, dto);
		} else {
			console.log(
				`[SlimChatSyncManager] Adding late completion for message ${messageId} (no placeholder found)`
			);
			// Add the completed message to chat - this handles late completions
			// where the message completed after a new message started streaming
			this.addMessageToChat(dto);
		}

		// Mark snapshot as complete (retain entry for UI phase-based reactions)
		this.streamingStateStore.update((state) => {
			const next: any = { ...state };
			if (state.streamingSnapshots && state.streamingSnapshots[messageId]) {
				next.streamingSnapshots = { ...state.streamingSnapshots };
				next.streamingSnapshots[messageId] = {
					...next.streamingSnapshots[messageId],
					isStreaming: false,
					phase: 'complete'
				};
			}
			return next;
		});
	}

	// ... (keep existing initialization and state management methods)

	/**
	 * Set current user message for streaming context
	 */
	setCurrentUserMessage(userMessage: string): void {
		this.currentUserMessage = userMessage;
	}

	/**
	 * Initialize a new chat or continue existing chat
	 */
	initializeChat(event: InitEvent, userMessage: string): void {
		this.currentChatId = event.chatId;

		// Create user message DTO for immediate UI feedback
		const userMessageDto: MessageDto = {
			id: event.userMessageId,
			chatId: event.chatId,
			role: 'user',
			timestamp: new Date(event.userTimestamp),
			sequenceNumber: event.userSequenceNumber,
			messageType: 'text',
			text: userMessage
		} as MessageDto;

		// If chat doesn't exist, create it. Otherwise, append/update message.
		const existing = get(this.currentChatStore);
		if (!existing || existing.id !== event.chatId) {
			const newChat: ChatDto = {
				id: event.chatId,
				userId: 'unknown',
				title: userMessage.substring(0, 50) + (userMessage.length > 50 ? '...' : ''),
				messages: [userMessageDto],
				createdAt: new Date(event.userTimestamp),
				updatedAt: new Date(event.userTimestamp)
			};
			this.currentChatStore.set(newChat);
			this.chatsStore.update((list) => [newChat, ...list.filter((c) => c.id !== newChat.id)]);

			// Initialize task manager for the new chat
			import('../stores/taskManager').then(({ taskManager }) => {
				taskManager.initializeTasks(event.chatId, []);
			});

			// Ensure the new chat is selected - update currentChatId store as well
			this.updateCurrentChatSelection(event.chatId);
		} else {
			// For existing chats, check if this user message already exists
			const already = existing.messages.find((m) => m.id === event.userMessageId);
			if (!already) {
				// Add new user message while preserving all existing messages
				const updatedMessages = [...existing.messages, userMessageDto].sort(
					(a, b) => a.sequenceNumber - b.sequenceNumber
				);
				this.currentChatStore.update((chat) =>
					chat
						? {
								...chat,
								messages: updatedMessages,
								updatedAt: new Date()
							}
						: chat
				);
				this.syncCurrentChatToChatsStore();
			}
		}

		// Set streaming state true at start; DO NOT clear existing snapshots here.
		// Keeping snapshots allows previously-streamed-but-not-completed messages
		// (e.g., prior turn's reasoning) to remain visible while new text streams.
		this.streamingStateStore.update((s) => ({
			...s,
			isStreaming: true,
			currentMessageId: null,
			error: null
		}));
	}

	/**
	 * Handle errors
	 */
	handleError(error: ErrorEvent): void {
		console.error('SlimChatSyncManager error:', error);
		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			error: error.message
		}));
	}

	// Helper method to check if a message has tool calls
	private hasToolCalls(dto: MessageDto): boolean {
		if (dto.messageType === 'tools_aggregate') {
			return !!((dto as any).toolCallPairs && (dto as any).toolCallPairs.length > 0);
		}
		return !!(dto as any).toolCalls;
	}

	// Helper methods for chat state management
	private addMessageToChat(messageDto: MessageDto): void {
		console.log('[SlimChatSyncManager] addMessageToChat called:', {
			messageId: messageDto.id,
			messageType: messageDto.messageType,
			hasToolCalls: this.hasToolCalls(messageDto),
			toolCallsCount:
				(messageDto as any).toolCalls?.length || (messageDto as any).toolCallPairs?.length || 0
		});

		this.currentChatStore.update((chat) => {
			if (!chat) return null;

			// Check if message already exists to avoid duplicates
			const exists = chat.messages.some((m) => m.id === messageDto.id);
			if (exists) {
				console.log('[SlimChatSyncManager] Message already exists, skipping:', messageDto.id);
				return chat;
			}

			const updatedChat = {
				...chat,
				messages: [...chat.messages, messageDto].sort(
					(a, b) => a.sequenceNumber - b.sequenceNumber
				),
				updatedAt: new Date()
			};

			console.log('[SlimChatSyncManager] Added message to chat:', {
				messageId: messageDto.id,
				totalMessages: updatedChat.messages.length,
				messageTypes: updatedChat.messages.map((m) => m.messageType)
			});

			return updatedChat;
		});
		this.syncCurrentChatToChatsStore();
	}

	private updateMessageInChat(messageId: string, dto: MessageDto): void {
		console.log('[SlimChatSyncManager] updateMessageInChat called:', {
			messageId,
			newMessageType: dto.messageType,
			hasToolCalls: this.hasToolCalls(dto),
			toolCallsCount: (dto as any).toolCalls?.length || (dto as any).toolCallPairs?.length || 0
		});

		this.currentChatStore.update((chat) => {
			if (!chat) return null;

			const updatedChat = {
				...chat,
				messages: chat.messages.map((m) => (m.id === messageId ? dto : m)),
				updatedAt: new Date()
			};

			const updatedMessage = updatedChat.messages.find((m) => m.id === messageId);
			console.log('[SlimChatSyncManager] Updated message in chat:', {
				messageId,
				updatedMessageType: updatedMessage?.messageType,
				hasToolCalls: updatedMessage ? this.hasToolCalls(updatedMessage) : false,
				toolCallsCount:
					(updatedMessage as any)?.toolCalls?.length ||
					(updatedMessage as any)?.toolCallPairs?.length ||
					0
			});

			return updatedChat;
		});
		this.syncCurrentChatToChatsStore();
	}

	/** Sync currentChat to chats store list */
	private syncCurrentChatToChatsStore(): void {
		const current = get(this.currentChatStore);
		if (!current) return;

		this.chatsStore.update((chats) => {
			const index = chats.findIndex((c) => c.id === current.id);
			if (index >= 0) {
				// Update existing chat in the list
				const updatedChats = [...chats];
				updatedChats[index] = current;
				return updatedChats;
			} else {
				// Add new chat to the beginning of the list
				return [current, ...chats];
			}
		});
	}

	private updateStreamingState(messageId: string, messageType: string, snapshot: any): void {
		console.log('[SlimChatSyncManager] updateStreamingState called:', {
			messageId,
			messageType,
			snapshotTextDelta: snapshot.textDelta,
			snapshotReasoningDelta: snapshot.reasoningDelta,
			snapshotToolCalls: snapshot.toolCalls,
			snapshotIsStreaming: snapshot.isStreaming
		});

		this.streamingStateStore.update((state) => {
			// Always reflect the actively streaming message id so UI ties
			// streaming state to the correct renderer (reasoning -> text, etc.)
			const next: any = { ...state, currentMessageId: messageId, isStreaming: true };
			const prev = state.streamingSnapshots || {};
			// Mark all existing snapshots as not streaming to avoid cross-talk
			const cleared: any = {};
			for (const key in prev) {
				cleared[key] = { ...prev[key], isStreaming: false };
			}
			const existing = cleared[messageId] || { messageType, isStreaming: true, phase: 'initial' };
			const updated: any = { ...existing, messageType, isStreaming: true, phase: 'streaming' };
			if (messageType === 'text') {
				updated.textDelta = snapshot.textDelta || '';
			} else if (messageType === 'reasoning') {
				updated.reasoningDelta = snapshot.reasoningDelta || '';
				updated.visibility = snapshot.visibility ?? existing.visibility ?? null;
			} else if (messageType === 'tool_call') {
				updated.toolCalls = snapshot.toolCalls || [];
				console.log('[SlimChatSyncManager] Storing tool calls in streaming state:', {
					messageId,
					toolCalls: updated.toolCalls
				});
			} else if (messageType === 'tools_aggregate') {
				updated.toolCallPairs = snapshot.toolCallPairs || [];
				console.log('[SlimChatSyncManager] Storing tool call pairs in streaming state:', {
					messageId,
					toolCallPairs: updated.toolCallPairs
				});
			}
			next.streamingSnapshots = { ...cleared, [messageId]: updated };
			return next;
		});
	}

	private finalizeAllPendingMessages(): void {
		const chat = get(this.currentChatStore);
		if (!chat) return;
		const handlers = this.handlerRegistry.getAllHandlers();
		for (const msg of chat.messages) {
			if (msg.role !== 'assistant') continue;
			const handler = handlers.find((h) => h.canHandle(msg.messageType ?? 'text'));
			if (!handler) continue;
			const dto = handler.finalizeMessage(msg.id);
			if (dto) {
				this.updateMessageInChat(msg.id, dto);
			}
		}
	}

	/** Assign or get a stable provisional sequence number for a streaming message */
	private getOrAssignProvisionalSequence(chatId: string, messageId: string): number {
		const existing = this.provisionalSeqByMessageId.get(messageId);
		if (existing !== undefined) return existing;

		const current = get(this.currentChatStore);
		if (!current || current.id !== chatId) {
			// No current chat or different chat - start from 0
			const seq = 0;
			this.provisionalSeqByMessageId.set(messageId, seq);
			return seq;
		}

		// Calculate next sequence based on current messages + already assigned provisional sequences
		const baseSeq = current.messages?.length ?? 0;
		const assignedProvisionalSeqs = Array.from(this.provisionalSeqByMessageId.values());
		const maxProvisional =
			assignedProvisionalSeqs.length > 0 ? Math.max(...assignedProvisionalSeqs) : baseSeq - 1;
		const seq = Math.max(baseSeq, maxProvisional + 1);

		this.provisionalSeqByMessageId.set(messageId, seq);
		return seq;
	}

	/**
	 * Update the current chat selection in the store
	 */
	private updateCurrentChatSelection(chatId: string): void {
		if (this.currentChatIdStore) {
			this.currentChatIdStore.set(chatId);
		}
	}
}

/**
 * Factory function for creating slim chat sync manager
 */
export function createSlimChatSyncManager(
	handlerRegistry: MessageHandlerRegistry,
	currentChatStore: Writable<ChatDto | null>,
	chatsStore: Writable<ChatDto[]>,
	streamingStateStore: Writable<StreamingUIState>,
	currentChatIdStore?: Writable<string | null>
): SlimChatSyncManager {
	return new SlimChatSyncManager(
		handlerRegistry,
		currentChatStore,
		chatsStore,
		streamingStateStore,
		currentChatIdStore
	);
}
