/**
 * Handler-Based Chat Orchestrator
 *
 * Clean, modern orchestrator that uses the message handler architecture.
 */

import { writable, type Writable } from 'svelte/store';
import type { ChatDto, CreateChatRequest } from '$lib/types/chat';
import type { StreamingUIState, ErrorEvent } from './types';
import type {
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope,
	TaskOperationEventEnvelope,
	ErrorEventEnvelope
} from './sseEventTypes';
import type { MessageHandlerRegistry } from './messageHandlers';

import { createMessageHandlerRegistry } from './messageHandlerRegistry';
import { createTextMessageHandler } from './handlers/textMessageHandler';
import { createReasoningMessageHandler } from './handlers/reasoningMessageHandler';
import { createToolCallMessageHandler } from './handlers/toolCallMessageHandler';
import { createToolsAggregateMessageHandler } from './handlers/toolsAggregateMessageHandler';
import { createSlimChatSyncManager } from './slimChatSyncManager';
import { createSSEParser } from './sseParser';
import { apiClient } from '$lib/api/client';
import { taskManager } from '$lib/stores/taskManager';
import type { TaskItem } from '$shared/types/tasks';

/**
 * Modern SSE Stream Orchestrator using handler-based architecture
 */
export class HandlerBasedSSEOrchestrator {
	private handlerRegistry: MessageHandlerRegistry;
	private chatSyncManager: ReturnType<typeof createSlimChatSyncManager>;
	private sseParser = createSSEParser();

	// Svelte stores for reactive state
	private currentChatStore: Writable<ChatDto | null>;
	private chatsStore: Writable<ChatDto[]>;
	private streamingStateStore: Writable<StreamingUIState>;
	private getUserId: () => string;

	// Current stream state
	private currentReader: ReadableStreamDefaultReader<Uint8Array> | null = null;
	private isStreamActive = false;
	private sseBuffer = '';

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

		// Register tool call handler for both streaming updates and complete messages
		const toolCallHandler = createToolCallMessageHandler(this.chatSyncManager);
		this.handlerRegistry.register(toolCallHandler);

