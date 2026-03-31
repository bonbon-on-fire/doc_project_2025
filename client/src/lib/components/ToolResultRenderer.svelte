<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import CollapsibleMessageRenderer from '$lib/components/CollapsibleMessageRenderer.svelte';
	import type { RichMessageDto } from '$lib/types';
	import type { MessageRenderer } from '$lib/types/renderer';

	// Component props
	export let message: any & RichMessageDto;
	export let isLatest: boolean = false;
	export let isLastAssistantMessage: boolean = false;
	export let expanded: boolean = true;
	export let renderPhase: 'initial' | 'streaming' | 'complete' = 'initial';

	const dispatch = createEventDispatcher<{
		stateChange: { expanded: boolean };
		toggleExpansion: { expanded: boolean };
	}>();

	// Component implements MessageRenderer interface
	const rendererInterface: MessageRenderer<any> = {
		messageType: 'tool_result'
	};

	// Extract tool result from message
	function getToolResult(msg: any): {
		toolName: string;
		toolId?: string;
		result: any;
		success: boolean;
		error?: string;
	} {
		// Try different possible formats
		if (msg.toolName || msg.tool_name) {
			return {
				toolName: msg.toolName || msg.tool_name,
				toolId: msg.toolId || msg.tool_id,
				result: msg.result || msg.content || '',
				success: msg.success !== false && !msg.error,
				error: msg.error
			};
		}

		// Try parsing from content if it's a string
		if (msg.content && typeof msg.content === 'string') {
			try {
				const parsed = JSON.parse(msg.content);
				return {
					toolName: parsed.toolName || parsed.tool_name || 'unknown',
					toolId: parsed.toolId || parsed.tool_id,
					result: parsed.result || parsed.content || parsed,
					success: parsed.success !== false && !parsed.error,
					error: parsed.error
				};
			} catch {
				return {
					toolName: 'unknown',
					result: msg.content,
					success: true
				};
			}
		}

		return {
			toolName: 'unknown',
			result: 'No result data',
			success: false
		};
	}

	// Format result data with syntax highlighting
	function formatResult(result: any): string {
		if (result === null) return '<span class="text-gray-500">null</span>';
		if (result === undefined) return '<span class="text-gray-500">undefined</span>';

		if (typeof result === 'string') {
			// Simple string - just return as-is with subtle styling
			return `<span class="text-gray-800 dark:text-gray-200">${result}</span>`;
		}

		if (typeof result === 'number') {
			return `<span class="text-blue-600 dark:text-blue-400">${result}</span>`;
		}

		if (typeof result === 'boolean') {
			return `<span class="text-purple-600 dark:text-purple-400">${result}</span>`;
		}

		// For objects/arrays, format as JSON with basic highlighting
		try {
			const formatted = JSON.stringify(result, null, 2);
			return `<span class="text-gray-800 dark:text-gray-200">${formatted}</span>`;
		} catch {
			return `<span class="text-gray-800 dark:text-gray-200">${String(result)}</span>`;
		}
	}

	// Create collapsed preview
	function createCollapsedPreview(toolResult: any): string {
		const resultText =
			typeof toolResult.result === 'string'
				? toolResult.result.substring(0, 50) + (toolResult.result.length > 50 ? '...' : '')
				: 'Result data';

		const status = toolResult.success ? '✓' : '✗';
		return `${status} ${toolResult.toolName}: ${resultText}`;
	}

	$: toolResult = getToolResult(message);
	$: collapsedPreview = createCollapsedPreview(toolResult);

	// Forward child events from CollapsibleMessageRenderer
	function forwardStateChange(e: CustomEvent<{ expanded: boolean }>) {
		expanded = e.detail.expanded;
		dispatch('stateChange', e.detail);
	}
	function forwardToggle(e: CustomEvent<{ expanded: boolean }>) {
		dispatch('toggleExpansion', e.detail);
	}
</script>

