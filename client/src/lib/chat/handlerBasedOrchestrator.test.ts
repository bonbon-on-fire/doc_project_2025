import { describe, it, expect, vi, beforeEach } from 'vitest';
import { writable } from 'svelte/store';
import { HandlerBasedSSEOrchestrator } from './handlerBasedOrchestrator';
import type { TaskOperationEventEnvelope } from './sseEventTypes';
import { taskManager } from '$lib/stores/taskManager';

// Mock the taskManager module
vi.mock('$lib/stores/taskManager', () => ({
	taskManager: {
		setLoading: vi.fn(),
		updateFromServerEvent: vi.fn(),
		loadTasks: vi.fn()
	}
}));

describe('HandlerBasedSSEOrchestrator - Task Event Processing', () => {
	let orchestrator: HandlerBasedSSEOrchestrator;

	beforeEach(() => {
		// Reset mocks
		vi.clearAllMocks();

		// Create test stores
		const currentChatStore = writable(null);
		const chatsStore = writable([]);
		const streamingStateStore = writable({
			isStreaming: false,
			currentMessageId: null,
			streamingSnapshots: {},
			error: null
		});

		orchestrator = new HandlerBasedSSEOrchestrator(
			currentChatStore,
			chatsStore,
			streamingStateStore,
			() => 'test-user-id'
		);
	});

	describe('handleTaskOperationEvent', () => {
		it('should set loading state when operation starts', () => {
			const event: TaskOperationEventEnvelope = {
				chatId: 'chat-123',
				version: 1,
				ts: new Date().toISOString(),
				kind: 'task_operation',
				payload: {
					operationType: 'start'
				}
			};

			// Call the private method via type assertion
			(orchestrator as any).handleTaskOperationEvent(event);

			expect(taskManager.setLoading).toHaveBeenCalledWith('chat-123', true);
		});

		it('should update task state when operation completes', () => {
			const mockTasks = [
				{ id: '1', title: 'Task 1', status: 'NotStarted', level: 0 },
				{ id: '2', title: 'Task 2', status: 'Completed', level: 0 }
			];

			const event: TaskOperationEventEnvelope = {
				chatId: 'chat-123',
				version: 1,
				ts: new Date().toISOString(),
				kind: 'task_operation',
				payload: {
					operationType: 'complete',
					taskState: { tasks: mockTasks },
					version: 2
				}
			};

			(orchestrator as any).handleTaskOperationEvent(event);

			expect(taskManager.updateFromServerEvent).toHaveBeenCalledWith('chat-123', mockTasks, 2);
			expect(taskManager.setLoading).toHaveBeenCalledWith('chat-123', false);
		});

		it('should sync task state without changing loading state', () => {
			const mockTasks = [{ id: '1', title: 'Task 1', status: 'InProgress', level: 0 }];

			const event: TaskOperationEventEnvelope = {
				chatId: 'chat-123',
				version: 1,
				ts: new Date().toISOString(),
				kind: 'task_operation',
				payload: {
					operationType: 'sync',
					taskState: mockTasks, // Direct array format
					version: 3
				}
			};

			(orchestrator as any).handleTaskOperationEvent(event);

			expect(taskManager.updateFromServerEvent).toHaveBeenCalledWith('chat-123', mockTasks, 3);
			expect(taskManager.setLoading).not.toHaveBeenCalled();
		});

		it('should handle missing task state gracefully', () => {
			const event: TaskOperationEventEnvelope = {
				chatId: 'chat-123',
				version: 1,
				ts: new Date().toISOString(),
				kind: 'task_operation',
				payload: {
					operationType: 'complete'
				}
			};

			// Should not throw
			expect(() => {
				(orchestrator as any).handleTaskOperationEvent(event);
			}).not.toThrow();

			expect(taskManager.setLoading).toHaveBeenCalledWith('chat-123', false);
			expect(taskManager.updateFromServerEvent).not.toHaveBeenCalled();
		});

		it('should handle errors and clear loading state', () => {
			const event: TaskOperationEventEnvelope = {
				chatId: 'chat-123',
				version: 1,
				ts: new Date().toISOString(),
				kind: 'task_operation',
				payload: {
					operationType: 'complete',
					taskState: 'invalid-data' // Invalid format
				}
			};

			// Mock console.error to avoid test output noise
			const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

			(orchestrator as any).handleTaskOperationEvent(event);

			expect(consoleErrorSpy).toHaveBeenCalled();
			expect(taskManager.setLoading).toHaveBeenCalledWith('chat-123', false);

			consoleErrorSpy.mockRestore();
		});
	});

	describe('handleConnectionLoss', () => {
		it('should attempt to reload tasks after connection loss', async () => {
			// Set up current chat
			const currentChatStore = writable({ id: 'chat-456', title: 'Test Chat' });
			const chatsStore = writable([]);
			const streamingStateStore = writable({
				isStreaming: false,
				currentMessageId: null,
				streamingSnapshots: {},
				error: null
			});

			orchestrator = new HandlerBasedSSEOrchestrator(
				currentChatStore,
				chatsStore,
				streamingStateStore,
				() => 'test-user-id'
			);

			// Mock loadTasks
			vi.mocked(taskManager.loadTasks).mockResolvedValue(undefined);

			// Call handleConnectionLoss
			(orchestrator as any).handleConnectionLoss();

			// Wait for the timeout
			await new Promise((resolve) => setTimeout(resolve, 2100));

			expect(taskManager.loadTasks).toHaveBeenCalledWith('chat-456');
		});

		it('should not reload tasks if no current chat', () => {
			(orchestrator as any).handleConnectionLoss();

			// Should not call loadTasks immediately
			expect(taskManager.loadTasks).not.toHaveBeenCalled();
		});
	});
});
