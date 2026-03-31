<script lang="ts">
	import type {
		MessageDto,
		TextMessageDto,
		ReasoningMessageDto,
		UsageMessageDto
	} from '$lib/types/chat';
	import MessageRouter from './MessageRouter.svelte';
	import { isStreaming } from '$lib/stores/chat';
	export let messages: (MessageDto | TextMessageDto | ReasoningMessageDto | UsageMessageDto)[] = [];

	// Filter out hidden messages for display while preserving sequence integrity
	$: visibleMessages = messages.filter((message) => !message.isHidden);

	let chatContainer: HTMLDivElement;
</script>

<div bind:this={chatContainer} class="mx-auto max-w-4xl space-y-6 px-4 py-6">
	{#if visibleMessages.length === 0}
		<!-- Empty State -->
		<div class="py-12 text-center">
			<svg
				class="mx-auto mb-4 h-12 w-12 text-gray-400 dark:text-gray-500"
				fill="none"
				stroke="currentColor"
				viewBox="0 0 24 24"
			>
				<path
					stroke-linecap="round"
					stroke-linejoin="round"
					stroke-width="2"
					d="M8 10h.01M12 10h.01M16 10h.01M9 16H5a2 2 0 01-2-2V6a2 2 0 012-2h14a2 2 0 012 2v8a2 2 0 01-2 2h-5l-5 5v-5z"
				></path>
			</svg>
			<p class="text-gray-500 dark:text-gray-400">Start the conversation</p>
		</div>
	{:else}
		<!-- Messages -->
		{#each visibleMessages as message, index (message.id)}
			<MessageRouter {message} isLatest={index === visibleMessages.length - 1} />
		{/each}

		<!-- Trailing streaming indicator when awaiting assistant response -->
		{#if $isStreaming && visibleMessages.length > 0 && visibleMessages[visibleMessages.length - 1]?.role === 'user'}
			<div class="flex justify-start">
				<div class="flex max-w-xs items-start space-x-3 sm:max-w-md lg:max-w-lg xl:max-w-xl">
					<!-- AI Avatar -->
					<div
						class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-r from-blue-500 to-purple-600"
					>
						<svg class="h-5 w-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"
							/>
						</svg>
					</div>

					<!-- Typing indicator bubble -->
					<div class="relative order-last">
						<div
							class="rounded-2xl border border-gray-200 bg-white px-4 py-3 text-gray-900 shadow-sm dark:border-gray-700 dark:bg-gray-800 dark:text-white"
						>
							<div class="flex items-center space-x-2 text-gray-500 dark:text-gray-400">
								<span class="text-sm">AI is typing</span>
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
						</div>
					</div>
				</div>
			</div>
		{/if}
	{/if}
</div>
