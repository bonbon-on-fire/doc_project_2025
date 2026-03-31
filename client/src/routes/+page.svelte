<script lang="ts">
	import { goto } from '$app/navigation';
	import { browser } from '$app/environment';
	import { onMount } from 'svelte';

	onMount(() => {
		// Redirect to chat page (skip during SSR/tests)
		const isTest = typeof import.meta !== 'undefined' && (import.meta as any).env?.MODE === 'test';
		if (browser && !isTest) {
			try {
				goto('/chat');
			} catch {
				// ignore navigation errors in non-app contexts
			}
		}
	});
</script>

<svelte:head>
	<title>AI Chat App</title>
	<meta name="description" content="AI-powered chat application with real-time streaming" />
</svelte:head>

<main class="flex h-screen items-center justify-center bg-gray-100 dark:bg-gray-900">
	<div class="text-center">
		<div class="mx-auto mb-4 h-12 w-12 animate-spin rounded-full border-b-2 border-blue-600"></div>
		<p class="text-gray-600 dark:text-gray-300">Loading AI Chat...</p>
	</div>
</main>
