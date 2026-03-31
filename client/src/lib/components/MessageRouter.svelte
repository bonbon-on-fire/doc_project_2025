<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { getRenderer, getRendererComponent } from '$lib/renderers';
	import {
		initializeMessageState,
		updateMessageState,
		messageStates,
		type MessageState
	} from '$lib/stores/messageState';
	import { streamingSnapshots } from '$lib/stores/chat';
	import type { RichMessageDto } from '$lib/types';
	import MessageBubble from './MessageBubble.svelte';

	// Component props with proper TypeScript typing
	export let message: RichMessageDto;
	export let isLatest: boolean = false;
	export let onStateChange: ((messageId: string, state: MessageState) => void) | undefined =
		undefined;

	// Internal component state
	let mounted = false;
	let renderError: Error | null = null;
	let RendererComponent: any = null;
	let fallbackToMessageBubble = false;

	// Current message state from store
	$: currentMessageState = $messageStates[message.id];

	// Message-specific snapshot tracking to avoid over-broad reactive updates
	$: messageSnapshot = $streamingSnapshots?.[message.id];
	$: messageSnapshotPhase = messageSnapshot?.phase;
	$: messageSnapshotIsStreaming = messageSnapshot?.isStreaming;
	$: messageSnapshotType = messageSnapshot?.messageType;

	/**
	 * Attempts to resolve the appropriate renderer for the message type.
	 * Falls back to MessageBubble on any error.
	 */
	async function resolveRenderer() {
		try {
			renderError = null;
			fallbackToMessageBubble = false;

			// Determine the effective message type
			const effectiveMessageType = message.messageType || 'text';

			// Log routing decision for debugging
			if (effectiveMessageType === 'tools_aggregate') {
				console.log('[MessageRouter] Routing tools_aggregate message:', {
					id: message.id,
					messageType: effectiveMessageType,
					toolCallPairs: (message as any).toolCallPairs
				});
			} else if (effectiveMessageType === 'tool_call') {
				console.log('[MessageRouter] Routing tool_call message:', {
					id: message.id,
					messageType: effectiveMessageType,
					toolCalls: (message as any).toolCalls
				});
			}

			// Get renderer from registry
			const renderer = getRenderer(effectiveMessageType);

			// Try to get the actual Svelte component for this renderer
			const RendererComponentModule = await getRendererComponent(effectiveMessageType);

			if (RendererComponentModule && renderer.messageType !== 'fallback') {
				RendererComponent = RendererComponentModule;
				fallbackToMessageBubble = false;
			} else {
				console.warn(
					`No specific renderer found for message type '${effectiveMessageType}', using MessageBubble fallback`
				);
				fallbackToMessageBubble = true;
				RendererComponent = MessageBubble;
			}
		} catch (error) {
			console.error('Error resolving renderer for message:', message.id, error);
			renderError = error instanceof Error ? error : new Error('Unknown renderer error');
			fallbackToMessageBubble = true;
			RendererComponent = MessageBubble;
		}
	}

	/**
	 * Handles state changes and notifies parent component.
	 */
	function handleStateChange(newState: Partial<MessageState>) {
		if (!message.id) return;

		updateMessageState(message.id, newState);

		// Notify parent component if callback provided
		if (onStateChange && currentMessageState) {
			onStateChange(message.id, { ...currentMessageState, ...newState });
		}
	}

	/**
	 * Handles expand/collapse toggle for the message.
	 */
	function handleToggleExpansion() {
		if (!currentMessageState) return;

		const newExpanded = !currentMessageState.expanded;
		handleStateChange({ expanded: newExpanded });
	}

	// Initialize message state when component mounts
	onMount(() => {
		mounted = true;

		// Initialize message state if not already present
		if (message.id && !currentMessageState) {
			initializeMessageState(message.id, isLatest);
		}

		// Resolve the appropriate renderer
		resolveRenderer();
	});

	// Clean up when component unmounts
	onDestroy(() => {
		mounted = false;
	});

	// Reactive statements for prop changes
	$: if (mounted && message) {
		resolveRenderer();
	}

	$: if (mounted && message.id && isLatest !== undefined) {
		// Update expansion state when isLatest changes
		handleStateChange({ expanded: isLatest });
	}

	// Reactive: drive render phase and expansion from external policy and snapshots
	// Now uses message-specific values to prevent unnecessary updates
	$: if (mounted && message?.id && messageSnapshotPhase !== undefined) {
		const newPhase =
			messageSnapshotPhase === 'streaming'
				? 'streaming'
				: messageSnapshotPhase === 'complete'
					? 'complete'
					: (currentMessageState?.renderPhase ?? 'initial');

		if (currentMessageState?.renderPhase !== newPhase) {
			handleStateChange({ renderPhase: newPhase });
		}
	}

	// Separate reactive statement for expansion logic to minimize update frequency
	$: if (mounted && message?.id && messageSnapshot) {
		if (messageSnapshotIsStreaming && !currentMessageState?.expanded) {
			// Expand while streaming
			handleStateChange({ expanded: true });
		} else if (
			!messageSnapshotIsStreaming &&
			messageSnapshotType === 'reasoning' &&
			messageSnapshotPhase === 'complete' &&
			currentMessageState?.expanded
		) {
			// Collapse reasoning on completion; text stays as-is
			handleStateChange({ expanded: false });
		}
	}
