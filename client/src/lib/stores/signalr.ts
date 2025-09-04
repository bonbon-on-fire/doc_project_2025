/**
 * SignalR Store for managing SignalR connection state and messages
 *
 * This store provides reactive state management for the SignalR service,
 * exposing connection state, messages, and actions to Svelte components.
 */

import { writable, derived, get } from 'svelte/store';
import {
	signalRService,
	type ConnectionState,
	type SignalRMessage,
	type StreamChunk
} from '$lib/services/signalr';
import { logger } from '$lib/utils/logger';
import type { ChatDto } from '$lib/types/chat';

// SignalR State Interface
export interface SignalRState {
	connectionState: ConnectionState;
	isConnected: boolean;
	reconnectAttempts: number;
	lastError: string | null;
	messageQueue: SignalRMessage[];
	streamingChunks: Map<string, StreamChunk[]>;
}

// Initialize SignalR state
const initialState: SignalRState = {
	connectionState: 'disconnected',
	isConnected: false,
	reconnectAttempts: 0,
	lastError: null,
	messageQueue: [],
	streamingChunks: new Map()
};

// Main SignalR state store
export const signalRState = writable<SignalRState>(initialState);

// Derived stores for convenient access
export const connectionState = derived(signalRState, ($state) => $state.connectionState);

export const isConnected = derived(signalRState, ($state) => $state.isConnected);

export const lastError = derived(signalRState, ($state) => $state.lastError);

export const messageQueue = derived(signalRState, ($state) => $state.messageQueue);

// Connection status message for UI
export const connectionStatusMessage = derived(signalRState, ($state) => {
	switch ($state.connectionState) {
		case 'connected':
			return 'Connected to server';
		case 'connecting':
			return 'Connecting...';
		case 'reconnecting':
			return `Reconnecting... (Attempt ${$state.reconnectAttempts})`;
		case 'disconnected':
			return 'Disconnected';
		case 'disconnecting':
			return 'Disconnecting...';
		case 'error':
			return $state.lastError || 'Connection error';
		default:
			return 'Unknown state';
	}
});

// Connection status color for UI
export const connectionStatusColor = derived(signalRState, ($state) => {
	switch ($state.connectionState) {
		case 'connected':
			return 'green';
		case 'connecting':
		case 'reconnecting':
			return 'yellow';
		case 'disconnected':
		case 'disconnecting':
			return 'gray';
		case 'error':
			return 'red';
		default:
			return 'gray';
	}
});

// Store for tracking active subscriptions
const activeSubscriptions: Set<() => void> = new Set();

/**
 * SignalR Actions
 */
export const signalRActions = {
	/**
	 * Initialize SignalR connection
	 */
	async connect(userId: string): Promise<void> {
		try {
			logger.info({ userId }, 'Initializing SignalR connection');

			// Subscribe to connection state changes
			const unsubscribeState = signalRService.onStateChange((state) => {
				signalRState.update((current) => ({
					...current,
					connectionState: state,
					isConnected: state === 'connected',
					reconnectAttempts: state === 'reconnecting' ? current.reconnectAttempts + 1 : 0
				}));
			});

			activeSubscriptions.add(unsubscribeState);

			// Subscribe to messages
			const unsubscribeMessage = signalRService.subscribe('message', (message: SignalRMessage) => {
				logger.debug({ message }, 'Received SignalR message');

				signalRState.update((current) => ({
					...current,
					messageQueue: [...current.messageQueue, message]
				}));

				// Process the message (integrate with chat store)
				processMessage(message);
			});

			activeSubscriptions.add(unsubscribeMessage);

			// Subscribe to stream chunks
			const unsubscribeChunk = signalRService.subscribe('streamChunk', (chunk: StreamChunk) => {
				logger.debug({ chunk }, 'Received stream chunk');

				signalRState.update((current) => {
					const chunks = current.streamingChunks.get(chunk.messageId) || [];
					chunks.push(chunk);

					const newChunks = new Map(current.streamingChunks);
					newChunks.set(chunk.messageId, chunks);

					return {
						...current,
						streamingChunks: newChunks
					};
				});

				// Process the chunk
				processStreamChunk(chunk);
			});

			activeSubscriptions.add(unsubscribeChunk);

			// Subscribe to errors
			const unsubscribeError = signalRService.subscribe('error', (error: any) => {
				logger.error({ error }, 'SignalR error received');

				signalRState.update((current) => ({
					...current,
					lastError: error.message || 'Unknown error'
				}));
			});

			activeSubscriptions.add(unsubscribeError);

			// Connect to SignalR
			await signalRService.connect(userId);

			logger.info('SignalR connected successfully');
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to connect to SignalR';
			logger.error({ error }, 'Failed to connect to SignalR');

			signalRState.update((current) => ({
				...current,
				lastError: errorMessage
			}));

			throw error;
		}
	},

	/**
	 * Send a message via SignalR
	 */
	async sendMessage(method: string, ...args: any[]): Promise<void> {
		try {
			await signalRService.sendMessage(method, ...args);
			logger.debug({ method, args }, 'Message sent via SignalR');
		} catch (error) {
			logger.error({ error, method, args }, 'Failed to send message via SignalR');
			throw error;
		}
	},

	/**
	 * Send a chat message
	 */
	async sendChatMessage(chatId: string, message: string): Promise<void> {
		try {
			await signalRService.sendMessage('SendMessage', {
				chatId,
				content: message,
				timestamp: new Date().toISOString()
			});

			logger.debug({ chatId, message }, 'Chat message sent via SignalR');
		} catch (error) {
			logger.error({ error, chatId }, 'Failed to send chat message');
			throw error;
		}
	},

	/**
	 * Start streaming a response
	 */
	async startStream(chatId: string, prompt: string, systemPrompt?: string): Promise<void> {
		try {
			await signalRService.sendMessage('StartStream', {
				chatId,
				prompt,
				systemPrompt,
				timestamp: new Date().toISOString()
			});

			logger.debug({ chatId, prompt }, 'Stream started via SignalR');
		} catch (error) {
			logger.error({ error, chatId }, 'Failed to start stream');
			throw error;
		}
	},

	/**
	 * Clear message queue
	 */
	clearMessageQueue(): void {
		signalRState.update((current) => ({
			...current,
			messageQueue: []
		}));
	},

	/**
	 * Clear streaming chunks for a message
	 */
	clearStreamingChunks(messageId: string): void {
		signalRState.update((current) => {
			const newChunks = new Map(current.streamingChunks);
			newChunks.delete(messageId);

			return {
				...current,
				streamingChunks: newChunks
			};
		});
	},

	/**
	 * Clear error
	 */
	clearError(): void {
		signalRState.update((current) => ({
			...current,
			lastError: null
		}));
	},

	/**
	 * Disconnect from SignalR
	 */
	async disconnect(): Promise<void> {
		try {
			// Unsubscribe all handlers
			activeSubscriptions.forEach((unsubscribe) => unsubscribe());
			activeSubscriptions.clear();

			// Disconnect from SignalR
			await signalRService.disconnect();

			// Reset state
			signalRState.set(initialState);

			logger.info('SignalR disconnected');
		} catch (error) {
			logger.error({ error }, 'Error disconnecting from SignalR');
			throw error;
		}
	},

	/**
	 * Reconnect to SignalR
	 */
	async reconnect(userId: string): Promise<void> {
		await this.disconnect();
		await this.connect(userId);
	}
};

