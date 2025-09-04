/**
 * SignalR Chat Orchestrator
 *
 * Orchestrates SignalR-based real-time chat communication,
 * integrating with the existing message handler architecture.
 */

import { writable, type Writable } from 'svelte/store';
import type { ChatDto, CreateChatRequest } from '$lib/types/chat';
import type { StreamingUIState } from './types';
import type { MessageHandlerRegistry } from './messageHandlers';
import type { StreamChunkEventEnvelope, MessageCompleteEventEnvelope } from './sseEventTypes';

import { createMessageHandlerRegistry } from './messageHandlerRegistry';
import { createTextMessageHandler } from './handlers/textMessageHandler';
import { createReasoningMessageHandler } from './handlers/reasoningMessageHandler';
import { createToolCallMessageHandler } from './handlers/toolCallMessageHandler';
import { createToolsAggregateMessageHandler } from './handlers/toolsAggregateMessageHandler';
import { createSlimChatSyncManager } from './slimChatSyncManager';
import { signalRService, type SignalRMessage, type StreamChunk } from '$lib/services/signalr';
import { apiClient } from '$lib/api/client';
import { logger } from '$lib/utils/logger';

/**
 * SignalR-based chat orchestrator using handler-based architecture
 */
export class SignalROrchestrator {
	private handlerRegistry: MessageHandlerRegistry;
	private chatSyncManager: ReturnType<typeof createSlimChatSyncManager>;

	// Svelte stores for reactive state
	private currentChatStore: Writable<ChatDto | null>;
	private chatsStore: Writable<ChatDto[]>;
	private streamingStateStore: Writable<StreamingUIState>;
	private getUserId: () => string;

	// SignalR subscriptions
	private signalRSubscriptions: (() => void)[] = [];

	// Current stream state
	private currentStreamChatId: string | null = null;
	private currentActiveChatId: string | null = null;
	private chatIdUnsubscribe: (() => void) | null = null;

	// Message assembly for streaming
	private streamingMessages = new Map<
		string,
		{
			content: string;
			messageType: string;
			sequenceNumber: number;
		}
	>();

	constructor(
		currentChatStore: Writable<ChatDto | null>,
		chatsStore: Writable<ChatDto[]>,
		streamingStateStore: Writable<StreamingUIState>,
		getUserId: () => string,
		currentChatIdStore?: Writable<string | null>
	) {
		this.currentChatStore = currentChatStore;
		this.chatsStore = chatsStore;
		this.streamingStateStore = streamingStateStore;
		this.getUserId = getUserId;

		// Initialize handler registry and register handlers
		this.handlerRegistry = createMessageHandlerRegistry();

		// Create chat sync manager with handler registry
		this.chatSyncManager = createSlimChatSyncManager(
			this.handlerRegistry,
			currentChatStore,
			chatsStore,
			streamingStateStore,
			currentChatIdStore
		);

		// Register handlers with the sync manager as listener
		this.handlerRegistry.register(createTextMessageHandler(this.chatSyncManager));
		this.handlerRegistry.register(createReasoningMessageHandler(this.chatSyncManager));
		this.handlerRegistry.register(createToolCallMessageHandler(this.chatSyncManager));
		this.handlerRegistry.register(createToolsAggregateMessageHandler(this.chatSyncManager));

		// Subscribe to currentChatIdStore to track the active chat
		if (currentChatIdStore) {
			this.chatIdUnsubscribe = currentChatIdStore.subscribe((chatId) => {
				this.currentActiveChatId = chatId;
				logger.debug(
					{ currentActiveChatId: this.currentActiveChatId },
					'Active chat ID updated from store'
				);
			});
		}

		// Set up SignalR event handlers
		this.setupSignalRHandlers();
	}

