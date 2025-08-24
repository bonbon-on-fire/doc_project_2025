import { describe, it, expect } from 'vitest';
import {
	MessageVisibility,
	normalizeVisibility,
	isContentHidden,
	isTaskManagerFunction,
	ANIMATION_DELAYS,
	TEXT_PREVIEW,
	TASK_MANAGER_FUNCTIONS
} from './ui';

describe('UI Constants', () => {
	describe('normalizeVisibility', () => {
		it('should normalize lowercase string values', () => {
			expect(normalizeVisibility('plain')).toBe(MessageVisibility.Plain);
			expect(normalizeVisibility('summary')).toBe(MessageVisibility.Summary);
			expect(normalizeVisibility('encrypted')).toBe(MessageVisibility.Encrypted);
		});

		it('should normalize uppercase string values', () => {
			expect(normalizeVisibility('PLAIN')).toBe(MessageVisibility.Plain);
			expect(normalizeVisibility('SUMMARY')).toBe(MessageVisibility.Summary);
			expect(normalizeVisibility('ENCRYPTED')).toBe(MessageVisibility.Encrypted);
		});

		it('should normalize mixed-case string values', () => {
			expect(normalizeVisibility('Plain')).toBe(MessageVisibility.Plain);
			expect(normalizeVisibility('Summary')).toBe(MessageVisibility.Summary);
			expect(normalizeVisibility('Encrypted')).toBe(MessageVisibility.Encrypted);
		});

		it('should handle numeric values (legacy)', () => {
			expect(normalizeVisibility(0)).toBe(MessageVisibility.Plain);
			expect(normalizeVisibility(1)).toBe(MessageVisibility.Summary);
			expect(normalizeVisibility(2)).toBe(MessageVisibility.Encrypted);
		});

		it('should return undefined for invalid numeric values', () => {
			expect(normalizeVisibility(3)).toBeUndefined();
			expect(normalizeVisibility(-1)).toBeUndefined();
			expect(normalizeVisibility(999)).toBeUndefined();
		});

		it('should return undefined for invalid string values', () => {
			expect(normalizeVisibility('invalid')).toBeUndefined();
			expect(normalizeVisibility('unknown')).toBeUndefined();
			expect(normalizeVisibility('')).toBeUndefined();
		});

		it('should handle undefined and null inputs', () => {
			expect(normalizeVisibility(undefined)).toBeUndefined();
			expect(normalizeVisibility(null)).toBeUndefined();
		});

		it('should preserve already normalized values', () => {
			expect(normalizeVisibility(MessageVisibility.Plain)).toBe(MessageVisibility.Plain);
			expect(normalizeVisibility(MessageVisibility.Summary)).toBe(MessageVisibility.Summary);
			expect(normalizeVisibility(MessageVisibility.Encrypted)).toBe(MessageVisibility.Encrypted);
		});
	});

	describe('isContentHidden', () => {
		it('should return true for encrypted visibility', () => {
			expect(isContentHidden('encrypted')).toBe(true);
			expect(isContentHidden('Encrypted')).toBe(true);
			expect(isContentHidden('ENCRYPTED')).toBe(true);
			expect(isContentHidden(2)).toBe(true);
			expect(isContentHidden(MessageVisibility.Encrypted)).toBe(true);
		});

		it('should return false for non-encrypted visibility', () => {
			expect(isContentHidden('plain')).toBe(false);
			expect(isContentHidden('Plain')).toBe(false);
			expect(isContentHidden('summary')).toBe(false);
			expect(isContentHidden('Summary')).toBe(false);
			expect(isContentHidden(0)).toBe(false);
			expect(isContentHidden(1)).toBe(false);
			expect(isContentHidden(MessageVisibility.Plain)).toBe(false);
			expect(isContentHidden(MessageVisibility.Summary)).toBe(false);
		});

		it('should return false for invalid or undefined values', () => {
			expect(isContentHidden(undefined)).toBe(false);
			expect(isContentHidden(null)).toBe(false);
			expect(isContentHidden('invalid')).toBe(false);
			expect(isContentHidden(999)).toBe(false);
		});
	});

	describe('isTaskManagerFunction', () => {
		it('should identify task manager functions', () => {
			expect(isTaskManagerFunction('add_task')).toBe(true);
			expect(isTaskManagerFunction('update_task')).toBe(true);
			expect(isTaskManagerFunction('delete_task')).toBe(true);
			expect(isTaskManagerFunction('complete_task')).toBe(true);
			expect(isTaskManagerFunction('task_manager')).toBe(true);
		});

		it('should identify task manager functions case-insensitively', () => {
			expect(isTaskManagerFunction('ADD_TASK')).toBe(true);
			expect(isTaskManagerFunction('Update_Task')).toBe(true);
			expect(isTaskManagerFunction('DELETE_TASK')).toBe(true);
		});

		it('should identify task manager functions with different separators', () => {
			expect(isTaskManagerFunction('add-task')).toBe(true);
			expect(isTaskManagerFunction('addtask')).toBe(true);
			expect(isTaskManagerFunction('add_task')).toBe(true);
		});

		it('should identify task manager functions as part of longer names', () => {
			expect(isTaskManagerFunction('my_add_task_function')).toBe(true);
			expect(isTaskManagerFunction('custom_task_manager')).toBe(true);
			expect(isTaskManagerFunction('prefix_complete_task_suffix')).toBe(true);
		});

		it('should not identify non-task manager functions', () => {
			expect(isTaskManagerFunction('calculate')).toBe(false);
			expect(isTaskManagerFunction('fetch_data')).toBe(false);
			expect(isTaskManagerFunction('process_image')).toBe(false);
			expect(isTaskManagerFunction('random_function')).toBe(false);
		});
	});

	describe('Constants', () => {
		it('should export animation delays', () => {
			expect(ANIMATION_DELAYS.INSTANT).toBe(0);
			expect(ANIMATION_DELAYS.FAST).toBe(150);
			expect(ANIMATION_DELAYS.NORMAL).toBe(300);
			expect(ANIMATION_DELAYS.SLOW).toBe(500);
		});

		it('should export text preview lengths', () => {
			expect(TEXT_PREVIEW.COLLAPSED_LENGTH).toBe(60);
			expect(TEXT_PREVIEW.MAX_SNIPPET_LENGTH).toBe(100);
		});

		it('should export task manager functions array', () => {
			expect(TASK_MANAGER_FUNCTIONS).toContain('add_task');
			expect(TASK_MANAGER_FUNCTIONS).toContain('update_task');
			expect(TASK_MANAGER_FUNCTIONS).toContain('delete_task');
			expect(TASK_MANAGER_FUNCTIONS.length).toBeGreaterThan(10);
		});

		it('should export MessageVisibility enum', () => {
			expect(MessageVisibility.Plain).toBe('Plain');
			expect(MessageVisibility.Summary).toBe('Summary');
			expect(MessageVisibility.Encrypted).toBe('Encrypted');
		});
	});
});
