import { writable, derived, get } from 'svelte/store';

// Debug flag for development mode logging
const isDev = process.env.NODE_ENV === 'development';

/**
 * Validates that a message ID is valid (non-empty string).
 * @param messageId - The message ID to validate
 * @throws Error if messageId is invalid
 */
function validateMessageId(messageId: string): void {
	if (!messageId || typeof messageId !== 'string' || messageId.trim() === '') {
		throw new Error('Message ID must be a non-empty string');
	}
}

/**
 * Validates that state updates are in the correct format.
 * @param updates - The state updates to validate
 * @throws Error if updates are invalid
 */
function validateStateUpdates(updates: Partial<MessageState>): void {
	if (updates.expanded !== undefined && typeof updates.expanded !== 'boolean') {
		throw new Error('expanded must be a boolean');
	}

	if (updates.renderPhase !== undefined) {
		const validPhases = ['initial', 'streaming', 'enhanced', 'complete'];
		if (!validPhases.includes(updates.renderPhase)) {
			throw new Error(`renderPhase must be one of: ${validPhases.join(', ')}`);
		}
	}
}

/**
 * Logs state changes in development mode.
 * @param messageId - The message ID
 * @param action - The action being performed
 * @param data - Additional data to log
 */
function logStateChange(messageId: string, action: string, data?: any): void {
	if (isDev) {
		console.log(`[MessageState] ${action} for message ${messageId}`, data);
	}
}

/**
 * Represents the current state of a message in the Rich Message Rendering system.
 * Tracks expansion state and rendering progress for individual messages.
 */
export interface MessageState {
	/**
	 * Whether the message is currently expanded (showing full content).
	 * Collapsed messages show preview/summary content only.
	 */
	expanded: boolean;

	/**
	 * Current rendering phase of the message content.
	 * - 'initial': Message just created, basic rendering
	 * - 'streaming': Currently receiving streaming updates
	 * - 'enhanced': Streaming complete, enhanced rendering applied
	 * - 'complete': Full rendering with all features enabled
	 */
	renderPhase: 'initial' | 'streaming' | 'enhanced' | 'complete';
}

/**
 * Map of message IDs to their current state.
 * Provides centralized state management for all message rendering states.
 */
export interface MessageStates {
	[messageId: string]: MessageState;
}

/**
 * Global store containing the state of all messages.
 * Used by MessageRouter and individual renderer components.
 */
export const messageStates = writable<MessageStates>({});

/**
 * Store containing the ID of the currently latest (most recent) message.
 * Used to determine which message should be expanded by default.
 */
export const latestMessageId = writable<string | null>(null);

/**
 * Derived store that combines message states with latest message tracking.
 * Automatically manages expand/collapse behavior based on latest message changes.
 */
export const expandedStates = derived([messageStates, latestMessageId], ([states, latest]) => {
	const result: { [messageId: string]: boolean } = {};

	for (const messageId in states) {
		// Latest message is expanded by default, others follow their state
		result[messageId] = messageId === latest ? true : states[messageId].expanded;
	}

	return result;
});

/**
 * Updates the state of a specific message with partial state changes.
 * Merges provided updates with existing state.
 * Only triggers store update if values actually changed.
 *
 * @param messageId - The ID of the message to update
 * @param updates - Partial state updates to apply
 * @throws Error if messageId or updates are invalid
 */
export function updateMessageState(messageId: string, updates: Partial<MessageState>) {
	// Validate inputs
	validateMessageId(messageId);
	validateStateUpdates(updates);

	messageStates.update((states) => {
		const existingState = states[messageId] || {
			expanded: false,
			renderPhase: 'initial' as const
		};

		// Check if any values actually changed
		const hasChanges = Object.keys(updates).some((key) => {
			const updateKey = key as keyof MessageState;
			const newValue = updates[updateKey];
			const existingValue = existingState[updateKey];
			return newValue !== existingValue;
		});

		if (!hasChanges) {
			return states; // Return unchanged state to prevent unnecessary reactive updates
		}

		const newState = {
			...existingState,
			...updates
		};

		logStateChange(messageId, 'Updated state', { from: existingState, to: newState });

		return {
			...states,
			[messageId]: newState
		};
	});
}

/**
 * Initializes state for a new message with default values.
 * Called when a message is first rendered.
 *
 * @param messageId - The ID of the message to initialize
 * @param isLatest - Whether this is the latest message (should be expanded)
 * @throws Error if messageId is invalid
 */
