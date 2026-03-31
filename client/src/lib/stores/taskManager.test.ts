import { describe, it, expect, beforeEach, vi } from 'vitest';
import { get } from 'svelte/store';
import { taskManager, currentChatTasks, taskStats } from './taskManager';
import type { TaskItem, TaskOperation } from '$shared/types/tasks';

// Mock fetch
global.fetch = vi.fn();

describe('taskManager Store', () => {
	beforeEach(() => {
		// Reset store state
		taskManager.clearTasks('test-chat-1');
		taskManager.clearTasks('test-chat-2');
		taskManager.setActiveChat(null);
		taskManager.clearError();
		vi.clearAllMocks();
	});

	describe('loadTasks', () => {
		it('should load tasks from API successfully', async () => {
			const mockTasks: TaskItem[] = [
				{ id: 1, title: 'Task 1', status: 'NotStarted', notes: [] },
				{ id: 2, title: 'Task 2', status: 'InProgress', notes: [] }
			];

			(global.fetch as any).mockResolvedValueOnce({
				ok: true,
				json: async () => ({
					chatId: 'test-chat-1',
					tasks: mockTasks,
					version: 1
				})
			});

			await taskManager.loadTasks('test-chat-1');

			const state = get(taskManager);
			expect(state.isLoading).toBe(false);
			expect(state.error).toBe(null);
			expect(state.activeChat).toBe('test-chat-1');

			const chatTasks = state.tasks.get('test-chat-1');
			expect(chatTasks).toBeDefined();
			expect(chatTasks?.tasks).toEqual(mockTasks);
			expect(chatTasks?.version).toBe(1);
		});

		it('should handle 404 gracefully when no tasks exist', async () => {
			(global.fetch as any).mockResolvedValueOnce({
				ok: false,
				status: 404,
				statusText: 'Not Found'
			});

			await taskManager.loadTasks('test-chat-1');

			const state = get(taskManager);
			expect(state.isLoading).toBe(false);
			expect(state.error).toBe(null);
			// When 404, we now initialize an empty task state
			expect(state.tasks.has('test-chat-1')).toBe(true);
			const chatTasks = state.tasks.get('test-chat-1');
			expect(chatTasks?.tasks).toEqual([]);
			expect(chatTasks?.version).toBe(0);
		});

		it('should handle errors during task loading', async () => {
			(global.fetch as any).mockResolvedValueOnce({
				ok: false,
				status: 500,
				statusText: 'Internal Server Error'
			});

			await taskManager.loadTasks('test-chat-1');

			const state = get(taskManager);
			expect(state.isLoading).toBe(false);
			expect(state.error).toBe('Failed to load tasks: Internal Server Error');
		});
	});

	describe('updateFromServerEvent', () => {
		it('should update tasks from server event', () => {
			const tasks: TaskItem[] = [{ id: 1, title: 'Task 1', status: 'Completed', notes: [] }];

			taskManager.updateFromServerEvent('test-chat-1', tasks, 2);

			const state = get(taskManager);
			const chatTasks = state.tasks.get('test-chat-1');
			expect(chatTasks?.tasks).toEqual(tasks);
			expect(chatTasks?.version).toBe(2);
		});

		it('should increment version if not provided', () => {
			const tasks1: TaskItem[] = [{ id: 1, title: 'Task 1', status: 'NotStarted', notes: [] }];
			const tasks2: TaskItem[] = [{ id: 1, title: 'Task 1', status: 'InProgress', notes: [] }];

			taskManager.updateFromServerEvent('test-chat-1', tasks1, 1);
			taskManager.updateFromServerEvent('test-chat-1', tasks2);

			const state = get(taskManager);
			const chatTasks = state.tasks.get('test-chat-1');
			expect(chatTasks?.version).toBe(2);
		});
	});

	describe('updateFromToolCall', () => {
		it('should update tasks from tool call result', () => {
			const operation: TaskOperation = {
				type: 'add-task',
				taskId: 1,
				title: 'New Task'
			};

			const resultTasks: TaskItem[] = [
				{ id: 1, title: 'New Task', status: 'NotStarted', notes: [] }
			];

			taskManager.updateFromToolCall('test-chat-1', operation, resultTasks);

			const state = get(taskManager);
			const chatTasks = state.tasks.get('test-chat-1');
			expect(chatTasks?.tasks).toEqual(resultTasks);
		});
	});

	describe('clearTasks', () => {
		it('should remove tasks for a specific chat', () => {
			const tasks: TaskItem[] = [{ id: 1, title: 'Task 1', status: 'NotStarted', notes: [] }];
			taskManager.updateFromServerEvent('test-chat-1', tasks);

			taskManager.clearTasks('test-chat-1');

			const state = get(taskManager);
			expect(state.tasks.has('test-chat-1')).toBe(false);
		});
	});

	describe('setActiveChat', () => {
		it('should set the active chat', () => {
			taskManager.setActiveChat('test-chat-1');

			const state = get(taskManager);
			expect(state.activeChat).toBe('test-chat-1');
		});

		it('should clear active chat when null', () => {
			taskManager.setActiveChat('test-chat-1');
			taskManager.setActiveChat(null);

			const state = get(taskManager);
			expect(state.activeChat).toBe(null);
		});
	});

	describe('setLoading', () => {
		it('should set loading state', () => {
			taskManager.setLoading('test-chat-1', true);

			const state = get(taskManager);
			expect(state.isLoading).toBe(true);
			expect(state.activeChat).toBe('test-chat-1');
		});

		it('should clear loading state', () => {
			taskManager.setLoading('test-chat-1', true);
			taskManager.setLoading('test-chat-1', false);

			const state = get(taskManager);
			expect(state.isLoading).toBe(false);
		});
	});
});

