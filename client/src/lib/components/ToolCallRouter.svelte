<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { getToolRendererComponent } from '$lib/renderers/ToolRendererRegistry';
	import type { ToolCallPair } from '$lib/types/chat';
	import type { ToolRendererProps } from '$lib/types/toolRenderer';

	// Component props
	export let toolCallPair: ToolCallPair;
	export let isStreaming: boolean = false;
	export let renderPhase: 'initial' | 'streaming' | 'complete' = 'initial';
	export let index: number = 0;
	export let expanded: boolean = true;

	// Internal state
	let mounted = false;
	let renderError: Error | null = null;
	let RendererComponent: any = null;
	let isLoading = true;
	let lastResolvedToolName: string | null = null;

	// Extract tool name from the tool call
	function getToolName(pair: ToolCallPair): string {
		return pair.toolCall.function_name || pair.toolCall.name || 'unknown';
	}

	/**
	 * Resolves the appropriate renderer for the tool call.
	 * Falls back to default renderer on any error.
	 */
	async function resolveRenderer() {
		try {
			const toolName = getToolName(toolCallPair);

			// Skip re-resolution if we already resolved this tool
			if (lastResolvedToolName === toolName && RendererComponent) {
				console.log(
					`[ToolCallRouter] Skipping re-resolution for tool: '${toolName}' (already loaded)`
				);
				return;
			}

			renderError = null;
			isLoading = true;
			lastResolvedToolName = toolName;

			console.log(`[ToolCallRouter] Resolving renderer for tool: '${toolName}'`);

			// Get the component from the tool renderer registry
			const component = await getToolRendererComponent(toolName);

			if (component) {
				console.log(
					`[ToolCallRouter] Found renderer component for '${toolName}':`,
					component.name || 'AnonymousComponent'
				);
				// Special logging for calculator tool
				if (toolName === 'calculate' || toolName.toLowerCase().includes('calc')) {
					console.log(`[ToolCallRouter] Calculator tool detected! Component:`, component);
				}
				RendererComponent = component;
			} else {
				// No renderer found - this shouldn't happen if default is registered
				console.warn(`No tool renderer found for '${toolName}'`);
				renderError = new Error(`No renderer available for tool: ${toolName}`);
			}
		} catch (error) {
			console.error('Error resolving tool renderer:', error);
			renderError = error instanceof Error ? error : new Error('Unknown renderer error');
		} finally {
			isLoading = false;
		}
	}

	// Initialize when component mounts
	onMount(() => {
		mounted = true;
		resolveRenderer();
	});

	// Clean up when component unmounts
	onDestroy(() => {
		mounted = false;
	});

	// Re-resolve renderer only if tool name changes
	$: currentToolName = getToolName(toolCallPair);
	$: if (mounted && currentToolName !== lastResolvedToolName) {
		resolveRenderer();
	}

	// Prepare props for the renderer component
	$: rendererProps = {
		toolCallPair,
		isStreaming,
		renderPhase,
		index,
		expanded
	} satisfies ToolRendererProps;
</script>

<!-- Tool call router container with test interface -->
<div
	class="tool-call-router"
	data-testid="tool-call-item"
	data-tool-name={getToolName(toolCallPair)}
	data-tool-index={index}
>
	{#if renderError}
		<!-- Error state -->
		<div
			class="tool-error rounded-lg border border-red-300 bg-red-50 p-3 dark:border-red-700 dark:bg-red-900/20"
		>
			<div class="flex items-start">
				<svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
					<path
						fill-rule="evenodd"
						d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
						clip-rule="evenodd"
					/>
				</svg>
				<div class="ml-3">
					<h3
						class="text-sm font-medium text-red-800 dark:text-red-200"
						data-testid="tool-call-name"
					>
						Tool Rendering Error
					</h3>
					<div class="mt-1 text-sm text-red-700 dark:text-red-300" data-testid="tool-call-args">
						<p>Failed to render tool: {getToolName(toolCallPair)}</p>
						{#if renderError.message}
							<p class="mt-1 font-mono text-xs">{renderError.message}</p>
						{/if}
					</div>
				</div>
			</div>
		</div>
	{:else if isLoading}
		<!-- Loading state -->
		<div
			class="tool-loading flex items-center space-x-2 rounded-lg bg-gray-50 p-3 dark:bg-gray-800"
		>
			<div class="h-4 w-4 animate-spin rounded-full border-b-2 border-blue-600"></div>
			<span class="text-sm text-gray-600 dark:text-gray-400" data-testid="tool-call-name">
				Loading renderer for {getToolName(toolCallPair)}...
			</span>
			<div data-testid="tool-call-args" style="display: none;">Loading</div>
		</div>
	{:else if RendererComponent}
		<!-- Dynamic component rendering -->
		<svelte:component this={RendererComponent} {...rendererProps} />
	{:else}
		<!-- Fallback if no component available -->
		<div
			class="tool-fallback rounded-lg border border-gray-300 bg-gray-50 p-3 dark:border-gray-700 dark:bg-gray-800"
		>
			<p class="text-sm text-gray-600 dark:text-gray-400" data-testid="tool-call-name">
				No renderer available for tool: {getToolName(toolCallPair)}
			</p>
			<div data-testid="tool-call-args" style="display: none;">No arguments</div>
		</div>
	{/if}
</div>

<style>
	.tool-call-router {
		position: relative;
	}
</style>
