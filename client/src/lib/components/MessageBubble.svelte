<script lang="ts">
	import type { RichMessageDto } from '$lib/types/chat';
	import { formatTime } from '$lib/utils/time';
	import { streamingSnapshots } from '$lib/stores/chat';
	import MarkdownRenderer from './MarkdownRenderer.svelte';

	export let message: RichMessageDto;
	export let isLastAssistantMessage = false;

	// Additional props for compatibility with MessageRouter
	// removed unused props to avoid build warnings
	$: if (isLastAssistantMessage !== undefined) {
		// This prop is provided by MessageRouter for potential future enhancements
	}

	function getMessageText(msg: any): string {
		if (!msg) return '';
		// Support new DTOs (camelCase)
		if (typeof (msg as any).text === 'string' && (msg as any).text.trim()) return (msg as any).text;
		if (typeof (msg as any).reasoning === 'string' && (msg as any).reasoning.trim())
			return (msg as any).reasoning;
		// Legacy/casing fallbacks from server serialization
		if (typeof (msg as any).Text === 'string' && (msg as any).Text.trim()) return (msg as any).Text;
		if (typeof (msg as any).Reasoning === 'string' && (msg as any).Reasoning.trim())
			return (msg as any).Reasoning;
		return '';
	}

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
</script>

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

				<!-- Message text with elegant markdown rendering -->
				<div class={message.role === 'system' ? 'text-yellow-800 dark:text-yellow-200' : ''}>
					{#if Boolean($streamingSnapshots?.[message.id]?.isStreaming)}
						{@const reasoningDelta = $streamingSnapshots?.[message.id]?.reasoningDelta || ''}
						{@const textDelta = $streamingSnapshots?.[message.id]?.textDelta || ''}
						{@const reasoningShouldUseMarkdown =
							message.role !== 'user' || containsMarkdownSyntax(reasoningDelta)}
						{@const textShouldUseMarkdown =
							message.role !== 'user' || containsMarkdownSyntax(textDelta)}

						{#if reasoningDelta.trim()}
							<div
								class="mb-2 border-l-2 border-gray-300 pl-2 text-xs dark:border-gray-600"
								data-testid="reasoning-content"
							>
								{#if reasoningShouldUseMarkdown}
									<MarkdownRenderer
										content={reasoningDelta}
										size="sm"
										theme={message.role === 'user' ? 'user' : 'auto'}
									/>
								{:else}
									<span class="whitespace-pre-wrap break-words">{reasoningDelta}</span>
								{/if}
							</div>
						{/if}
						<div data-testid="message-content">
							{#if textShouldUseMarkdown}
								<MarkdownRenderer
									content={textDelta}
									size="sm"
									theme={message.role === 'user' ? 'user' : 'auto'}
								/>
							{:else}
								<span class="whitespace-pre-wrap break-words">{textDelta}</span>
							{/if}
						</div>
						<span class="animate-pulse">▋</span>
					{:else}
						{@const messageText = getMessageText(message)}
						{@const shouldUseMarkdown =
							message.role !== 'user' || containsMarkdownSyntax(messageText)}

						<div data-testid="message-content">
							{#if shouldUseMarkdown}
								<MarkdownRenderer
									content={messageText}
									size="sm"
									theme={message.role === 'user' ? 'user' : 'auto'}
								/>
							{:else}
								<span class="whitespace-pre-wrap break-words">{messageText}</span>
							{/if}
						</div>
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
