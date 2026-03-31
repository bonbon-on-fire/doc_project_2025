<script lang="ts">
	import CollapsibleMessageRenderer from '../components/CollapsibleMessageRenderer.svelte';
	import TaskListDisplay from '$lib/components/TaskListDisplay.svelte';
	import type { RichMessageDto } from '$shared/types';
	import type { TaskItem } from '$shared/types/tasks';

	interface Props {
		message: RichMessageDto;
		isStreaming?: boolean;
	}

	let { message, isStreaming = false }: Props = $props();

	// Parse tool call data
	let toolName = $state('');
	let operation = $state('');
	let parameters = $state<any>({});
	let resultTasks = $state<TaskItem[]>([]);
	let error = $state<string | null>(null);

	$effect(() => {
		try {
			if (message.content?.toolCalls?.length > 0) {
				const toolCall = message.content.toolCalls[0];
				toolName = toolCall.name || 'task_manager';

				// Parse function name to get operation type
				const fnName = toolCall.name?.toLowerCase() || '';
				if (fnName.includes('add')) operation = 'add';
				else if (fnName.includes('update')) operation = 'update';
				else if (fnName.includes('delete') || fnName.includes('remove')) operation = 'delete';
				else if (fnName.includes('list')) operation = 'list';
				else if (fnName.includes('clear')) operation = 'clear';
				else operation = 'unknown';

				// Parse parameters
				if (toolCall.arguments) {
					try {
						parameters = JSON.parse(toolCall.arguments);
					} catch {
						parameters = { raw: toolCall.arguments };
					}
				}
			}

			// Parse result to get task state
			if (message.content?.toolResults?.length > 0) {
				const result = message.content.toolResults[0];
				if (result.output) {
					try {
						const parsed = JSON.parse(result.output);
						if (parsed.tasks) {
							resultTasks = parsed.tasks;
						} else if (Array.isArray(parsed)) {
							resultTasks = parsed;
						}
					} catch {
						// Result might be markdown or plain text
						resultTasks = [];
					}
				}
			}
		} catch (e) {
			console.error('Error parsing task manager tool call:', e);
			error = 'Failed to parse tool call data';
		}
	});

	// Generate summary for collapsed view
	function getSummary(): string {
		switch (operation) {
			case 'add':
				return `Added task: ${parameters.title || parameters.task || 'New task'}`;
			case 'update':
				return `Updated task: ${parameters.taskId || parameters.id || 'Task'}`;
			case 'delete':
				return `Removed task: ${parameters.taskId || parameters.id || 'Task'}`;
			case 'list':
				return `Listed ${resultTasks.length} task${resultTasks.length !== 1 ? 's' : ''}`;
			case 'clear':
				return 'Cleared all tasks';
			default:
				return 'Task operation';
		}
	}

	// Format parameters for display
	function formatParameters(): string {
		const lines: string[] = [];

		for (const [key, value] of Object.entries(parameters)) {
			if (key === 'raw') {
				lines.push(`arguments: ${value}`);
			} else {
				const displayValue =
					typeof value === 'object' ? JSON.stringify(value, null, 2) : String(value);
				lines.push(`${key}: ${displayValue}`);
			}
		}

		return lines.join('\n');
	}
</script>

<CollapsibleMessageRenderer
	title="Task Manager"
	summary={getSummary()}
	icon="📋"
	defaultExpanded={false}
	{isStreaming}
>
	<div class="space-y-3">
		{#if error}
			<div class="text-sm text-red-500">{error}</div>
		{/if}

		<!-- Operation Details -->
		<div class="space-y-2">
			<div class="text-sm font-medium text-gray-700 dark:text-gray-300">
				Operation: {operation}
			</div>

			{#if Object.keys(parameters).length > 0}
				<div class="rounded-md bg-gray-50 p-3 dark:bg-gray-800">
					<div class="mb-1 font-mono text-xs text-gray-600 dark:text-gray-400">Parameters:</div>
					<pre
						class="font-mono text-xs whitespace-pre-wrap text-gray-700 dark:text-gray-300">{formatParameters()}</pre>
				</div>
			{/if}
		</div>

		<!-- Result Tasks -->
		{#if resultTasks.length > 0}
			<div class="border-t pt-3">
				<div class="mb-2 text-sm font-medium text-gray-700 dark:text-gray-300">Current Tasks:</div>
				<div
					class="rounded-md border border-gray-200 bg-white p-3 dark:border-gray-700 dark:bg-gray-900"
				>
					<TaskListDisplay tasks={resultTasks} variant="full" maxHeight="300px" />
				</div>
			</div>
		{/if}

		<!-- Raw result for other output -->
		{#if message.content?.toolResults?.length > 0 && !resultTasks.length}
			<div class="border-t pt-3">
				<div class="mb-1 font-mono text-xs text-gray-600 dark:text-gray-400">Result:</div>
				<pre
					class="rounded-md bg-gray-50 p-3 font-mono text-xs whitespace-pre-wrap text-gray-700 dark:bg-gray-800 dark:text-gray-300">{message
						.content.toolResults[0].output || '(empty)'}</pre>
			</div>
		{/if}
	</div>
</CollapsibleMessageRenderer>