	/**
	 * Set up SignalR event handlers
	 */
	private setupSignalRHandlers(): void {
		// Handle complete messages
		const unsubMessage = signalRService.subscribe('message', (message: SignalRMessage) => {
			this.handleSignalRMessage(message);
		});
		this.signalRSubscriptions.push(unsubMessage);

		// Handle stream chunks
		const unsubChunk = signalRService.subscribe('streamChunk', (chunk: StreamChunk) => {
			this.handleStreamChunk(chunk);
		});
		this.signalRSubscriptions.push(unsubChunk);

		// Handle errors
		const unsubError = signalRService.subscribe('error', (error: any) => {
			this.handleError(error);
		});
		this.signalRSubscriptions.push(unsubError);

		// Handle operation status
		const unsubStatus = signalRService.subscribe('operationStatus', (status: any) => {
			this.handleOperationStatus(status);
		});
		this.signalRSubscriptions.push(unsubStatus);
	}

	/**
	 * Handle complete SignalR message
	 */
	private handleSignalRMessage(message: SignalRMessage): void {
		logger.debug({ message }, 'Processing SignalR message');

		// Only process messages for the current chat
		if (this.currentActiveChatId && message.chatId !== this.currentActiveChatId) {
			logger.debug(
				{ messageChat: message.chatId, activeChat: this.currentActiveChatId },
				'Ignoring message for different chat'
			);
			return;
		}

		// Convert to handler-compatible format
		const messageData = {
			id: message.messageId,
			chatId: message.chatId,
			...message.content,
			timestamp: new Date(message.timestamp),
			sequenceNumber: message.sequenceNumber
		};

		// Process through handler registry
		const messageType = message.content.messageType || 'text';
		const handler = this.handlerRegistry.getHandler(messageType);

		if (handler) {
			// For SignalR complete messages, create a properly structured envelope
			const envelope: MessageCompleteEventEnvelope = {
				chatId: message.chatId,
				messageId: message.messageId,
				sequenceId: message.sequenceNumber,
				version: 1,
				ts: message.timestamp,
				kind: 'message_complete',
				payload: {
					messageType: messageType as any,
					content:
						typeof message.content === 'string' ? message.content : message.content.text || '',
					...message.content
				}
			};

			handler.completeMessage(message.messageId, envelope);
		} else {
			logger.warn({ messageType }, 'No handler found for message type');
		}
	}

