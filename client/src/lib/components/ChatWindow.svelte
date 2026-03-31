<script lang="ts">
	import { onMount, afterUpdate } from 'svelte';
	import {
		currentChat,
		currentChatId,
		currentChatMessages,
		chatActions,
		isStreaming
	} from '$lib/stores/chat';
	import MessageList from './MessageList.svelte';
	import MessageInput from './MessageInput.svelte';
	import PinnedTaskTracker from './PinnedTaskTracker.svelte';

	let messagesContainer: HTMLElement;
	let isSending = false;
	let userHasScrolled = false;
	let previousMessageCount = 0;
	let isAutoScrolling = false;
	let previousStreamingState = false;

	// Track total message count for detecting new messages
	$: totalMessages = $currentChatMessages.length;

	// Watch for messages array changes to trigger scroll (less aggressive)
	$: if ($currentChatMessages && $currentChatMessages.length > 0) {
		// Trigger scroll when messages are loaded/updated but with fewer attempts
		requestAnimationFrame(() => {
			setTimeout(() => {
				if (!userHasScrolled) {
					scrollToBottom(true);
				}
			}, 200);
		});
	}

	// Watch for streaming state changes (more conservative)
	$: {
		if ($isStreaming && !previousStreamingState) {
			// Only auto-scroll during streaming if user is close to bottom and hasn't manually scrolled
			const isNearBottom = messagesContainer
				? messagesContainer.scrollHeight -
						messagesContainer.scrollTop -
						messagesContainer.clientHeight <
					200
				: false;
			if (isNearBottom && !userHasScrolled) {
				setTimeout(() => scrollToBottom(), 200);
			}
		}
		previousStreamingState = $isStreaming;
	}

	// Watch for conversation changes to reset scroll (more conservative)
	$: if ($currentChatId) {
		userHasScrolled = false;
		previousMessageCount = 0;
		// Single scroll attempt when switching conversations
		setTimeout(() => scrollToBottom(true), 400);
	}

	const scrollToBottom = (force = false) => {
		if (messagesContainer && messagesContainer.scrollHeight && (!userHasScrolled || force)) {
			isAutoScrolling = true;

			// Use requestAnimationFrame to ensure DOM is updated, then scroll
			requestAnimationFrame(() => {
				const target = messagesContainer.scrollHeight;
				messagesContainer.scrollTop = target;
				messagesContainer.scrollTo({ top: target, behavior: 'auto' });
			});

			// Reset the flag after scrolling
			setTimeout(() => {
				isAutoScrolling = false;
			}, 300);
		}
	};

	const handleScroll = () => {
		if (!messagesContainer || !messagesContainer.scrollHeight || isAutoScrolling) return;

		// Check if user is near the bottom (within 200px - more generous threshold)
		const distanceFromBottom =
			messagesContainer.scrollHeight - messagesContainer.scrollTop - messagesContainer.clientHeight;

		const isNearBottom = distanceFromBottom < 200;

		// Only update userHasScrolled if user has scrolled significantly away from bottom
		// This prevents the auto-scroll from being too aggressive
		userHasScrolled = !isNearBottom;
	};

	// Also listen for wheel/touchstart to mark manual interaction sooner
	const markManualScroll = () => {
		// Immediately mark that user has scrolled manually
		// This prevents auto-scroll from interfering with user intent
		userHasScrolled = true;
	};

	onMount(() => {
		previousMessageCount = totalMessages;

		// Attach scroll listener to the messages container
		if (messagesContainer) {
			messagesContainer.addEventListener('scroll', handleScroll);
			messagesContainer.addEventListener('wheel', markManualScroll, { passive: true });
			messagesContainer.addEventListener('touchstart', markManualScroll, { passive: true });

			// Force scroll to bottom after DOM is fully rendered (single attempt)
			requestAnimationFrame(() => {
				setTimeout(() => scrollToBottom(true), 300);
			});

			// Cleanup on component destroy
			return () => {
				messagesContainer.removeEventListener('scroll', handleScroll);
				messagesContainer.removeEventListener('wheel', markManualScroll as any);
				messagesContainer.removeEventListener('touchstart', markManualScroll as any);
			};
		}
	});

	// Auto-scroll when new messages arrive or during streaming (simplified)
	afterUpdate(() => {
		// Auto-scroll in two scenarios:
		// 1. New messages arrived (message count increased)
		// 2. Currently streaming (content is being updated)
		const hasNewMessages = totalMessages > previousMessageCount;

		if (hasNewMessages || $isStreaming) {
			// Only auto-scroll if user hasn't manually scrolled away
			if (!userHasScrolled) {
				setTimeout(() => scrollToBottom(), 100);
			}
		}

		// If this is initial load (no previous messages), force scroll once
		if (totalMessages > 0 && previousMessageCount === 0) {
			setTimeout(() => scrollToBottom(true), 400);
		}

		// Update previous message count
		if (hasNewMessages) {
			previousMessageCount = totalMessages;
		}
	});

	// Removed duplicate onMount that re-attached listeners and forced additional scrolls

	async function handleSend(event: CustomEvent<{ message: string }>) {
		// Allow sending even if streaming is in progress

		// Reset user scroll state when sending a new message
		userHasScrolled = false;
		// Immediately move to bottom so the latest input is in view
		scrollToBottom(true);

		if ($currentChatId) {
			await chatActions.streamReply(event.detail.message);
		} else {
			await chatActions.streamNewChat(event.detail.message);
		}
	}
