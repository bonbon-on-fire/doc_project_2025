<script lang="ts">
	import type { ClientToolsCallAggregateMessageDto } from '$lib/types/chat';
	import ToolCallRouter from './ToolCallRouter.svelte';

	export let message: ClientToolsCallAggregateMessageDto;
	export let isStreaming = false;
	export let renderPhase: 'initial' | 'streaming' | 'complete' = 'initial';
	export let expanded: boolean = true;

	// Handle toggle functionality
	function toggleExpanded() {
		expanded = !expanded;
	}

	// Handle keyboard navigation
	function handleKeydown(event: KeyboardEvent) {
		if (event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			toggleExpanded();
		}
	}

	// Calculate tool call summary for collapsed view
	function getToolCallSummary(pairs: typeof message.toolCallPairs) {
		if (!pairs || pairs.length === 0) return '';

		// Count occurrences of each tool
		const toolCounts = new Map<string, number>();
		for (const pair of pairs) {
			const toolName = pair.toolCall.function_name || pair.toolCall.name || 'unknown';
			toolCounts.set(toolName, (toolCounts.get(toolName) || 0) + 1);
		}

		// Format as "tool1, tool2(3), tool3" etc
		const summaryParts: string[] = [];
		for (const [name, count] of toolCounts) {
			if (count === 1) {
				summaryParts.push(name);
			} else {
				summaryParts.push(`${name}(${count})`);
			}
		}

		return summaryParts.join(', ');
	}

	$: toolSummary = getToolCallSummary(message.toolCallPairs);
</script>

<div class="tools-aggregate-container" data-testid="tool-call-renderer">
	<div class="tools-header">
		<div class="tools-header-content">
			<svg class="tools-icon" viewBox="0 0 24 24" width="16" height="16">
				<path
					fill="currentColor"
					d="M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z"
				/>
			</svg>
			<span class="tools-title">Tool Calls</span>
			{#if !expanded && toolSummary}
				<span class="tools-summary">: {toolSummary}</span>
			{/if}
		</div>
		<button
			class="toggle-button"
			data-testid="tool call-toggle-button"
			aria-expanded={expanded}
			aria-label={expanded ? 'Collapse tool calls' : 'Expand tool calls'}
			tabindex="0"
			on:click={toggleExpanded}
			on:keydown={handleKeydown}
		>
			<svg class="toggle-icon" viewBox="0 0 24 24" width="16" height="16">
				{#if expanded}
					<path fill="currentColor" d="M19 9l-7 7-7-7" />
				{:else}
					<path fill="currentColor" d="M9 5l7 7-7 7" />
				{/if}
			</svg>
		</button>
	</div>

	{#if expanded}
		<div class="tool-calls-list">
			{#each message.toolCallPairs || [] as pair, index}
				{console.log('[ToolsCallAggregateRenderer] Rendering tool call pair:', {
					index,
					toolName: pair.toolCall.function_name || pair.toolCall.name,
					hasResult: !!pair.toolResult,
					pairId: pair.toolCall.id || pair.toolCall.tool_call_id
				})}
				<ToolCallRouter toolCallPair={pair} {isStreaming} {renderPhase} {index} {expanded} />
			{/each}

			{#if (!message.toolCallPairs || message.toolCallPairs.length === 0) && isStreaming}
				<div class="no-tools-message">
					<div class="spinner"></div>
					<span>Preparing tool calls...</span>
				</div>
			{/if}
		</div>
	{/if}
</div>

<style>
	.tools-aggregate-container {
		background: linear-gradient(135deg, rgba(17, 24, 39, 0.85), rgba(11, 15, 25, 0.85));
		border: 1px solid rgba(59, 130, 246, 0.2);
		border-radius: 12px;
		overflow: hidden;
		margin: 0.5rem 0;
		backdrop-filter: blur(10px);
		box-shadow:
			0 4px 6px -1px rgba(0, 0, 0, 0.3),
			0 0 40px rgba(59, 130, 246, 0.05);
		transition: all 0.3s ease;
	}

	.tools-aggregate-container:hover {
		border-color: rgba(59, 130, 246, 0.3);
		box-shadow:
			0 4px 6px -1px rgba(0, 0, 0, 0.4),
			0 0 50px rgba(59, 130, 246, 0.08);
	}

	.tools-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		background: linear-gradient(90deg, rgba(59, 130, 246, 0.1), rgba(99, 102, 241, 0.05));
		border-bottom: 1px solid rgba(59, 130, 246, 0.15);
	}

	.tools-header-content {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.tools-icon {
		color: #60a5fa;
	}

	.tools-title {
		font-weight: 500;
		color: #e2e8f0;
		font-size: 0.875rem;
	}

	.tools-summary {
		color: #94a3b8;
		font-weight: 400;
		font-size: 0.813rem;
		margin-left: 0.25rem;
		opacity: 0.9;
	}

	.toggle-button {
		background: none;
		border: none;
		color: #94a3b8;
		cursor: pointer;
		padding: 0.25rem;
		border-radius: 0.25rem;
		transition: all 0.2s ease;
		display: flex;
		align-items: center;
		justify-content: center;
	}

	.toggle-button:hover {
		color: #60a5fa;
		background: rgba(59, 130, 246, 0.1);
	}

	.toggle-button:focus {
		outline: 2px solid rgba(59, 130, 246, 0.5);
		outline-offset: 2px;
	}

	.toggle-icon {
		transition: transform 0.2s ease;
	}

	.tool-calls-list {
		padding: 1rem;
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.tool-calls-list > :global(.tool-call-router) {
		margin: 0;
	}

	.no-tools-message {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		padding: 0.75rem;
		background: rgba(59, 130, 246, 0.1);
		border: 1px solid rgba(59, 130, 246, 0.2);
		border-radius: 6px;
		font-size: 0.813rem;
		color: #94a3b8;
	}

	.no-tools-message {
		margin-top: 0;
	}

	.spinner {
		width: 14px;
		height: 14px;
		border: 2px solid rgba(59, 130, 246, 0.2);
		border-top-color: #60a5fa;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to {
			transform: rotate(360deg);
		}
	}
</style>