<!-- Tool result message using reusable collapsible renderer -->
<div data-component="tool-result-renderer" data-testid="tool-result-renderer">
	<CollapsibleMessageRenderer
		{message}
		{isLatest}
		{expanded}
		collapsible={true}
		iconPath={toolResult.success
			? 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z'
			: 'M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z'}
		iconColors={toolResult.success ? 'from-green-500 to-emerald-600' : 'from-red-500 to-rose-600'}
		messageType="Tool Result"
		{collapsedPreview}
		borderColor={toolResult.success ? 'border-green-200' : 'border-red-200'}
		bgColor={toolResult.success ? 'bg-green-50' : 'bg-red-50'}
		textColor={toolResult.success ? 'text-green-900' : 'text-red-900'}
		darkBorderColor={toolResult.success ? 'dark:border-green-700' : 'dark:border-red-700'}
		darkBgColor={toolResult.success ? 'dark:bg-green-900/20' : 'dark:bg-red-900/20'}
		darkTextColor={toolResult.success ? 'dark:text-green-100' : 'dark:text-red-100'}
		on:stateChange={forwardStateChange}
		on:toggleExpansion={forwardToggle}
	>
		<!-- Tool result content -->
		<div class="space-y-3">
			<div
				class="rounded-lg border p-4 {toolResult.success
					? 'border-green-300 bg-white dark:border-green-600 dark:bg-green-800/30'
					: 'border-red-300 bg-white dark:border-red-600 dark:bg-red-800/30'}"
				data-testid="tool-result-item"
				data-tool-name={toolResult.toolName}
				data-success={toolResult.success}
			>
				<!-- Tool result header -->
				<div class="mb-3 flex items-center justify-between">
					<div class="flex items-center space-x-2">
						<svg
							class="h-5 w-5 {toolResult.success
								? 'text-green-600 dark:text-green-400'
								: 'text-red-600 dark:text-red-400'}"
							fill="none"
							stroke="currentColor"
							viewBox="0 0 24 24"
						>
							{#if toolResult.success}
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"
								></path>
							{:else}
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z"
								></path>
							{/if}
						</svg>
						<span
							class="font-semibold {toolResult.success
								? 'text-green-800 dark:text-green-200'
								: 'text-red-800 dark:text-red-200'}"
							data-testid="tool-result-name"
						>
							{toolResult.toolName}
						</span>
						{#if toolResult.toolId}
							<span
								class="text-xs {toolResult.success
									? 'text-green-600 dark:text-green-400'
									: 'text-red-600 dark:text-red-400'}"
								data-testid="tool-result-id"
							>
								#{toolResult.toolId}
							</span>
						{/if}
					</div>
					<div class="flex items-center space-x-2">
						<span
							class="text-xs {toolResult.success
								? 'text-green-600 dark:text-green-400'
								: 'text-red-600 dark:text-red-400'}"
						>
							{toolResult.success ? 'Success' : 'Error'}
						</span>
					</div>
				</div>

				<!-- Error message if failed -->
				{#if !toolResult.success && toolResult.error}
					<div class="mb-3 rounded bg-red-100 p-2 dark:bg-red-900/30">
						<div class="mb-1 text-xs font-medium text-red-700 dark:text-red-300">Error:</div>
						<div class="text-sm text-red-800 dark:text-red-200" data-testid="tool-result-error">
							{toolResult.error}
						</div>
					</div>
				{/if}

				<!-- Tool result data -->
				<div class="mt-2">
					<div
						class="mb-1 text-xs font-medium {toolResult.success
							? 'text-green-700 dark:text-green-300'
							: 'text-red-700 dark:text-red-300'}"
					>
						Result:
					</div>
					<div
						class="rounded bg-gray-50 p-3 text-sm dark:bg-gray-800"
						data-testid="tool-result-content"
					>
						<pre class="font-mono whitespace-pre-wrap">{@html formatResult(toolResult.result)}</pre>
					</div>
				</div>

				<!-- Show streaming indicator if this is the latest and streaming -->
				{#if isLatest && renderPhase === 'streaming'}
					<div
						class="mt-2 flex items-center space-x-2 text-xs {toolResult.success
							? 'text-green-600 dark:text-green-400'
							: 'text-red-600 dark:text-red-400'}"
					>
						<span class="animate-pulse">▋</span>
						<span>Processing result...</span>
					</div>
				{/if}
			</div>
		</div>
	</CollapsibleMessageRenderer>
</div>