describe('currentChatTasks derived store', () => {
	beforeEach(() => {
		// Reset store state
		taskManager.setActiveChat(null);
		taskManager.clearTasks('test-chat-1');
		taskManager.clearTasks('test-chat-2');
	});

	it('should return null when no active chat', () => {
		const tasks = get(currentChatTasks);
		expect(tasks).toBeNull();
	});

	it('should return tasks for active chat', () => {
		const mockTasks: TaskItem[] = [{ id: 1, title: 'Task 1', status: 'NotStarted', notes: [] }];

		taskManager.updateFromServerEvent('test-chat-1', mockTasks);
		taskManager.setActiveChat('test-chat-1');

		const tasks = get(currentChatTasks);
		expect(tasks?.tasks).toEqual(mockTasks);
	});
});

describe('taskStats derived store', () => {
	beforeEach(() => {
		// Reset store state
		taskManager.setActiveChat(null);
		taskManager.clearTasks('test-chat-1');
		taskManager.clearTasks('test-chat-2');
	});

	it('should return zero stats when no tasks', () => {
		const stats = get(taskStats);
		expect(stats).toEqual({
			total: 0,
			completed: 0,
			inProgress: 0,
			notStarted: 0,
			removed: 0
		});
	});

	it('should calculate task statistics correctly', () => {
		const tasks: TaskItem[] = [
			{ id: 1, title: 'Task 1', status: 'Completed', notes: [] },
			{ id: 2, title: 'Task 2', status: 'InProgress', notes: [] },
			{ id: 3, title: 'Task 3', status: 'NotStarted', notes: [] },
			{ id: 4, title: 'Task 4', status: 'NotStarted', notes: [] }
		];

		taskManager.updateFromServerEvent('test-chat-1', tasks);
		taskManager.setActiveChat('test-chat-1');

		const stats = get(taskStats);
		expect(stats).toEqual({
			total: 4,
			completed: 1,
			inProgress: 1,
			notStarted: 2,
			removed: 0
		});
	});

	it('should count subtasks recursively', () => {
		const tasks: TaskItem[] = [
			{
				id: 1,
				title: 'Parent Task',
				status: 'InProgress',
				notes: [],
				subtasks: [
					{ id: 11, title: 'Subtask 1', status: 'Completed', parentId: 1, notes: [] },
					{ id: 12, title: 'Subtask 2', status: 'NotStarted', parentId: 1, notes: [] }
				]
			},
			{ id: 2, title: 'Task 2', status: 'Completed', notes: [] }
		];

		taskManager.updateFromServerEvent('test-chat-1', tasks);
		taskManager.setActiveChat('test-chat-1');

		const stats = get(taskStats);
		expect(stats).toEqual({
			total: 4, // 2 parent tasks + 2 subtasks
			completed: 2, // Task 2 + Subtask 1
			inProgress: 1, // Parent Task
			notStarted: 1, // Subtask 2
			removed: 0
		});
	});

	it('should ignore removed tasks', () => {
		const tasks: TaskItem[] = [
			{ id: 1, title: 'Task 1', status: 'Completed', notes: [] },
			{ id: 2, title: 'Task 2', status: 'Removed', notes: [] },
			{ id: 3, title: 'Task 3', status: 'NotStarted', notes: [] }
		];

		taskManager.updateFromServerEvent('test-chat-1', tasks);
		taskManager.setActiveChat('test-chat-1');

		const stats = get(taskStats);
		expect(stats).toEqual({
			total: 3, // All tasks are counted including removed
			completed: 1,
			inProgress: 0,
			notStarted: 1,
			removed: 1
		});
	});
});
