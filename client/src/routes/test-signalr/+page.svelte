<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import {
		signalRState,
		signalRActions,
		connectionStatusMessage,
		connectionStatusColor
	} from '$lib/stores/signalr';

	let userId = 'test-user-' + Math.random().toString(36).substr(2, 9);
	let isConnecting = false;
	let messages: string[] = [];
	let messageInput = '';

	async function connect() {
		isConnecting = true;
		messages.push(`Connecting as ${userId}...`);
		try {
			await signalRActions.connect(userId);
			messages.push('Connected successfully!');
		} catch (error) {
			messages.push(`Connection failed: ${error}`);
		} finally {
			isConnecting = false;
		}
	}

	async function disconnect() {
		messages.push('Disconnecting...');
		await signalRActions.disconnect();
		messages.push('Disconnected');
	}

	async function sendTestMessage() {
		if (!messageInput.trim()) return;

		try {
			await signalRActions.sendChatMessage('test-chat', messageInput);
			messages.push(`Sent: ${messageInput}`);
			messageInput = '';
		} catch (error) {
			messages.push(`Failed to send: ${error}`);
		}
	}

	function clearMessages() {
		messages = [];
	}

	// Subscribe to message queue
	const unsubscribe = signalRState.subscribe((state) => {
		if (state.messageQueue.length > 0) {
			state.messageQueue.forEach((msg) => {
				messages.push(`Received: ${JSON.stringify(msg)}`);
			});
			signalRActions.clearMessageQueue();
		}
	});

	onDestroy(() => {
		unsubscribe();
	});
</script>

<div class="mx-auto max-w-4xl p-4">
	<h1 class="mb-4 text-2xl font-bold">SignalR Test Page</h1>

	<!-- Connection Status -->
	<div class="mb-4 rounded-lg border p-4">
		<h2 class="mb-2 text-lg font-semibold">Connection Status</h2>
		<div class="flex items-center gap-2">
			<div class="h-3 w-3 rounded-full" style="background-color: {$connectionStatusColor}"></div>
			<span>{$connectionStatusMessage}</span>
		</div>
		<div class="mt-2">
			<span class="text-sm text-gray-600">State: {$signalRState.connectionState}</span>
			{#if $signalRState.lastError}
				<div class="mt-1 text-sm text-red-600">Error: {$signalRState.lastError}</div>
			{/if}
		</div>
	</div>

	<!-- Controls -->
	<div class="mb-4 flex gap-2">
		<button
			on:click={connect}
			disabled={$signalRState.isConnected || isConnecting}
			class="rounded bg-blue-500 px-4 py-2 text-white disabled:bg-gray-300"
		>
			Connect
		</button>
		<button
			on:click={disconnect}
			disabled={!$signalRState.isConnected}
			class="rounded bg-red-500 px-4 py-2 text-white disabled:bg-gray-300"
		>
			Disconnect
		</button>
		<button on:click={clearMessages} class="rounded bg-gray-500 px-4 py-2 text-white">
			Clear Messages
		</button>
	</div>

	<!-- Send Message -->
	<div class="mb-4">
		<div class="flex gap-2">
			<input
				type="text"
				bind:value={messageInput}
				placeholder="Enter test message"
				class="flex-1 rounded border px-3 py-2"
				on:keydown={(e) => e.key === 'Enter' && sendTestMessage()}
			/>
			<button
				on:click={sendTestMessage}
				disabled={!$signalRState.isConnected}
				class="rounded bg-green-500 px-4 py-2 text-white disabled:bg-gray-300"
			>
				Send
			</button>
		</div>
	</div>

	<!-- Message Log -->
	<div class="h-64 overflow-y-auto rounded-lg border p-4">
		<h2 class="mb-2 text-lg font-semibold">Message Log</h2>
		{#if messages.length === 0}
			<p class="text-gray-500">No messages yet</p>
		{:else}
			{#each messages as message}
				<div class="mb-1 font-mono text-sm">{message}</div>
			{/each}
		{/if}
	</div>

	<!-- Debug Info -->
	<details class="mt-4">
		<summary class="cursor-pointer text-sm text-gray-600">Debug Info</summary>
		<pre class="mt-2 overflow-x-auto rounded bg-gray-100 p-2 text-xs">{JSON.stringify(
				$signalRState,
				null,
				2
			)}</pre>
	</details>
</div>
