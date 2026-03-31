<script lang="ts">
	import {
		chats,
		currentChatId,
		chatActions,
		isLoading,
		currentUser,
		isStreaming
	} from '$lib/stores/chat';
	import { formatTime } from '$lib/utils/time';

	function getPreviewText(msg: any): string {
		if (!msg) return '';
		// Support both legacy and new DTOs
		if (typeof msg.text === 'string' && msg.text.trim()) return msg.text;
		if (typeof msg.reasoning === 'string' && msg.reasoning.trim()) return msg.reasoning;
		// Be defensive in case casing slips through
		if (typeof msg.Text === 'string' && msg.Text.trim()) return msg.Text;
		if (typeof msg.Reasoning === 'string' && msg.Reasoning.trim()) return msg.Reasoning;
		return '';
	}

	let newChatMessage = '';

	async function createNewChat() {
		if (!newChatMessage.trim() || $isStreaming) return;

		try {
			await chatActions.streamNewChat(newChatMessage.trim());
			newChatMessage = '';
		} catch (err) {
			console.error('Failed to create chat:', err);
		}
	}

	async function selectChat(chatId: string) {
		if (chatId === $currentChatId) return;
		await chatActions.selectChat(chatId);
	}

	async function deleteChat(chatId: string, event: Event) {
		event.stopPropagation();
		if (confirm('Are you sure you want to delete this chat?')) {
			await chatActions.deleteChat(chatId);
		}
	}

	function formatDate(date: Date | string): string {
		const now = new Date();
		const chatDate = new Date(date);
		const diffInHours = (now.getTime() - chatDate.getTime()) / (1000 * 60 * 60);

		if (diffInHours < 24) {
			// Use the new centralized function for correct local time display.
			return formatTime(chatDate);
		} else if (diffInHours < 24 * 7) {
			return chatDate.toLocaleDateString([], { weekday: 'short' });
		} else {
			return chatDate.toLocaleDateString([], { month: 'short', day: 'numeric' });
		}
	}
</script>

<div class="flex h-full flex-col">
	<!-- Header -->
	<div class="border-b border-gray-200 p-4 dark:border-gray-700">
		<div class="mb-4 flex items-center space-x-3">
			<div class="flex h-8 w-8 items-center justify-center rounded-full bg-blue-600">
				<span class="text-sm font-medium text-white">{$currentUser.name[0]}</span>
			</div>
			<div>
				<p class="font-medium text-gray-900 dark:text-white">{$currentUser.name}</p>
			</div>
		</div>

		<!-- New Chat Input -->
		<form on:submit|preventDefault={createNewChat} class="space-y-2">
			<textarea
				bind:value={newChatMessage}
				placeholder="Start a new conversation..."
				class="w-full resize-none rounded-lg border border-gray-300 bg-white p-3
               text-sm text-gray-900 focus:border-transparent focus:ring-2
               focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-700
               dark:text-white"
				rows="2"
				disabled={$isStreaming}
			></textarea>
			<button
				type="submit"
				disabled={!newChatMessage.trim() || $isStreaming}
				class="w-full rounded-lg bg-blue-600 px-4
               py-2 font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed
               disabled:bg-gray-400"
			>
				{$isStreaming ? 'Creating...' : 'New Chat'}
			</button>
		</form>
	</div>

	<!-- Chat List -->
	<div class="flex-1 overflow-y-auto">
		{#if $isLoading && $chats.length === 0}
			<div class="p-4 text-center text-gray-500 dark:text-gray-400">
				<div class="animate-pulse">Loading chats...</div>
			</div>
		{:else if $chats.length === 0}
			<div class="p-4 text-center text-gray-500 dark:text-gray-400">
				<svg
					class="mx-auto mb-2 h-12 w-12 opacity-50"
					fill="none"
					stroke="currentColor"
					viewBox="0 0 24 24"
				>
					<path
						stroke-linecap="round"
						stroke-linejoin="round"
						stroke-width="2"
						d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-3.582 8-8 8a8.991 8.991 0 01-4.436-1.176L3 20l1.176-5.564A8.991 8.991 0 013 12c0-4.418 3.582-8 8-8s8 3.582 8 8z"
					></path>
				</svg>
				<p>No conversations yet</p>
				<p class="mt-1 text-xs">Start your first chat above</p>
			</div>
		{:else}
			<div class="space-y-1 p-2">
				{#each $chats as chat (chat.id)}
					<div
						class="group relative w-full cursor-pointer rounded-lg p-3 text-left transition-colors
                   {$currentChatId === chat.id
							? 'border-l-4 border-blue-500 bg-blue-100 dark:bg-blue-900'
							: 'hover:bg-gray-100 dark:hover:bg-gray-700'}"
						on:click={() => selectChat(chat.id)}
						on:keydown={(e) => e.key === 'Enter' && selectChat(chat.id)}
						role="button"
						tabindex="0"
						data-testid="chat-item"
					>
						<div class="flex items-start justify-between">
							<div class="min-w-0 flex-1">
								<p
									class="truncate font-medium text-gray-900 dark:text-white"
									data-testid="chat-item-title"
								>
									{chat.title}
								</p>
								{#if chat.messages.length > 0}
									<p class="mt-1 truncate text-sm text-gray-500 dark:text-gray-400">
										{getPreviewText(chat.messages[chat.messages.length - 1])}
									</p>
								{/if}
								<p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
									{formatDate(chat.updatedAt)}
								</p>
							</div>
							<button
								on:click={(e) => deleteChat(chat.id, e)}
								class="ml-2 p-1 text-gray-400 opacity-0 transition-all group-hover:opacity-100 hover:text-red-500"
								title="Delete chat"
								aria-label="Delete chat"
							>
								<svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
									<path
										stroke-linecap="round"
										stroke-linejoin="round"
										stroke-width="2"
										d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
									></path>
								</svg>
							</button>
						</div>
					</div>
				{/each}
			</div>
		{/if}
	</div>
</div>
