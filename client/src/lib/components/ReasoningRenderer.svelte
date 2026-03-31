<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import CollapsibleMessageRenderer from '$lib/components/CollapsibleMessageRenderer.svelte';
	import type { ReasoningMessageDto, RichMessageDto } from '$lib/types';
	import type { MessageRenderer } from '$lib/types/renderer';
	import { streamingSnapshots } from '$lib/stores/chat';

	// Component props with proper TypeScript typing
	export let message: ReasoningMessageDto & RichMessageDto;
	export let isLatest: boolean = false;
	export let isLastAssistantMessage: boolean = false;
	// Props driven by MessageRouter for expansion and render phase
	export let expanded: boolean = true;

	const dispatch = createEventDispatcher<{
		stateChange: { expanded: boolean };
		toggleExpansion: { expanded: boolean };
	}>();

	// Basic formatting function for reasoning content
	function formatContent(content: string): string {
		// Basic markdown-like formatting
		return content
			.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
			.replace(/\*(.*?)\*/g, '<em>$1</em>')
			.replace(
				/`(.*?)`/g,
				'<code class="bg-gray-200 dark:bg-gray-700 px-1 rounded text-sm">$1</code>'
			)
			.replace(/\n/g, '<br>');
	}

	function getReasoningText(msg: any): string {
		if (!msg) return '';

		// Check visibility - don't display encrypted reasoning at all
		const visibility = (msg as any).visibility || (msg as any).Visibility;
		if (visibility === 'encrypted' || visibility === 'Encrypted' || visibility === 2) {
			return '';
		}

		// Support new DTOs (camelCase)
		if (typeof (msg as any).reasoning === 'string' && (msg as any).reasoning.trim())
			return (msg as any).reasoning;
		// Legacy/casing fallbacks from server serialization
		if (typeof (msg as any).Reasoning === 'string' && (msg as any).Reasoning.trim())
			return (msg as any).Reasoning;
		return '';
	}

	// Check if the reasoning should be hidden completely
	function isReasoningHidden(msg: any): boolean {
		const visibility = (msg as any).visibility || (msg as any).Visibility;
		return visibility === 'encrypted' || visibility === 'Encrypted' || visibility === 2;
	}

	// Component implements MessageRenderer interface
	const rendererInterface: MessageRenderer<ReasoningMessageDto> = {
		messageType: 'reasoning'
	};

	// Streaming state for this message comes solely from its snapshot
	$: isStreamingForThis = Boolean($streamingSnapshots?.[message.id]?.isStreaming);

	// Touch isLastAssistantMessage to avoid unused export warning (can be used for further UX tweaks)
	$: if (isLastAssistantMessage !== undefined) {
		// no-op: prop acknowledged
	}

	// Ensure reasoning is expanded while streaming
	$: if (isStreamingForThis && !expanded) {
		dispatch('stateChange', { expanded: true });
	}

	// Compute reasoning text for use in collapsed preview; fall back to per-message delta if final text absent
	$: reasoningText = (() => {
		const dtoText = getReasoningText(message);
		if (dtoText && dtoText.trim()) return dtoText;
		const snap = $streamingSnapshots?.[message.id];

		// Check visibility in snapshot for streaming messages
		if (snap?.visibility === 'Encrypted') {
			return '';
		}

		return snap?.reasoningDelta || '';
	})();
	$: collapsedPreview =
		reasoningText.length > 60 ? reasoningText.substring(0, 60) + '...' : reasoningText;

	// Handle child events from CollapsibleMessageRenderer
	function forwardStateChange(e: CustomEvent<{ expanded: boolean }>) {
		// Update local expanded state when child changes it
		expanded = e.detail.expanded;
		dispatch('stateChange', e.detail);
	}
	function forwardToggle(e: CustomEvent<{ expanded: boolean }>) {
		dispatch('toggleExpansion', e.detail);
	}
</script>

<!-- Reasoning message using reusable collapsible renderer -->
{#if !isReasoningHidden(message)}
	<div data-component="reasoning-renderer" data-testid="reasoning-renderer">
		<CollapsibleMessageRenderer
			{message}
			{isLatest}
			{expanded}
			collapsible={true}
			iconPath="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"
			iconColors="from-amber-500 to-orange-600"
			messageType="Reasoning"
			{collapsedPreview}
			borderColor="border-amber-200"
			bgColor="bg-amber-50"
			textColor="text-amber-900"
			darkBorderColor="dark:border-amber-700"
			darkBgColor="dark:bg-amber-900/20"
			darkTextColor="dark:text-amber-100"
			on:stateChange={forwardStateChange}
			on:toggleExpansion={forwardToggle}
		>
			<!-- Reasoning indicator -->
			<div class="mb-2 flex items-center space-x-2">
				<svg
					class="h-4 w-4 text-amber-600 dark:text-amber-400"
					fill="none"
					stroke="currentColor"
					viewBox="0 0 24 24"
				>
					<path
						stroke-linecap="round"
						stroke-linejoin="round"
						stroke-width="2"
						d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"
					></path>
				</svg>
				<span class="text-xs font-medium tracking-wide text-amber-700 uppercase dark:text-amber-300"
					>Reasoning</span
				>
			</div>

			<!-- Reasoning content with smaller font styling -->
			<div
				class="max-w-none text-xs text-amber-700 dark:text-amber-300"
				class:text-yellow-800={message.role === 'system'}
				class:dark\:text-yellow-200={message.role === 'system'}
				data-testid="reasoning-content"
			>
				{#if isStreamingForThis}
					<!-- Show reasoning while its own message is streaming, or before text message id is known -->
					{#if ($streamingSnapshots?.[message.id]?.reasoningDelta || '').trim()}
						<div
							class="mb-2 border-l-2 border-amber-300 pl-2 text-xs text-amber-600 dark:border-amber-600 dark:text-amber-400"
							data-testid="streaming-reasoning-content"
						>
							{@html formatContent($streamingSnapshots?.[message.id]?.reasoningDelta || '')}
						</div>
					{/if}
					<span class="animate-pulse">▋</span>
				{:else}
					<!-- When not streaming, render final reasoning if present; otherwise fall back to this message's snapshot delta -->
					{@html formatContent(reasoningText)}
				{/if}
			</div>
		</CollapsibleMessageRenderer>
	</div>
{/if}
