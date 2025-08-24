/**
 * UI Constants
 *
 * Centralized constants for UI-related values used across components.
 * Extracted from magic numbers/strings found in the codebase.
 */

// Animation delays (in milliseconds)
export const ANIMATION_DELAYS = {
	INSTANT: 0,
	FAST: 150,
	NORMAL: 300,
	SLOW: 500
} as const;

// Text preview lengths
export const TEXT_PREVIEW = {
	COLLAPSED_LENGTH: 60,
	MAX_SNIPPET_LENGTH: 100
} as const;

// Visibility types for message content
export enum MessageVisibility {
	Plain = 'Plain',
	Summary = 'Summary',
	Encrypted = 'Encrypted'
}

// Task manager function names
export const TASK_MANAGER_FUNCTIONS = [
	'add_task',
	'addtask',
	'add-task',
	'update_task',
	'updatetask',
	'update-task',
	'delete_task',
	'deletetask',
	'delete-task',
	'remove_task',
	'removetask',
	'list_tasks',
	'listtasks',
	'list-tasks',
	'get_tasks',
	'gettasks',
	'clear_tasks',
	'cleartasks',
	'clear-tasks',
	'complete_task',
	'completetask',
	'complete-task',
	'start_task',
	'starttask',
	'start-task',
	'add_subtask',
	'addsubtask',
	'add-subtask',
	'add_note',
	'addnote',
	'add-note',
	'task_manager',
	'taskmanager'
] as const;

/**
 * Check if a function name is a task manager function
 */
export function isTaskManagerFunction(functionName: string): boolean {
	const normalized = functionName.toLowerCase();
	return TASK_MANAGER_FUNCTIONS.some((fn) => normalized.includes(fn));
}

/**
 * Convert server visibility format to client format
 * Server sends lowercase, client uses PascalCase
 */
export function normalizeVisibility(
	visibility: string | number | undefined
): MessageVisibility | undefined {
	if (visibility === undefined || visibility === null) {
		return undefined;
	}

	// Handle numeric values (legacy)
	if (typeof visibility === 'number') {
		switch (visibility) {
			case 0:
				return MessageVisibility.Plain;
			case 1:
				return MessageVisibility.Summary;
			case 2:
				return MessageVisibility.Encrypted;
			default:
				return undefined;
		}
	}

	// Handle string values (case-insensitive)
	const visStr = String(visibility).toLowerCase();
	switch (visStr) {
		case 'plain':
			return MessageVisibility.Plain;
		case 'summary':
			return MessageVisibility.Summary;
		case 'encrypted':
			return MessageVisibility.Encrypted;
		default:
			// Try exact match for already normalized values
			if (Object.values(MessageVisibility).includes(visibility as MessageVisibility)) {
				return visibility as MessageVisibility;
			}
			return undefined;
	}
}

/**
 * Check if content should be hidden based on visibility
 */
export function isContentHidden(visibility: string | number | undefined): boolean {
	const normalized = normalizeVisibility(visibility);
	return normalized === MessageVisibility.Encrypted;
}
