import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte';
import { get } from 'svelte/store';
import TaskManagerToolCallRenderer from './TaskManagerToolCallRenderer.svelte';
import { taskManager } from '$lib/stores/taskManager';
import type { ToolCall } from '$shared/types/tools';
import type { TaskItem, TaskOperation } from '$shared/types/tasks';

// Mock the taskManager store
vi.mock('$lib/stores/taskManager', () => {
	const { writable } = require('svelte/store');
	const mockStore = writable({
		tasks: new Map(),
		activeChat: null,
		isLoading: false,
		error: null
	});

	return {
		taskManager: {
			...mockStore,
			updateFromToolCall: vi.fn(),
			setActiveChat: vi.fn()
		}
	};
});

describe('TaskManagerToolCallRenderer', () => {
	const mockChatId = 'test-chat-123';

	beforeEach(() => {
		vi.clearAllMocks();
	});

	describe('Rendering', () => {
		it('should render task manager tool call with basic info', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'add',
					task: {
						id: '1',
						title: 'Test Task',
						status: 'NotStarted'
					}
				},
				result: {
					success: true,
					tasks: [{ id: '1', title: 'Test Task', status: 'NotStarted' }]
				}
			};

			const { container } = render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Check header is rendered
			expect(screen.getByText('Task Manager')).toBeInTheDocument();

			// Check operation type is shown
			expect(container.querySelector('.text-xs.text-gray-500')).toHaveTextContent('Operation: add');
		});

		it('should show task details when expanded', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'update',
					task: {
						id: '1',
						title: 'Updated Task',
						status: 'InProgress',
						description: 'Task description here'
					}
				},
				result: {
					success: true,
					tasks: [
						{
							id: '1',
							title: 'Updated Task',
							status: 'InProgress',
							description: 'Task description here'
						}
					]
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: true
				}
			});

			// Check task details are shown when expanded
			expect(screen.getByText(/Updated Task/)).toBeInTheDocument();
			expect(screen.getByText(/InProgress/)).toBeInTheDocument();
		});

		it('should handle error results', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'add',
					task: {
						id: '1',
						title: 'Failed Task',
						status: 'NotStarted'
					}
				},
				result: {
					success: false,
					error: 'Failed to add task'
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: true
				}
			});

			// Check error is displayed
			expect(screen.getByText(/Failed to add task/)).toBeInTheDocument();
		});
	});

	describe('Task Operations', () => {
		it('should handle add operation', () => {
			const newTask: TaskItem = {
				id: '1',
				title: 'New Task',
				status: 'NotStarted'
			};

			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'add',
					task: newTask
				},
				result: {
					success: true,
					tasks: [newTask]
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Verify the operation was processed
			expect(taskManager.updateFromToolCall).toHaveBeenCalledWith(
				mockChatId,
				expect.objectContaining({
					type: 'add',
					task: newTask
				}),
				[newTask]
			);
		});

		it('should handle update operation', () => {
			const updatedTask: TaskItem = {
				id: '1',
				title: 'Updated Task',
				status: 'Completed'
			};

			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'update',
					taskId: '1',
					updates: {
						status: 'Completed'
					}
				},
				result: {
					success: true,
					tasks: [updatedTask]
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Verify the operation was processed
			expect(taskManager.updateFromToolCall).toHaveBeenCalledWith(
				mockChatId,
				expect.objectContaining({
					type: 'update',
					taskId: '1',
					updates: { status: 'Completed' }
				}),
				[updatedTask]
			);
		});

		it('should handle remove operation', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'remove',
					taskId: '1'
				},
				result: {
					success: true,
					tasks: []
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Verify the operation was processed
			expect(taskManager.updateFromToolCall).toHaveBeenCalledWith(
				mockChatId,
				expect.objectContaining({
					type: 'remove',
					taskId: '1'
				}),
				[]
			);
		});

		it('should handle list operation', () => {
			const tasks: TaskItem[] = [
				{ id: '1', title: 'Task 1', status: 'NotStarted' },
				{ id: '2', title: 'Task 2', status: 'InProgress' }
			];

			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'list'
				},
				result: {
					success: true,
					tasks
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: true
				}
			});

			// Check that task count is displayed
			expect(screen.getByText(/2 tasks/i)).toBeInTheDocument();
		});
	});

	describe('Visual Styling', () => {
		it('should apply correct status colors', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'list'
				},
				result: {
					success: true,
					tasks: [
						{ id: '1', title: 'Not Started', status: 'NotStarted' },
						{ id: '2', title: 'In Progress', status: 'InProgress' },
						{ id: '3', title: 'Completed', status: 'Completed' }
					]
				}
			};

			const { container } = render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: true
				}
			});

			// Check for status-specific styling
			const statusElements = container.querySelectorAll('[class*="bg-"]');
			expect(statusElements.length).toBeGreaterThan(0);
		});

		it('should show appropriate icons for operations', () => {
			const operations = [
				{ type: 'add', icon: '➕' },
				{ type: 'update', icon: '✏️' },
				{ type: 'remove', icon: '🗑️' },
				{ type: 'list', icon: '📋' }
			];

			operations.forEach(({ type, icon }) => {
				const toolCall: ToolCall = {
					id: `tool-${type}`,
					name: 'task_manager',
					arguments: {
						operation: type,
						...(type === 'add' ? { task: { id: '1', title: 'Test', status: 'NotStarted' } } : {}),
						...(type === 'update' || type === 'remove' ? { taskId: '1' } : {})
					},
					result: {
						success: true,
						tasks: []
					}
				};

				const { container } = render(TaskManagerToolCallRenderer, {
					props: {
						toolCall,
						chatId: mockChatId,
						isExpanded: false
					}
				});

				// Check for operation icon
				expect(container.textContent).toContain(icon);
			});
		});
	});

	describe('Edge Cases', () => {
		it('should handle missing result gracefully', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'list'
				}
				// No result property
			};

			const { container } = render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Should still render without crashing
			expect(screen.getByText('Task Manager')).toBeInTheDocument();
		});

		it('should handle malformed arguments', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {} // Missing operation
			};

			const { container } = render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: false
				}
			});

			// Should render with unknown operation
			expect(container.textContent).toContain('Operation: unknown');
		});

		it('should handle empty task list', () => {
			const toolCall: ToolCall = {
				id: 'tool-1',
				name: 'task_manager',
				arguments: {
					operation: 'list'
				},
				result: {
					success: true,
					tasks: []
				}
			};

			render(TaskManagerToolCallRenderer, {
				props: {
					toolCall,
					chatId: mockChatId,
					isExpanded: true
				}
			});

			// Should show empty state
			expect(screen.getByText(/No tasks/i)).toBeInTheDocument();
		});
	});
});
