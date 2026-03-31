<script lang="ts">
	import type { ToolRendererProps } from '$lib/types/toolRenderer';
	import type { ToolRenderer } from '$lib/types/toolRenderer';

	// Accept standard tool renderer props
	export let toolCallPair: ToolRendererProps['toolCallPair'];
	export let isStreaming: boolean = false;
	export let renderPhase: ToolRendererProps['renderPhase'] = 'initial';
	export let index: number = 0;
	export let expanded: boolean = true;

	// Renderer interface implementation
	const rendererInterface: ToolRenderer = {
		toolNamePattern: '*',
		priority: -100,
		supportsStreaming: true,
		getPreviewContent: (pair) => {
			const name = getToolName(pair);
			const hasResult = !!pair.toolResult;
			return hasResult ? `${name} (complete)` : `${name} (running)`;
		}
	};

	// Extract tool name
	function getToolName(pair: typeof toolCallPair): string {
		return pair.toolCall.function_name || pair.toolCall.name || 'Unknown Tool';
	}

	// Get tool call ID
	function getToolCallId(pair: typeof toolCallPair): string {
		return pair.toolCall.tool_call_id || pair.toolCall.id || `tool_${pair.toolCall.index || index}`;
	}

	// Check if result is an error
	function hasError(pair: typeof toolCallPair): boolean {
		return (
			!!pair.toolResult &&
			(pair.toolResult.result.startsWith('Error') ||
				pair.toolResult.result.includes('error') ||
				pair.toolResult.result.includes('failed'))
		);
	}

	// Cache previous formatted lines to prevent flickering during incremental updates
	// This maintains the previous state when JsonFragmentRebuilder returns undefined between keys
	let lines_back: Array<{ type: string; content: string }> | undefined = undefined;
	let lines_back_toolCallId: string | undefined = undefined;

	// Reset cache when tool call changes (new tool call or different ID)
	$: if (toolCallPair?.toolCall?.tool_call_id || toolCallPair?.toolCall?.id) {
		// Check if this is a different tool call than what we cached
		const currentId = toolCallPair.toolCall.tool_call_id || toolCallPair.toolCall.id;
		if (lines_back_toolCallId !== currentId) {
			lines_back = undefined;
			lines_back_toolCallId = currentId;
		}
	}

	// Format arguments as YAML-like structure with syntax highlighting
	function formatArgsAsYaml(args: any): Array<{ type: string; content: string }> {
		// Handle null/undefined - show loading indicator
		if (args === null || args === undefined) {
			return lines_back ?? [{ type: 'text', content: 'Loading...' }];
		}

		// This function should only be called with valid objects or parsed JSON
		// The component logic above ensures we don't pass partial JSON here
		let parsed = args;
		if (typeof args === 'string') {
			try {
				parsed = JSON.parse(args);
			} catch (e) {
				// This shouldn't happen with our new logic, but as a safety net
				// show empty object rather than broken JSON
				return [{ type: 'text', content: '{}' }];
			}
		}

		const lines: Array<{ type: string; content: string }> = [];

		function formatValue(value: any, indent = 0): void {
			const spaces = '  '.repeat(indent);

			if (value === null) {
				lines.push({ type: 'null', content: 'null' });
			} else if (typeof value === 'boolean') {
				lines.push({ type: 'boolean', content: String(value) });
			} else if (typeof value === 'number') {
				lines.push({ type: 'number', content: String(value) });
			} else if (typeof value === 'string') {
				// Check if it's a multiline string
				if (value.includes('\n')) {
					lines.push({ type: 'text', content: '|' });
					value.split('\n').forEach((line) => {
						lines.push({ type: 'text', content: '\n' + spaces + '  ' });
						lines.push({ type: 'string', content: line });
					});
				} else {
					lines.push({ type: 'string', content: value });
				}
			} else if (Array.isArray(value)) {
				if (value.length === 0) {
					// Don't show empty arrays during streaming, show ... instead
					lines.push({ type: 'text', content: '...' });
				} else {
					value.forEach((item, i) => {
						lines.push({ type: 'text', content: (i === 0 ? '' : '\n' + spaces) + '- ' });
						formatValue(item, indent + 1);
					});
				}
			} else if (typeof value === 'object') {
				const entries = Object.entries(value);
				if (entries.length === 0) {
					// For truly empty root object, show {}
					if (indent === 0) {
						lines.push({ type: 'text', content: '{}' });
					} else {
						// For nested empty objects during streaming, show ... to indicate pending
						lines.push({ type: 'text', content: '...' });
					}
				} else {
					// Filter out empty nested objects during streaming (unless it's the only entry)
					// Only filter at nested levels, not at root
					const filteredEntries =
						indent > 0
							? entries.filter(([key, val]) => {
									if (
										typeof val === 'object' &&
										val !== null &&
										!Array.isArray(val) &&
										Object.keys(val).length === 0 &&
										entries.length > 1
									) {
										// Skip this empty nested object
										return false;
									}
									return true;
								})
							: entries;

					filteredEntries.forEach(([key, val], i) => {
						if (i > 0) lines.push({ type: 'text', content: '\n' + spaces });
						lines.push({ type: 'key', content: key + ':' });
						lines.push({ type: 'text', content: ' ' });
						formatValue(val, indent + 1);
					});
				}
			} else {
				lines.push({ type: 'text', content: String(value) });
			}
		}

		formatValue(parsed);
		lines_back = lines;
		return lines;
	}

	// Format result based on content type
	function formatResult(result: string): {
		isJson: boolean;
		formatted: string;
		isMarkdown: boolean;
	} {
		// Try to parse as JSON first
		try {
			const parsed = JSON.parse(result);
			return {
				isJson: true,
				formatted: JSON.stringify(parsed, null, 2),
				isMarkdown: false
			};
		} catch {
			// Check if it looks like structured data
			if (result.includes('|') && result.includes('\n') && result.includes('-')) {
				// Might be a table or structured output
				return {
					isJson: false,
					formatted: result,
					isMarkdown: false
				};
			}

			// Check if it contains markdown indicators
			if (result.includes('```') || result.includes('**') || result.includes('##')) {
				// For now, just return as plain text
				// TODO: Add markdown rendering support
				return {
					isJson: false,
					formatted: result,
					isMarkdown: false
				};
			}

			// Default to plain text
			return {
				isJson: false,
				formatted: result,
				isMarkdown: false
			};
		}
	}

	$: toolName = getToolName(toolCallPair);
	$: toolId = getToolCallId(toolCallPair);
	$: isError = hasError(toolCallPair);
	$: formattedResult = toolCallPair.toolResult
		? formatResult(toolCallPair.toolResult.result)
		: null;
