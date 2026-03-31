import { describe, test, expect, beforeEach } from 'vitest';
import { writable, get, type Writable } from 'svelte/store';
import type { ChatDto } from '$lib/types/chat';
import type { StreamingUIState } from './types';
import { createSlimChatSyncManager } from './slimChatSyncManager';
import { createMessageHandlerRegistry } from './messageHandlerRegistry';
import { createTextMessageHandler } from './handlers/textMessageHandler';
import { createReasoningMessageHandler } from './handlers/reasoningMessageHandler';
import type {
	InitEventEnvelope,
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	StreamCompleteEventEnvelope
} from './sseEventTypes';

describe('SlimChatSyncManager streamingSnapshots behavior', () => {
	let currentChatStore: Writable<ChatDto | null>;
	let chatsStore: Writable<ChatDto[]>;
	let streamingStateStore: Writable<StreamingUIState>;

	const mkBase = () => ({ version: 1, ts: new Date().toISOString() });

	beforeEach(() => {
		currentChatStore = writable<ChatDto | null>(null);
		chatsStore = writable<ChatDto[]>([]);
		streamingStateStore = writable<StreamingUIState>({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});
	});

	test('updates per-message snapshots and switches active streaming id', () => {
		const registry = createMessageHandlerRegistry();
		const mgr = createSlimChatSyncManager(
			registry,
			currentChatStore,
			chatsStore,
			streamingStateStore
		);

		// Register handlers
		registry.register(createTextMessageHandler(mgr));
		registry.register(createReasoningMessageHandler(mgr));

		// Initialize chat with a user message
		const init: InitEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'meta',
			payload: {
				userMessageId: 'u1',
				userTimestamp: new Date().toISOString(),
				userSequenceNumber: 0
			}
		};
		mgr.processSSEEvent(init);

		// Stream a reasoning chunk
		const r1: StreamChunkEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'reasoning',
			messageId: 'm_reason',
			sequenceId: 1,
			payload: { delta: 'thinking...', visibility: 'plain' }
		} as any;
		mgr.processSSEEvent(r1);

		// Expect snapshot for reasoning exists and is active
		let state = get(streamingStateStore);
		expect(state.currentMessageId).toBe('m_reason');
		expect(state.streamingSnapshots['m_reason']).toBeDefined();
		expect(state.streamingSnapshots['m_reason'].isStreaming).toBe(true);
		expect(state.streamingSnapshots['m_reason'].phase).toBe('streaming');
		expect(state.streamingSnapshots['m_reason'].reasoningDelta).toBe('thinking...');

		// Stream a text chunk for a new assistant message
		const t1: StreamChunkEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'text',
			messageId: 'm_text',
			sequenceId: 2,
			payload: { delta: 'Hello', done: false }
		} as any;
		mgr.processSSEEvent(t1);

		state = get(streamingStateStore);
		// Active stream switches to text message
		expect(state.currentMessageId).toBe('m_text');
		// Reasoning snapshot remains but is no longer marked streaming
		expect(state.streamingSnapshots['m_reason']).toBeDefined();
		expect(state.streamingSnapshots['m_reason'].isStreaming).toBe(false);
		// Text snapshot is streaming with its own delta
		expect(state.streamingSnapshots['m_text']).toBeDefined();
		expect(state.streamingSnapshots['m_text'].isStreaming).toBe(true);
		expect(state.streamingSnapshots['m_text'].phase).toBe('streaming');
		expect(state.streamingSnapshots['m_text'].textDelta).toBe('Hello');

		// Complete the reasoning message
		const rc: MessageCompleteEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'reasoning',
			messageId: 'm_reason',
			sequenceId: 1,
			payload: { reasoning: 'final thinking', visibility: 'plain' }
		} as any;
		mgr.processSSEEvent(rc);

		// Its snapshot is cleaned up after completion
		state = get(streamingStateStore);
		// On completion, snapshot is retained with phase 'complete' and isStreaming=false
		expect(state.streamingSnapshots['m_reason:reasoning']).toBeDefined();
		expect(state.streamingSnapshots['m_reason:reasoning'].isStreaming).toBe(false);
		expect(state.streamingSnapshots['m_reason:reasoning'].phase).toBe('complete');

		// Chat contains completed reasoning message DTO
		const chat = get(currentChatStore);
		expect(chat).toBeTruthy();
		const completed = chat!.messages.find((m) => m.id === 'm_reason:reasoning');
		expect(completed).toBeTruthy();
		expect((completed as any).messageType).toBe('reasoning');
		expect((completed as any).reasoning).toBe('final thinking');

		// Complete the text message and ensure its snapshot is also removed
		const tc: MessageCompleteEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'text',
			messageId: 'm_text',
			sequenceId: 2,
			payload: { text: 'Hello world' }
		} as any;
		mgr.processSSEEvent(tc);

		state = get(streamingStateStore);
		expect(state.streamingSnapshots['m_text:text']).toBeDefined();
		expect(state.streamingSnapshots['m_text:text'].isStreaming).toBe(false);
		expect(state.streamingSnapshots['m_text:text'].phase).toBe('complete');
	});

	test('clears all snapshots on stream complete', () => {
		const registry = createMessageHandlerRegistry();
		const mgr = createSlimChatSyncManager(
			registry,
			currentChatStore,
			chatsStore,
			streamingStateStore
		);
		registry.register(createTextMessageHandler(mgr));
		registry.register(createReasoningMessageHandler(mgr));

		const init: InitEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'meta',
			payload: {
				userMessageId: 'u1',
				userTimestamp: new Date().toISOString(),
				userSequenceNumber: 0
			}
		};
		mgr.processSSEEvent(init);

		const r1: StreamChunkEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'reasoning',
			messageId: 'm_reason',
			sequenceId: 1,
			payload: { delta: 'thinking...', visibility: 'plain' }
		} as any;
		mgr.processSSEEvent(r1);

		// Complete stream
		const done: StreamCompleteEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'complete'
		} as any;
		mgr.processSSEEvent(done);

		const state = get(streamingStateStore);
		expect(state.isStreaming).toBe(false);
		expect(state.currentMessageId).toBeNull();
		expect(Object.keys(state.streamingSnapshots).length).toBe(0);
	});

	test('does not clear prior snapshots on new init (start of next turn)', () => {
		const registry = createMessageHandlerRegistry();
		const mgr = createSlimChatSyncManager(
			registry,
			currentChatStore,
			chatsStore,
			streamingStateStore
		);
		registry.register(createTextMessageHandler(mgr));
		registry.register(createReasoningMessageHandler(mgr));

		const init1: InitEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'meta',
			payload: {
				userMessageId: 'u1',
				userTimestamp: new Date().toISOString(),
				userSequenceNumber: 0
			}
		};
		mgr.processSSEEvent(init1);

		const r1: StreamChunkEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'reasoning',
			messageId: 'm_reason1',
			sequenceId: 1,
			payload: { delta: 'thinking turn1', visibility: 'plain' }
		} as any;
		mgr.processSSEEvent(r1);

		// Start next turn (new init). Snapshots from previous messages should NOT be cleared here.
		const init2: InitEventEnvelope = {
			...mkBase(),
			chatId: 'c1',
			kind: 'meta',
			payload: {
				userMessageId: 'u2',
				userTimestamp: new Date().toISOString(),
				userSequenceNumber: 2
			}
		};
		mgr.processSSEEvent(init2);

		const state = get(streamingStateStore);
		expect(state.isStreaming).toBe(true);
		// Ensure prior reasoning snapshot still present (until its own completion arrives)
		expect(state.streamingSnapshots['m_reason1:reasoning']).toBeDefined();
		expect(state.streamingSnapshots['m_reason1:reasoning'].reasoningDelta).toContain(
			'thinking turn1'
		);
	});
});
