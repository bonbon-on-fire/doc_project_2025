/**
 * Snapshot Helper for Svelte Reactivity
 *
 * Provides utilities to ensure Svelte's reactivity system detects changes
 * by creating new object/array references when values are updated.
 *
 * @module snapshotHelper
 */

/**
 * Creates a shallow clone of arrays and objects to trigger Svelte reactivity.
 *
 * Svelte's reactivity system relies on reference changes to detect updates.
 * This function ensures that when an object or array is modified, a new
 * reference is created so Svelte will re-render components that depend on it.
 *
 * @param value - The value to snapshot
 * @returns A shallow clone for objects/arrays, or the original value for primitives
 *
 * @example
 * ```typescript
 * // In a Svelte store or component
 * let state = { count: 0, items: [] };
 *
 * // Modify state
 * state.count++;
 * state.items.push('new item');
 *
 * // Trigger reactivity by creating new reference
 * state = snapshot(state);
 * ```
 */
export function snapshot<T>(value: T): T {
	// Handle arrays - create shallow clone
	if (Array.isArray(value)) {
		return [...value] as T;
	}

	// Handle objects (excluding null) - create shallow clone
	if (value && typeof value === 'object') {
		return { ...value };
	}

	// Primitives (string, number, boolean, null, undefined, etc.) are immutable
	// Functions and other types are returned as-is
	return value;
}

/**
 * Deep snapshot that recursively clones nested structures.
 *
 * Use this when you need to ensure all nested objects/arrays also
 * get new references. More expensive than shallow snapshot.
 *
 * @param value - The value to deep snapshot
 * @returns A deep clone for objects/arrays, or the original value for primitives
 */
export function deepSnapshot<T>(value: T): T {
	// Handle primitives and functions
	if (value === null || value === undefined) {
		return value;
	}

	if (typeof value !== 'object') {
		return value;
	}

	// Handle arrays
	if (Array.isArray(value)) {
		return value.map((item) => deepSnapshot(item)) as T;
	}

	// Handle objects
	const cloned: any = {};
	for (const key in value) {
		if (value.hasOwnProperty(key)) {
			cloned[key] = deepSnapshot(value[key]);
		}
	}

	// Handle symbol properties
	const symbols = Object.getOwnPropertySymbols(value);
	for (const sym of symbols) {
		cloned[sym] = deepSnapshot((value as any)[sym]);
	}

	return cloned as T;
}

/**
 * Checks if two values would trigger Svelte reactivity if swapped.
 *
 * @param oldValue - The previous value
 * @param newValue - The new value
 * @returns True if swapping would trigger reactivity, false otherwise
 */
export function wouldTriggerReactivity(oldValue: any, newValue: any): boolean {
	// Different references always trigger reactivity
	if (oldValue !== newValue) {
		return true;
	}

	// Same reference for objects/arrays won't trigger
	if (typeof oldValue === 'object' && oldValue !== null) {
		return false;
	}

	// Same primitive values won't trigger
	return false;
}

export default snapshot;