	/**
	 * Handle stream chunk from SignalR
	 */
	private handleStreamChunk(chunk: StreamChunk): void {
		logger.debug({ chunk }, 'Processing stream chunk');

		// Only process chunks for the current chat
		if (this.currentActiveChatId && chunk.chatId !== this.currentActiveChatId) {
			return;
		}

		// Update streaming state
		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: !chunk.done,
			currentMessageId: chunk.messageId
		}));

		// Accumulate content
		let messageData = this.streamingMessages.get(chunk.messageId);
		if (!messageData) {
			messageData = {
				content: '',
				messageType: chunk.messageType,
				sequenceNumber: 0
			};
			this.streamingMessages.set(chunk.messageId, messageData);
		}

		messageData.content += chunk.delta;

		// Process through appropriate handler
		const handler = this.handlerRegistry.getHandler(chunk.messageType);

		if (handler) {
			// Initialize message if this is the first chunk
			if (!handler.getSnapshot(chunk.messageId)) {
				handler.initializeMessage(
					chunk.messageId,
					chunk.chatId,
					new Date(),
					messageData.sequenceNumber
				);
			}

			// Process the chunk with proper envelope structure
			const envelope: StreamChunkEventEnvelope = {
				chatId: chunk.chatId,
				messageId: chunk.messageId,
				sequenceId: messageData.sequenceNumber,
				version: 1,
				ts: new Date().toISOString(),
				kind: 'stream_chunk',
				payload: {
					messageType: chunk.messageType as any,
					delta: chunk.delta
				}
			};

			handler.processChunk(chunk.messageId, envelope);
		}

		// If done, clean up and finalize
		if (chunk.done) {
			this.finalizeStreamedMessage(chunk.messageId, chunk.chatId);
		}
	}

	/**
	 * Finalize a streamed message
	 */
	private finalizeStreamedMessage(messageId: string, chatId: string): void {
		const messageData = this.streamingMessages.get(messageId);

		if (!messageData) {
			return;
		}

		// Clean up streaming state
		this.streamingMessages.delete(messageId);

		// Update streaming state
		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			currentMessageId: null
		}));

		logger.debug({ messageId, chatId }, 'Finalized streamed message');
	}

	/**
	 * Handle errors from SignalR
	 */
	private handleError(error: any): void {
		logger.error({ error }, 'SignalR error received');

		// Update streaming state with error
		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			error: error.message || 'Unknown error occurred'
		}));
	}

	/**
	 * Handle operation status from SignalR
	 */
	private handleOperationStatus(status: any): void {
		logger.debug({ status }, 'Operation status received');

		// Handle based on status
		if (status.status === 'failure') {
			this.streamingStateStore.update((state) => ({
				...state,
				error: status.message || 'Operation failed'
			}));
		}
	}

	/**
	 * Start a new chat stream via SignalR
	 */
	async streamNewChat(message: string, systemPrompt?: string, modeId?: string): Promise<void> {
		const userId = this.getUserId();

		try {
			logger.info({ userId, message }, 'Starting new chat via SignalR');

			// Create chat first via REST API
			const newChat = await apiClient.createChat({
				userId,
				message,
				systemPrompt,
				modeId
			});

			// Update stores
			this.currentChatStore.set(newChat);
			this.chatsStore.update((chats) => [newChat, ...chats]);

			this.currentStreamChatId = newChat.id;

			// Update streaming state
			this.streamingStateStore.update((state) => ({
				...state,
				isStreaming: true,
				currentMessageId: null,
				error: null
			}));

			// Start streaming via SignalR
			await signalRService.sendMessage('StreamNewChat', {
				chatId: newChat.id,
				userId,
				message,
				systemPrompt,
				modeId
			});
		} catch (error) {
			logger.error({ error }, 'Failed to start new chat stream');

			this.streamingStateStore.update((state) => ({
				...state,
				isStreaming: false,
				error: error instanceof Error ? error.message : 'Failed to start chat'
			}));

			throw error;
		}
	}

	/**
	 * Stream a reply to an existing chat via SignalR
	 */
	async streamReply(message: string, chatId: string): Promise<void> {
		const userId = this.getUserId();

		try {
			logger.info({ userId, chatId, message }, 'Streaming reply via SignalR');

			this.currentStreamChatId = chatId;

			// Update streaming state
			this.streamingStateStore.update((state) => ({
				...state,
				isStreaming: true,
				currentMessageId: null,
				error: null
			}));

			// Send reply via SignalR
			await signalRService.sendMessage('StreamReply', {
				chatId,
				userId,
				message
			});
		} catch (error) {
			logger.error({ error }, 'Failed to stream reply');

			this.streamingStateStore.update((state) => ({
				...state,
				isStreaming: false,
				error: error instanceof Error ? error.message : 'Failed to send reply'
			}));

			throw error;
		}
	}

	/**
	 * Send a non-streaming message via SignalR
	 */
	async sendMessage(chatId: string, message: string): Promise<void> {
		const userId = this.getUserId();

		try {
			await signalRService.sendMessage('SendMessage', {
				chatId,
				userId,
				message
			});

			logger.debug({ chatId, message }, 'Message sent via SignalR');
		} catch (error) {
			logger.error({ error }, 'Failed to send message');
			throw error;
		}
	}

	/**
	 * Clean up resources
	 */
	async cleanup(): Promise<void> {
		// Unsubscribe from SignalR events
		this.signalRSubscriptions.forEach((unsub) => unsub());
		this.signalRSubscriptions = [];

		// Clean up chat ID subscription
		if (this.chatIdUnsubscribe) {
			this.chatIdUnsubscribe();
			this.chatIdUnsubscribe = null;
		}

		// Clear streaming messages
		this.streamingMessages.clear();

		// Reset state
		this.currentStreamChatId = null;
		this.currentActiveChatId = null;

		logger.info('SignalR orchestrator cleaned up');
	}
}