/**
 * Process incoming SignalR message
 * This will be integrated with the chat store
 */
function processMessage(message: SignalRMessage): void {
	// Import chat store dynamically to avoid circular dependencies
	import('./chat')
		.then(({ currentChat, chats }) => {
			// Update the chat with the new message
			currentChat.update((chat) => {
				if (chat && chat.id === message.chatId) {
					// Add message to current chat
					const updatedChat = {
						...chat,
						messages: [
							...chat.messages,
							{
								id: message.messageId,
								chatId: message.chatId,
								content: message.content,
								timestamp: new Date(message.timestamp),
								sequenceNumber: message.sequenceNumber,
								role: 'assistant' as const,
								messageType: 'text'
							}
						]
					};

					return updatedChat;
				}
				return chat;
			});

			// Also update in the chats list
			chats.update((chatList) => {
				return chatList.map((chat) => {
					if (chat.id === message.chatId) {
						return {
							...chat,
							messages: [
								...chat.messages,
								{
									id: message.messageId,
									chatId: message.chatId,
									content: message.content,
									timestamp: new Date(message.timestamp),
									sequenceNumber: message.sequenceNumber,
									role: 'assistant' as const,
									messageType: 'text'
								}
							]
						};
					}
					return chat;
				});
			});
		})
		.catch((error) => {
			logger.error({ error }, 'Failed to process message');
		});
}

/**
 * Process incoming stream chunk
 * This will be integrated with the streaming state
 */
function processStreamChunk(chunk: StreamChunk): void {
	// Import streaming state dynamically
	import('./chat')
		.then(({ streamingState }) => {
			streamingState.update((state) => {
				// Get or create streaming snapshot for this message
				const currentSnapshot = state.streamingSnapshots[chunk.messageId] || {
					messageType: chunk.messageType,
					isStreaming: true,
					phase: 'streaming' as const,
					textDelta: ''
				};

				// Update the text delta with new content
				const updatedSnapshot = {
					...currentSnapshot,
					textDelta: (currentSnapshot.textDelta || '') + chunk.delta,
					isStreaming: !chunk.done,
					phase: chunk.done ? ('complete' as const) : ('streaming' as const)
				};

				return {
					...state,
					isStreaming: !chunk.done,
					currentMessageId: chunk.messageId,
					streamingSnapshots: {
						...state.streamingSnapshots,
						[chunk.messageId]: updatedSnapshot
					}
				};
			});

			// If chunk is done, clear from SignalR chunks
			if (chunk.done) {
				signalRActions.clearStreamingChunks(chunk.messageId);
			}
		})
		.catch((error) => {
			logger.error({ error }, 'Failed to process stream chunk');
		});
}

// Auto-connect on module load if user is available
if (typeof window !== 'undefined') {
	// Check for user ID in URL or storage
	const params = new URLSearchParams(window.location.search);
	const userId = params.get('userId') || localStorage.getItem('userId');

	if (userId) {
		// Delay connection to allow stores to initialize
		setTimeout(() => {
			signalRActions.connect(userId).catch((error) => {
				logger.error({ error }, 'Auto-connect failed');
			});
		}, 100);
	}
}
