<script lang="ts">
	import { createEventDispatcher, onMount } from 'svelte';
	import { fly, scale } from 'svelte/transition';
	import type { Mode, ModeCategory } from '$shared/types/mode';
	import {
		availableModes,
		selectedModeId,
		selectedMode,
		groupedModes,
		modesLoading,
		modesError,
		unavailableTools,
		availableTools,
		modeActions
	} from '$lib/stores/modes';
	import { logger } from '$lib/utils/logger';

	// Props - following single responsibility principle
	export let disabled = false;
	export let compact = false;
	export let showTooltip = true;
	export let autoFetch = true;

	// Component state - minimal and focused
	let isOpen = false;
	let dropdownElement: HTMLElement;
	let selectedElement: HTMLElement;
	let showingTooltip = false;
	let tooltipTimeout: ReturnType<typeof setTimeout>;
	let modeChangeAnimation = false;

	// Event dispatcher for loose coupling
	const dispatch = createEventDispatcher<{
		modeSelected: { mode: Mode | null; previousMode: Mode | null };
		modeChanged: { modeId: string | null };
		error: { message: string };
	}>();

	// Track previous mode for change detection
	let previousSelectedMode: Mode | null = null;

	// Reactive statements for state management
	$: if ($selectedMode) {
		// Trigger animation on mode change
		if (previousSelectedMode && previousSelectedMode.id !== $selectedMode.id) {
			modeChangeAnimation = true;
			setTimeout(() => {
				modeChangeAnimation = false;
			}, 500);
		}
		previousSelectedMode = $selectedMode;

		dispatch('modeSelected', {
			mode: $selectedMode,
			previousMode: previousSelectedMode
		});
		dispatch('modeChanged', { modeId: $selectedMode.id });
	}

	$: if ($modesError) {
		dispatch('error', { message: $modesError });
	}

	// Mode selection handler - following command pattern
	function handleModeSelect(mode: Mode | null) {
		try {
			const previousMode = $selectedMode;
			modeActions.selectMode(mode?.id || null);

			// Close dropdown after selection
			isOpen = false;

			logger.info('Mode selected from UI', {
				modeId: mode?.id,
				name: mode?.name,
				previous: previousMode?.id
			});
		} catch (error) {
			logger.error('Error selecting mode', { error, modeId: mode?.id });
			dispatch('error', { message: 'Failed to select mode' });
		}
	}

	// Dropdown toggle handler
	function toggleDropdown() {
		if (disabled || $modesLoading) return;
		isOpen = !isOpen;
	}

	// Tooltip handlers for better UX
	function showModeTooltip(mode: Mode) {
		if (!showTooltip || compact) return;
		clearTimeout(tooltipTimeout);
		tooltipTimeout = setTimeout(() => {
			showingTooltip = true;
		}, 500);
	}

	function hideTooltip() {
		clearTimeout(tooltipTimeout);
		showingTooltip = false;
	}

	// Click outside handler for dropdown
	function handleClickOutside(event: Event) {
		if (dropdownElement && !dropdownElement.contains(event.target as Node)) {
			isOpen = false;
		}
	}

	// Keyboard navigation support
	function handleKeydown(event: KeyboardEvent) {
		if (disabled) return;

		switch (event.key) {
			case 'Enter':
			case 'Space':
				event.preventDefault();
				toggleDropdown();
				break;
			case 'Escape':
				isOpen = false;
				selectedElement?.focus();
				break;
			case 'ArrowDown':
				if (!isOpen) {
					event.preventDefault();
					toggleDropdown();
				}
				break;
		}
	}

	// Component lifecycle
	onMount(() => {
		// Auto-fetch modes if enabled and not already loaded
		if (autoFetch && $availableModes.length === 0 && !$modesLoading) {
			modeActions.fetchModes();
		}

		// Add click outside listener
		document.addEventListener('click', handleClickOutside);

		return () => {
			document.removeEventListener('click', handleClickOutside);
			clearTimeout(tooltipTimeout);
		};
	});

	// Utility functions for UI logic
	function getCategoryLabel(category: ModeCategory | 'system'): string {
		const labels: Record<ModeCategory | 'system', string> = {
			task: 'Task-Focused',
			role: 'Role-Based',
			custom: 'Custom Modes',
			system: 'System Modes'
		};
		return labels[category] || 'Other';
	}

	function getCategoryIcon(category: ModeCategory | 'system'): string {
		const icons: Record<ModeCategory | 'system', string> = {
			task: '🎯',
			role: '🎭',
			custom: '⚡',
			system: '🔧'
		};
		return icons[category] || '📁';
	}

	// Error retry handler
	function retryFetch() {
		modeActions.clearError();
		modeActions.fetchModes();
	}
