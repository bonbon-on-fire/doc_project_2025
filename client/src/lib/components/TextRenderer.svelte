<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import type { MessageDto, TextMessageDto, RichMessageDto } from '$lib/types';
	import type { MessageRenderer } from '$lib/types/renderer';
	import { formatTime } from '$lib/utils/time';
	import { streamingSnapshots } from '$lib/stores/chat';

	// Component props with proper TypeScript typing
	export let message: TextMessageDto & RichMessageDto;
	export let isLastAssistantMessage: boolean = false;

	// Create event dispatcher for custom events
	const dispatch = createEventDispatcher<{
		stateChange: { expanded: boolean };
	}>();

	// Markdown + sanitization (Phase 2 enhancement)
	import { parseMarkdown } from '$lib/markdown/parse';

	/**
	 * Convert markdown content to sanitized HTML. Empty content yields ''.
	 */
	function renderContent(content: string | null | undefined): string {
		if (!content) return '';
		return parseMarkdown(content, { devLogging: true });
	}

	$: safeContent = renderContent((message as TextMessageDto).text);

	// Component implements MessageRenderer interface
	const rendererInterface: MessageRenderer<TextMessageDto> = {
		messageType: 'text',
		// Text messages don't have expand functionality - they're always visible
		onExpand: undefined
	};

	// Text renderer configuration
	const supportsStreaming = true; // Supports streaming via chat stores
	const supportsCollapse = false; // Text messages don't collapse
</script>

<!-- Text message with chat bubble layout -->
<div class="flex {message.role === 'user' ? 'justify-end' : 'justify-start'}">
	<div class="flex max-w-xs items-start space-x-3 sm:max-w-md lg:max-w-lg xl:max-w-xl">
		<!-- Avatar -->
		{#if message.role !== 'user'}
			<div
				class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-r from-blue-500 to-purple-600"
			>
				<svg class="h-5 w-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
					<path
						stroke-linecap="round"
						stroke-linejoin="round"
						stroke-width="2"
						d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"
					></path>
				</svg>
			</div>
		{/if}

		<!-- Message Content -->
		<div class="{message.role === 'user' ? 'order-first' : 'order-last'} relative">
			<div
				class="rounded-2xl px-4 py-3 shadow-sm
                  {message.role === 'user'
					? 'ml-auto bg-blue-600 text-white'
					: 'border border-gray-200 bg-white text-gray-900 dark:border-gray-700 dark:bg-gray-800 dark:text-white'}
                  {message.role === 'system'
					? 'border border-yellow-200 bg-yellow-100 text-yellow-800 dark:border-yellow-800 dark:bg-yellow-900 dark:text-yellow-200'
					: ''}"
			>
				<!-- System message indicator -->
				{#if message.role === 'system'}
					<div class="mb-2 flex items-center space-x-2">
						<svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
							></path>
						</svg>
						<span class="text-xs font-medium tracking-wide uppercase">System</span>
					</div>
				{/if}

				<!-- Message text with markdown rendering and streaming support -->
				<div
					class="prose prose-sm dark:prose-invert max-w-none"
					class:prose-invert={message.role === 'user'}
					class:text-yellow-800={message.role === 'system'}
					class:dark\:text-yellow-200={message.role === 'system'}
					class:dark\:text-gray-300={message.role !== 'system'}
					data-testid="message-content"
				>
					{#if Boolean($streamingSnapshots?.[message.id]?.isStreaming)}
						{#if ($streamingSnapshots?.[message.id]?.textDelta || '').trim()}
							{@html renderContent($streamingSnapshots?.[message.id]?.textDelta || '')}
						{:else}
							<!-- Thinking indicator while first tokens arrive -->
							<div class="flex items-center space-x-1 text-gray-500 dark:text-gray-400">
								<span class="text-sm">AI is thinking</span>
								<div class="flex space-x-1">
									<div
										class="h-2 w-2 animate-bounce rounded-full bg-gray-400"
										style="animation-delay: 0ms"
									></div>
									<div
										class="h-2 w-2 animate-bounce rounded-full bg-gray-400"
										style="animation-delay: 150ms"
									></div>
									<div
										class="h-2 w-2 animate-bounce rounded-full bg-gray-400"
										style="animation-delay: 300ms"
									></div>
								</div>
							</div>
						{/if}

						<!-- Cursor indicator for streaming -->
						<span class="ml-1 inline-block animate-pulse align-middle">▋</span>
					{:else if safeContent}
						{@html safeContent}
					{:else}
						<span
							class="text-[0.8rem] text-gray-400 italic dark:text-gray-500"
							aria-label="Empty message">No content</span
						>
					{/if}
				</div>
			</div>

			<!-- Timestamp -->
			<div
				class="mt-1 text-xs text-gray-500 dark:text-gray-400
                  {message.role === 'user' ? 'text-right' : 'text-left'}"
				data-testid="message-timestamp"
			>
				{formatTime(message.timestamp)}
			</div>
		</div>

		<!-- User Avatar -->
		{#if message.role === 'user'}
			<div class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gray-600">
				<svg class="h-5 w-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
					<path
						stroke-linecap="round"
						stroke-linejoin="round"
						stroke-width="2"
						d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z"
					></path>
				</svg>
			</div>
		{/if}
	</div>
</div>
