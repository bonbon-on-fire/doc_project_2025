<script lang="ts">
	import { createEventDispatcher } from 'svelte';

	export let disabled = false;
	export let placeholder = 'Type your message...';

	const dispatch = createEventDispatcher<{
		send: { message: string };
	}>();

	let message = '';
	let textareaElement: HTMLTextAreaElement;

	function handleSubmit() {
		if (!message.trim() || disabled) return;

		dispatch('send', { message: message.trim() });
		message = '';
		autoResize();
	}

	function handleKeyDown(event: KeyboardEvent) {
		if (event.key === 'Enter' && !event.shiftKey) {
			event.preventDefault();
			handleSubmit();
		}
	}

	function autoResize() {
		if (textareaElement) {
			textareaElement.style.height = 'auto'; // Reset height
			textareaElement.style.height = `${textareaElement.scrollHeight}px`;
		}
	}

	// Auto-resize on input
	$: if (textareaElement && message !== undefined) {
		autoResize();
	}
</script>

<div class="p-4">
	<form on:submit|preventDefault={handleSubmit} class="relative">
		<div class="flex items-end space-x-4">
			<!-- Message Input -->
			<div class="relative flex-1">
				<textarea
					bind:this={textareaElement}
					bind:value={message}
					on:keydown={handleKeyDown}
					on:input={autoResize}
					{disabled}
					{placeholder}
					class="w-full resize-none overflow-y-auto rounded-xl border border-gray-300 bg-white p-3
                 pr-12 text-sm text-gray-900 focus:border-transparent
                 focus:ring-2 focus:ring-blue-500 disabled:cursor-not-allowed
                 disabled:bg-gray-100 dark:border-gray-600 dark:bg-gray-700
                 dark:text-white dark:disabled:bg-gray-800"
					rows="1"
					style="min-height: 44px; max-height: 6rem;"
				></textarea>

				<!-- Character count (optional) -->
				{#if message.length > 1000}
					<div class="absolute bottom-1 left-3 text-xs text-gray-400">
						{message.length}/2000
					</div>
				{/if}
			</div>

			<!-- Send Button -->
			<button
				type="submit"
				disabled={!message.trim() || disabled}
				class="flex h-11 w-11 flex-shrink-0 items-center justify-center
               rounded-xl bg-blue-600 text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed
               disabled:bg-gray-400"
				title="Send message (Enter)"
			>
				{#if disabled}
					<svg class="h-5 w-5 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							stroke-width="2"
							d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
						></path>
					</svg>
				{:else}
					<svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							stroke-width="2"
							d="M12 19l9 2-9-18-9 18 9-2zm0 0v-8"
						></path>
					</svg>
				{/if}
			</button>
		</div>

		<!-- Hint Text -->
		<div class="mt-2 flex items-center justify-between text-xs text-gray-500 dark:text-gray-400">
			<span>Press Enter to send, Shift+Enter for new line</span>
			{#if !disabled}
				<span class="flex items-center space-x-1">
					<div class="h-2 w-2 rounded-full bg-green-500"></div>
					<span>Connected</span>
				</span>
			{/if}
		</div>
	</form>
</div>
