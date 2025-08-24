// Svelte stores for mode state management following loose coupling principles
import { writable, derived, get } from 'svelte/store';
import type { Mode, ModeCategory } from '$shared/types/mode';
import { apiClient } from '$lib/api/client';
import { logger } from '$lib/utils/logger';
import { currentUser } from './chat';
import { toastActions } from './toast';

// Core mode state stores - single responsibility principle
export const availableModes = writable<Mode[]>([]);
export const selectedModeId = writable<string | null>(null);
export const modesLoading = writable<boolean>(false);
export const modesError = writable<string | null>(null);

// Derived stores for computed state - loose coupling through composition
export const selectedMode = derived(
	[availableModes, selectedModeId],
	([$availableModes, $selectedModeId]) =>
		$selectedModeId ? $availableModes.find((m) => m.id === $selectedModeId) || null : null
);

// Grouped modes by category for UI organization
export const groupedModes = derived(availableModes, ($modes) => {
	const groups: Record<ModeCategory | 'system', Mode[]> = {
		task: [],
		role: [],
		custom: [],
		system: [] // For modes without explicit category
	};

	$modes.forEach((mode) => {
		const category = mode.category || (mode.isSystem ? ('system' as const) : 'custom');
		if (category in groups) {
			groups[category].push(mode);
		} else {
			groups.system.push(mode);
		}
	});

	return groups;
});

// System vs custom mode separation
export const systemModes = derived(availableModes, ($modes) => $modes.filter((m) => m.isSystem));

export const customModes = derived(availableModes, ($modes) => $modes.filter((m) => !m.isSystem));

// Tool availability tracking
export const availableTools = writable<string[]>([]);
export const unavailableTools = derived([selectedMode, availableTools], ([$mode, $tools]) => {
	if (!$mode) return [];
	return $mode.tools.filter((tool) => !$tools.includes(tool));
});

