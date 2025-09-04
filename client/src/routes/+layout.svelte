<script lang="ts">
	import { onMount } from 'svelte';
	import '../app.css';
	import favicon from '$lib/assets/favicon.svg';
	import { registerBuiltInRenderers, registerBuiltInToolRenderers } from '$lib/renderers';
	import Toast from '$lib/components/Toast.svelte';
	import ConnectionStatus from '$lib/components/ConnectionStatus.svelte';

	let { children } = $props();

	// Check if SignalR mode is enabled
	const isSignalRMode = import.meta.env.VITE_COMM_MODE === 'signalr';

	// Register built-in renderers when the app starts
	onMount(() => {
		registerBuiltInRenderers();
		registerBuiltInToolRenderers();
	});
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children?.()}

<!-- SignalR Connection Status (only in SignalR mode) -->
{#if isSignalRMode}
	<ConnectionStatus />
{/if}

<!-- Toast notifications -->
<Toast position="top-right" />
