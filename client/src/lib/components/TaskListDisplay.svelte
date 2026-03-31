<script lang="ts">
	import type { TaskItem } from '$shared/types/tasks';
	import { Square, SquareDot, SquareCheck, SquareX } from 'lucide-svelte';

	interface Props {
		tasks: TaskItem[];
		variant?: 'full' | 'compact';
		maxHeight?: string;
	}

	let { tasks = [], variant = 'full', maxHeight = 'none' }: Props = $props();

	// Get status color class for icons
	function getStatusIconClass(status: string): string {
		// Handle both PascalCase and lowercase status values
		const normalizedStatus = status.charAt(0).toUpperCase() + status.slice(1);
		switch (normalizedStatus) {
			case 'NotStarted':
			case 'Notstarted': // Handle "notStarted" -> "NotStarted"
				return 'text-gray-500';
			case 'InProgress':
			case 'Inprogress': // Handle "inProgress" -> "InProgress"
				return 'text-blue-600';
			case 'Completed':
				return 'text-green-600';
			case 'Removed':
				return 'text-red-500';
			default:
				return 'text-gray-500';
		}
	}

	// Get status color class for text
	function getStatusTextClass(status: string): string {
		// Handle both PascalCase and lowercase status values
		const normalizedStatus = status.charAt(0).toUpperCase() + status.slice(1);
		switch (normalizedStatus) {
			case 'NotStarted':
			case 'Notstarted': // Handle "notStarted" -> "NotStarted"
				return 'text-gray-500';
			case 'InProgress':
			case 'Inprogress': // Handle "inProgress" -> "InProgress"
				return 'text-blue-600';
			case 'Completed':
				return 'text-green-600';
			case 'Removed':
				return 'text-red-500 line-through';
			default:
				return 'text-gray-500';
		}
	}

	// Helper to render a task with proper indentation
	function renderTask(task: TaskItem, depth: number = 0) {
		const hasSubtasks = task.subtasks && task.subtasks.length > 0;
		const hasNotes = task.notes && task.notes.length > 0;
		const indent = depth * 20;
		// Icon size decreases with depth: 18px for main, 16px for subtasks, 14px for deeper
		const iconSize = Math.max(14, 18 - depth * 2);
		return { task, hasSubtasks, hasNotes, indent, depth, iconSize };
	}

	// Flatten tasks for rendering
	function flattenTasks(
		taskList: TaskItem[],
		depth: number = 0
	): Array<ReturnType<typeof renderTask>> {
		const result: Array<ReturnType<typeof renderTask>> = [];
		for (const task of taskList) {
			const taskInfo = renderTask(task, depth);
			result.push(taskInfo);
			if (taskInfo.hasSubtasks && task.subtasks) {
				result.push(...flattenTasks(task.subtasks, depth + 1));
			}
		}
		return result;
	}

	const flatTasks = $derived(flattenTasks(tasks));
</script>

<div
	class="task-list-display"
	style={maxHeight !== 'none' ? `max-height: ${maxHeight}; overflow-y: auto` : ''}
>
	{#if tasks.length === 0}
		<div class="py-4 text-center text-gray-500 dark:text-gray-400">No tasks yet</div>
	{:else}
		<div class="space-y-0.5">
			{#each flatTasks as taskInfo}
				<div
					class="flex items-start gap-2 rounded py-1 hover:bg-gray-50 dark:hover:bg-gray-800"
					style="padding-left: {taskInfo.indent}px"
				>
					<div
						class="mt-0.5 {getStatusIconClass(taskInfo.task.status)}"
						aria-label="Task status: {taskInfo.task.status}"
					>
						{#if taskInfo.task.status === 'NotStarted' || taskInfo.task.status === 'notStarted'}
							<Square size={taskInfo.iconSize} />
						{:else if taskInfo.task.status === 'InProgress' || taskInfo.task.status === 'inProgress'}
							<SquareDot size={taskInfo.iconSize} />
						{:else if taskInfo.task.status === 'Completed' || taskInfo.task.status === 'completed'}
							<SquareCheck size={taskInfo.iconSize} />
						{:else if taskInfo.task.status === 'Removed' || taskInfo.task.status === 'removed'}
							<SquareX size={taskInfo.iconSize} />
						{:else}
							<Square size={taskInfo.iconSize} />
						{/if}
					</div>
					<div class="min-w-0 flex-1">
						<span
							class="{getStatusTextClass(taskInfo.task.status)} {variant === 'compact'
								? 'text-sm'
								: taskInfo.depth === 0
									? 'text-base font-medium'
									: 'text-sm'}"
						>
							{taskInfo.task.title}
						</span>
						{#if variant === 'full' && taskInfo.hasNotes}
							<div class="mt-1 space-y-0.5">
								{#each taskInfo.task.notes as note}
									<div class="pl-4 text-xs text-gray-600 dark:text-gray-400">
										• {note}
									</div>
								{/each}
							</div>
						{/if}
					</div>
				</div>
			{/each}
		</div>
	{/if}
</div>

<style>
	.task-list-display {
		border-radius: 0.5rem;
	}

	.task-list-display::-webkit-scrollbar {
		width: 0.5rem;
	}

	.task-list-display::-webkit-scrollbar-track {
		border-radius: 0.25rem;
		background-color: #f3f4f6;
	}

	:global(.dark) .task-list-display::-webkit-scrollbar-track {
		background-color: #1f2937;
	}

	.task-list-display::-webkit-scrollbar-thumb {
		border-radius: 0.25rem;
		background-color: #9ca3af;
	}

	.task-list-display::-webkit-scrollbar-thumb:hover {
		background-color: #6b7280;
	}

	:global(.dark) .task-list-display::-webkit-scrollbar-thumb {
		background-color: #4b5563;
	}

	:global(.dark) .task-list-display::-webkit-scrollbar-thumb:hover {
		background-color: #6b7280;
	}
</style>