</script>

<!-- Main message container with error boundary -->
<div
	class="message-router"
	data-message-id={message.id}
	data-message-type={message.messageType || 'text'}
	data-testid={fallbackToMessageBubble
		? 'fallback-renderer'
		: `${message.messageType || 'text'}-renderer`}
>
	{#if renderError}
		<!-- Error state with fallback rendering -->
		<div class="message-error mb-4 border-l-4 border-red-500 bg-red-50 p-4 dark:bg-red-900/20">
			<div class="flex items-start">
				<div class="flex-shrink-0">
					<svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
						<path
							fill-rule="evenodd"
							d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
							clip-rule="evenodd"
						/>
					</svg>
				</div>
				<div class="ml-3">
					<h3 class="text-sm font-medium text-red-800 dark:text-red-200">
						Message Rendering Error
					</h3>
					<div class="mt-2 text-sm text-red-700 dark:text-red-300">
						<p>
							Failed to render message of type "{message.messageType || 'unknown'}". Using fallback
							renderer.
						</p>
						{#if renderError.message}
							<p class="mt-1 font-mono text-xs text-red-600 dark:text-red-400">
								{renderError.message}
							</p>
						{/if}
					</div>
				</div>
			</div>
		</div>
	{/if}

	<!-- Dynamic component rendering -->
	{#if RendererComponent}
		{#if message.messageType === 'tool_call'}
			{@const logMessage = (() => {
				console.log('[MessageRouter] Passing to ToolCallRenderer:', {
					messageId: message.id,
					messageType: message.messageType,
					toolCalls: (message as any).toolCalls,
					tool_calls: (message as any).tool_calls,
					content: (message as any).content,
					role: message.role,
					renderPhase: currentMessageState?.renderPhase ?? 'initial'
				});
				return null;
			})()}
		{/if}
		<svelte:component
			this={RendererComponent}
			{message}
			{isLatest}
			expanded={currentMessageState?.expanded ?? isLatest}
			renderPhase={currentMessageState?.renderPhase ?? 'initial'}
			isLastAssistantMessage={isLatest && message.role === 'assistant'}
			on:toggleExpansion={handleToggleExpansion}
			on:stateChange={(event: CustomEvent) => handleStateChange(event.detail)}
		/>
	{:else}
		<!-- Loading state while resolver works -->
		<div class="message-loading flex items-center space-x-2 p-4">
			<div class="h-4 w-4 animate-spin rounded-full border-b-2 border-blue-600"></div>
			<span class="text-sm text-gray-600 dark:text-gray-400">Loading message renderer...</span>
		</div>
	{/if}
</div>

<style>
	.message-router {
		position: relative;
	}

	.message-error {
		border-radius: 0.375rem;
	}

	.message-loading {
		background-color: rgb(249 250 251);
		border-radius: 0.5rem;
	}

	/* Dark mode styles */
	:global(.dark) .message-loading {
		background-color: rgb(31 41 55);
	}

	/* Ensure proper z-index for error states */
	.message-error {
		z-index: 10;
	}
</style>
