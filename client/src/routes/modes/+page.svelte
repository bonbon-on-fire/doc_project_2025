<script lang="ts">
	import { onMount } from 'svelte';
	import { fade, slide } from 'svelte/transition';
	import { goto } from '$app/navigation';
	import type { Mode } from '$shared/types/mode';
	import {
		availableModes,
		customModes,
		systemModes,
		modeActions,
		modesLoading,
		modesError
	} from '$lib/stores/modes';
	import { currentUser } from '$lib/stores/chat';
	import { apiClient } from '$lib/api/client';
	import { logger } from '$lib/utils/logger';
	import ModeEditor from '$lib/components/ModeEditor.svelte';
	import { toastActions } from '$lib/stores/toast';

	// Component state
	let showEditor = false;
	let editingMode: Mode | null = null;
	let isEditing = false;
	let selectedModeForPreview: Mode | null = null;
	let showImportDialog = false;
	let importError: string | null = null;
	let isDeletingMode: string | null = null;

	// File input for import
	let fileInput: HTMLInputElement;

	// Refresh modes on mount
	onMount(() => {
		logger.info('Modes management page mounted');
		modeActions.fetchModes();
	});

	// Handle create new mode
	function handleCreateMode() {
		editingMode = null;
		isEditing = false;
		showEditor = true;
	}

	// Handle edit mode
	function handleEditMode(mode: Mode) {
		if (mode.isSystem) {
			toastActions.error('System modes cannot be edited');
			return;
		}
		editingMode = mode;
		isEditing = true;
		showEditor = true;
	}

	// Handle delete mode
	async function handleDeleteMode(mode: Mode) {
		if (mode.isSystem) {
			toastActions.error('System modes cannot be deleted');
			return;
		}

		const user = $currentUser;
		if (!user?.id) {
			toastActions.error('User not authenticated');
			return;
		}

		if (
			!confirm(
				`Are you sure you want to delete the mode "${mode.name}"? This action cannot be undone.`
			)
		) {
			return;
		}

		isDeletingMode = mode.id;

		try {
			logger.info('Deleting mode', { modeId: mode.id, name: mode.name });
			await apiClient.deleteMode(mode.id, user.id);

			// Update local state
			const updatedModes = $availableModes.filter((m) => m.id !== mode.id);
			availableModes.set(updatedModes);

			toastActions.success(`Mode "${mode.name}" deleted successfully`);
			logger.info('Mode deleted successfully', { modeId: mode.id });
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to delete mode';
			logger.error('Error deleting mode', { error: errorMessage, modeId: mode.id });
			toastActions.error(errorMessage);
		} finally {
			isDeletingMode = null;
		}
	}

	// Handle save from editor
	function handleEditorSave(event: CustomEvent<{ mode: Mode }>) {
		const { mode } = event.detail;
		logger.info('Mode saved from editor', { modeId: mode.id, name: mode.name });

		// Refresh modes list
		modeActions.fetchModes();

		// Close editor
		showEditor = false;
		editingMode = null;
		isEditing = false;

		toastActions.success(`Mode "${mode.name}" ${isEditing ? 'updated' : 'created'} successfully`);
	}

	// Handle cancel from editor
	function handleEditorCancel() {
		showEditor = false;
		editingMode = null;
		isEditing = false;
	}

	// Handle test mode (open chat with mode)
	function handleTestMode(mode: Mode) {
		logger.info('Testing mode', { modeId: mode.id, name: mode.name });
		// Navigate to chat with mode selected
		modeActions.selectMode(mode.id);
		goto('/chat');
	}

	// Handle export mode
	function handleExportMode(mode: Mode) {
		try {
			// Prepare export data (remove server-specific fields)
			const exportData = {
				name: mode.name,
				description: mode.description,
				prompt: mode.prompt,
				tools: mode.tools,
				defaultModel: mode.defaultModel,
				category: mode.category
			};

			// Convert to JSON with pretty formatting
			const jsonString = JSON.stringify(exportData, null, 2);

			// Create blob and download link
			const blob = new Blob([jsonString], { type: 'application/json' });
			const url = URL.createObjectURL(blob);
			const link = document.createElement('a');
			link.href = url;
			link.download = `${mode.name.toLowerCase().replace(/\s+/g, '-')}-mode.json`;

			// Trigger download
			document.body.appendChild(link);
			link.click();
			document.body.removeChild(link);

			// Clean up
			URL.revokeObjectURL(url);

			logger.info('Mode exported', { modeId: mode.id, name: mode.name });
			toastActions.success(`Mode "${mode.name}" exported successfully`);
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to export mode';
			logger.error('Error exporting mode', { error: errorMessage, modeId: mode.id });
			toastActions.error(errorMessage);
		}
	}

	// Handle export all custom modes
	function handleExportAll() {
		try {
			const modes = $customModes;
			if (modes.length === 0) {
				toastActions.warning('No custom modes to export');
				return;
			}

			// Prepare export data for all custom modes
			const exportData = modes.map((mode) => ({
				name: mode.name,
				description: mode.description,
				prompt: mode.prompt,
				tools: mode.tools,
				defaultModel: mode.defaultModel,
				category: mode.category
			}));

			// Convert to JSON with pretty formatting
			const jsonString = JSON.stringify(exportData, null, 2);

			// Create blob and download link
			const blob = new Blob([jsonString], { type: 'application/json' });
			const url = URL.createObjectURL(blob);
			const link = document.createElement('a');
			link.href = url;
			link.download = `custom-modes-${new Date().toISOString().split('T')[0]}.json`;

			// Trigger download
			document.body.appendChild(link);
			link.click();
			document.body.removeChild(link);

			// Clean up
			URL.revokeObjectURL(url);

			logger.info('All custom modes exported', { count: modes.length });
			toastActions.success(`Exported ${modes.length} custom mode${modes.length !== 1 ? 's' : ''}`);
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to export modes';
			logger.error('Error exporting modes', { error: errorMessage });
			toastActions.error(errorMessage);
		}
	}

	// Handle import modes
	async function handleImport() {
		const file = fileInput?.files?.[0];
		if (!file) {
			importError = 'Please select a file to import';
			return;
		}

		importError = null;

		try {
			// Read file content
			const content = await file.text();
			let importData: any;

			try {
				importData = JSON.parse(content);
			} catch (parseError) {
				importError = 'Invalid JSON file format';
				return;
			}

			// Normalize import data (handle both single mode and array)
			const modesToImport = Array.isArray(importData) ? importData : [importData];

			// Validate imported modes
			let importedCount = 0;
			const errors: string[] = [];

			for (const modeData of modesToImport) {
				// Validate required fields
				if (!modeData.name || !modeData.description || !modeData.prompt || !modeData.tools) {
					errors.push(`Invalid mode data: missing required fields`);
					continue;
				}

				// Validate tools is an array
				if (!Array.isArray(modeData.tools)) {
					errors.push(`Invalid mode "${modeData.name}": tools must be an array`);
					continue;
				}

				try {
					// Create the mode
					const newMode = await modeActions.createMode({
						name: modeData.name,
						description: modeData.description,
						prompt: modeData.prompt,
						tools: modeData.tools,
						defaultModel: modeData.defaultModel,
						category: modeData.category || 'custom'
					});

					if (newMode) {
						importedCount++;
						logger.info('Mode imported', { name: modeData.name });
					}
				} catch (error) {
					const errorMessage = error instanceof Error ? error.message : 'Import failed';
					errors.push(`Failed to import "${modeData.name}": ${errorMessage}`);
				}
			}

			// Show results
			if (importedCount > 0) {
				toastActions.success(
					`Successfully imported ${importedCount} mode${importedCount !== 1 ? 's' : ''}`
				);
			}

			if (errors.length > 0) {
				importError = errors.join('\n');
			}

			// Close dialog if all successful
			if (errors.length === 0) {
				showImportDialog = false;
				fileInput.value = '';
			}

			// Refresh modes list
			modeActions.fetchModes();
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to import modes';
			logger.error('Error importing modes', { error: errorMessage });
			importError = errorMessage;
		}
	}

	// Handle preview mode
	function handlePreviewMode(mode: Mode) {
		selectedModeForPreview = mode;
	}

	// Close preview
	function closePreview() {
		selectedModeForPreview = null;
	}
