<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import type { MessageDto, TextMessageDto, RichMessageDto } from '$lib/types';
	import type { MessageRenderer } from '$lib/types/renderer';
	import { formatTime } from '$lib/utils/time';
	import { streamingSnapshots } from '$lib/stores/chat';

	// Component props with proper TypeScript typing
	export let message: TextMessageDto & RichMessageDto;
	export let isLastAssistantMessage: boolean = false;

	// Acknowledge the prop to avoid unused warning
	$: if (isLastAssistantMessage !== undefined) {
		// This prop is used by MessageRouter for potential UX enhancements
	}

	// Create event dispatcher for custom events
	const dispatch = createEventDispatcher<{
		stateChange: { expanded: boolean };
	}>();

	// Import the elegant markdown renderer
	import MarkdownRenderer from './MarkdownRenderer.svelte';

	$: messageText = (message as TextMessageDto).text || '';

	// Heuristic to detect if text contains markdown syntax
	function containsMarkdownSyntax(text: string): boolean {
		if (!text || typeof text !== 'string') return false;

		// Check for common markdown patterns
		const markdownPatterns = [
			/#{1,6}\s+/, // Headers: # ## ### etc.
			/\*\*.*?\*\*/, // Bold: **text**
			/\*(?!\d).*?(?<!\d)\*/, // Italic: *text* (but not *123* which could be math)
			/__.*?__/, // Bold: __text__
			/_(?!\d).*?(?<!\d)_/, // Italic: _text_ (but not _123_ which could be math)
			/`.*?`/, // Inline code: `code`
			/```[\s\S]*?```/, // Code blocks: ```code```
			/\[.*?\]\(.*?\)/, // Links: [text](url)
			/^\s*[-+]\s+/m, // Unordered lists: - + (excluding * to avoid math conflicts)
			/^\s*\d+\.\s+/m, // Ordered lists: 1. 2. 3.
			/^\s*>\s+/m, // Blockquotes: >
			/\$\$[\s\S]*?\$\$/, // Math blocks: $$equation$$
			/\\\([\s\S]*?\\\)/, // Inline math: \(equation\)
			/\\\[[\s\S]*?\\\]/, // Math blocks: \[equation\]
			/\|.*?\|/, // Tables: | column |
			/---+/, // Horizontal rules: ---
			/~~.*?~~/ // Strikethrough: ~~text~~
		];

		return markdownPatterns.some((pattern) => pattern.test(text));
	}

	$: shouldUseMarkdown = message.role !== 'user' || containsMarkdownSyntax(messageText);

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
						<span class="text-xs font-medium uppercase tracking-wide">System</span>
					</div>
				{/if}

				<!-- Message text with elegant markdown rendering and streaming support -->
				<div
					data-testid="message-content"
					class={message.role === 'system' ? 'text-yellow-800 dark:text-yellow-200' : ''}
				>
					{#if Boolean($streamingSnapshots?.[message.id]?.isStreaming)}
						{#if ($streamingSnapshots?.[message.id]?.textDelta || '').trim()}
							{@const streamingText = $streamingSnapshots?.[message.id]?.textDelta || ''}
							{@const streamingShouldUseMarkdown =
								message.role !== 'user' || containsMarkdownSyntax(streamingText)}
							{#if streamingShouldUseMarkdown}
								<MarkdownRenderer
									content={streamingText}
									size="sm"
									theme={message.role === 'user' ? 'user' : 'auto'}
								/>
							{:else}
								<span class="whitespace-pre-wrap break-words">{streamingText}</span>
							{/if}
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
					{:else if messageText.trim()}
						{#if shouldUseMarkdown}
							<MarkdownRenderer
								content={messageText}
								size="sm"
								theme={message.role === 'user' ? 'user' : 'auto'}
							/>
						{:else}
							<span class="whitespace-pre-wrap break-words">{messageText}</span>
						{/if}
					{:else}
						<span
							class="text-[0.8rem] italic text-gray-400 dark:text-gray-500"
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
