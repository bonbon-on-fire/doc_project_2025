/**
 * SignalR Service for real-time communication with Orleans grains
 *
 * This service manages the SignalR connection lifecycle, handles messages,
 * implements auto-reconnection with exponential backoff, and manages
 * client-side message buffering for offline scenarios.
 */

import * as signalR from '@microsoft/signalr';
import { PUBLIC_API_BASE_URL } from '$env/static/public';
import { logger } from '$lib/utils/logger';
import type {
	ChatDto,
	TextMessageDto,
	ReasoningMessageDto,
	ToolCallMessageDto,
	ToolsCallAggregateMessageDto
} from '$lib/types/chat';

// SignalR Message Types
export interface SignalRMessage {
	chatId: string;
	messageId: string;
	content: any;
	timestamp: string;
	sequenceNumber: number;
}

export interface StreamChunk {
	chatId: string;
	messageId: string;
	delta: string;
	done: boolean;
	messageType: 'text' | 'reasoning' | 'tool_call';
}

export interface OperationStatus {
	operationId: string;
	status: 'success' | 'failure' | 'pending';
	message?: string;
	timestamp: string;
}

// Connection State
export type ConnectionState =
	| 'disconnected'
	| 'connecting'
	| 'connected'
	| 'reconnecting'
	| 'disconnecting'
	| 'error';

// Service Configuration
export interface SignalRConfig {
	hubUrl?: string;
	maxReconnectAttempts?: number;
	initialRetryDelayMs?: number;
	maxRetryDelayMs?: number;
	messageBufferSize?: number;
	enableLogging?: boolean;
}

// Message Buffer Entry
interface BufferedMessage {
	id: string;
	method: string;
	args: any[];
	timestamp: number;
	retryCount: number;
}

/**
 * SignalR Service Class
 * Manages real-time bidirectional communication with the server
 */
export class SignalRService {
	private connection: signalR.HubConnection | null = null;
	private connectionState: ConnectionState = 'disconnected';
	private reconnectAttempts = 0;
	private reconnectTimeoutId: NodeJS.Timeout | null = null;

	// Configuration
	private config: Required<SignalRConfig>;

	// Message buffering
	private messageBuffer: BufferedMessage[] = [];
	private messageIdCounter = 0;

	// Subscriptions
	private subscriptions = new Map<string, Set<(data: any) => void>>();

	// Event handlers
	private stateChangeHandlers = new Set<(state: ConnectionState) => void>();

	constructor(config: SignalRConfig = {}) {
		// Apply default configuration
		this.config = {
			hubUrl: config.hubUrl || this.getDefaultHubUrl(),
			maxReconnectAttempts: config.maxReconnectAttempts ?? 10,
			initialRetryDelayMs: config.initialRetryDelayMs ?? 1000,
			maxRetryDelayMs: config.maxRetryDelayMs ?? 30000,
			messageBufferSize: config.messageBufferSize ?? 100,
			enableLogging: config.enableLogging ?? true
		};

		if (this.config.enableLogging) {
			logger.info({ config: this.config }, 'SignalR service initialized');
		}
	}

	/**
	 * Get default hub URL based on environment
	 */
	private getDefaultHubUrl(): string {
		let baseUrl = PUBLIC_API_BASE_URL as string;

		// In development, use direct server URL
		if (!baseUrl && typeof window !== 'undefined' && window.location.port === '5173') {
			baseUrl = 'http://localhost:5099';
		}

		return `${baseUrl || ''}/api/chat-hub`;
	}

	/**
	 * Initialize and start the SignalR connection
	 */
	async connect(userId: string): Promise<void> {
		if (this.connection && this.connectionState === 'connected') {
			logger.warn('SignalR already connected');
			return;
		}

		try {
			this.updateConnectionState('connecting');

			// Create connection with automatic reconnect disabled
			// We'll handle reconnection manually for more control
			this.connection = new signalR.HubConnectionBuilder()
				.withUrl(this.config.hubUrl, {
					// Add query parameters for user identification
					accessTokenFactory: () => Promise.resolve(''),
					transport:
						signalR.HttpTransportType.WebSockets |
						signalR.HttpTransportType.ServerSentEvents |
						signalR.HttpTransportType.LongPolling
				})
				.withAutomaticReconnect({
					nextRetryDelayInMilliseconds: (retryContext) => {
						return this.calculateRetryDelay(retryContext.previousRetryCount);
					}
				})
				.configureLogging(
					this.config.enableLogging ? signalR.LogLevel.Debug : signalR.LogLevel.Error
				)
				.build();

			// Set up connection lifecycle handlers
			this.setupConnectionHandlers();

			// Set up message handlers
			this.setupMessageHandlers();

			// Start the connection
			await this.connection.start();

			// Subscribe to user-specific channel
			await this.subscribeToUser(userId);

			this.updateConnectionState('connected');
			this.reconnectAttempts = 0;

			// Flush any buffered messages
			await this.flushMessageBuffer();

			logger.info({ userId }, 'SignalR connected successfully');
		} catch (error) {
			logger.error({ error }, 'Failed to connect to SignalR');
			this.updateConnectionState('error');

			// Trigger reconnection
			this.scheduleReconnect();

			throw error;
		}
	}

