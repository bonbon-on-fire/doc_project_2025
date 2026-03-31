<script lang="ts">
	import { createEventDispatcher } from 'svelte';
	import { slide } from 'svelte/transition';
	import { quintOut } from 'svelte/easing';
	import { formatTime } from '$lib/utils/time';

	// Component props
	export let message: any; // Generic message type
	export let isLatest: boolean = false;
	export let expanded: boolean = true;
	export let collapsible: boolean = true;

	// Component assets
	export let iconPath: string; // SVG path for the icon
	export let iconColors: string = 'from-blue-500 to-purple-600'; // Gradient colors
	export let messageType: string = 'Message'; // Display name
	export let collapsedPreview: string = ''; // One-line preview when collapsed

	// Theme colors
	export let borderColor: string = 'border-gray-200';
	export let bgColor: string = 'bg-white';
	export let textColor: string = 'text-gray-900';
	export let darkBorderColor: string = 'dark:border-gray-700';
	export let darkBgColor: string = 'dark:bg-gray-800';
	export let darkTextColor: string = 'dark:text-white';

	const dispatch = createEventDispatcher<{
		stateChange: { expanded: boolean };
		toggleExpansion: { expanded: boolean };
	}>();

	let hasManuallyToggled = false;

	function toggle() {
		if (!collapsible) return;

		expanded = !expanded;
		hasManuallyToggled = true;

		dispatch('stateChange', { expanded });
		dispatch('toggleExpansion', { expanded });
	}

	function handleKeydown(event: KeyboardEvent) {
		if (!collapsible) return;
		if (event.key === 'Enter' || event.key === ' ') {
			event.preventDefault();
			toggle();
		}
	}
</script>

<div class="flex {message.role === 'user' ? 'justify-end' : 'justify-start'}">
	<div class="flex max-w-xs items-start space-x-3 sm:max-w-md lg:max-w-lg xl:max-w-xl">
		<!-- Avatar - clickable when collapsible -->
		{#if message.role !== 'user'}
			<div class="relative">
				<button
					class="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-r {iconColors} {collapsible
						? 'cursor-pointer transition-all duration-200 hover:scale-105'
						: ''} focus:ring-2 focus:ring-amber-500 focus:ring-offset-2 focus:outline-none"
					class:opacity-70={!expanded && collapsible}
					on:click={toggle}
					on:keydown={handleKeydown}
					tabindex={collapsible ? 0 : -1}
					aria-expanded={expanded}
					aria-label={expanded ? `Collapse ${messageType}` : `Expand ${messageType}`}
					data-testid={`${messageType.toLowerCase()}-toggle-button`}
					disabled={!collapsible}
				>
					<svg class="h-5 w-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
						<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d={iconPath} />
					</svg>
				</button>

				<!-- Expand/collapse indicator -->
				{#if collapsible}
					<div
						class="absolute -right-0.5 -bottom-0.5 flex h-4 w-4 items-center justify-center rounded-full border border-gray-200 bg-white shadow-md"
					>
						<svg
							class="h-2.5 w-2.5 transform text-gray-600 transition-transform duration-200"
							class:rotate-90={expanded}
							fill="currentColor"
							viewBox="0 0 20 20"
						>
							<path
								fill-rule="evenodd"
								d="M7.293 14.707a1 1 0 010-1.414L10.586 10 7.293 6.707a1 1 0 011.414-1.414l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414 0z"
								clip-rule="evenodd"
							/>
						</svg>
					</div>
				{/if}
			</div>
		{/if}

		<!-- Message Content -->
		<div class="{message.role === 'user' ? 'order-first' : 'order-last'} relative flex-1">
			{#if !expanded && collapsible}
				<!-- Collapsed state: one-line preview -->
				<div
					class="rounded-2xl border px-4 py-2 shadow-sm {borderColor} {bgColor} {textColor} {darkBorderColor} {darkBgColor} {darkTextColor} cursor-pointer transition-shadow duration-200 hover:shadow-md"
					on:click={toggle}
					on:keydown={handleKeydown}
					tabindex="0"
					role="button"
					aria-expanded="false"
					aria-label="Expand {messageType}"
				>
					<div class="flex items-center space-x-2">
						<span class="text-xs font-medium tracking-wide uppercase opacity-75">{messageType}</span
						>
						<span class="flex-1 truncate text-sm">{collapsedPreview}</span>
						<span class="text-xs opacity-50">▶</span>
					</div>
				</div>
			{:else}
				<!-- Expanded state: full content -->
				<div
					class="rounded-2xl border px-4 py-3 shadow-sm {borderColor} {bgColor} {textColor} {darkBorderColor} {darkBgColor} {darkTextColor}"
					transition:slide={{ duration: 200, easing: quintOut }}
				>
					<slot />
				</div>
			{/if}

			<!-- Timestamp -->
			<div
				class="mt-1 text-xs text-gray-500 dark:text-gray-400 {message.role === 'user'
					? 'text-right'
					: 'text-left'}"
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
					/>
				</svg>
			</div>
		{/if}
	</div>
</div>
