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
		messageType: 'tool_call'
	};

	// Extract and normalize tool calls from message
	function getToolCalls(msg: any): Array<{ name: string; args: any; id?: string }> {
		console.log('[ToolCallRenderer] Extracting tool calls from message:', msg);

		let rawToolCalls: any[] = [];

		if (msg.toolCalls) {
			console.log('[ToolCallRenderer] Found toolCalls:', msg.toolCalls);
			rawToolCalls = msg.toolCalls;
		} else if (msg.tool_calls) {
			console.log('[ToolCallRenderer] Found tool_calls:', msg.tool_calls);
			rawToolCalls = msg.tool_calls;
		} else if (msg.content && typeof msg.content === 'string') {
			try {
				const parsed = JSON.parse(msg.content);
				if (parsed.tool_calls) {
					console.log('[ToolCallRenderer] Found tool_calls in content:', parsed.tool_calls);
					rawToolCalls = parsed.tool_calls;
				}
			} catch {}
		}

		if (rawToolCalls.length === 0) {
			console.log('[ToolCallRenderer] No tool calls found');
			return [];
		}

		// Normalize the tool calls to the expected format
		return rawToolCalls.map((tc) => {
			// Prefer args (from JUF stitching) over function_args string
			let args = tc.args ?? tc.function_args;
			if (typeof args === 'string') {
				try {
					args = JSON.parse(args);
				} catch (e) {
					console.warn('[ToolCallRenderer] Failed to parse function_args:', args, e);
					args = { raw: args }; // Fallback to showing raw string
				}
			}

			return {
				name: tc.name || tc.function_name || 'unknown',
				args: args || {},
				id: tc.id || tc.tool_call_id
			};
		});
	}

	// Convert JSON to YAML-like format with syntax highlighting
	function formatAsYaml(obj: any, indent: number = 0): string {
		if (obj === null) return '<span class="text-gray-500">null</span>';
		if (obj === undefined) return '<span class="text-gray-500">undefined</span>';

		const spaces = '  '.repeat(indent);

		if (typeof obj === 'string') {
			return `<span class="text-green-600 dark:text-green-400">"${obj}"</span>`;
		}

		if (typeof obj === 'number') {
			return `<span class="text-blue-600 dark:text-blue-400">${obj}</span>`;
		}

		if (typeof obj === 'boolean') {
			return `<span class="text-purple-600 dark:text-purple-400">${obj}</span>`;
		}

		if (Array.isArray(obj)) {
			if (obj.length === 0) return '<span class="text-gray-500">[]</span>';

			let result = '';
			obj.forEach((item, index) => {
				result += `\n${spaces}- ${formatAsYaml(item, indent + 1)}`;
			});
			return result;
		}

		if (typeof obj === 'object') {
			const keys = Object.keys(obj);
			if (keys.length === 0) return '<span class="text-gray-500">{}</span>';

			let result = '';
			keys.forEach((key) => {
				const value = obj[key];
				const formattedValue = formatAsYaml(value, indent + 1);
				result += `\n${spaces}<span class="text-orange-600 dark:text-orange-400">${key}:</span> ${formattedValue}`;
			});
			return result;
		}

		return String(obj);
	}

	// Create collapsed preview
	function createCollapsedPreview(toolCalls: any[]): string {
		if (toolCalls.length === 0) return 'No tool calls';
		if (toolCalls.length === 1) {
			return `${toolCalls[0].name}(${Object.keys(toolCalls[0].args || {}).length} args)`;
		}
		return `${toolCalls.length} tool calls: ${toolCalls.map((tc) => tc.name).join(', ')}`;
	}

	$: toolCalls = getToolCalls(message);
	$: collapsedPreview = createCollapsedPreview(toolCalls);

	// Forward child events from CollapsibleMessageRenderer
	function forwardStateChange(e: CustomEvent<{ expanded: boolean }>) {
		expanded = e.detail.expanded;
		dispatch('stateChange', e.detail);
	}
	function forwardToggle(e: CustomEvent<{ expanded: boolean }>) {
		dispatch('toggleExpansion', e.detail);
	}
</script>

<!-- Tool call message using reusable collapsible renderer -->
<div data-component="tool-call-renderer" data-testid="tool-call-renderer">
	<CollapsibleMessageRenderer
		{message}
		{isLatest}
		{expanded}
		collapsible={true}
		iconPath="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z M15 12a3 3 0 11-6 0 3 3 0 016 0z"
		iconColors="from-blue-500 to-indigo-600"
		messageType="Tool Call"
		{collapsedPreview}
		borderColor="border-blue-200"
		bgColor="bg-blue-50"
		textColor="text-blue-900"
		darkBorderColor="dark:border-blue-700"
		darkBgColor="dark:bg-blue-900/20"
		darkTextColor="dark:text-blue-100"
		on:stateChange={forwardStateChange}
		on:toggleExpansion={forwardToggle}
	>
		<!-- Tool calls content -->
		<div class="space-y-3">
			{#each toolCalls as toolCall, index}
				<div
					class="rounded-lg border border-blue-300 bg-white p-4 dark:border-blue-600 dark:bg-blue-800/30"
					data-testid="tool-call-item"
					data-tool-name={toolCall.name}
					data-tool-index={index}
				>
					<!-- Tool call header -->
					<div class="mb-3 flex items-center justify-between">
						<div class="flex items-center space-x-2">
							<svg
								class="h-5 w-5 text-blue-600 dark:text-blue-400"
								fill="none"
								stroke="currentColor"
								viewBox="0 0 24 24"
							>
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M13 10V3L4 14h7v7l9-11h-7z"
								></path>
							</svg>
							<span
								class="font-semibold text-blue-800 dark:text-blue-200"
								data-testid="tool-call-name"
							>
								{toolCall.name}
							</span>
							{#if toolCall.id}
								<span class="text-xs text-blue-600 dark:text-blue-400" data-testid="tool-call-id">
									#{toolCall.id}
								</span>
							{/if}
						</div>
						<span class="text-xs text-blue-600 dark:text-blue-400">
							Call #{index + 1}
						</span>
					</div>

					<!-- Tool arguments in YAML-like format -->
					{#if toolCall.args && Object.keys(toolCall.args).length > 0}
						<div class="mt-2">
							<div class="mb-1 text-xs font-medium text-blue-700 dark:text-blue-300">
								Arguments:
							</div>
							<div
								class="rounded bg-gray-50 p-3 font-mono text-sm dark:bg-gray-800"
								data-testid="tool-call-args"
							>
								<pre
									class="whitespace-pre-wrap text-gray-800 dark:text-gray-200">{@html formatAsYaml(
										toolCall.args
									)}</pre>
							</div>
						</div>
					{:else}
						<div class="text-xs text-blue-600 dark:text-blue-400">No arguments</div>
					{/if}

					<!-- Show streaming indicator if this is the latest and streaming -->
					{#if isLatest && renderPhase === 'streaming' && index === toolCalls.length - 1}
						<div class="mt-2 flex items-center space-x-2 text-xs text-blue-600 dark:text-blue-400">
							<span class="animate-pulse">▋</span>
							<span>Building arguments...</span>
						</div>
					{/if}
				</div>
			{/each}

			{#if toolCalls.length === 0}
				<div class="text-center text-gray-500 dark:text-gray-400">
					<svg class="mx-auto mb-2 h-8 w-8" fill="none" stroke="currentColor" viewBox="0 0 24 24">
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							stroke-width="2"
							d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"
						></path>
					</svg>
					<p class="text-sm">No tool calls in this message</p>
				</div>
			{/if}
		</div>
	</CollapsibleMessageRenderer>
</div>
