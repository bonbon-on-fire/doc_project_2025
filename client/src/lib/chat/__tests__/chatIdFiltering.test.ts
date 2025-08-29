import { describe, test, expect, beforeEach, vi, afterEach } from 'vitest';
import { writable, get } from 'svelte/store';
import { HandlerBasedSSEOrchestrator } from '../handlerBasedOrchestrator';
import type { ChatDto } from '$lib/types/chat';
import type { StreamingUIState } from '../types';
import * as apiClientModule from '$lib/api/client';

describe('Chat ID Filtering in SSE Events', () => {
	let orchestrator: HandlerBasedSSEOrchestrator;
	let currentChatStore: ReturnType<typeof writable<ChatDto | null>>;
	let chatsStore: ReturnType<typeof writable<ChatDto[]>>;
	let streamingStateStore: ReturnType<typeof writable<StreamingUIState>>;
	let currentChatIdStore: ReturnType<typeof writable<string | null>>;

	beforeEach(() => {
		// Clear all mocks before each test
		vi.clearAllMocks();

		currentChatStore = writable<ChatDto | null>(null);
		chatsStore = writable<ChatDto[]>([]);
		streamingStateStore = writable<StreamingUIState>({
			isStreaming: false,
			streamingMessage: null,
			error: null
		});
		currentChatIdStore = writable<string | null>(null);

		orchestrator = new HandlerBasedSSEOrchestrator(
			currentChatStore,
			chatsStore,
			streamingStateStore,
			() => 'test-user',
			currentChatIdStore
		);
	});

	afterEach(() => {
		vi.restoreAllMocks();
	});

	test('should only process SSE events for the current streaming chat', async () => {
		// Set up an initial chat in the store
		const existingChat: ChatDto = {
			id: 'chat-a',
			userId: 'test-user',
			title: 'Test Chat A',
			messages: [],
			systemPrompt: '',
			mode: null,
			createdAt: new Date().toISOString(),
			updatedAt: new Date().toISOString()
		};
		currentChatStore.set(existingChat);
		chatsStore.set([existingChat]);

		// Mock the API client's streamChatCompletion method
		const mockResponseA = new Response(
			new ReadableStream({
				async start(controller) {
					// Send events for chat A
					controller.enqueue(
						new TextEncoder().encode(
							`event: init\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								version: 1,
								ts: new Date().toISOString(),
								payload: {
									userMessageId: 'msg-1',
									userTimestamp: new Date().toISOString(),
									userSequenceNumber: 1
								}
							})}\n\n`
						)
					);

					// Send a text chunk for chat A
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'text',
								messageId: 'msg-2',
								sequenceId: 2,
								payload: { delta: 'Response from chat A' }
							})}\n\n`
						)
					);

					// Now simulate events from chat B (should be ignored)
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-b',
								kind: 'text',
								messageId: 'msg-3',
								sequenceId: 1,
								payload: { delta: 'Response from chat B' }
							})}\n\n`
						)
					);

					// More events for chat A (should be processed)
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'text',
								messageId: 'msg-2',
								sequenceId: 3,
								payload: { delta: ' continued' }
							})}\n\n`
						)
					);

					// Complete the stream
					controller.enqueue(
						new TextEncoder().encode(
							`event: done\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'done'
							})}\n\n`
						)
					);

					controller.close();
				}
			})
		);

		// Mock the apiClient.streamChatCompletion method
		vi.spyOn(apiClientModule.apiClient, 'streamChatCompletion').mockResolvedValueOnce(
			mockResponseA
		);

		// Start streaming for chat A using streamReply (since we have an existing chat)
		await orchestrator.streamReply('Hello from chat A', 'chat-a');

		// Wait for processing
		await new Promise((resolve) => setTimeout(resolve, 100));

		// Verify that only chat A messages were processed
		const currentChat = get(currentChatStore);
		expect(currentChat).toBeDefined();
		expect(currentChat?.id).toBe('chat-a');

		// Check messages
		const messages = currentChat?.messages || [];
		console.log(
			'Messages in chat:',
			messages.map((m) => ({
				id: m.id,
				type: m.type,
				content: m.content,
				text: (m as any).text,
				textDelta: (m as any).textDelta,
				role: m.role
			}))
		);

		// Check that chat B messages were not added
		const hasChatBContent = messages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('chat B');
		});
		expect(hasChatBContent).toBe(false);

		// Check that chat A messages were added
		const hasChatAContent = messages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('chat A');
		});
		expect(hasChatAContent).toBe(true);
	});

	test('should handle switching between chats without message contamination', async () => {
		// Set up chat A
		const chatA: ChatDto = {
			id: 'chat-a',
			userId: 'test-user',
			title: 'Test Chat A',
			messages: [],
			systemPrompt: '',
			mode: null,
			createdAt: new Date().toISOString(),
			updatedAt: new Date().toISOString()
		};

		// Set up chat B
		const chatB: ChatDto = {
			id: 'chat-b',
			userId: 'test-user',
			title: 'Test Chat B',
			messages: [],
			systemPrompt: '',
			mode: null,
			createdAt: new Date().toISOString(),
			updatedAt: new Date().toISOString()
		};

		currentChatStore.set(chatA);
		chatsStore.set([chatA, chatB]);

		// Mock response for chat A (quick completion)
		const mockResponseA = new Response(
			new ReadableStream({
				async start(controller) {
					// Initial events for chat A
					controller.enqueue(
						new TextEncoder().encode(
							`event: init\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								version: 1,
								ts: new Date().toISOString(),
								payload: {
									userMessageId: 'msg-a1',
									userTimestamp: new Date().toISOString(),
									userSequenceNumber: 1
								}
							})}\n\n`
						)
					);

					// Send a few chat A events
					for (let i = 0; i < 3; i++) {
						controller.enqueue(
							new TextEncoder().encode(
								`event: messageupdate\ndata: ${JSON.stringify({
									chatId: 'chat-a',
									kind: 'text',
									messageId: 'msg-a2',
									sequenceId: i + 2,
									payload: { delta: ` chunk${i} from A` }
								})}\n\n`
							)
						);
					}

					// Complete the stream for chat A
					controller.enqueue(
						new TextEncoder().encode(
							`event: done\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'done'
							})}\n\n`
						)
					);

					controller.close();
				}
			})
		);

		// Mock the first API call for chat A
		const streamChatCompletionSpy = vi.spyOn(apiClientModule.apiClient, 'streamChatCompletion');
		streamChatCompletionSpy.mockResolvedValueOnce(mockResponseA);

		// Stream for chat A and wait for completion
		await orchestrator.streamReply('Start chat A', 'chat-a');

		// Verify chat A has correct content
		let currentChat = get(currentChatStore);
		expect(currentChat?.id).toBe('chat-a');

		const chatAMessages = currentChat?.messages || [];
		const hasChatAText = chatAMessages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('from A');
		});
		expect(hasChatAText).toBe(true);

		// Now switch to chat B
		currentChatStore.set(chatB);
		chatsStore.set([chatA, chatB]);

		// Mock response for chat B with mixed events
		const mockResponseB = new Response(
			new ReadableStream({
				async start(controller) {
					controller.enqueue(
						new TextEncoder().encode(
							`event: init\ndata: ${JSON.stringify({
								chatId: 'chat-b',
								version: 1,
								ts: new Date().toISOString(),
								payload: {
									userMessageId: 'msg-b1',
									userTimestamp: new Date().toISOString(),
									userSequenceNumber: 1
								}
							})}\n\n`
						)
					);

					// Send chat B events
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-b',
								kind: 'text',
								messageId: 'msg-b2',
								sequenceId: 2,
								payload: { delta: 'Response from chat B' }
							})}\n\n`
						)
					);

					// Simulate leftover events from chat A (should be ignored)
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'text',
								messageId: 'msg-a3',
								sequenceId: 10,
								payload: { delta: ' late chunk from A' }
							})}\n\n`
						)
					);

					// More chat B events
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-b',
								kind: 'text',
								messageId: 'msg-b2',
								sequenceId: 3,
								payload: { delta: ' continued' }
							})}\n\n`
						)
					);

					// Complete chat B
					controller.enqueue(
						new TextEncoder().encode(
							`event: done\ndata: ${JSON.stringify({
								chatId: 'chat-b',
								kind: 'done'
							})}\n\n`
						)
					);

					controller.close();
				}
			})
		);

		// Mock the second API call for chat B
		streamChatCompletionSpy.mockResolvedValueOnce(mockResponseB);

		// Stream for chat B
		await orchestrator.streamReply('Start chat B', 'chat-b');

		// Verify chat B doesn't have chat A content
		currentChat = get(currentChatStore);
		expect(currentChat?.id).toBe('chat-b');

		const messages = currentChat?.messages || [];
		console.log(
			'Messages in chat B:',
			messages.map((m) => ({
				id: m.id,
				type: m.type,
				content: m.content,
				text: (m as any).text,
				textDelta: (m as any).textDelta,
				role: m.role
			}))
		);

		// Check that late chat A messages were not added to chat B
		const hasChatAContent = messages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('from A') || text.includes('late chunk');
		});
		expect(hasChatAContent).toBe(false);

		const hasChatBContent = messages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('from chat B');
		});
		expect(hasChatBContent).toBe(true);
	});

	test('should handle switching to non-streaming chat correctly', async () => {
		// Set up chat A with existing messages
		const chatA: ChatDto = {
			id: 'chat-a',
			userId: 'test-user',
			title: 'Test Chat A',
			messages: [
				{
					id: 'msg-a1',
					chatId: 'chat-a',
					role: 'user',
					timestamp: new Date(),
					sequenceNumber: 1,
					messageType: 'text',
					text: 'Hello'
				} as MessageDto
			],
			systemPrompt: '',
			mode: null,
			createdAt: new Date().toISOString(),
			updatedAt: new Date().toISOString()
		};

		// Set up chat B with existing messages (no streaming)
		const chatB: ChatDto = {
			id: 'chat-b',
			userId: 'test-user',
			title: 'Test Chat B',
			messages: [
				{
					id: 'msg-b1',
					chatId: 'chat-b',
					role: 'user',
					timestamp: new Date(),
					sequenceNumber: 1,
					messageType: 'text',
					text: 'Previous conversation'
				} as MessageDto
			],
			systemPrompt: '',
			mode: null,
			createdAt: new Date().toISOString(),
			updatedAt: new Date().toISOString()
		};

		// Start with chat A
		currentChatStore.set(chatA);
		currentChatIdStore.set('chat-a');
		chatsStore.set([chatA, chatB]);

		// Mock response for chat A that continues streaming
		const mockResponseA = new Response(
			new ReadableStream({
				async start(controller) {
					// Initial events for chat A
					controller.enqueue(
						new TextEncoder().encode(
							`event: init\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'meta',
								payload: {
									userMessageId: 'user-msg-a',
									userTimestamp: new Date().toISOString(),
									userSequenceNumber: 2
								}
							})}\n\n`
						)
					);

					// Stream some initial content
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'text',
								messageId: 'msg-a2',
								sequenceId: 1,
								payload: { delta: 'Streaming from chat A' }
							})}\n\n`
						)
					);

					// Simulate delay then more events after user switches
					await new Promise((resolve) => setTimeout(resolve, 50));

					// These events should be ignored after switching
					controller.enqueue(
						new TextEncoder().encode(
							`event: messageupdate\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'text',
								messageId: 'msg-a2',
								sequenceId: 2,
								payload: { delta: ' - should be ignored after switch' }
							})}\n\n`
						)
					);

					controller.enqueue(
						new TextEncoder().encode(
							`event: done\ndata: ${JSON.stringify({
								chatId: 'chat-a',
								kind: 'done'
							})}\n\n`
						)
					);

					controller.close();
				}
			})
		);

		// Mock the API call
		vi.spyOn(apiClientModule.apiClient, 'streamChatCompletion').mockResolvedValueOnce(
			mockResponseA
		);

		// Start streaming for chat A
		const streamPromise = orchestrator.streamReply('Stream in chat A', 'chat-a');

		// Wait a bit for initial streaming to start
		await new Promise((resolve) => setTimeout(resolve, 30));

		// Now switch to chat B (non-streaming chat)
		currentChatStore.set(chatB);
		currentChatIdStore.set('chat-b');

		// Wait for the stream to complete
		await streamPromise;

		// Verify chat A didn't get contaminated with late events
		const chatAInStore = get(chatsStore).find((c) => c.id === 'chat-a');
		const chatAMessages = chatAInStore?.messages || [];

		// Check that the "should be ignored" text was not added
		const hasIgnoredContent = chatAMessages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('should be ignored');
		});
		expect(hasIgnoredContent).toBe(false);

		// Verify chat B wasn't contaminated with chat A events
		const currentChat = get(currentChatStore);
		expect(currentChat?.id).toBe('chat-b');

		const chatBMessages = currentChat?.messages || [];
		const hasChatAContent = chatBMessages.some((msg) => {
			const text = msg.content || (msg as any).text || (msg as any).textDelta || '';
			return text.includes('chat A');
		});
		expect(hasChatAContent).toBe(false);

		// Chat B should still have its original message
		expect(chatBMessages.length).toBe(1);
		expect(chatBMessages[0].text).toBe('Previous conversation');
	});
});