</script>

<svelte:head>
	<title>Chat Modes Management</title>
	<meta name="description" content="Manage your custom chat modes and system modes" />
</svelte:head>

<main class="min-h-screen bg-gray-50 py-8 dark:bg-gray-900">
	<div class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
		<!-- Header -->
		<div class="mb-8">
			<div class="flex items-center justify-between">
				<div>
					<h1 class="text-3xl font-bold text-gray-900 dark:text-white">Chat Modes</h1>
					<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
						Manage your custom chat modes and explore system modes
					</p>
				</div>
				<div class="flex space-x-3">
					{#if $customModes.length > 0}
						<button
							on:click={handleExportAll}
							class="inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700"
						>
							<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3V10"
								/>
							</svg>
							Export All
						</button>
					{/if}
					<button
						on:click={() => (showImportDialog = true)}
						class="inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700"
					>
						<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"
							/>
						</svg>
						Import
					</button>
					<button
						on:click={handleCreateMode}
						class="inline-flex items-center rounded-md border border-transparent bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none dark:bg-blue-500 dark:hover:bg-blue-600"
					>
						<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M12 4v16m8-8H4"
							/>
						</svg>
						Create Mode
					</button>
				</div>
			</div>
		</div>

		<!-- Loading State -->
		{#if $modesLoading}
			<div class="flex items-center justify-center py-12">
				<div class="text-center">
					<svg class="mx-auto h-12 w-12 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
						<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"
						></circle>
						<path
							class="opacity-75"
							fill="currentColor"
							d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
						></path>
					</svg>
					<p class="mt-2 text-sm text-gray-600 dark:text-gray-400">Loading modes...</p>
				</div>
			</div>
		{:else if $modesError}
			<!-- Error State -->
			<div class="rounded-lg bg-red-50 p-4 dark:bg-red-900/20">
				<div class="flex">
					<div class="flex-shrink-0">
						<svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
							<path
								fill-rule="evenodd"
								d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
								clip-rule="evenodd"
							/>
						</svg>
					</div>
					<div class="ml-3">
						<p class="text-sm text-red-800 dark:text-red-200">{$modesError}</p>
					</div>
				</div>
			</div>
		{:else}
			<!-- Custom Modes Section -->
			{#if $customModes.length > 0}
				<div class="mb-8">
					<h2 class="mb-4 text-xl font-semibold text-gray-900 dark:text-white">Custom Modes</h2>
					<div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
						{#each $customModes as mode (mode.id)}
							<div
								class="relative rounded-lg border border-gray-200 bg-white p-6 shadow-sm transition-shadow hover:shadow-md dark:border-gray-700 dark:bg-gray-800"
							>
								<!-- Mode Info -->
								<div class="mb-4">
									<h3 class="text-lg font-medium text-gray-900 dark:text-white">{mode.name}</h3>
									<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">{mode.description}</p>
								</div>

								<!-- Mode Details -->
								<div class="mb-4 space-y-2">
									<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
										<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
											/>
										</svg>
										{mode.tools.length} tool{mode.tools.length !== 1 ? 's' : ''} enabled
									</div>
									{#if mode.defaultModel}
										<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
											<svg
												class="mr-2 h-4 w-4"
												fill="none"
												stroke="currentColor"
												viewBox="0 0 24 24"
											>
												<path
													stroke-linecap="round"
													stroke-linejoin="round"
													stroke-width="2"
													d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"
												/>
											</svg>
											{mode.defaultModel}
										</div>
									{/if}
									{#if mode.category}
										<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
											<svg
												class="mr-2 h-4 w-4"
												fill="none"
												stroke="currentColor"
												viewBox="0 0 24 24"
											>
												<path
													stroke-linecap="round"
													stroke-linejoin="round"
													stroke-width="2"
													d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
												/>
											</svg>
											{mode.category}
										</div>
									{/if}
								</div>

								<!-- Actions -->
								<div class="flex flex-wrap gap-2">
									<button
										on:click={() => handlePreviewMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
											/>
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"
											/>
										</svg>
										Preview
									</button>
									<button
										on:click={() => handleTestMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z"
											/>
										</svg>
										Test
									</button>
									<button
										on:click={() => handleEditMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
											/>
										</svg>
										Edit
									</button>
									<button
										on:click={() => handleExportMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3V10"
											/>
										</svg>
										Export
									</button>
									<button
										on:click={() => handleDeleteMode(mode)}
										disabled={isDeletingMode === mode.id}
										class="inline-flex items-center rounded-md border border-red-300 bg-white px-3 py-1 text-sm font-medium text-red-700 hover:bg-red-50 disabled:opacity-50 dark:border-red-600 dark:bg-gray-700 dark:text-red-400 dark:hover:bg-red-900/20"
									>
										{#if isDeletingMode === mode.id}
											<svg class="mr-1 h-3 w-3 animate-spin" fill="none" viewBox="0 0 24 24">
												<circle
													class="opacity-25"
													cx="12"
													cy="12"
													r="10"
													stroke="currentColor"
													stroke-width="4"
												></circle>
												<path
													class="opacity-75"
													fill="currentColor"
													d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
												></path>
											</svg>
										{:else}
											<svg
												class="mr-1 h-3 w-3"
												fill="none"
												stroke="currentColor"
												viewBox="0 0 24 24"
											>
												<path
													stroke-linecap="round"
													stroke-linejoin="round"
													stroke-width="2"
													d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
												/>
											</svg>
										{/if}
										Delete
									</button>
								</div>
							</div>
						{/each}
					</div>
				</div>
			{/if}

			<!-- System Modes Section -->
			{#if $systemModes.length > 0}
				<div>
					<h2 class="mb-4 text-xl font-semibold text-gray-900 dark:text-white">System Modes</h2>
					<div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
						{#each $systemModes as mode (mode.id)}
							<div
								class="relative rounded-lg border border-gray-200 bg-white p-6 shadow-sm transition-shadow hover:shadow-md dark:border-gray-700 dark:bg-gray-800"
							>
								<!-- System Badge -->
								<div class="absolute top-2 right-2">
									<span
										class="inline-flex items-center rounded-full bg-blue-100 px-2 py-1 text-xs font-medium text-blue-800 dark:bg-blue-900/20 dark:text-blue-300"
									>
										System
									</span>
								</div>

								<!-- Mode Info -->
								<div class="mb-4">
									<h3 class="text-lg font-medium text-gray-900 dark:text-white">{mode.name}</h3>
									<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">{mode.description}</p>
								</div>

								<!-- Mode Details -->
								<div class="mb-4 space-y-2">
									<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
										<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
											/>
										</svg>
										{mode.tools.length} tool{mode.tools.length !== 1 ? 's' : ''} enabled
									</div>
									{#if mode.defaultModel}
										<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
											<svg
												class="mr-2 h-4 w-4"
												fill="none"
												stroke="currentColor"
												viewBox="0 0 24 24"
											>
												<path
													stroke-linecap="round"
													stroke-linejoin="round"
													stroke-width="2"
													d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"
												/>
											</svg>
											{mode.defaultModel}
										</div>
									{/if}
									{#if mode.category}
										<div class="flex items-center text-sm text-gray-500 dark:text-gray-400">
											<svg
												class="mr-2 h-4 w-4"
												fill="none"
												stroke="currentColor"
												viewBox="0 0 24 24"
											>
												<path
													stroke-linecap="round"
													stroke-linejoin="round"
													stroke-width="2"
													d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
												/>
											</svg>
											{mode.category}
										</div>
									{/if}
								</div>

								<!-- Actions -->
								<div class="flex flex-wrap gap-2">
									<button
										on:click={() => handlePreviewMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
											/>
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"
											/>
										</svg>
										Preview
									</button>
									<button
										on:click={() => handleTestMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z"
											/>
										</svg>
										Test
									</button>
									<button
										on:click={() => handleExportMode(mode)}
										class="inline-flex items-center rounded-md border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
									>
										<svg class="mr-1 h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
											<path
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-width="2"
												d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M9 19l3 3m0 0l3-3m-3 3V10"
											/>
										</svg>
										Export
									</button>
								</div>
							</div>
						{/each}
					</div>
				</div>
			{/if}

			<!-- Empty State -->
			{#if $availableModes.length === 0}
				<div class="py-12 text-center">
					<svg
						class="mx-auto h-12 w-12 text-gray-400"
						fill="none"
						stroke="currentColor"
						viewBox="0 0 24 24"
					>
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							stroke-width="2"
							d="M9 3v2m6-2v2M9 19v2m6-2v2M5 9H3m2 6H3m18-6h-2m2 6h-2M7 19h10a2 2 0 002-2V7a2 2 0 00-2-2H7a2 2 0 00-2 2v10a2 2 0 002 2zM9 9h6v6H9V9z"
						/>
					</svg>
					<h3 class="mt-2 text-sm font-medium text-gray-900 dark:text-white">No modes available</h3>
					<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
						Get started by creating a new mode or importing existing ones.
					</p>
					<div class="mt-6">
						<button
							on:click={handleCreateMode}
							class="inline-flex items-center rounded-md border border-transparent bg-blue-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-blue-700 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none dark:bg-blue-500 dark:hover:bg-blue-600"
						>
							<svg class="mr-2 h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M12 4v16m8-8H4"
								/>
							</svg>
							Create Your First Mode
						</button>
					</div>
				</div>
			{/if}
		{/if}
	</div>
</main>

<!-- Mode Editor Modal -->
{#if showEditor}
	<div class="fixed inset-0 z-50 overflow-y-auto" transition:fade={{ duration: 200 }}>
		<div
			class="flex min-h-screen items-end justify-center px-4 pt-4 pb-20 text-center sm:block sm:p-0"
		>
			<!-- Background overlay -->
			<div
				class="bg-opacity-75 fixed inset-0 bg-gray-500 transition-opacity"
				on:click={handleEditorCancel}
			></div>

			<!-- Modal panel -->
			<div
				class="inline-block bg-transparent text-left align-bottom transition-all sm:my-8 sm:w-full sm:max-w-3xl sm:align-middle"
			>
				<ModeEditor
					mode={editingMode}
					{isEditing}
					on:save={handleEditorSave}
					on:cancel={handleEditorCancel}
				/>
			</div>
		</div>
	</div>
{/if}

<!-- Mode Preview Modal -->
{#if selectedModeForPreview}
	<div class="fixed inset-0 z-50 overflow-y-auto" transition:fade={{ duration: 200 }}>
		<div
			class="flex min-h-screen items-end justify-center px-4 pt-4 pb-20 text-center sm:block sm:p-0"
		>
			<!-- Background overlay -->
			<div
				class="bg-opacity-75 fixed inset-0 bg-gray-500 transition-opacity"
				on:click={closePreview}
			></div>

			<!-- Modal panel -->
			<div
				class="inline-block rounded-lg bg-white text-left align-bottom shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-3xl sm:align-middle dark:bg-gray-800"
				transition:slide
			>
				<div class="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4 dark:bg-gray-800">
					<div class="sm:flex sm:items-start">
						<div class="mt-3 w-full text-center sm:mt-0 sm:ml-4 sm:text-left">
							<h3 class="text-lg leading-6 font-medium text-gray-900 dark:text-white">
								{selectedModeForPreview.name}
								{#if selectedModeForPreview.isSystem}
									<span
										class="ml-2 inline-flex items-center rounded-full bg-blue-100 px-2 py-1 text-xs font-medium text-blue-800 dark:bg-blue-900/20 dark:text-blue-300"
									>
										System
									</span>
								{/if}
							</h3>
							<div class="mt-4 space-y-4">
								<!-- Description -->
								<div>
									<h4 class="text-sm font-medium text-gray-700 dark:text-gray-300">Description</h4>
									<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
										{selectedModeForPreview.description}
									</p>
								</div>

								<!-- Category -->
								{#if selectedModeForPreview.category}
									<div>
										<h4 class="text-sm font-medium text-gray-700 dark:text-gray-300">Category</h4>
										<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
											{selectedModeForPreview.category}
										</p>
									</div>
								{/if}

								<!-- Model -->
								{#if selectedModeForPreview.defaultModel}
									<div>
										<h4 class="text-sm font-medium text-gray-700 dark:text-gray-300">
											Preferred Model
										</h4>
										<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
											{selectedModeForPreview.defaultModel}
										</p>
									</div>
								{/if}

								<!-- System Prompt -->
								<div>
									<h4 class="text-sm font-medium text-gray-700 dark:text-gray-300">
										System Prompt
									</h4>
									<div
										class="mt-1 max-h-48 overflow-y-auto rounded-md bg-gray-50 p-3 dark:bg-gray-900"
									>
										<pre
											class="text-sm whitespace-pre-wrap text-gray-600 dark:text-gray-400">{selectedModeForPreview.prompt}</pre>
									</div>
								</div>

								<!-- Tools -->
								<div>
									<h4 class="text-sm font-medium text-gray-700 dark:text-gray-300">
										Enabled Tools ({selectedModeForPreview.tools.length})
									</h4>
									<div
										class="mt-1 max-h-32 overflow-y-auto rounded-md bg-gray-50 p-3 dark:bg-gray-900"
									>
										<div class="flex flex-wrap gap-2">
											{#each selectedModeForPreview.tools as tool}
												<span
													class="inline-flex items-center rounded-full bg-gray-200 px-2.5 py-0.5 text-xs font-medium text-gray-800 dark:bg-gray-700 dark:text-gray-300"
												>
													{tool}
												</span>
											{/each}
										</div>
									</div>
								</div>
							</div>
						</div>
					</div>
				</div>
				<div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6 dark:bg-gray-900">
					<button
						on:click={() => handleTestMode(selectedModeForPreview)}
						class="inline-flex w-full justify-center rounded-md border border-transparent bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none sm:ml-3 sm:w-auto sm:text-sm dark:bg-blue-500 dark:hover:bg-blue-600"
					>
						Test Mode
					</button>
					<button
						on:click={closePreview}
						class="mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:ring-2 focus:ring-gray-500 focus:ring-offset-2 focus:outline-none sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
					>
						Close
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}

<!-- Import Dialog -->
{#if showImportDialog}
	<div class="fixed inset-0 z-50 overflow-y-auto" transition:fade={{ duration: 200 }}>
		<div
			class="flex min-h-screen items-end justify-center px-4 pt-4 pb-20 text-center sm:block sm:p-0"
		>
			<!-- Background overlay -->
			<div
				class="bg-opacity-75 fixed inset-0 bg-gray-500 transition-opacity"
				on:click={() => (showImportDialog = false)}
			></div>

			<!-- Modal panel -->
			<div
				class="inline-block rounded-lg bg-white text-left align-bottom shadow-xl transition-all sm:my-8 sm:w-full sm:max-w-lg sm:align-middle dark:bg-gray-800"
				transition:slide
			>
				<div class="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4 dark:bg-gray-800">
					<div class="sm:flex sm:items-start">
						<div class="mt-3 w-full text-center sm:mt-0 sm:ml-4 sm:text-left">
							<h3 class="text-lg leading-6 font-medium text-gray-900 dark:text-white">
								Import Modes
							</h3>
							<div class="mt-4">
								<p class="text-sm text-gray-600 dark:text-gray-400">
									Select a JSON file containing mode definitions to import.
								</p>

								<!-- File Input -->
								<div class="mt-4">
									<input
										bind:this={fileInput}
										type="file"
										accept=".json,application/json"
										class="block w-full text-sm text-gray-900 file:mr-4 file:rounded-md file:border-0 file:bg-blue-50 file:px-4 file:py-2 file:text-sm file:font-semibold file:text-blue-700 hover:file:bg-blue-100 dark:text-gray-300 dark:file:bg-blue-900/20 dark:file:text-blue-300"
									/>
								</div>

								<!-- Import Error -->
								{#if importError}
									<div class="mt-4 rounded-lg bg-red-50 p-4 dark:bg-red-900/20">
										<div class="flex">
											<div class="flex-shrink-0">
												<svg class="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
													<path
														fill-rule="evenodd"
														d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
														clip-rule="evenodd"
													/>
												</svg>
											</div>
											<div class="ml-3">
												<p class="text-sm whitespace-pre-line text-red-800 dark:text-red-200">
													{importError}
												</p>
											</div>
										</div>
									</div>
								{/if}
							</div>
						</div>
					</div>
				</div>
				<div class="bg-gray-50 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6 dark:bg-gray-900">
					<button
						on:click={handleImport}
						class="inline-flex w-full justify-center rounded-md border border-transparent bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none sm:ml-3 sm:w-auto sm:text-sm dark:bg-blue-500 dark:hover:bg-blue-600"
					>
						Import
					</button>
					<button
						on:click={() => {
							showImportDialog = false;
							importError = null;
							if (fileInput) fileInput.value = '';
						}}
						class="mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:ring-2 focus:ring-gray-500 focus:ring-offset-2 focus:outline-none sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300 dark:hover:bg-gray-600"
					>
						Cancel
					</button>
				</div>
			</div>
		</div>
	</div>
{/if}