</script>

<!-- Default tool renderer -->
<div
	class="default-tool-renderer rounded-lg border bg-white p-4 dark:bg-gray-800"
	class:border-gray-300={!isError}
	class:dark:border-gray-600={!isError}
	class:border-red-300={isError}
	class:dark:border-red-700={isError}
	data-testid="default-tool-renderer"
	data-tool-name={toolName}
	data-tool-id={toolId}
>
	<!-- Tool header -->
	<div class="mb-3 flex items-center justify-between">
		<div class="flex items-center space-x-2">
			<svg
				class="h-5 w-5 text-gray-600 dark:text-gray-400"
				fill="none"
				stroke="currentColor"
				viewBox="0 0 24 24"
			>
				<path
					stroke-linecap="round"
					stroke-linejoin="round"
					stroke-width="2"
					d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z M15 12a3 3 0 11-6 0 3 3 0 016 0z"
				/>
			</svg>
			<span class="font-semibold text-gray-800 dark:text-gray-200" data-testid="tool-call-name">
				{toolName}
			</span>
			{#if toolId !== `tool_${index}`}
				<span class="text-xs text-gray-500 dark:text-gray-400">
					#{toolId}
				</span>
			{/if}
		</div>

		<!-- Status badge -->
		{#if toolCallPair.toolResult}
			{#if isError}
				<span class="status-badge error">Error</span>
			{:else}
				<span class="status-badge success">Complete</span>
			{/if}
		{:else if isStreaming}
			<span class="status-badge pending">Running...</span>
		{:else}
			<span class="status-badge waiting">Waiting</span>
		{/if}
	</div>

	<!-- Tool arguments -->
	{#if toolCallPair.toolCall.args !== undefined || toolCallPair.toolCall.function_args}
		{@const argsToFormat = (() => {
			// IMPORTANT: Argument precedence for incremental rendering
			// 1. Always prefer JUF-built args object if it exists (incremental updates)
			// 2. Only use function_args if it's complete JSON and no JUF args exist
			// 3. Show empty object {} only when starting (no args at all)
			// This ensures smooth incremental rendering without flickering.

			// Always prefer JUF-built args object if it exists and is an object
			// This includes empty objects {} which is fine - they show as {}
			if (
				toolCallPair.toolCall.args !== null &&
				toolCallPair.toolCall.args !== undefined &&
				typeof toolCallPair.toolCall.args === 'object'
			) {
				return toolCallPair.toolCall.args;
			}

			// Fall back to parsing function_args only if it's valid complete JSON
			// This prevents showing partial JSON or empty objects during streaming
			// Only use function_args if args is truly undefined (not set yet)
			if (toolCallPair.toolCall.function_args && toolCallPair.toolCall.args === undefined) {
				const str = toolCallPair.toolCall.function_args.trim();
				// Only try to parse if it looks complete
				if (str.startsWith('{') && str.endsWith('}')) {
					try {
						const parsed = JSON.parse(str);
						// Only return if we successfully parsed
						return parsed;
					} catch {
						// Invalid JSON, fall through
					}
				}
			}

			// If we have neither valid JUF args nor parseable function_args,
			// don't show anything yet - wait for actual data
			return null;
		})()}
		<div class="tool-args" data-testid="tool-call-args">
			<div class="args-label">Arguments:</div>
			<pre
				class="args-content"><!--
				-->{#each formatArgsAsYaml(argsToFormat) as segment}<!--
					-->{#if segment.type === 'key'}<!--
						--><span
							class="text-orange-600 dark:text-orange-400">{segment.content}</span
						><!--
					-->{:else if segment.type === 'string'}<!--
						--><span
							class="text-green-600 dark:text-green-400">{segment.content}</span
						><!--
					-->{:else if segment.type === 'number'}<!--
						--><span
							class="text-blue-600 dark:text-blue-400">{segment.content}</span
						><!--
					-->{:else if segment.type === 'boolean'}<!--
						--><span
							class="text-purple-600 dark:text-purple-400">{segment.content}</span
						><!--
					-->{:else if segment.type === 'null'}<!--
						--><span
							class="text-gray-500 dark:text-gray-500">{segment.content}</span
						><!--
					-->{:else}<!--
						--><span>{segment.content}</span
						><!--
					-->{/if}<!--
				-->{/each}<!--
			--></pre>
		</div>
	{:else}
		<div class="tool-args" data-testid="tool-call-args">
			<span class="args-empty">No arguments</span>
		</div>
	{/if}

	<!-- Tool result -->
	{#if toolCallPair.toolResult}
		<div class="tool-result" class:error={isError}>
			<div class="result-label">Result:</div>
			{#if formattedResult}
				{#if formattedResult.isJson}
					<pre
						class="result-content json"
						data-testid="tool-result">{formattedResult.formatted}</pre>
				{:else if formattedResult.isMarkdown}
					<div class="result-content markdown" data-testid="tool-result">
						{@html formattedResult.formatted}
					</div>
				{:else}
					<pre class="result-content" data-testid="tool-result">{formattedResult.formatted}</pre>
				{/if}
			{:else}
				<pre class="result-content" data-testid="tool-result">{toolCallPair.toolResult.result}</pre>
			{/if}
		</div>
	{:else if isStreaming}
		<div class="tool-pending">
			<div class="spinner"></div>
			<span>Executing {toolName}...</span>
		</div>
	{/if}
</div>

<style>
	.default-tool-renderer {
		transition: border-color 0.2s;
	}

	.status-badge {
		padding: 0.125rem 0.5rem;
		border-radius: 12px;
		font-size: 0.75rem;
		font-weight: 500;
	}

	.status-badge.success {
		background: #d4edda;
		color: #155724;
	}

	.status-badge.error {
		background: #f8d7da;
		color: #721c24;
	}

	.status-badge.pending {
		background: #fff3cd;
		color: #856404;
		animation: pulse 1.5s infinite;
	}

	.status-badge.waiting {
		background: #e2e3e5;
		color: #383d41;
	}

	@keyframes pulse {
		0%,
		100% {
			opacity: 1;
		}
		50% {
			opacity: 0.7;
		}
	}

	.tool-args,
	.tool-result {
		margin-top: 0.75rem;
	}

	.args-label,
	.result-label {
		font-size: 0.75rem;
		color: #666;
		margin-bottom: 0.25rem;
		font-weight: 500;
	}

	:global(.dark) .args-label,
	:global(.dark) .result-label {
		color: #9ca3af;
	}

	.args-content,
	.result-content {
		background: #f5f5f5;
		border: 1px solid #e0e0e0;
		border-radius: 4px;
		padding: 0.75rem;
		font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
		font-size: 0.813rem;
		overflow-x: auto;
		margin: 0;
		white-space: pre-wrap;
		word-break: break-word;
		line-height: 1.5;
	}

	:global(.dark) .args-content,
	:global(.dark) .result-content {
		background: #1f2937;
		border-color: #374151;
		color: #e5e7eb;
	}

	.result-content.json {
		color: #333;
	}

	:global(.dark) .result-content.json {
		color: #e5e7eb;
	}

	.result-content.markdown {
		font-family: inherit;
		white-space: normal;
	}

	.result-content.markdown :global(pre) {
		background: #f0f0f0;
		padding: 0.5rem;
		border-radius: 4px;
		overflow-x: auto;
	}

	:global(.dark) .result-content.markdown :global(pre) {
		background: #111827;
	}

	.tool-result.error .result-content {
		background: #fff5f5;
		border-color: #ffcccc;
		color: #cc0000;
	}

	:global(.dark) .tool-result.error .result-content {
		background: #7f1d1d;
		border-color: #991b1b;
		color: #fca5a5;
	}

	.args-empty {
		color: #999;
		font-style: italic;
		font-size: 0.813rem;
	}

	:global(.dark) .args-empty {
		color: #6b7280;
	}

	.tool-pending {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.5rem;
		background: #f0f8ff;
		border-radius: 4px;
		font-size: 0.813rem;
		color: #666;
		margin-top: 0.75rem;
	}

	:global(.dark) .tool-pending {
		background: #1e3a5f;
		color: #9ca3af;
	}

	.spinner {
		width: 14px;
		height: 14px;
		border: 2px solid #ddd;
		border-top-color: #666;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	:global(.dark) .spinner {
		border-color: #4b5563;
		border-top-color: #9ca3af;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}
</style>