</script>

<!-- Main container with proper accessibility -->
<div
	class="relative"
	class:opacity-50={disabled}
	bind:this={dropdownElement}
	role="combobox"
	aria-expanded={isOpen}
	aria-haspopup="listbox"
	aria-controls="mode-dropdown-listbox"
	aria-label="Select chat mode"
>
	<!-- Selected mode display -->
	<button
		bind:this={selectedElement}
		on:click={toggleDropdown}
		on:keydown={handleKeydown}
		{disabled}
		class="flex w-full items-center justify-between rounded-lg border border-gray-300 bg-white px-3 py-2
			text-left text-sm transition-colors duration-200 hover:border-gray-400 focus:border-blue-500
			focus:outline-none focus:ring-2 focus:ring-blue-500/20 disabled:cursor-not-allowed
			disabled:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-white
			dark:hover:border-gray-500 dark:focus:border-blue-400 dark:disabled:bg-gray-800"
		class:compact
		aria-describedby={showingTooltip && $selectedMode
			? `mode-tooltip-${$selectedMode.id}`
			: undefined}
		data-testid="mode-selector"
	>
		<!-- Loading state -->
		{#if $modesLoading}
			<div class="flex items-center space-x-2">
				<div
					class="h-4 w-4 animate-spin rounded-full border-2 border-gray-300 border-t-blue-600"
				></div>
				<span class="text-gray-600 dark:text-gray-400">Loading modes...</span>
			</div>
			<!-- Error state -->
		{:else if $modesError}
			<div class="flex items-center space-x-2 text-red-600 dark:text-red-400">
				<span class="text-sm">⚠️ Error loading modes</span>
			</div>
			<!-- Selected mode display -->
		{:else if $selectedMode}
			<div
				class="flex items-center space-x-2 transition-all duration-300"
				class:scale-105={modeChangeAnimation}
				class:bg-blue-50={modeChangeAnimation}
				class:dark:bg-blue-900={modeChangeAnimation}
			>
				<span
					class="text-base transition-transform duration-300"
					aria-hidden="true"
					class:animate-bounce={modeChangeAnimation}
				>
					{getCategoryIcon(
						$selectedMode.category || ($selectedMode.isSystem ? 'system' : 'custom')
					)}
				</span>
				<div class="min-w-0 flex-1">
					<div class="flex items-center space-x-2">
						<div class="truncate font-medium text-gray-900 dark:text-white">
							{$selectedMode.name}
						</div>
						{#if $unavailableTools.length > 0}
							<span
								class="inline-flex items-center rounded-full bg-yellow-100 px-1.5 py-0.5 text-xs
								font-medium text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200"
								title="Some tools unavailable"
								aria-label="{$unavailableTools.length} tools unavailable"
							>
								⚠ {$unavailableTools.length}
							</span>
						{/if}
					</div>
					{#if !compact}
						<div class="truncate text-xs text-gray-500 dark:text-gray-400">
							{$selectedMode.description}
						</div>
					{/if}
				</div>
			</div>
			<!-- No selection state -->
		{:else}
			<div class="flex items-center space-x-2 text-gray-500 dark:text-gray-400">
				<span class="text-base" aria-hidden="true">🔧</span>
				<span>Select a mode...</span>
			</div>
		{/if}

		<!-- Dropdown arrow -->
		<svg
			class="h-5 w-5 text-gray-400 transition-transform duration-200"
			class:rotate-180={isOpen}
			fill="none"
			stroke="currentColor"
			viewBox="0 0 24 24"
			aria-hidden="true"
		>
			<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
		</svg>
	</button>

	<!-- Error retry button (outside main button to avoid nesting) -->
	{#if $modesError}
		<button
			on:click={retryFetch}
			class="absolute right-2 top-2 text-xs text-red-600 underline hover:no-underline dark:text-red-400"
			aria-label="Retry loading modes"
		>
			Retry
		</button>
	{/if}

	<!-- Dropdown menu -->
	{#if isOpen && !$modesLoading}
		<div
			id="mode-dropdown-listbox"
			class="absolute z-50 mt-1 max-h-96 w-full overflow-auto rounded-lg border border-gray-200
				bg-white py-1 shadow-lg ring-1 ring-black/5 dark:border-gray-600 dark:bg-gray-700
				dark:ring-white/10"
			role="listbox"
			aria-label="Available modes"
			data-testid="mode-dropdown"
		>
			<!-- Grouped modes display -->
			{#each Object.entries($groupedModes) as [category, modes]}
				{#if modes.length > 0}
					<!-- Category header -->
					<div class="border-b border-gray-100 px-3 py-2 dark:border-gray-600">
						<div
							class="flex items-center space-x-2 text-xs font-semibold uppercase tracking-wide text-gray-600 dark:text-gray-300"
						>
							<span aria-hidden="true">{getCategoryIcon(category as ModeCategory | 'system')}</span>
							<span>{getCategoryLabel(category as ModeCategory | 'system')}</span>
						</div>
					</div>

					<!-- Mode options -->
					{#each modes as mode}
						{@const unavailableToolsInMode = mode.tools.filter(
							(tool) => !$availableTools.includes(tool)
						)}
						<button
							on:click={() => handleModeSelect(mode)}
							on:mouseenter={() => showModeTooltip(mode)}
							on:mouseleave={hideTooltip}
							class="flex w-full items-center space-x-3 px-3 py-2 text-left text-sm
								transition-colors duration-150 hover:bg-gray-50 focus:bg-gray-50
								focus:outline-none dark:hover:bg-gray-600 dark:focus:bg-gray-600"
							class:bg-blue-50={$selectedModeId === mode.id}
							class:dark:bg-blue-900={$selectedModeId === mode.id}
							role="option"
							aria-selected={$selectedModeId === mode.id}
							data-mode-id={mode.id}
							data-testid="mode-option-{mode.id}"
						>
							<!-- Mode icon -->
							<span class="flex-shrink-0 text-base" aria-hidden="true">
								{getCategoryIcon(mode.category || (mode.isSystem ? 'system' : 'custom'))}
							</span>

							<!-- Mode info -->
							<div class="min-w-0 flex-1">
								<div class="flex items-center space-x-2">
									<span class="truncate font-medium text-gray-900 dark:text-white">
										{mode.name}
									</span>
									{#if mode.isSystem}
										<span
											class="inline-flex items-center rounded bg-gray-100 px-1.5 py-0.5
											text-xs font-medium text-gray-800 dark:bg-gray-600 dark:text-gray-200"
											aria-label="System mode"
										>
											System
										</span>
									{/if}
									{#if unavailableToolsInMode.length > 0}
										<span
											class="inline-flex items-center rounded-full bg-yellow-100 px-1.5 py-0.5 text-xs
											font-medium text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200"
											title="{unavailableToolsInMode.length} tools unavailable: {unavailableToolsInMode.join(
												', '
											)}"
											aria-label="{unavailableToolsInMode.length} tools unavailable for this mode"
										>
											⚠ {unavailableToolsInMode.length}
										</span>
									{/if}
								</div>
								<div class="truncate text-xs text-gray-500 dark:text-gray-400">
									{mode.description}
								</div>
								{#if mode.tools.length > 0}
									<div class="mt-1 text-xs text-gray-400 dark:text-gray-500">
										{mode.tools.length} tool{mode.tools.length !== 1 ? 's' : ''} available
									</div>
								{/if}
							</div>

							<!-- Selection indicator -->
							{#if $selectedModeId === mode.id}
								<svg
									class="h-4 w-4 flex-shrink-0 text-blue-600 dark:text-blue-400"
									fill="currentColor"
									viewBox="0 0 20 20"
									aria-hidden="true"
								>
									<path
										fill-rule="evenodd"
										d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z"
										clip-rule="evenodd"
									/>
								</svg>
							{/if}
						</button>
					{/each}
				{/if}
			{/each}

			<!-- Empty state -->
			{#if $availableModes.length === 0}
				<div class="px-3 py-8 text-center text-sm text-gray-500 dark:text-gray-400">
					<div class="mb-2 text-2xl" aria-hidden="true">📭</div>
					<div>No modes available</div>
				</div>
				<div class="px-3 pb-3 text-center">
					<button
						on:click={retryFetch}
						class="text-xs text-blue-600 underline hover:no-underline dark:text-blue-400"
					>
						Refresh
					</button>
				</div>
			{/if}
		</div>
	{/if}

	<!-- Tooltip for selected mode -->
	{#if showingTooltip && showTooltip && $selectedMode && !compact}
		<div
			id="mode-tooltip-{$selectedMode.id}"
			class="z-60 absolute mt-2 w-80 rounded-lg border border-gray-200 bg-white p-3 shadow-lg
				ring-1 ring-black/5 dark:border-gray-600 dark:bg-gray-800 dark:ring-white/10"
			role="tooltip"
		>
			<div class="flex items-start space-x-3">
				<span class="flex-shrink-0 text-lg" aria-hidden="true">
					{getCategoryIcon(
						$selectedMode.category || ($selectedMode.isSystem ? 'system' : 'custom')
					)}
				</span>
				<div>
					<h3 class="font-semibold text-gray-900 dark:text-white">{$selectedMode.name}</h3>
					<p class="mt-1 text-sm text-gray-600 dark:text-gray-300">{$selectedMode.description}</p>

					{#if $selectedMode.tools.length > 0}
						{@const availableToolsInMode = $selectedMode.tools.filter((tool) =>
							$availableTools.includes(tool)
						)}
						{@const unavailableToolsInMode = $selectedMode.tools.filter(
							(tool) => !$availableTools.includes(tool)
						)}
						{@const totalShown = Math.min(
							6,
							availableToolsInMode.length + unavailableToolsInMode.length
						)}
						<div class="mt-2">
							<p class="text-xs font-medium text-gray-700 dark:text-gray-300">
								Tools ({$selectedMode.tools.length}):
							</p>
							<div class="mt-1 flex flex-wrap gap-1">
								{#each availableToolsInMode.slice(0, 3) as tool}
									<span
										class="inline-flex items-center rounded bg-green-100 px-2 py-0.5
										text-xs font-medium text-green-800 dark:bg-green-900 dark:text-green-200"
									>
										✓ {tool}
									</span>
								{/each}
								{#each unavailableToolsInMode.slice(0, 3) as tool}
									<span
										class="inline-flex items-center rounded bg-yellow-100 px-2 py-0.5
										text-xs font-medium text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200"
									>
										⚠ {tool}
									</span>
								{/each}
								{#if $selectedMode.tools.length > totalShown}
									<span class="text-xs text-gray-500 dark:text-gray-400">
										+{$selectedMode.tools.length - totalShown} more
									</span>
								{/if}
							</div>
							{#if unavailableToolsInMode.length > 0}
								<div class="mt-1 text-xs text-yellow-600 dark:text-yellow-400">
									⚠ {unavailableToolsInMode.length} tool{unavailableToolsInMode.length !== 1
										? 's'
										: ''} unavailable
								</div>
							{/if}
						</div>
					{/if}

					{#if $selectedMode.defaultModel}
						<div class="mt-2 text-xs text-gray-500 dark:text-gray-400">
							Model: {$selectedMode.defaultModel}
						</div>
					{/if}
				</div>
			</div>
		</div>
	{/if}
</div>

<style>
	.compact {
		padding-top: 0.375rem;
		padding-bottom: 0.375rem;
	}
</style>
