import { describe, it, expect } from 'vitest';
import { snapshot } from './snapshotHelper';

describe('snapshot', () => {
	describe('primitives', () => {
		it('should return primitives unchanged', () => {
			expect(snapshot(5)).toBe(5);
			expect(snapshot('hello')).toBe('hello');
			expect(snapshot(true)).toBe(true);
			expect(snapshot(false)).toBe(false);
			expect(snapshot(null)).toBe(null);
			expect(snapshot(undefined)).toBe(undefined);
		});

		it('should handle special number values', () => {
			expect(snapshot(0)).toBe(0);
			expect(snapshot(-0)).toBe(-0);
			expect(snapshot(Infinity)).toBe(Infinity);
			expect(snapshot(-Infinity)).toBe(-Infinity);
			expect(snapshot(NaN)).toBeNaN();
		});
	});

	describe('arrays', () => {
		it('should create shallow clone of arrays', () => {
			const original = [1, 2, 3];
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original); // Different reference
		});

		it('should handle empty arrays', () => {
			const original: any[] = [];
			const cloned = snapshot(original);

			expect(cloned).toEqual([]);
			expect(cloned).not.toBe(original);
		});

		it('should handle nested arrays (shallow clone only)', () => {
			const inner = [4, 5];
			const original = [1, 2, inner];
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original);
			expect(cloned[2]).toBe(inner); // Inner array is same reference (shallow)
		});

		it('should handle arrays with mixed types', () => {
			const original = [1, 'two', true, null, { key: 'value' }];
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original);
			expect(cloned[4]).toBe(original[4]); // Object reference is same (shallow)
		});
	});

	describe('objects', () => {
		it('should create shallow clone of objects', () => {
			const original = { a: 1, b: 2, c: 3 };
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original); // Different reference
		});

		it('should handle empty objects', () => {
			const original = {};
			const cloned = snapshot(original);

			expect(cloned).toEqual({});
			expect(cloned).not.toBe(original);
		});

		it('should handle nested objects (shallow clone only)', () => {
			const inner = { d: 4 };
			const original = { a: 1, b: inner };
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original);
			expect(cloned.b).toBe(inner); // Inner object is same reference (shallow)
		});

		it('should handle objects with mixed value types', () => {
			const original = {
				num: 1,
				str: 'hello',
				bool: true,
				nil: null,
				arr: [1, 2],
				obj: { nested: true }
			};
			const cloned = snapshot(original);

			expect(cloned).toEqual(original);
			expect(cloned).not.toBe(original);
			expect(cloned.arr).toBe(original.arr); // Array reference is same (shallow)
			expect(cloned.obj).toBe(original.obj); // Object reference is same (shallow)
		});

		it('should preserve property order', () => {
			const original = { z: 1, a: 2, m: 3 };
			const cloned = snapshot(original);

			expect(Object.keys(cloned)).toEqual(['z', 'a', 'm']);
		});

		it('should handle objects with symbol properties', () => {
			const sym = Symbol('test');
			const original = { [sym]: 'value', regular: 'prop' };
			const cloned = snapshot(original);

			expect(cloned[sym]).toBe('value');
			expect(cloned.regular).toBe('prop');
			expect(cloned).not.toBe(original);
		});
	});

	describe('edge cases', () => {
		it('should handle functions as-is', () => {
			const fn = () => 'test';
			expect(snapshot(fn)).toBe(fn);
		});

		it('should handle Date objects', () => {
			const date = new Date();
			const cloned = snapshot(date);

			// Date is an object, so it gets shallow cloned
			expect(cloned).not.toBe(date);
			expect(cloned).toEqual({ ...date });
		});

		it('should handle class instances as objects', () => {
			class TestClass {
				constructor(public value: number) {}
			}

			const instance = new TestClass(42);
			const cloned = snapshot(instance);

			expect(cloned).toEqual({ value: 42 });
			expect(cloned).not.toBe(instance);
		});

		it('should handle objects with null prototype', () => {
			const original = Object.create(null);
			original.prop = 'value';
			const cloned = snapshot(original);

			expect(cloned.prop).toBe('value');
			expect(cloned).not.toBe(original);
		});
	});

	describe('Svelte reactivity use case', () => {
		it('should trigger reactivity by creating new references', () => {
			// Simulating Svelte store update pattern
			let state = { count: 0, items: [1, 2, 3] };

			// Direct mutation (wouldn't trigger reactivity)
			const oldState = state;
			state.count++;
			expect(state).toBe(oldState); // Same reference

			// Using snapshot (would trigger reactivity)
			state = snapshot(state);
			expect(state).not.toBe(oldState); // New reference
			expect(state.count).toBe(1); // Value preserved
		});

		it('should work with incremental updates', () => {
			let state = { user: { name: 'Alice' }, messages: [] };

			// Add message
			state.messages.push('Hello');
			state = snapshot(state);
			expect(state.messages).toEqual(['Hello']);

			// Update user
			state.user.name = 'Bob';
			state = snapshot(state);
			expect(state.user.name).toBe('Bob');
		});
	});
});