// Mode service actions - encapsulating business logic
export const modeActions = {
	/**
	 * Fetch all available modes for the current user
	 * Implements error handling and loading state management
	 */
	async fetchModes(): Promise<void> {
		const user = get(currentUser);
		if (!user?.id) {
			logger.warn('Cannot fetch modes: No user ID available');
			modesError.set('User not authenticated');
			return;
		}

		modesLoading.set(true);
		modesError.set(null);

		try {
			logger.info('Fetching modes for user', { userId: user.id });
			const response = await apiClient.getModes(user.id);

			// Sort modes for consistent UI presentation
			const sortedModes = response.modes.sort((a, b) => {
				// System modes first, then by name
				if (a.isSystem && !b.isSystem) return -1;
				if (!a.isSystem && b.isSystem) return 1;
				return a.name.localeCompare(b.name);
			});

			availableModes.set(sortedModes);
			logger.info('Successfully fetched modes', { count: sortedModes.length });

			// Auto-select the first mode if none selected and modes available
			const currentSelected = get(selectedModeId);
			if (!currentSelected && sortedModes.length > 0) {
				// Prefer 'general' mode if available, otherwise first mode
				const generalMode = sortedModes.find((m) => m.id === 'general');
				const defaultMode = generalMode || sortedModes[0];
				selectedModeId.set(defaultMode.id);
				logger.info('Auto-selected default mode', {
					modeId: defaultMode.id,
					name: defaultMode.name
				});
			}
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to fetch modes';
			logger.error('Error fetching modes', { error: errorMessage, userId: user.id });
			modesError.set(errorMessage);
			availableModes.set([]);
		} finally {
			modesLoading.set(false);
		}
	},

	/**
	 * Select a mode by ID with validation
	 * Implements business rule validation and state consistency
	 * Includes visual feedback and tool availability warnings
	 */
	selectMode(modeId: string | null): void {
		if (modeId === null) {
			selectedModeId.set(null);
			logger.info('Deselected mode');
			return;
		}

		const modes = get(availableModes);
		const mode = modes.find((m) => m.id === modeId);

		if (!mode) {
			logger.warn('Attempted to select non-existent mode', { modeId });
			modesError.set(`Mode '${modeId}' not found`);
			toastActions.error(`Mode '${modeId}' not found`);
			return;
		}

		const previousModeId = get(selectedModeId);
		const previousMode = previousModeId ? modes.find((m) => m.id === previousModeId) : null;

		selectedModeId.set(modeId);
		modesError.set(null); // Clear any previous errors

		// Show success toast for mode change (skip on initial load)
		if (previousMode && previousMode.id !== modeId) {
			toastActions.modeChanged(mode.name);
		}

		// Check for unavailable tools and show warning
		const tools = get(availableTools);
		const unavailable = mode.tools.filter((tool) => !tools.includes(tool));
		if (unavailable.length > 0) {
			toastActions.toolsUnavailable(unavailable, mode.name);
			logger.warn('Selected mode has unavailable tools', {
				modeId,
				modeName: mode.name,
				unavailableTools: unavailable
			});
		}

		logger.info('Mode selected', {
			modeId,
			name: mode.name,
			isSystem: mode.isSystem,
			previousMode: previousMode?.name || 'none',
			unavailableToolsCount: unavailable.length
		});
	},

	/**
	 * Create a new custom mode
	 * Implements optimistic updates for better UX
	 */
	async createMode(modeData: {
		name: string;
		description: string;
		prompt: string;
		tools: string[];
		defaultModel?: string;
		category?: ModeCategory;
	}): Promise<Mode | null> {
		const user = get(currentUser);
		if (!user?.id) {
			modesError.set('User not authenticated');
			return null;
		}

		modesLoading.set(true);
		modesError.set(null);

		try {
			logger.info('Creating custom mode', { name: modeData.name, userId: user.id });

			const newMode = await apiClient.createMode({
				userId: user.id,
				...modeData
			});

			// Update local state optimistically
			const currentModes = get(availableModes);
			const updatedModes = [...currentModes, newMode].sort((a, b) => {
				if (a.isSystem && !b.isSystem) return -1;
				if (!a.isSystem && b.isSystem) return 1;
				return a.name.localeCompare(b.name);
			});

			availableModes.set(updatedModes);
			logger.info('Successfully created custom mode', { modeId: newMode.id, name: newMode.name });

			return newMode;
		} catch (error) {
			const errorMessage = error instanceof Error ? error.message : 'Failed to create mode';
			logger.error('Error creating mode', { error: errorMessage, name: modeData.name });
			modesError.set(errorMessage);
			return null;
		} finally {
			modesLoading.set(false);
		}
	},

	/**
	 * Clear all mode-related errors
	 * Utility for error recovery
	 */
	clearError(): void {
		modesError.set(null);
	},

	/**
	 * Reset mode selection to default (general mode if available)
	 * Utility for UI reset operations
	 */
	resetToDefault(): void {
		const modes = get(availableModes);
		const generalMode = modes.find((m) => m.id === 'general');
		const defaultMode = generalMode || modes.find((m) => m.isSystem) || modes[0];

		if (defaultMode) {
			this.selectMode(defaultMode.id);
		} else {
			selectedModeId.set(null);
		}
	},

	/**
	 * Fetch available tools from the server
	 * Used to check tool availability for modes
	 */
	async fetchAvailableTools(): Promise<void> {
		try {
			logger.info('Fetching available tools');
			// This would need to be implemented in the API client
			// For now, we'll use a mock list of commonly available tools
			const mockTools = [
				'Task',
				'Read',
				'Write',
				'Edit',
				'MultiEdit',
				'Bash',
				'Grep',
				'Glob',
				'LS',
				'WebSearch',
				'WebFetch',
				'NotebookEdit',
				'ExitPlanMode'
			];
			availableTools.set(mockTools);
			logger.info('Successfully fetched available tools', { count: mockTools.length });
		} catch (error) {
			logger.error('Error fetching available tools', { error });
			// Set empty array on error to show all tools as unavailable
			availableTools.set([]);
		}
	}
};

// Auto-initialize modes when store is first accessed
// This ensures data is available when components mount
let initialized = false;

selectedModeId.subscribe(() => {
	if (!initialized) {
		initialized = true;
		Promise.all([modeActions.fetchModes(), modeActions.fetchAvailableTools()]).catch((error) => {
			logger.error('Failed to auto-initialize modes', { error });
		});
	}
});

// Export types for component usage
export type { Mode, ModeCategory };
