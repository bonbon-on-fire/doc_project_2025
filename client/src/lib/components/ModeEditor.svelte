<script lang="ts">
	import { createEventDispatcher, onMount } from 'svelte';
	import { fade, slide } from 'svelte/transition';
	import type {
		Mode,
		ModeCategory,
		CreateModeRequest,
		UpdateModeRequest
	} from '$shared/types/mode';
	import { availableTools, modeActions } from '$lib/stores/modes';
	import { currentUser } from '$lib/stores/chat';
	import { apiClient } from '$lib/api/client';
	import { logger } from '$lib/utils/logger';

	// Props - supporting both create and edit modes
	export let mode: Mode | null = null; // null for create, existing mode for edit
	export let isEditing = false; // true when editing an existing mode

	// Component state
	let formData = {
		name: '',
		description: '',
		prompt: '',
		tools: [] as string[],
		defaultModel: '',
		category: 'task' as ModeCategory
	};

	// Form validation state
	let errors: Record<string, string> = {};
	let touched: Record<string, boolean> = {};

	// UI state
	let isSaving = false;
	let saveError: string | null = null;
	let showToolsDropdown = false;
	let searchQuery = '';

	// Constants
	const MAX_PROMPT_LENGTH = 4000;
	const MAX_NAME_LENGTH = 50;
	const MAX_DESCRIPTION_LENGTH = 200;

	// Available categories
	const categories: { value: ModeCategory; label: string; icon: string }[] = [
		{ value: 'task', label: 'Task-Focused', icon: '🎯' },
		{ value: 'role', label: 'Role-Based', icon: '🎭' },
		{ value: 'custom', label: 'Custom', icon: '⚡' }
	];

	// Available models (this could be fetched from API in the future)
	const availableModels = [
		{ value: '', label: 'Default Model' },
		{ value: 'gpt-4', label: 'GPT-4' },
		{ value: 'gpt-4-turbo', label: 'GPT-4 Turbo' },
		{ value: 'gpt-3.5-turbo', label: 'GPT-3.5 Turbo' },
		{ value: 'claude-3-opus', label: 'Claude 3 Opus' },
		{ value: 'claude-3-sonnet', label: 'Claude 3 Sonnet' },
		{ value: 'claude-3-haiku', label: 'Claude 3 Haiku' }
	];

	// Event dispatcher for parent component communication
	const dispatch = createEventDispatcher<{
		save: { mode: Mode };
		cancel: void;
		error: { message: string };
	}>();

	// Initialize form data from existing mode if editing
	$: if (mode && isEditing) {
		formData = {
			name: mode.name,
			description: mode.description,
			prompt: mode.prompt,
			tools: [...mode.tools],
			defaultModel: mode.defaultModel || '',
			category: mode.category || 'custom'
		};
	}

	// Filtered tools based on search query
	$: filteredTools = $availableTools.filter((tool) =>
		tool.toLowerCase().includes(searchQuery.toLowerCase())
	);

	// Form validation functions
	function validateField(field: string, value: any): string | null {
		switch (field) {
			case 'name':
				if (!value || value.trim() === '') {
					return 'Name is required';
				}
				if (value.length > MAX_NAME_LENGTH) {
					return `Name must be less than ${MAX_NAME_LENGTH} characters`;
				}
				return null;

			case 'description':
				if (!value || value.trim() === '') {
					return 'Description is required';
				}
				if (value.length > MAX_DESCRIPTION_LENGTH) {
					return `Description must be less than ${MAX_DESCRIPTION_LENGTH} characters`;
				}
				return null;

			case 'prompt':
				if (!value || value.trim() === '') {
					return 'Prompt is required';
				}
				if (value.length > MAX_PROMPT_LENGTH) {
					return `Prompt must be less than ${MAX_PROMPT_LENGTH} characters`;
				}
				return null;

			case 'tools':
				if (!value || value.length === 0) {
					return 'At least one tool must be selected';
				}
				return null;

			default:
				return null;
		}
	}

	// Validate entire form
	function validateForm(): boolean {
		const newErrors: Record<string, string> = {};

		// Validate each field
		const nameError = validateField('name', formData.name);
		if (nameError) newErrors.name = nameError;

		const descriptionError = validateField('description', formData.description);
		if (descriptionError) newErrors.description = descriptionError;

		const promptError = validateField('prompt', formData.prompt);
		if (promptError) newErrors.prompt = promptError;

		const toolsError = validateField('tools', formData.tools);
		if (toolsError) newErrors.tools = toolsError;

		errors = newErrors;
		return Object.keys(newErrors).length === 0;
	}

	// Handle field blur for validation
	function handleBlur(field: string) {
		touched[field] = true;
		const error = validateField(field, formData[field as keyof typeof formData]);
		if (error) {
			errors[field] = error;
		} else {
			delete errors[field];
		}
	}

	// Handle tool selection
	function toggleTool(tool: string) {
		if (formData.tools.includes(tool)) {
			formData.tools = formData.tools.filter((t) => t !== tool);
		} else {
			formData.tools = [...formData.tools, tool];
		}

		// Validate tools after change
		const error = validateField('tools', formData.tools);
		if (error) {
			errors.tools = error;
		} else {
			delete errors.tools;
		}
	}

	// Select/deselect all tools
	function toggleAllTools() {
		if (formData.tools.length === $availableTools.length) {
			formData.tools = [];
		} else {
			formData.tools = [...$availableTools];
		}

		// Validate tools after change
		const error = validateField('tools', formData.tools);
		if (error) {
			errors.tools = error;
		} else {
			delete errors.tools;
		}
	}

	// Handle save
	async function handleSave() {
		// Mark all fields as touched to show validation errors
		touched = {
			name: true,
			description: true,
			prompt: true,
			tools: true
		};

		// Validate form
		if (!validateForm()) {
			logger.warn('Form validation failed', { errors });
			return;
		}

		isSaving = true;
		saveError = null;

		try {
			const user = $currentUser;
			if (!user?.id) {
				throw new Error('User not authenticated');
			}

			let savedMode: Mode;

			if (isEditing && mode) {
				// Update existing mode
				logger.info('Updating mode', { modeId: mode.id, name: formData.name });

				const updateRequest = {
					name: formData.name,
					description: formData.description,
					prompt: formData.prompt,
					tools: formData.tools,
					defaultModel: formData.defaultModel,
					category: formData.category
				};

				savedMode = (await modeActions.updateMode(mode.id, updateRequest)) as Mode;

				if (!savedMode) {
					throw new Error('Failed to update mode');
				}

				logger.info('Mode updated successfully', { modeId: savedMode.id });
			} else {
				// Create new mode
				logger.info('Creating new mode', { name: formData.name });

				const createRequest: CreateModeRequest = {
					name: formData.name,
					description: formData.description,
					prompt: formData.prompt,
					tools: formData.tools,
					category: formData.category
				};

				if (formData.defaultModel) {
					createRequest.defaultModel = formData.defaultModel;
				}

				savedMode = (await modeActions.createMode(createRequest)) as Mode;

				if (!savedMode) {
					throw new Error('Failed to create mode');
				}

				logger.info('Mode created successfully', { modeId: savedMode.id });
			}

			// Dispatch success event
			dispatch('save', { mode: savedMode });
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to save mode';
			logger.error('Error saving mode', { error: errorMessage, isEditing });
			saveError = errorMessage;
			dispatch('error', { message: errorMessage });
		} finally {
			isSaving = false;
		}
	}

	// Handle cancel
	function handleCancel() {
		dispatch('cancel');
	}

	// Initialize component
	onMount(() => {
		// Fetch available tools if not already loaded
		if ($availableTools.length === 0) {
			modeActions.fetchAvailableTools();
		}

		// Focus on first input
		const firstInput = document.querySelector('input[name="name"]') as HTMLInputElement;
		if (firstInput) {
			firstInput.focus();
		}
	});
