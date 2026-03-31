import { writable, derived, get } from 'svelte/store';
import type {
	ChatTaskState,
	TaskItem,
	TaskOperation,
	BulkTaskItem,
	TaskStatus
} from '$shared/types/tasks';

interface TaskManagerState {
	tasks: Map<string, ChatTaskState>; // chatId -> task state
	activeChat: string | null;
	isLoading: boolean;
	error: string | null;
	nextIds: Map<string, number>; // chatId -> next task ID counter
}

// Helper functions matching C# implementation
function getStatusSymbol(status: TaskStatus): string {
	switch (status) {
		case 'NotStarted':
			return '[ ]';
		case 'InProgress':
			return '[-]';
		case 'Completed':
			return '[x]';
		case 'Removed':
			return '[d]';
		default:
			return '[ ]';
	}
}

function tryParseStatus(input: string | undefined): TaskStatus | null {
	if (!input) return null;

	const normalized = input.trim().toLowerCase();
	switch (normalized) {
		case 'not started':
		case 'not_started':
		case 'notstarted':
		case 'todo':
		case 'to do':
		case 'pending':
			return 'NotStarted';
		case 'in progress':
		case 'in_progress':
		case 'inprogress':
		case 'doing':
			return 'InProgress';
		case 'completed':
		case 'done':
		case 'complete':
			return 'Completed';
		case 'removed':
		case 'deleted':
		case 'remove':
		case 'delete':
			return 'Removed';
		default:
			return null;
	}
}

function normalizeStatusText(status: TaskStatus): string {
	switch (status) {
		case 'NotStarted':
			return 'not started';
		case 'InProgress':
			return 'in progress';
		case 'Completed':
			return 'completed';
		case 'Removed':
			return 'removed';
		default:
			return 'not started';
	}
}

