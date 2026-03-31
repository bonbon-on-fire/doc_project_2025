<script lang="ts">
	import { onMount } from 'svelte';
	import { ChevronDown, ChevronRight, ClipboardList } from 'lucide-svelte';
	import TaskListDisplay from './TaskListDisplay.svelte';
	import { taskManager, currentChatTasks, taskStats } from '$lib/stores/taskManager';
	import type { ChatTaskState } from '$shared/types/tasks';

	interface Props {
		chatId: string;
	}

	let { chatId }: Props = $props();

	let isExpanded = $state(false);

	// Load persisted expansion state
	onMount(() => {
		const stored = localStorage.getItem('taskTrackerExpanded');
		if (stored !== null) {
			isExpanded = stored === 'true';
		}

		// Set active chat when mounted
		if (chatId) {
			taskManager.setActiveChat(chatId);
		}
	});

	// Save expansion state when it changes
	$effect(() => {
		localStorage.setItem('taskTrackerExpanded', String(isExpanded));
	});

	// Update active chat when chat changes
	$effect(() => {
		if (chatId) {
			taskManager.setActiveChat(chatId);
		}
	});

	function toggleExpanded() {
		isExpanded = !isExpanded;
	}

	function getStatusSummary(): string {
		const stats = $taskStats;
		if (stats.total === 0) return 'No tasks';

		const parts = [];
		if (stats.completed > 0) parts.push(`${stats.completed} completed`);
		if (stats.inProgress > 0) parts.push(`${stats.inProgress} in progress`);
		if (stats.notStarted > 0) parts.push(`${stats.notStarted} not started`);

		return `${stats.total} task${stats.total !== 1 ? 's' : ''}: ${parts.join(', ')}`;
	}
</script>

{#if $currentChatTasks && $currentChatTasks.tasks.length > 0}
	<div
		class="pinned-task-tracker border-t border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800"
	>
		<!-- Header (always visible) -->
		<button
			onclick={toggleExpanded}
			class="dark:hover:bg-gray-750 flex w-full items-center justify-between px-4 py-2 transition-colors hover:bg-gray-50"
			aria-expanded={isExpanded}
			aria-controls="task-list"
		>
			<div class="flex items-center gap-2">
				<div class="text-gray-500 dark:text-gray-400">
					{#if isExpanded}
						<ChevronDown size={16} />
					{:else}
						<ChevronRight size={16} />
					{/if}
				</div>
				<ClipboardList size={16} class="text-gray-600 dark:text-gray-400" />
				<span class="text-sm font-medium text-gray-700 dark:text-gray-300"> Tasks </span>
				{#if !isExpanded}
					<span class="text-sm text-gray-600 dark:text-gray-400">
						({getStatusSummary()})
					</span>
				{/if}
			</div>
		</button>

		<!-- Expanded content -->
		{#if isExpanded}
			<div
				id="task-list"
				class="border-t border-gray-200 px-4 py-3 transition-all dark:border-gray-700"
			>
				{#if $currentChatTasks}
					<TaskListDisplay tasks={$currentChatTasks.tasks} variant="full" maxHeight="300px" />
				{:else}
					<div class="py-2 text-center text-sm text-gray-500 dark:text-gray-400">
						Loading tasks...
					</div>
				{/if}

				<!-- Task statistics -->
				<div class="mt-3 border-t border-gray-200 pt-3 dark:border-gray-700">
					<div class="text-xs text-gray-600 dark:text-gray-400">
						{getStatusSummary()}
					</div>
				</div>
			</div>
		{/if}
	</div>
{/if}

<style>
	.pinned-task-tracker {
		box-shadow: 0 1px 2px 0 rgb(0 0 0 / 0.05);
		position: sticky;
		top: 0;
		z-index: 10;
	}

	:global(.dark) .pinned-task-tracker {
		box-shadow:
			0 10px 15px -3px rgb(0 0 0 / 0.1),
			0 4px 6px -4px rgb(0 0 0 / 0.1);
	}
</style>