export function initializeMessageState(messageId: string, isLatest = false) {
	validateMessageId(messageId);

	messageStates.update((states) => {
		// Don't overwrite existing state
		if (states[messageId]) {
			logStateChange(messageId, 'Already initialized, skipping');
			return states;
		}

		const newState = {
			expanded: isLatest,
			renderPhase: 'initial' as const
		};

		logStateChange(messageId, 'Initialized', newState);

		return {
			...states,
			[messageId]: newState
		};
	});

	if (isLatest) {
		latestMessageId.set(messageId);
	}
}

/**
 * Auto-collapses all messages except the specified latest message.
 * Called when a new message becomes the latest.
 *
 * @param newLatestMessageId - The ID of the new latest message
 * @throws Error if newLatestMessageId is invalid
 */
export function setLatestMessage(newLatestMessageId: string) {
	validateMessageId(newLatestMessageId);

	logStateChange(newLatestMessageId, 'Set as latest message');

	latestMessageId.set(newLatestMessageId);

	messageStates.update((states) => {
		const updatedStates: MessageStates = {};

		for (const messageId in states) {
			const isLatest = messageId === newLatestMessageId;
			updatedStates[messageId] = {
				...states[messageId],
				expanded: isLatest
			};

			if (!isLatest && states[messageId].expanded) {
				logStateChange(messageId, 'Auto-collapsed (no longer latest)');
			}
		}

		return updatedStates;
	});
}

/**
 * Toggles the expanded state of a specific message.
 * Used for manual expand/collapse interactions.
 *
 * @param messageId - The ID of the message to toggle
 * @throws Error if messageId is invalid
 */
export function toggleMessageExpansion(messageId: string) {
	validateMessageId(messageId);

	messageStates.update((states) => {
		const currentState = states[messageId];
		if (!currentState) {
			logStateChange(messageId, 'Toggle failed - message not found');
			return states;
		}

		const newExpanded = !currentState.expanded;
		logStateChange(messageId, `Toggled expansion to ${newExpanded}`);

		return {
			...states,
			[messageId]: {
				...currentState,
				expanded: newExpanded
			}
		};
	});
}

/**
 * Gets the current state of a specific message.
 * Returns default state if message not found.
 *
 * @param messageId - The ID of the message to get state for
 * @returns The current message state or default state
 * @throws Error if messageId is invalid
 */
export function getMessageState(messageId: string): MessageState {
	validateMessageId(messageId);

	const currentStates = get(messageStates);

	return (
		currentStates[messageId] || {
			expanded: false,
			renderPhase: 'initial'
		}
	);
}

/**
 * Removes state for a specific message.
 * Used for cleanup when messages are destroyed or removed.
 *
 * @param messageId - The ID of the message to clean up
 * @throws Error if messageId is invalid
 */
export function cleanupMessageState(messageId: string) {
	validateMessageId(messageId);

	logStateChange(messageId, 'Cleaning up state');

	messageStates.update((states) => {
		const { [messageId]: removed, ...remainingStates } = states;
		return remainingStates;
	});

	// If this was the latest message, clear the latest ID
	const currentLatest = get(latestMessageId);
	if (currentLatest === messageId) {
		latestMessageId.set(null);
	}
}

/**
 * Cleans up multiple message states at once.
 * Useful for bulk cleanup operations.
 *
 * @param messageIds - Array of message IDs to clean up
 */
export function cleanupMultipleMessageStates(messageIds: string[]) {
	if (!Array.isArray(messageIds) || messageIds.length === 0) {
		return;
	}

	messageIds.forEach((id) => validateMessageId(id));

	logStateChange('bulk', `Cleaning up ${messageIds.length} message states`, messageIds);

	messageStates.update((states) => {
		const remainingStates = { ...states };
		messageIds.forEach((id) => {
			delete remainingStates[id];
		});
		return remainingStates;
	});

	// Clear latest ID if it was one of the cleaned up messages
	const currentLatest = get(latestMessageId);
	if (currentLatest && messageIds.includes(currentLatest)) {
		latestMessageId.set(null);
	}
}

/**
 * Clears all message states.
 * Useful for testing or when navigating to a new chat.
 */
export function clearMessageStates() {
	logStateChange('all', 'Clearing all message states');
	messageStates.set({});
	latestMessageId.set(null);
}

/**
 * Gets debug information about the current state.
 * Only available in development mode.
 *
 * @returns Debug information object
 */
export function getDebugInfo() {
	if (!isDev) {
		return null;
	}

	const states = get(messageStates);
	const latest = get(latestMessageId);

	return {
		totalMessages: Object.keys(states).length,
		latestMessageId: latest,
		expandedCount: Object.values(states).filter((s) => s.expanded).length,
		messageStates: states
	};
}