function createTaskManagerStore() {
	const { subscribe, set, update } = writable<TaskManagerState>({
		tasks: new Map(),
		activeChat: null,
		isLoading: false,
		error: null,
		nextIds: new Map()
	});

	// Helper to get next ID for a chat
	function getNextId(state: TaskManagerState, chatId: string): number {
		const currentId = state.nextIds.get(chatId) || 1;
		state.nextIds.set(chatId, currentId + 1);
		return currentId;
	}

	// Helper to find task by ID
	function findTaskById(tasks: TaskItem[], taskId: number): TaskItem | null {
		for (const task of tasks) {
			if (task.id === taskId) return task;
			if (task.subtasks) {
				const found = findTaskById(task.subtasks, taskId);
				if (found) return found;
			}
		}
		return null;
	}

	// Helper to find parent task
	function findParentTask(tasks: TaskItem[], subtaskId: number): TaskItem | null {
		for (const task of tasks) {
			if (task.subtasks) {
				const found = task.subtasks.find((st: TaskItem) => st.id === subtaskId);
				if (found) return task;
			}
		}
		return null;
	}

	// Helper to normalize task data from SSE (converts C# PascalCase to JavaScript camelCase)
	function normalizeTaskFromSSE(task: any): TaskItem {
		return {
			id: typeof task.Id === 'string' ? parseInt(task.Id, 10) : task.Id || task.id,
			title: (task.Title || task.title || '').replace(/\r/g, '').trim(),
			status: task.Status || task.status,
			parentId:
				typeof task.ParentId === 'string'
					? parseInt(task.ParentId, 10)
					: task.ParentId || task.parentId,
			// Handle various casings: Subtasks, subtasks, subTasks
			subtasks:
				task.Subtasks || task.subtasks || task.subTasks
					? (task.Subtasks || task.subtasks || task.subTasks).map(normalizeTaskFromSSE)
					: [],
			notes: task.Notes || task.notes || []
		};
	}

	return {
		subscribe,

		// Initialize tasks for a chat (from initial chat data or SSE updates)
		initializeTasks: (chatId: string, tasks?: TaskItem[] | any) => {
			update((state) => {
				// Handle various input formats
				let taskItems: TaskItem[] = [];

				if (tasks) {
					if (Array.isArray(tasks)) {
						taskItems = tasks;
					} else if (typeof tasks === 'object') {
						// Handle case where tasks might be wrapped in an object
						if (tasks.tasks && Array.isArray(tasks.tasks)) {
							taskItems = tasks.tasks;
						} else if (tasks.value && Array.isArray(tasks.value)) {
							// Handle JsonElement format from server
							taskItems = tasks.value;
						} else {
							// Try to extract array from object values
							const values = Object.values(tasks);
							if (values.length > 0 && Array.isArray(values[0])) {
								taskItems = values[0] as TaskItem[];
							}
						}
					}
				}

				// Normalize task field names from C# PascalCase to JavaScript camelCase
				taskItems = taskItems.map(normalizeTaskFromSSE);

				const taskState: ChatTaskState = {
					chatId,
					tasks: taskItems,
					version: 0,
					lastUpdatedUtc: new Date().toISOString()
				};

				// Calculate next ID based on existing tasks
				let maxId = 0;
				const findMaxId = (taskList: TaskItem[]) => {
					if (!taskList || !Array.isArray(taskList)) return;
					for (const task of taskList) {
						if (task.id > maxId) maxId = task.id;
						if (task.subtasks) findMaxId(task.subtasks);
					}
				};
				findMaxId(taskItems);

				state.tasks.set(chatId, taskState);
				state.nextIds.set(chatId, maxId + 1);
				return { ...state, activeChat: chatId };
			});
		},

		// Add a new task or subtask
		addTask: (chatId: string, title: string, parentId?: number): string => {
			let result = '';

			update((state) => {
				const taskState = state.tasks.get(chatId);
				if (!taskState) {
					result = 'Error: Chat tasks not initialized.';
					return state;
				}

				if (!title || title.trim().length === 0) {
					result = 'Error: Title cannot be empty.';
					return state;
				}

				const newId = getNextId(state, chatId);
				const newTask: TaskItem = {
					id: newId,
					title: title.trim(),
					status: 'NotStarted',
					parentId,
					notes: []
				};

				if (parentId === undefined) {
					// Add as root task
					newTask.subtasks = [];
					taskState.tasks.push(newTask);
					result = `Added task ${newId}: ${newTask.title}`;
				} else {
					// Add as subtask
					const parent = findTaskById(taskState.tasks, parentId);
					if (!parent) {
						result = `Error: Parent task ${parentId} not found.`;
						state.nextIds.set(chatId, newId); // Reset ID counter
						return state;
					}

					if (parent.parentId !== undefined) {
						result = `Error: Only two levels supported. Task ${parent.id} is already a subtask.`;
						state.nextIds.set(chatId, newId); // Reset ID counter
						return state;
					}

					if (!parent.subtasks) parent.subtasks = [];
					parent.subtasks.push(newTask);
					result = `Added subtask ${newId} under task ${parent.id}: ${newTask.title}`;
				}

				taskState.version++;
				taskState.lastUpdatedUtc = new Date().toISOString();
				return { ...state };
			});

			return result;
		},

		// Bulk initialize tasks
		bulkInitialize: (
			chatId: string,
			tasks: BulkTaskItem[],
			clearExisting: boolean = false
		): string => {
			let result = '';

			update((state) => {
				if (!tasks || tasks.length === 0) {
					result = 'Error: No tasks provided for initialization.';
					return state;
				}

				let taskState = state.tasks.get(chatId);
				if (!taskState || clearExisting) {
					taskState = {
						chatId,
						tasks: [],
						version: 0,
						lastUpdatedUtc: new Date().toISOString()
					};
					state.tasks.set(chatId, taskState);
					state.nextIds.set(chatId, 1);
				}

				if (clearExisting) {
					taskState.tasks = [];
					state.nextIds.set(chatId, 1);
				}

				const results: string[] = [];
				if (clearExisting) {
					results.push('Cleared existing tasks.');
				}

				const addedTasks: string[] = [];

				for (const bulkItem of tasks) {
					if (!bulkItem.task || bulkItem.task.trim().length === 0) {
						continue; // Silent skip for empty tasks
					}

					const mainTaskId = getNextId(state, chatId);
					const mainTask: TaskItem = {
						id: mainTaskId,
						title: bulkItem.task.trim(),
						status: 'NotStarted',
						subtasks: [],
						notes: []
					};

					// Add notes to main task
					if (bulkItem.notes) {
						for (const note of bulkItem.notes) {
							if (note && note.trim().length > 0) {
								mainTask.notes!.push(note.trim());
							}
						}
					}

					// Add subtasks
					if (bulkItem.subTasks) {
						for (const subTaskTitle of bulkItem.subTasks) {
							if (!subTaskTitle || subTaskTitle.trim().length === 0) {
								continue; // Silent skip for empty subtasks
							}

							const subTaskId = getNextId(state, chatId);
							const subTask: TaskItem = {
								id: subTaskId,
								title: subTaskTitle.trim(),
								status: 'NotStarted',
								parentId: mainTaskId,
								notes: []
							};
							mainTask.subtasks!.push(subTask);
						}
					}

					taskState.tasks.push(mainTask);
					addedTasks.push(`Task ${mainTaskId}: ${mainTask.title}`);
				}

				if (addedTasks.length > 0) {
					results.push(`Added ${addedTasks.length} task(s):`);
					for (const task of addedTasks) {
						results.push(`  - ${task}`);
					}
				}

				taskState.version++;
				taskState.lastUpdatedUtc = new Date().toISOString();
				result = results.join('\n');
				return { ...state };
			});

			return result;
		},

		// Update task status
		updateTaskStatus: (
			chatId: string,
			taskId: number,
			subtaskId: number | undefined,
			status: string
		): string => {
			let result = '';

			update((state) => {
				const taskState = state.tasks.get(chatId);
				if (!taskState) {
					result = 'Error: Chat tasks not initialized.';
					return state;
				}

				let targetTask: TaskItem | null = null;
				let taskRef = '';

				if (subtaskId !== undefined) {
					// Find subtask
					const parent = findTaskById(taskState.tasks, taskId);
					if (!parent) {
						result = `Error: Parent task ${taskId} not found.`;
						return state;
					}

					targetTask = parent.subtasks?.find((st: TaskItem) => st.id === subtaskId) || null;
					if (!targetTask) {
						result = `Error: Subtask ${subtaskId} not found under task ${taskId}.`;
						return state;
					}
					taskRef = `subtask ${subtaskId} of task ${taskId}`;
				} else {
					// Find main task
					targetTask = findTaskById(taskState.tasks, taskId);
					if (!targetTask) {
						result = `Error: Task ${taskId} not found.`;
						return state;
					}
					taskRef = `task ${taskId}`;
				}

				const newStatus = tryParseStatus(status);
				if (!newStatus) {
					result = 'Error: Invalid status. Use: not started, in progress, completed, removed.';
					return state;
				}

				targetTask.status = newStatus;
				taskState.version++;
				taskState.lastUpdatedUtc = new Date().toISOString();
				result = `Updated ${taskRef} status to '${normalizeStatusText(newStatus)}'.`;
				return { ...state };
			});

			return result;
		},

		// Delete task or subtask
		deleteTask: (chatId: string, taskId: number, subtaskId?: number): string => {
			let result = '';

			update((state) => {
				const taskState = state.tasks.get(chatId);
				if (!taskState) {
					result = 'Error: Chat tasks not initialized.';
					return state;
				}

				if (subtaskId !== undefined) {
					// Delete subtask
					const parent = findTaskById(taskState.tasks, taskId);
					if (!parent) {
						result = `Error: Parent task ${taskId} not found.`;
						return state;
					}

					const subtaskIndex =
						parent.subtasks?.findIndex((st: TaskItem) => st.id === subtaskId) ?? -1;
					if (subtaskIndex === -1) {
						result = `Error: Subtask ${subtaskId} not found under task ${taskId}.`;
						return state;
					}

					const deletedSubtask = parent.subtasks![subtaskIndex];
					parent.subtasks!.splice(subtaskIndex, 1);
					result = `Deleted subtask ${subtaskId} from task ${taskId}: ${deletedSubtask.title}`;
				} else {
					// Delete main task
					const taskIndex = taskState.tasks.findIndex((t: TaskItem) => t.id === taskId);
					if (taskIndex === -1) {
						result = `Error: Task ${taskId} not found.`;
						return state;
					}

					const deletedTask = taskState.tasks[taskIndex];
					taskState.tasks.splice(taskIndex, 1);
					result = `Deleted task ${taskId} and all subtasks: ${deletedTask.title}`;
				}

				taskState.version++;
				taskState.lastUpdatedUtc = new Date().toISOString();
				return { ...state };
			});

			return result;
		},

		// Manage notes (add/edit/delete)
		manageNotes: (
			chatId: string,
			taskId: number,
			subtaskId: number | undefined,
			action: 'add' | 'edit' | 'delete',
			noteText?: string,
			noteIndex?: number
		): string => {
			let result = '';

			update((state) => {
				const taskState = state.tasks.get(chatId);
				if (!taskState) {
					result = 'Error: Chat tasks not initialized.';
					return state;
				}

				let targetTask: TaskItem | null = null;
				let taskRef = '';

				if (subtaskId !== undefined) {
					// Find subtask
					const parent = findTaskById(taskState.tasks, taskId);
					if (!parent) {
						result = `Error: Parent task ${taskId} not found.`;
						return state;
					}

					targetTask = parent.subtasks?.find((st: TaskItem) => st.id === subtaskId) || null;
					if (!targetTask) {
						result = `Error: Subtask ${subtaskId} not found under task ${taskId}.`;
						return state;
					}
					taskRef = `subtask ${subtaskId} of task ${taskId}`;
				} else {
					// Find main task
					targetTask = findTaskById(taskState.tasks, taskId);
					if (!targetTask) {
						result = `Error: Task ${taskId} not found.`;
						return state;
					}
					taskRef = `task ${taskId}`;
				}

				if (!targetTask.notes) targetTask.notes = [];

				switch (action) {
					case 'add':
						if (!noteText || noteText.trim().length === 0) {
							result = 'Error: Note text required for add action.';
							return state;
						}
						targetTask.notes.push(noteText.trim());
						result = `Added note to ${taskRef}.`;
						break;

					case 'edit':
						if (!noteIndex || !noteText || noteText.trim().length === 0) {
							result = 'Error: Note index and new text required for edit action.';
							return state;
						}
						if (noteIndex < 1 || noteIndex > targetTask.notes.length) {
							result = `Error: Note index ${noteIndex} out of range (1-${targetTask.notes.length}).`;
							return state;
						}
						targetTask.notes[noteIndex - 1] = noteText.trim();
						result = `Edited note ${noteIndex} on ${taskRef}.`;
						break;

					case 'delete':
						if (!noteIndex) {
							result = 'Error: Note index required for delete action.';
							return state;
						}
						if (noteIndex < 1 || noteIndex > targetTask.notes.length) {
							result = `Error: Note index ${noteIndex} out of range (1-${targetTask.notes.length}).`;
							return state;
						}
						targetTask.notes.splice(noteIndex - 1, 1);
						result = `Deleted note ${noteIndex} from ${taskRef}.`;
						break;

					default:
						result = 'Error: Invalid action. Use: add, edit, delete.';
						return state;
				}

				taskState.version++;
				taskState.lastUpdatedUtc = new Date().toISOString();
				return { ...state };
			});

			return result;
		},

		// Get task details
		getTask: (chatId: string, taskId: number, subtaskId?: number): string => {
			const state = get(taskManager);
			const taskState = state.tasks.get(chatId);

			if (!taskState) {
				return 'Error: Chat tasks not initialized.';
			}

			let targetTask: TaskItem | null = null;
			let taskRef = '';

			if (subtaskId !== undefined) {
				// Find subtask
				const parent = findTaskById(taskState.tasks, taskId);
				if (!parent) {
					return `Error: Parent task ${taskId} not found.`;
				}

				targetTask = parent.subtasks?.find((st: TaskItem) => st.id === subtaskId) || null;
				if (!targetTask) {
					return `Error: Subtask ${subtaskId} not found under task ${taskId}.`;
				}
				taskRef = `Subtask ${subtaskId} of task ${taskId}`;
			} else {
				// Find main task
				targetTask = findTaskById(taskState.tasks, taskId);
				if (!targetTask) {
					return `Error: Task ${taskId} not found.`;
				}
				taskRef = `Task ${taskId}`;
			}

			const lines: string[] = [];
			lines.push(`${taskRef}: ${targetTask.title}`);
			lines.push(`Status: ${normalizeStatusText(targetTask.status)}`);

			if (targetTask.notes && targetTask.notes.length > 0) {
				lines.push(`Notes (${targetTask.notes.length}):`);
				targetTask.notes.forEach((note: string, i: number) => {
					lines.push(`  ${i + 1}. ${note}`);
				});
			}

			if (targetTask.subtasks && targetTask.subtasks.length > 0) {
				lines.push(`Subtasks (${targetTask.subtasks.length}):`);
				targetTask.subtasks.forEach((subtask: TaskItem) => {
					const symbol = getStatusSymbol(subtask.status);
					lines.push(`  ${symbol} ${subtask.id}. ${subtask.title}`);
				});
			}

			return lines.join('\n');
		},

		// List tasks with optional filters
		listTasks: (chatId: string, filterStatus?: string, mainOnly: boolean = false): string => {
			const state = get(taskManager);
			const taskState = state.tasks.get(chatId);

			if (!taskState || taskState.tasks.length === 0) {
				return 'No tasks found.';
			}

			let statusFilter: TaskStatus | null = null;
			if (filterStatus) {
				statusFilter = tryParseStatus(filterStatus);
				if (!statusFilter) {
					return 'Error: Invalid status filter. Use: not started, in progress, completed, removed.';
				}
			}

			const lines: string[] = ['# TODO'];

			function appendTask(task: TaskItem, level: number) {
				if (statusFilter && task.status !== statusFilter) return;

				const indent = '  '.repeat(level);
				const symbol = getStatusSymbol(task.status);
				const removedSuffix = task.status === 'Removed' ? ' (removed)' : '';
				lines.push(`${indent}- ${symbol} ${task.id}. ${task.title}${removedSuffix}`);

				if (task.notes && task.notes.length > 0) {
					lines.push(`${indent}  Notes:`);
					task.notes.forEach((note: string, i: number) => {
						lines.push(`${indent}  - ${i + 1} ${note}`);
					});
				}

				if (!mainOnly && task.subtasks) {
					task.subtasks.forEach((subtask: TaskItem) => {
						appendTask(subtask, level + 1);
					});
				}
			}

			taskState.tasks.forEach((task: TaskItem) => {
				appendTask(task, 0);
			});

			const result = lines.join('\n');
			return result === '# TODO' ? 'No tasks match the specified criteria.' : result;
		},

		// Search tasks by title or get counts
		searchTasks: (chatId: string, searchTerm?: string, countType?: string): string => {
			const state = get(taskManager);
			const taskState = state.tasks.get(chatId);

			if (!taskState) {
				return 'Error: Chat tasks not initialized.';
			}

			if (countType) {
				let total = 0,
					completed = 0,
					pending = 0,
					removed = 0;

				function countTasksRecursive(tasks: TaskItem[]) {
					for (const task of tasks) {
						total++;
						switch (task.status) {
							case 'Completed':
								completed++;
								break;
							case 'NotStarted':
							case 'InProgress':
								pending++;
								break;
							case 'Removed':
								removed++;
								break;
						}
						if (task.subtasks) countTasksRecursive(task.subtasks);
					}
				}

				countTasksRecursive(taskState.tasks);

				switch (countType.toLowerCase()) {
					case 'total':
						return `Total tasks: ${total}`;
					case 'completed':
						return `Completed tasks: ${completed}`;
					case 'pending':
						return `Pending tasks: ${pending}`;
					case 'removed':
						return `Removed tasks: ${removed}`;
					default:
						return `Task counts - Total: ${total}, Completed: ${completed}, Pending: ${pending}, Removed: ${removed}`;
				}
			}

			if (!searchTerm || searchTerm.trim().length === 0) {
				return 'Error: Provide searchTerm or countType.';
			}

			const matches: Array<{ task: TaskItem; path: string }> = [];

			function searchRecursive(task: TaskItem, path: string) {
				if (task.title.toLowerCase().includes(searchTerm!.toLowerCase())) {
					matches.push({ task, path });
				}
				if (task.subtasks) {
					task.subtasks.forEach((subtask: TaskItem) => {
						searchRecursive(subtask, `${path}.${subtask.id}`);
					});
				}
			}

			taskState.tasks.forEach((task: TaskItem) => {
				searchRecursive(task, task.id.toString());
			});

			if (matches.length === 0) {
				return `No tasks found matching '${searchTerm}'.`;
			}

			const lines: string[] = [`Found ${matches.length} task(s) matching '${searchTerm}':`];
			matches.forEach(({ task, path }) => {
				const symbol = getStatusSymbol(task.status);
				lines.push(`- ${symbol} ${path}: ${task.title}`);
			});

			return lines.join('\n');
		},

		// Get markdown representation
		getMarkdown: (chatId: string): string => {
			return taskManager.listTasks(chatId);
		},

		// Update from SSE task update event
		updateFromSSE: (chatId: string, taskState: any) => {
			update((state) => {
				// Parse tasks from the SSE payload (could be JsonElement or parsed array)
				let tasks: TaskItem[] = [];

				if (taskState) {
					if (Array.isArray(taskState)) {
						tasks = taskState;
					} else if (typeof taskState === 'object') {
						// Handle case where taskState might be wrapped or in a different format
						if (taskState.tasks && Array.isArray(taskState.tasks)) {
							tasks = taskState.tasks;
						} else if (taskState.value) {
							// Handle JsonElement format
							if (Array.isArray(taskState.value)) {
								tasks = taskState.value;
							} else if (typeof taskState.value === 'string') {
								// Try to parse if it's a JSON string
								try {
									const parsed = JSON.parse(taskState.value);
									if (Array.isArray(parsed)) {
										tasks = parsed;
									}
								} catch {
									// Ignore parse errors
								}
							}
						} else if (taskState.Tasks && Array.isArray(taskState.Tasks)) {
							// Handle capitalized property name from C#
							tasks = taskState.Tasks;
						} else {
							// Try to extract array from object values
							const values = Object.values(taskState);
							if (values.length > 0 && Array.isArray(values[0])) {
								tasks = values[0] as TaskItem[];
							}
						}
					}
				}

				// Normalize task field names from C# PascalCase to JavaScript camelCase
				tasks = tasks.map(normalizeTaskFromSSE);

				const existing = state.tasks.get(chatId);
				const chatTaskState: ChatTaskState = {
					chatId,
					tasks,
					version: (existing?.version ?? 0) + 1,
					lastUpdatedUtc: new Date().toISOString()
				};
				state.tasks.set(chatId, chatTaskState);

				// Update next ID counter
				let maxId = 0;
				const findMaxId = (taskList: TaskItem[]) => {
					if (!taskList || !Array.isArray(taskList)) return;
					for (const task of taskList) {
						if (task.id > maxId) maxId = task.id;
						if (task.subtasks) findMaxId(task.subtasks);
					}
				};
				findMaxId(tasks);
				state.nextIds.set(chatId, maxId + 1);

				return { ...state };
			});
		},

		// Update from tool call operation
		updateFromToolCall: (chatId: string, operation: TaskOperation, resultTasks?: TaskItem[]) => {
			update((state) => {
				// If we have the full task state from the result, use it
				if (resultTasks) {
					const taskState: ChatTaskState = {
						chatId,
						tasks: resultTasks,
						version: (state.tasks.get(chatId)?.version ?? 0) + 1,
						lastUpdatedUtc: new Date().toISOString()
					};
					state.tasks.set(chatId, taskState);

					// Update next ID counter
					let maxId = 0;
					const findMaxId = (taskList: TaskItem[]) => {
						for (const task of taskList) {
							if (task.id > maxId) maxId = task.id;
							if (task.subtasks) findMaxId(task.subtasks);
						}
					};
					findMaxId(resultTasks);
					state.nextIds.set(chatId, maxId + 1);
				}
				return { ...state };
			});
		},

		// Clear tasks for a chat
		clearTasks: (chatId: string) => {
			update((state) => {
				state.tasks.delete(chatId);
				state.nextIds.delete(chatId);
				return { ...state };
			});
		},

		// Set active chat
		setActiveChat: (chatId: string | null) => {
			update((state) => ({ ...state, activeChat: chatId }));
		},

		// Set loading state
		setLoading: (chatId: string, isLoading: boolean) => {
			update((state) => ({
				...state,
				isLoading,
				activeChat: isLoading ? chatId : state.activeChat
			}));
		},

		// Clear error
		clearError: () => {
			update((state) => ({ ...state, error: null }));
		}
	};
}