</script>

<!-- Mode Editor Form -->
<div
	class="mx-auto max-w-2xl rounded-lg border border-gray-200 bg-white p-6 shadow-sm dark:border-gray-700 dark:bg-gray-800"
>
	<!-- Header -->
	<div class="mb-6">
		<h2 class="text-2xl font-bold text-gray-900 dark:text-white">
			{isEditing ? 'Edit Mode' : 'Create New Mode'}
		</h2>
		<p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
			{isEditing
				? 'Update the configuration for this chat mode'
				: 'Define a new chat mode with custom settings and tools'}
		</p>
	</div>

	<!-- Error Alert -->
	{#if saveError}
		<div class="mb-4 rounded-lg bg-red-50 p-4 dark:bg-red-900/20" transition:slide>
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
					<p class="text-sm text-red-800 dark:text-red-200">{saveError}</p>
				</div>
			</div>
		</div>
	{/if}

	<!-- Form -->
	<form on:submit|preventDefault={handleSave} class="space-y-6">
		<!-- Name Field -->
		<div>
			<label for="mode-name" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Name <span class="text-red-500">*</span>
			</label>
			<div class="mt-1">
				<input
					id="mode-name"
					name="name"
					type="text"
					bind:value={formData.name}
					on:blur={() => handleBlur('name')}
					maxlength={MAX_NAME_LENGTH}
					disabled={isSaving}
					class="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500
						disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white
						{touched.name && errors.name ? 'border-red-500' : ''}"
					placeholder="e.g., Python Developer"
					aria-required="true"
					aria-invalid={touched.name && !!errors.name}
					aria-describedby={touched.name && errors.name ? 'name-error' : undefined}
				/>
			</div>
			<div class="mt-1 flex justify-between">
				{#if touched.name && errors.name}
					<p id="name-error" class="text-sm text-red-600 dark:text-red-400" transition:slide>
						{errors.name}
					</p>
				{:else}
					<div></div>
				{/if}
				<span class="text-xs text-gray-500 dark:text-gray-400">
					{formData.name.length}/{MAX_NAME_LENGTH}
				</span>
			</div>
		</div>

		<!-- Description Field -->
		<div>
			<label
				for="mode-description"
				class="block text-sm font-medium text-gray-700 dark:text-gray-300"
			>
				Description <span class="text-red-500">*</span>
			</label>
			<div class="mt-1">
				<textarea
					id="mode-description"
					name="description"
					bind:value={formData.description}
					on:blur={() => handleBlur('description')}
					maxlength={MAX_DESCRIPTION_LENGTH}
					disabled={isSaving}
					rows="2"
					class="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500
						disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white
						{touched.description && errors.description ? 'border-red-500' : ''}"
					placeholder="Brief description of what this mode does"
					aria-required="true"
					aria-invalid={touched.description && !!errors.description}
					aria-describedby={touched.description && errors.description
						? 'description-error'
						: undefined}
				></textarea>
			</div>
			<div class="mt-1 flex justify-between">
				{#if touched.description && errors.description}
					<p id="description-error" class="text-sm text-red-600 dark:text-red-400" transition:slide>
						{errors.description}
					</p>
				{:else}
					<div></div>
				{/if}
				<span class="text-xs text-gray-500 dark:text-gray-400">
					{formData.description.length}/{MAX_DESCRIPTION_LENGTH}
				</span>
			</div>
		</div>

		<!-- Category Selection -->
		<div>
			<label for="mode-category" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Category
			</label>
			<div class="mt-1">
				<select
					id="mode-category"
					bind:value={formData.category}
					disabled={isSaving}
					class="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500
						disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
				>
					{#each categories as category}
						<option value={category.value}>
							{category.icon}
							{category.label}
						</option>
					{/each}
				</select>
			</div>
		</div>

		<!-- Model Selection -->
		<div>
			<label for="mode-model" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				Preferred Model (Optional)
			</label>
			<div class="mt-1">
				<select
					id="mode-model"
					bind:value={formData.defaultModel}
					disabled={isSaving}
					class="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500
						disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
				>
					{#each availableModels as model}
						<option value={model.value}>{model.label}</option>
					{/each}
				</select>
			</div>
			<p class="mt-1 text-xs text-gray-500 dark:text-gray-400">
				Leave as default to use the system's current model
			</p>
		</div>

		<!-- System Prompt Field -->
		<div>
			<label for="mode-prompt" class="block text-sm font-medium text-gray-700 dark:text-gray-300">
				System Prompt <span class="text-red-500">*</span>
			</label>
			<div class="mt-1">
				<textarea
					id="mode-prompt"
					name="prompt"
					bind:value={formData.prompt}
					on:blur={() => handleBlur('prompt')}
					maxlength={MAX_PROMPT_LENGTH}
					disabled={isSaving}
					rows="6"
					class="block w-full rounded-md border-gray-300 font-mono shadow-sm focus:border-blue-500
						focus:ring-blue-500 disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white
						{touched.prompt && errors.prompt ? 'border-red-500' : ''}"
					placeholder="Enter the system prompt that defines the behavior of this mode..."
					aria-required="true"
					aria-invalid={touched.prompt && !!errors.prompt}
					aria-describedby={touched.prompt && errors.prompt ? 'prompt-error' : undefined}
				></textarea>
			</div>
			<div class="mt-1 flex justify-between">
				{#if touched.prompt && errors.prompt}
					<p id="prompt-error" class="text-sm text-red-600 dark:text-red-400" transition:slide>
						{errors.prompt}
					</p>
				{:else}
					<div></div>
				{/if}
				<span class="text-xs text-gray-500 dark:text-gray-400">
					{formData.prompt.length}/{MAX_PROMPT_LENGTH}
				</span>
			</div>
		</div>

		<!-- Tools Selection -->
		<div>
			<div class="flex items-center justify-between">
				<label class="block text-sm font-medium text-gray-700 dark:text-gray-300">
					Available Tools <span class="text-red-500">*</span>
				</label>
				<button
					type="button"
					on:click={toggleAllTools}
					disabled={isSaving}
					class="text-sm text-blue-600 hover:text-blue-500 disabled:opacity-50 dark:text-blue-400 dark:hover:text-blue-300"
				>
					{formData.tools.length === $availableTools.length ? 'Deselect All' : 'Select All'}
				</button>
			</div>

			<!-- Tools Search -->
			<div class="mt-2">
				<input
					type="text"
					bind:value={searchQuery}
					disabled={isSaving}
					placeholder="Search tools..."
					class="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500
						disabled:opacity-50 sm:text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
				/>
			</div>

			<!-- Tools List -->
			<div
				class="mt-2 max-h-48 overflow-y-auto rounded-md border border-gray-200 bg-gray-50 p-3
					dark:border-gray-600 dark:bg-gray-900"
				role="group"
				aria-label="Available tools"
			>
				{#if filteredTools.length === 0}
					<p class="text-sm text-gray-500 dark:text-gray-400">
						{searchQuery ? 'No tools match your search' : 'No tools available'}
					</p>
				{:else}
					<div class="grid grid-cols-2 gap-2 sm:grid-cols-3">
						{#each filteredTools as tool}
							<label
								class="flex cursor-pointer items-center rounded p-1 hover:bg-gray-100
									dark:hover:bg-gray-800"
							>
								<input
									type="checkbox"
									checked={formData.tools.includes(tool)}
									on:change={() => toggleTool(tool)}
									disabled={isSaving}
									class="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500
										disabled:opacity-50 dark:border-gray-600 dark:bg-gray-700"
									aria-describedby="tool-{tool}"
								/>
								<span id="tool-{tool}" class="ml-2 text-sm text-gray-700 dark:text-gray-300">
									{tool}
								</span>
							</label>
						{/each}
					</div>
				{/if}
			</div>

			<div class="mt-1 flex justify-between">
				{#if touched.tools && errors.tools}
					<p class="text-sm text-red-600 dark:text-red-400" transition:slide>
						{errors.tools}
					</p>
				{:else}
					<div></div>
				{/if}
				<span class="text-xs text-gray-500 dark:text-gray-400">
					{formData.tools.length} tool{formData.tools.length !== 1 ? 's' : ''} selected
				</span>
			</div>
		</div>

		<!-- Action Buttons -->
		<div class="flex justify-end space-x-3 border-t border-gray-200 pt-6 dark:border-gray-700">
			<button
				type="button"
				on:click={handleCancel}
				disabled={isSaving}
				class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700
					shadow-sm hover:bg-gray-50 focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 focus:outline-none
					disabled:opacity-50 dark:border-gray-600 dark:bg-gray-700 dark:text-gray-300
					dark:hover:bg-gray-600"
			>
				Cancel
			</button>
			<button
				type="submit"
				disabled={isSaving}
				class="inline-flex items-center rounded-md border border-transparent bg-blue-600 px-4 py-2
					text-sm font-medium text-white shadow-sm hover:bg-blue-700 focus:ring-2 focus:ring-blue-500
					focus:ring-offset-2 focus:outline-none disabled:opacity-50 dark:bg-blue-500
					dark:hover:bg-blue-600"
			>
				{#if isSaving}
					<svg class="mr-2 h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
						<circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"
						></circle>
						<path
							class="opacity-75"
							fill="currentColor"
							d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
						></path>
					</svg>
					Saving...
				{:else}
					{isEditing ? 'Update Mode' : 'Create Mode'}
				{/if}
			</button>
		</div>
	</form>
</div>

<style>
	/* Custom scrollbar for tools list */
	:global(.dark) div[role='group'] {
		scrollbar-width: thin;
		scrollbar-color: rgb(107 114 128) rgb(17 24 39);
	}

	div[role='group']::-webkit-scrollbar {
		width: 8px;
	}

	div[role='group']::-webkit-scrollbar-track {
		background: rgb(249 250 251);
		border-radius: 4px;
	}

	:global(.dark) div[role='group']::-webkit-scrollbar-track {
		background: rgb(17 24 39);
	}

	div[role='group']::-webkit-scrollbar-thumb {
		background: rgb(209 213 219);
		border-radius: 4px;
	}

	:global(.dark) div[role='group']::-webkit-scrollbar-thumb {
		background: rgb(107 114 128);
	}

	div[role='group']::-webkit-scrollbar-thumb:hover {
		background: rgb(156 163 175);
	}

	:global(.dark) div[role='group']::-webkit-scrollbar-thumb:hover {
		background: rgb(156 163 175);
	}
</style>
