<script lang="ts">
	import { onMount } from 'svelte';
	import '../app.css';
	import favicon from '$lib/assets/favicon.svg';
	import { registerBuiltInRenderers, registerBuiltInToolRenderers } from '$lib/renderers';

	let { children } = $props();

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
