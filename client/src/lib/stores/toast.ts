// Toast notification store for user feedback
import { writable } from 'svelte/store';

export interface ToastMessage {
	id: string;
	type: 'success' | 'error' | 'warning' | 'info';
	message: string;
	duration?: number; // milliseconds, 0 = permanent until dismissed
	dismissible?: boolean;
}

// Toast store
export const toasts = writable<ToastMessage[]>([]);

// Toast actions
export const toastActions = {
	/**
	 * Add a new toast message
	 */
	add(toast: Omit<ToastMessage, 'id'>): string {
		const id = `toast-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
		const newToast: ToastMessage = {
			id,
			duration: 4000, // Default 4 seconds
			dismissible: true,
			...toast
		};

		toasts.update((current) => [...current, newToast]);

		// Auto-dismiss if duration is set
		if (newToast.duration && newToast.duration > 0) {
			setTimeout(() => {
				toastActions.dismiss(id);
			}, newToast.duration);
		}

		return id;
	},

	/**
	 * Dismiss a specific toast
	 */
	dismiss(id: string): void {
		toasts.update((current) => current.filter((toast) => toast.id !== id));
	},

	/**
	 * Clear all toasts
	 */
	clear(): void {
		toasts.set([]);
	},

	/**
	 * Convenience methods for common toast types
	 */
	success(message: string, duration = 3000): string {
		return toastActions.add({ type: 'success', message, duration });
	},

	error(message: string, duration = 5000): string {
		return toastActions.add({ type: 'error', message, duration });
	},

	warning(message: string, duration = 4000): string {
		return toastActions.add({ type: 'warning', message, duration });
	},

	info(message: string, duration = 4000): string {
		return toastActions.add({ type: 'info', message, duration });
	},

	/**
	 * Show mode change notification
	 */
	modeChanged(modeName: string): string {
		return toastActions.success(`Switched to ${modeName} mode`, 2500);
	},

	/**
	 * Show tool availability warning
	 */
	toolsUnavailable(unavailableTools: string[], modeName: string): string {
		const toolList =
			unavailableTools.length <= 3
				? unavailableTools.join(', ')
				: `${unavailableTools.slice(0, 3).join(', ')} and ${unavailableTools.length - 3} more`;

		return toastActions.warning(`Some tools unavailable in ${modeName} mode: ${toolList}`, 6000);
	}
};