</script>

<div class="flex h-full flex-col">
	{#if !$currentChat}
		<!-- No Chat Selected -->
		<div class="flex flex-1 items-center justify-center bg-gray-50 dark:bg-gray-800">
			<div class="max-w-md text-center">
				<svg
					class="mx-auto mb-4 h-16 w-16 text-gray-400 dark:text-gray-500"
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
				<h2 class="mb-2 text-xl font-semibold text-gray-900 dark:text-white">Welcome to AI Chat</h2>
				<p class="mb-4 text-gray-600 dark:text-gray-300">
					Select a conversation from the sidebar or start a new chat to begin.
				</p>
				<div class="space-y-2 text-sm text-gray-500 dark:text-gray-400">
					<p>Real-time AI conversations</p>
					<p>Streaming responses</p>
					<p>Chat history saved</p>
				</div>
			</div>
		</div>
	{:else}
		<!-- Chat Header -->
		<div class="border-b border-gray-200 bg-white px-6 py-4 dark:border-gray-700 dark:bg-gray-800">
			<div class="flex items-center justify-between">
				<div>
					<h1 class="text-lg font-semibold text-gray-900 dark:text-white">
						{$currentChat.title}
					</h1>
					<p class="text-sm text-gray-500 dark:text-gray-400" data-testid="message-count">
						{$currentChatMessages.length} messages
					</p>
				</div>
				<div class="flex items-center space-x-2">
					<!-- Connection Status -->
					<div class="flex items-center space-x-2">
						<!-- Removed SignalR connection status -->
					</div>
				</div>
			</div>
		</div>

		<!-- Messages Area -->
		<div
			bind:this={messagesContainer}
			class="flex-1 overflow-y-auto bg-gray-50 dark:bg-gray-900"
			data-testid="message-list"
		>
			<MessageList messages={$currentChatMessages} />
		</div>

		<!-- Message Input Area with Task Tracker -->
		<div class="border-t border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
			<!-- Pinned Task Tracker (above input) -->
			{#if $currentChatId}
				<PinnedTaskTracker chatId={$currentChatId} />
			{/if}

			<!-- Message Input -->
			<MessageInput
				on:send={handleSend}
				disabled={false}
				placeholder={$isStreaming ? 'AI is thinking...' : 'Type your message...'}
			/>
		</div>
	{/if}
</div>
