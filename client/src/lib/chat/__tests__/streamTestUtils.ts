import { writable, get, type Writable } from 'svelte/store';
import type { ChatDto } from '$lib/types/chat';
import type { StreamingUIState } from '../types';
import type { SSEEventEnvelopeUnion } from '../sseEventTypes';
import { createSlimChatSyncManager } from '../slimChatSyncManager';
import { createMessageHandlerRegistry } from '../messageHandlerRegistry';
import { createTextMessageHandler } from '../handlers/textMessageHandler';
import { createReasoningMessageHandler } from '../handlers/reasoningMessageHandler';
import { createToolCallMessageHandler } from '../handlers/toolCallMessageHandler';
import { createToolsAggregateMessageHandler } from '../handlers/toolsAggregateMessageHandler';
// File system imports removed - will use direct string content in tests

export interface SSEEvent {
	id?: string;
	event: string;
	data: string;
}

export interface TestSnapshot {
	chat: ChatDto | null;
	streamingState: StreamingUIState;
	eventIndex: number;
	eventType: string;
}

/**
 * Test harness for replaying SSE streams and capturing state snapshots
 */
export class SSEStreamTestHarness {
	private events: SSEEvent[] = [];
	private snapshots: TestSnapshot[] = [];
	private currentChatStore: Writable<ChatDto | null>;
	private chatsStore: Writable<ChatDto[]>;
	private streamingStateStore: Writable<StreamingUIState>;
	private syncManager: ReturnType<typeof createSlimChatSyncManager>;

	constructor(streamContent?: string, userMessage: string = 'Test user message') {
		// Initialize stores
		this.currentChatStore = writable<ChatDto | null>(null);
		this.chatsStore = writable<ChatDto[]>([]);
		this.streamingStateStore = writable<StreamingUIState>({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});

		// Initialize message handler registry and sync manager
		const registry = createMessageHandlerRegistry();
		this.syncManager = createSlimChatSyncManager(
			registry,
			this.currentChatStore,
			this.chatsStore,
			this.streamingStateStore
		);

		// Register handlers
		registry.register(createTextMessageHandler(this.syncManager));
		registry.register(createReasoningMessageHandler(this.syncManager));
		registry.register(createToolCallMessageHandler(this.syncManager));
		registry.register(createToolsAggregateMessageHandler(this.syncManager));

		// Set the user message for init event processing
		this.syncManager.setCurrentUserMessage(userMessage);

		// Parse stream content if provided
		if (streamContent) {
			this.events = this.parseSSEStream(streamContent);
		}
	}

	/**
	 * Load SSE stream from string content
	 */
	loadFromString(content: string): void {
		this.events = this.parseSSEStream(content);
	}

	/**
	 * Parse raw SSE stream into events
	 */
	private parseSSEStream(content: string): SSEEvent[] {
		const events: SSEEvent[] = [];
		const lines = content.split('\n');

		let currentEvent: Partial<SSEEvent> = {};

		for (const line of lines) {
			if (line.trim() === '') {
				// Empty line marks end of event
				if (currentEvent.event && currentEvent.data) {
					events.push(currentEvent as SSEEvent);
					currentEvent = {};
				}
			} else if (line.startsWith('id:')) {
				currentEvent.id = line.substring(3).trim();
			} else if (line.startsWith('event:')) {
				currentEvent.event = line.substring(6).trim();
			} else if (line.startsWith('data:')) {
				currentEvent.data = line.substring(5).trim();
			}
		}

		// Don't forget the last event if there's no trailing empty line
		if (currentEvent.event && currentEvent.data) {
			events.push(currentEvent as SSEEvent);
		}

		return events;
	}

	/**
	 * Process events up to a specific index and return state snapshot
	 */
	async processUntilEvent(eventIndex: number): Promise<TestSnapshot> {
		// Clear snapshots if starting fresh
		if (eventIndex === 0) {
			this.snapshots = [];
		}

		// Process events from last snapshot to requested index
		const startIndex = this.snapshots.length;
		for (let i = startIndex; i <= eventIndex && i < this.events.length; i++) {
			const event = this.events[i];

			// Map event type to kind for the sync manager
			const eventEnvelope = this.mapEventToEnvelope(event);
			if (eventEnvelope) {
				this.syncManager.processSSEEvent(eventEnvelope);
			}

			// Capture snapshot after processing
			const snapshot: TestSnapshot = {
				chat: get(this.currentChatStore),
				streamingState: JSON.parse(JSON.stringify(get(this.streamingStateStore))), // Deep copy
				eventIndex: i,
				eventType: event.event
			};
			this.snapshots.push(snapshot);
		}

		return this.snapshots[eventIndex] || this.snapshots[this.snapshots.length - 1];
	}

	/**
	 * Map SSE event to typed event envelope
	 */
	private mapEventToEnvelope(event: SSEEvent): SSEEventEnvelopeUnion | null {
		try {
			const data = JSON.parse(event.data);

			// Map event types to the expected format
			switch (event.event) {
				case 'init':
					return data as any; // InitEventEnvelope
				case 'messageupdate':
					// Handle both stream chunks and complete messages
					if (data.payload && 'done' in data.payload) {
						// Text stream chunk
						return data as any;
					} else if (data.payload && 'toolCallUpdate' in data.payload) {
						// Tool call update
						return data as any;
					} else if (data.payload && 'toolCallId' in data.payload) {
						// Tool result
						return data as any;
					}
					return data as any;
				case 'message':
					// Complete message event
					return data as any;
				case 'complete':
					// Stream complete event
					return data as any;
				default:
					console.warn(`Unknown event type: ${event.event}`);
					return null;
			}
		} catch (error) {
			console.error(`Failed to parse event data: ${error}`, event);
			return null;
		}
	}

	/**
	 * Get all events for inspection
	 */
	getEvents(): SSEEvent[] {
		return this.events;
	}

	/**
	 * Get current state without processing more events
	 */
	getCurrentState(): TestSnapshot {
		const currentIndex = this.snapshots.length - 1;
		if (currentIndex < 0) {
			return {
				chat: get(this.currentChatStore),
				streamingState: get(this.streamingStateStore),
				eventIndex: -1,
				eventType: 'none'
			};
		}
		return this.snapshots[currentIndex];
	}

	/**
	 * Reset the harness to initial state
	 */
	reset(): void {
		this.snapshots = [];
		this.currentChatStore.set(null);
		this.chatsStore.set([]);
		this.streamingStateStore.set({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});
	}

	/**
	 * Process all events and return final state
	 */
	async processAll(): Promise<TestSnapshot> {
		return this.processUntilEvent(this.events.length - 1);
	}
}

/**
 * Helper to create a test orchestrator with mock stores
 */
export function createTestOrchestrator() {
	const currentChatStore = writable<ChatDto | null>(null);
	const chatsStore = writable<ChatDto[]>([]);
	const streamingStateStore = writable<StreamingUIState>({
		isStreaming: false,
		currentMessageId: null,
		streamingSnapshots: {},
		error: null
	});

	const registry = createMessageHandlerRegistry();
	const syncManager = createSlimChatSyncManager(
		registry,
		currentChatStore,
		chatsStore,
		streamingStateStore
	);

	// Register handlers
	registry.register(createTextMessageHandler(syncManager));
	registry.register(createReasoningMessageHandler(syncManager));
	registry.register(createToolCallMessageHandler(syncManager));
	registry.register(createToolsAggregateMessageHandler(syncManager));

	return {
		syncManager,
		currentChatStore,
		chatsStore,
		streamingStateStore
	};
}