	/**
	 * Set up connection lifecycle event handlers
	 */
	private setupConnectionHandlers(): void {
		if (!this.connection) return;

		this.connection.onreconnecting((error) => {
			logger.info({ error }, 'SignalR reconnecting...');
			this.updateConnectionState('reconnecting');
		});

		this.connection.onreconnected((connectionId) => {
			logger.info({ connectionId }, 'SignalR reconnected');
			this.updateConnectionState('connected');
			this.reconnectAttempts = 0;

			// Flush buffered messages after reconnection
			this.flushMessageBuffer();
		});

		this.connection.onclose((error) => {
			logger.info({ error }, 'SignalR connection closed');
			this.updateConnectionState('disconnected');

			// Schedule reconnection if not intentional disconnect
			if (error) {
				this.scheduleReconnect();
			}
		});
	}

	/**
	 * Set up message handlers for incoming SignalR messages
	 */
	private setupMessageHandlers(): void {
		if (!this.connection) return;

		// Handle regular messages
		this.connection.on('ReceiveMessage', (message: SignalRMessage) => {
			logger.debug({ message }, 'Received message');
			this.notifySubscribers('message', message);
		});

		// Handle streaming chunks
		this.connection.on('ReceiveStreamChunk', (chunk: StreamChunk) => {
			logger.debug({ chunk }, 'Received stream chunk');
			this.notifySubscribers('streamChunk', chunk);
		});

		// Handle operation status updates
		this.connection.on('OperationStatus', (status: OperationStatus) => {
			logger.debug({ status }, 'Received operation status');
			this.notifySubscribers('operationStatus', status);
		});

		// Handle errors
		this.connection.on('Error', (error: any) => {
			logger.error({ error }, 'Received error from server');
			this.notifySubscribers('error', error);
		});

		// Handle connection status updates
		this.connection.on('ConnectionStatus', (status: string) => {
			logger.info({ status }, 'Connection status update');
			this.notifySubscribers('connectionStatus', status);
		});
	}

	/**
	 * Calculate retry delay with exponential backoff
	 */
	private calculateRetryDelay(attemptNumber: number): number | null {
		if (attemptNumber >= this.config.maxReconnectAttempts) {
			return null; // Stop retrying
		}

		const delay = Math.min(
			this.config.initialRetryDelayMs * Math.pow(2, attemptNumber),
			this.config.maxRetryDelayMs
		);

		// Add jitter to prevent thundering herd
		const jitter = Math.random() * 0.3 * delay;

		return delay + jitter;
	}

	/**
	 * Schedule reconnection attempt
	 */
	private scheduleReconnect(): void {
		if (this.reconnectTimeoutId) {
			clearTimeout(this.reconnectTimeoutId);
		}

		const delay = this.calculateRetryDelay(this.reconnectAttempts);

		if (delay === null) {
			logger.error('Max reconnect attempts reached');
			this.updateConnectionState('error');
			return;
		}

		logger.info({ delay, attempt: this.reconnectAttempts + 1 }, 'Scheduling reconnection');

		this.reconnectTimeoutId = setTimeout(() => {
			this.reconnectAttempts++;
			this.reconnect();
		}, delay);
	}

	/**
	 * Attempt to reconnect
	 */
	private async reconnect(): Promise<void> {
		if (this.connectionState === 'connected') {
			return;
		}

		try {
			this.updateConnectionState('reconnecting');

			if (this.connection) {
				await this.connection.start();

				this.updateConnectionState('connected');
				this.reconnectAttempts = 0;

				// Flush buffered messages
				await this.flushMessageBuffer();

				logger.info('SignalR reconnected successfully');
			}
		} catch (error) {
			logger.error({ error }, 'Reconnection attempt failed');
			this.updateConnectionState('error');

			// Schedule next reconnection attempt
			this.scheduleReconnect();
		}
	}

	/**
	 * Subscribe to user-specific hub
	 */
	private async subscribeToUser(userId: string): Promise<void> {
		if (!this.connection) {
			throw new Error('No connection available');
		}

		try {
			await this.connection.invoke('SubscribeToUser', userId);
			logger.info({ userId }, 'Subscribed to user channel');
		} catch (error) {
			logger.error({ error, userId }, 'Failed to subscribe to user channel');
			throw error;
		}
	}