export const taskManager = createTaskManagerStore();

// Derived store for current chat tasks
export const currentChatTasks = derived(taskManager, ($taskManager) => {
	if (!$taskManager.activeChat) return null;
	return $taskManager.tasks.get($taskManager.activeChat);
});

// Derived store for task statistics
export const taskStats = derived(currentChatTasks, ($tasks) => {
	if (!$tasks || !$tasks.tasks) {
		return { total: 0, completed: 0, inProgress: 0, notStarted: 0, removed: 0 };
	}

	const countTasks = (tasks: TaskItem[]): any => {
		let stats = { total: 0, completed: 0, inProgress: 0, notStarted: 0, removed: 0 };

		for (const task of tasks) {
			stats.total++;

			switch (task.status) {
				case 'Completed':
					stats.completed++;
					break;
				case 'InProgress':
					stats.inProgress++;
					break;
				case 'NotStarted':
					stats.notStarted++;
					break;
				case 'Removed':
					stats.removed++;
					break;
			}

			// Count subtasks recursively
			if (task.subtasks && task.subtasks.length > 0) {
				const subStats = countTasks(task.subtasks);
				stats.total += subStats.total;
				stats.completed += subStats.completed;
				stats.inProgress += subStats.inProgress;
				stats.notStarted += subStats.notStarted;
				stats.removed += subStats.removed;
			}
		}

		return stats;
	};

	return countTasks($tasks.tasks);
});

// Derived store for filtered tasks
export const filteredTasks = derived([currentChatTasks, taskManager], ([$tasks, $manager]) => {
	if (!$tasks || !$manager.activeChat) return [];

	// This can be extended to support various filters
	return $tasks.tasks;
});
