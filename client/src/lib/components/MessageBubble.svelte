<script lang="ts">
	import type { RichMessageDto } from '$lib/types/chat';
	import { formatTime } from '$lib/utils/time';
	import { streamingSnapshots } from '$lib/stores/chat';

	export let message: RichMessageDto;
	export let isLastAssistantMessage = false;

	// Additional props for compatibility with MessageRouter
	// removed unused props to avoid build warnings

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
						<span class="text-xs font-medium tracking-wide uppercase">System</span>
					</div>
				{/if}

				<!-- Message text with basic formatting -->
				<div
					class="prose prose-sm dark:prose-invert max-w-none"
					class:prose-invert={message.role === 'user'}
					class:text-yellow-800={message.role === 'system'}
					class:dark\:text-yellow-200={message.role === 'system'}
					class:dark\:text-gray-300={message.role !== 'system'}
				>
					{#if Boolean($streamingSnapshots?.[message.id]?.isStreaming)}
						<!-- Debug logging for streaming condition -->
						{#if typeof console !== 'undefined'}
							{console.log('[MessageBubble] Streaming condition met:', {
								isLastAssistantMessage,
								isStreaming: Boolean($streamingSnapshots?.[message.id]?.isStreaming),
								messageRole: message.role,
								textDelta: $streamingSnapshots?.[message.id]?.textDelta,
								reasoningDelta: $streamingSnapshots?.[message.id]?.reasoningDelta
							})}
						{/if}
						{#if ($streamingSnapshots?.[message.id]?.reasoningDelta || '').trim()}
							<div
								class="mb-2 border-l-2 border-gray-300 pl-2 text-xs text-gray-500 dark:border-gray-600 dark:text-gray-400"
								data-testid="reasoning-content"
							>
								{@html formatContent($streamingSnapshots?.[message.id]?.reasoningDelta || '')}
							</div>
						{/if}
						<div data-testid="message-content">
							{@html formatContent($streamingSnapshots?.[message.id]?.textDelta || '')}
						</div>
						<span class="animate-pulse">▋</span>
					{:else}
						<!-- Debug logging for non-streaming condition -->
						{#if typeof console !== 'undefined'}
							{console.log('[MessageBubble] Non-streaming condition:', {
								isLastAssistantMessage,
								isStreaming: Boolean($streamingSnapshots?.[message.id]?.isStreaming),
								messageRole: message.role,
								messageText: getMessageText(message)
							})}
						{/if}
						<div data-testid="message-content">{@html formatContent(getMessageText(message))}</div>
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