	/**
	 * Send a message to the server
	 */
	async sendMessage(method: string, ...args: any[]): Promise<void> {
		// If not connected, buffer the message
		if (this.connectionState !== 'connected' || !this.connection) {
			this.bufferMessage(method, args);
			return;
		}

		try {
			await this.connection.invoke(method, ...args);
			logger.debug({ method, args }, 'Message sent');
		} catch (error) {
			logger.error({ error, method, args }, 'Failed to send message');

			// Buffer the message for retry
			this.bufferMessage(method, args);

			throw error;
		}
	}

	/**
	 * Buffer a message for later delivery
	 */
	private bufferMessage(method: string, args: any[]): void {
		// Check buffer size limit
		if (this.messageBuffer.length >= this.config.messageBufferSize) {
			// Remove oldest message
			const removed = this.messageBuffer.shift();
			logger.warn({ removed }, 'Message buffer full, dropping oldest message');
		}

		const bufferedMessage: BufferedMessage = {
			id: `msg_${++this.messageIdCounter}`,
			method,
			args,
			timestamp: Date.now(),
			retryCount: 0
		};

		this.messageBuffer.push(bufferedMessage);
		logger.debug({ bufferedMessage }, 'Message buffered');
	}

	/**
	 * Flush buffered messages
	 */
	private async flushMessageBuffer(): Promise<void> {
		if (this.messageBuffer.length === 0) {
			return;
		}

		logger.info({ count: this.messageBuffer.length }, 'Flushing message buffer');

		const messagesToSend = [...this.messageBuffer];
		this.messageBuffer = [];

		for (const message of messagesToSend) {
			try {
				await this.sendMessage(message.method, ...message.args);
			} catch (error) {
				logger.error({ error, message }, 'Failed to send buffered message');

				// Re-buffer the message if it fails again
				message.retryCount++;
				if (message.retryCount < 3) {
					this.messageBuffer.push(message);
				}
			}
		}
	}

	/**
	 * Subscribe to events
	 */
	subscribe(event: string, handler: (data: any) => void): () => void {
		if (!this.subscriptions.has(event)) {
			this.subscriptions.set(event, new Set());
		}

		this.subscriptions.get(event)!.add(handler);

		// Return unsubscribe function
		return () => {
			const handlers = this.subscriptions.get(event);
			if (handlers) {
				handlers.delete(handler);
			}
		};
	}

	/**
	 * Subscribe to connection state changes
	 */
	onStateChange(handler: (state: ConnectionState) => void): () => void {
		this.stateChangeHandlers.add(handler);

		// Call immediately with current state
		handler(this.connectionState);

		// Return unsubscribe function
		return () => {
			this.stateChangeHandlers.delete(handler);
		};
	}

	/**
	 * Notify all subscribers of an event
	 */
	private notifySubscribers(event: string, data: any): void {
		const handlers = this.subscriptions.get(event);
		if (handlers) {
			handlers.forEach((handler) => {
				try {
					handler(data);
				} catch (error) {
					logger.error({ error, event }, 'Error in event handler');
				}
			});
		}
	}

	/**
	 * Update connection state and notify handlers
	 */
	private updateConnectionState(state: ConnectionState): void {
		if (this.connectionState === state) {
			return;
		}

		const previousState = this.connectionState;
		this.connectionState = state;

		logger.info({ previousState, newState: state }, 'Connection state changed');

		// Notify all state change handlers
		this.stateChangeHandlers.forEach((handler) => {
			try {
				handler(state);
			} catch (error) {
				logger.error({ error }, 'Error in state change handler');
			}
		});
	}

	/**
	 * Get current connection state
	 */
	getConnectionState(): ConnectionState {
		return this.connectionState;
	}

	/**
	 * Check if connected
	 */
	isConnected(): boolean {
		return this.connectionState === 'connected';
	}

	/**
	 * Disconnect from SignalR
	 */
	async disconnect(): Promise<void> {
		if (this.reconnectTimeoutId) {
			clearTimeout(this.reconnectTimeoutId);
			this.reconnectTimeoutId = null;
		}

		if (!this.connection) {
			return;
		}

		try {
			this.updateConnectionState('disconnecting');
			await this.connection.stop();
			this.updateConnectionState('disconnected');

			logger.info('SignalR disconnected');
		} catch (error) {
			logger.error({ error }, 'Error disconnecting from SignalR');
			this.updateConnectionState('error');
		}
	}

	/**
	 * Dispose of the service and clean up resources
	 */
	async dispose(): Promise<void> {
		await this.disconnect();

		this.subscriptions.clear();
		this.stateChangeHandlers.clear();
		this.messageBuffer = [];

		if (this.connection) {
			this.connection = null;
		}

		logger.info('SignalR service disposed');
	}
}

// Export singleton instance
export const signalRService = new SignalRService();