		// Register tools aggregate handler for combined tool calls and results
		const toolsAggregateHandler = createToolsAggregateMessageHandler(this.chatSyncManager);
		this.handlerRegistry.register(toolsAggregateHandler);
	}

	/**
	 * Start a new chat with streaming
	 */
	async streamNewChat(userMessage: string): Promise<void> {
		const request: CreateChatRequest = {
			userId: this.getUserId(),
			message: userMessage,
			systemPrompt: undefined
		};

		await this.startStream(request, userMessage);
	}

	/**
	 * Start streaming for an existing chat
	 */
	async streamReply(userMessage: string, chatId: string): Promise<void> {
		const request: CreateChatRequest = {
			userId: this.getUserId(),
			message: userMessage,
			chatId: chatId
		};

		// Pass chatId to tools aggregate handler for task manager updates
		const toolsAggregateHandler = this.handlerRegistry.getHandler('tools_aggregate');
		if (toolsAggregateHandler && 'setChatId' in toolsAggregateHandler) {
			(toolsAggregateHandler as any).setChatId(chatId);
		}

		await this.startStream(request, userMessage);
	}

	/**
	 * Start the SSE stream
	 */
	private async startStream(request: CreateChatRequest, userMessage: string): Promise<void> {
		if (this.isStreamActive) {
			console.warn('Stream already active, ignoring new request');
			return;
		}

		try {
			this.isStreamActive = true;
			this.chatSyncManager.setCurrentUserMessage(userMessage);

			// Reflect streaming state immediately for UI
			this.streamingStateStore.update((s) => ({
				...s,
				isStreaming: true,
				error: null
			}));

			const response = await apiClient.streamChatCompletion(request);

			if (!response.ok) {
				throw new Error(`HTTP ${response.status}: ${response.statusText}`);
			}

			if (!response.body) {
				throw new Error('No response body received');
			}

			await this.processSSEStream(response.body, userMessage);
		} catch (error) {
			console.error('Error starting stream:', error);
			this.handleError({
				message: error instanceof Error ? error.message : 'Unknown streaming error',
				kind: 'error'
			});
		} finally {
			this.isStreamActive = false;
		}
	}

	/**
	 * Process the SSE stream
	 */
	private async processSSEStream(
		body: ReadableStream<Uint8Array>,
		userMessage: string
	): Promise<void> {
		const reader = body.getReader();
		this.currentReader = reader;
		const decoder = new TextDecoder();

		try {
			while (true) {
				const { done, value } = await reader.read();

				if (done) {
					console.log('SSE stream completed');
					// Reset streaming state when stream naturally completes
					this.streamingStateStore.update((state) => ({
						...state,
						isStreaming: false,
						error: null
					}));
					break;
				}

				const chunk = decoder.decode(value, { stream: true });
				this.sseBuffer += chunk;

				// Split by SSE record delimiter (blank line). Support CRLF and LF.
				const parts = this.sseBuffer.split(/\r?\n\r?\n/);
				// Keep the last partial piece in the buffer
				this.sseBuffer = parts.pop() ?? '';

				for (const block of parts) {
					const trimmed = block.trim();
					if (trimmed.length === 0) continue;
					await this.processSSEBlock(trimmed);
				}
			}
		} catch (error) {
			if (error instanceof Error && error.name === 'AbortError') {
				console.log('SSE stream was cancelled');
			} else {
				console.error('Error processing SSE stream:', error);
				this.handleError({
					message: error instanceof Error ? error.message : 'Stream processing error',
					kind: 'error'
				});
				// On connection loss, try to reload task state if we have an active chat
				this.handleConnectionLoss();
			}
		} finally {
			this.currentReader = null;
		}
	}

	/**
	 * Process individual SSE block (an event with event: and data:)
	 */
	private async processSSEBlock(block: string): Promise<void> {
		try {
			console.log('[HandlerBasedSSEOrchestrator] Processing SSE block:', block);

			const parsedEvent = this.sseParser.parseSSEData(block);
			if (!parsedEvent) {
				console.log('[HandlerBasedSSEOrchestrator] No parsed event from block');
				return;
			}

			console.log('[HandlerBasedSSEOrchestrator] Parsed event:', {
				eventType: parsedEvent.eventType,
				eventData: parsedEvent.eventData
			});

			const raw = JSON.parse(parsedEvent.eventData);
			const base = {
				chatId: String(raw.chatId),
				version: Number(raw.version || 1),
				ts: String(raw.ts || new Date().toISOString())
			};

			switch (parsedEvent.eventType) {
				case 'init': {
					const env: InitEventEnvelope = {
						...base,
						kind: 'meta',
						payload: {
							userMessageId: String(raw.payload?.userMessageId),
							userTimestamp: String(raw.payload?.userTimestamp),
							userSequenceNumber: Number(raw.payload?.userSequenceNumber || 0)
						}
					};
					this.chatSyncManager.processSSEEvent(env);
					break;
				}
				case 'messageupdate': {
					if (raw.kind === 'tools_call_update') {
						console.log('[Orchestrator] Tool call update event:', JSON.stringify(raw, null, 2));
						console.log('[Orchestrator] Payload structure:', {
							hasPayload: !!raw.payload,
							hasToolCallUpdate: !!raw.payload?.toolCallUpdate,
							toolCallUpdate: raw.payload?.toolCallUpdate
						});
					}
					// Handle tool_result events
					if (raw.kind === 'tool_result') {
						console.log('[Orchestrator] Tool result event:', {
							messageId: raw.messageId,
							toolCallId: raw.payload?.toolCallId,
							isError: raw.payload?.isError
						});
						const env: StreamChunkEventEnvelope = {
							...base,
							kind: 'tool_result',
							messageId: String(raw.messageId),
							sequenceId: Number(raw.sequenceId),
							payload: raw.payload
						} as StreamChunkEventEnvelope;
						this.chatSyncManager.processSSEEvent(env);
						break;
					}
					// Handle tools_call_aggregate as a complete message
					if (raw.kind === 'tools_call_aggregate') {
						console.log('[Orchestrator] Tools call aggregate event:', {
							messageId: raw.messageId,
							toolCallsCount: raw.payload?.toolCalls?.length,
							toolResultsCount: raw.payload?.toolResults?.length
						});
						const env: MessageCompleteEventEnvelope = {
							...base,
							kind: 'tools_aggregate',
							messageId: String(raw.messageId),
							sequenceId: Number(raw.sequenceId),
							payload: {
								messageType: 'tools_aggregate',
								toolCalls: raw.payload?.toolCalls || [],
								toolResults: raw.payload?.toolResults || []
							}
						} as MessageCompleteEventEnvelope;
						this.chatSyncManager.processSSEEvent(env);
						break;
					}
					const env: StreamChunkEventEnvelope = {
						...base,
						kind: String(raw.kind), // 'text' | 'reasoning' | 'tools_call_update'
						messageId: String(raw.messageId),
						sequenceId: Number(raw.sequenceId),
						payload: raw.payload
					} as StreamChunkEventEnvelope;
					this.chatSyncManager.processSSEEvent(env);
					break;
				}
				case 'message': {
					if (raw.kind === 'error') {
						const env: ErrorEventEnvelope = {
							...base,
							kind: 'error',
							messageId: raw.messageId ? String(raw.messageId) : undefined,
							sequenceId: raw.sequenceId != null ? Number(raw.sequenceId) : undefined,
							payload: {
								message: String(raw.payload?.message || 'Unknown error'),
								code: raw.payload?.code
							}
						};
						this.chatSyncManager.processSSEEvent(env);
						break;
					}
					const env: MessageCompleteEventEnvelope = {
						...base,
						kind: String(raw.kind), // 'text' | 'reasoning' | 'usage'
						messageId: String(raw.messageId),
						sequenceId: Number(raw.sequenceId),
						payload: raw.payload
					} as MessageCompleteEventEnvelope;
					this.chatSyncManager.processSSEEvent(env);
					break;
				}
				case 'task_operation': {
					const env: TaskOperationEventEnvelope = {
						...base,
						kind: 'task_operation',
						messageId: raw.messageId ? String(raw.messageId) : undefined,
						payload: {
							operationType: raw.payload?.operationType || 'sync',
							operation: raw.payload?.operation,
							taskState: raw.payload?.taskState,
							version: raw.payload?.version
						}
					};
					this.handleTaskOperationEvent(env);
					break;
				}
				case 'complete': {
					const env: StreamCompleteEventEnvelope = {
						...base,
						kind: 'complete'
					};
					this.chatSyncManager.processSSEEvent(env);
					break;
				}
				default: {
					console.warn('Unhandled SSE eventType:', parsedEvent.eventType, raw);
				}
			}
		} catch (error) {
			console.error('Error processing SSE block:', error, { block });
		}
	}

	/**
	 * Stop the current stream
	 */
	async stopStream(): Promise<void> {
		if (this.currentReader) {
			await this.currentReader.cancel();
			this.currentReader = null;
		}
		this.isStreamActive = false;

		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			error: null
		}));
	}

	/**
	 * Handle connection loss by attempting to reload task state
	 */
	private handleConnectionLoss(): void {
		// Get current chat ID from the current chat store
		const currentChat = this.getCurrentChat();
		if (currentChat?.id) {
			console.log(
				'[Orchestrator] Connection lost, attempting to reload tasks for chat:',
				currentChat.id
			);
			// Attempt to reload tasks from API when connection is restored
			// This is a graceful recovery mechanism
			setTimeout(() => {
				taskManager.loadTasks(currentChat.id).catch((error) => {
					console.error('Failed to reload tasks after connection loss:', error);
				});
			}, 2000); // Wait 2 seconds before attempting to reload
		}
	}

	/**
	 * Get current chat from store
	 */
	private getCurrentChat(): ChatDto | null {
		let currentChat: ChatDto | null = null;
		// Get current value from store using subscribe
		const unsubscribe = this.currentChatStore.subscribe((value) => {
			currentChat = value;
		});
		unsubscribe();
		return currentChat;
	}

	/**
	 * Handle task operation events from SSE
	 */
	private handleTaskOperationEvent(event: TaskOperationEventEnvelope): void {
		const { chatId, payload } = event;

		console.log('[Orchestrator] Processing task operation event:', {
			chatId,
			operationType: payload.operationType,
			hasTaskState: !!payload.taskState,
			version: payload.version
		});

		try {
			switch (payload.operationType) {
				case 'start':
					// Set loading state when operation starts
					taskManager.setLoading(chatId, true);
					break;

				case 'complete':
					// Update task state when operation completes
					if (payload.taskState) {
						// Parse tasks from the server's task state
						let tasks: TaskItem[] = [];
						try {
							if (Array.isArray(payload.taskState)) {
								tasks = payload.taskState;
							} else if (typeof payload.taskState === 'object' && payload.taskState.tasks) {
								tasks = payload.taskState.tasks;
							} else {
								throw new Error('Invalid task state format');
							}
							taskManager.updateFromServerEvent(chatId, tasks, payload.version);
						} catch (parseError) {
							console.error('Error parsing task state:', parseError, payload.taskState);
						}
					}
					// Clear loading state
					taskManager.setLoading(chatId, false);
					break;

				case 'sync':
					// Full sync of task state from server
					if (payload.taskState) {
						const tasks: TaskItem[] = Array.isArray(payload.taskState)
							? payload.taskState
							: payload.taskState.tasks || [];

						taskManager.updateFromServerEvent(chatId, tasks, payload.version);
					}
					break;

				default:
					console.warn('Unknown task operation type:', payload.operationType);
			}
		} catch (error) {
			console.error('Error handling task operation event:', error);
			taskManager.setLoading(chatId, false);
		}
	}

	/**
	 * Handle errors
	 */
	private handleError(error: ErrorEvent): void {
		console.error('Stream orchestrator error:', error);

		this.streamingStateStore.update((state) => ({
			...state,
			isStreaming: false,
			error: error.message
		}));
	}

	/**
	 * Check if stream is currently active
	 */
	isStreaming(): boolean {
		return this.isStreamActive;
	}

	/**
	 * Cleanup orchestrator and stop any active streams
	 */
	async cleanup(): Promise<void> {
		await this.stopStream();
		this.handlerRegistry.clear();

		this.streamingStateStore.set({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});
	}
}
