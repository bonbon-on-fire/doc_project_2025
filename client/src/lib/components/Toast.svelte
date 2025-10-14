<script lang="ts">
	import { fly, fade } from 'svelte/transition';
	import { toasts, toastActions, type ToastMessage } from '$lib/stores/toast';

	// Props
	export let position: 'top-right' | 'top-left' | 'bottom-right' | 'bottom-left' = 'top-right';
	export let maxToasts = 5; // Maximum number of toasts to show

	// Get toast icon and colors for each type
	function getToastStyle(type: ToastMessage['type']) {
		const styles = {
			success: {
				icon: '✓',
				bgClass: 'bg-green-500 dark:bg-green-600',
				textClass: 'text-white',
				borderClass: 'border-green-600 dark:border-green-500'
			},
			error: {
				icon: '✕',
				bgClass: 'bg-red-500 dark:bg-red-600',
				textClass: 'text-white',
				borderClass: 'border-red-600 dark:border-red-500'
			},
			warning: {
				icon: '⚠',
				bgClass: 'bg-yellow-500 dark:bg-yellow-600',
				textClass: 'text-white',
				borderClass: 'border-yellow-600 dark:border-yellow-500'
			},
			info: {
				icon: 'ℹ',
				bgClass: 'bg-blue-500 dark:bg-blue-600',
				textClass: 'text-white',
				borderClass: 'border-blue-600 dark:border-blue-500'
			}
		};
		return styles[type];
	}

	function getPositionClasses(position: string) {
		const positions = {
			'top-right': 'top-4 right-4',
			'top-left': 'top-4 left-4',
			'bottom-right': 'bottom-4 right-4',
			'bottom-left': 'bottom-4 left-4'
		};
		return positions[position as keyof typeof positions] || positions['top-right'];
	}

	// Limit number of displayed toasts
	$: displayedToasts = $toasts.slice(-maxToasts);
</script>

<!-- Toast container -->
<div
	class="pointer-events-none fixed z-50 flex flex-col space-y-2 {getPositionClasses(position)}"
	role="region"
	aria-label="Notifications"
	aria-live="polite"
>
	{#each displayedToasts as toast (toast.id)}
		{@const style = getToastStyle(toast.type)}
		<div
			class="pointer-events-auto flex w-full max-w-sm items-center {style.bgClass} {style.textClass} 
				rounded-lg border shadow-lg {style.borderClass} overflow-hidden"
			role="alert"
			aria-describedby="toast-{toast.id}"
			transition:fly={{
				x: position.includes('right') ? 300 : -300,
				duration: 300
			}}
		>
			<!-- Toast content -->
			<div class="flex flex-1 items-center p-4">
				<!-- Icon -->
				<div class="flex h-6 w-6 flex-shrink-0 items-center justify-center">
					<span class="text-lg font-bold" aria-hidden="true">
						{style.icon}
					</span>
				</div>

				<!-- Message -->
				<div class="ml-3 flex-1">
					<p id="toast-{toast.id}" class="text-sm font-medium">
						{toast.message}
					</p>
				</div>

				<!-- Dismiss button -->
				{#if toast.dismissible}
					<button
						on:click={() => toastActions.dismiss(toast.id)}
						class="ml-4 inline-flex rounded text-white hover:text-gray-200
							focus:outline-none focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-current"
						aria-label="Dismiss notification"
					>
						<span class="sr-only">Close</span>
						<svg class="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
							<path
								fill-rule="evenodd"
								d="M4.293 4.293a1 1 0 011.414 0L10 8.586l4.293-4.293a1 1 0 111.414 1.414L11.414 10l4.293 4.293a1 1 0 01-1.414 1.414L10 11.414l-4.293 4.293a1 1 0 01-1.414-1.414L8.586 10 4.293 5.707a1 1 0 010-1.414z"
								clip-rule="evenodd"
							/>
						</svg>
					</button>
				{/if}
			</div>
		</div>
	{/each}
</div>
