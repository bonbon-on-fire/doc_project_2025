import { describe, it, expect, vi } from 'vitest';
import { JsonFragmentRebuilder } from './jsonFragmentRebuilder';
import type { JsonFragmentUpdate } from '../chat/sseEventTypes';

describe('JsonFragmentRebuilder', () => {
	it('reconstructs flat object with primitives', () => {
		const r = new JsonFragmentRebuilder();
		const updates: JsonFragmentUpdate[] = [
			{ path: 'root', kind: 'StartObject' },
			{ path: 'root.name', kind: 'CompleteString', textValue: '"John"' },
			{ path: 'root.age', kind: 'CompleteNumber', textValue: '25' },
			{ path: 'root.active', kind: 'CompleteBoolean', textValue: 'true' },
			{ path: 'root.extra', kind: 'CompleteNull', textValue: 'null' },
			{ path: 'root', kind: 'EndObject' },
			{ path: 'root', kind: 'JsonComplete' }
		];
		r.apply(updates);
		expect(r.isComplete()).toBe(true);
		expect(r.getValue()).toEqual({ name: 'John', age: 25, active: true, extra: null });
	});

	it('handles nested objects and arrays', () => {
		const r = new JsonFragmentRebuilder();
		const updates: JsonFragmentUpdate[] = [
			{ path: 'root', kind: 'StartObject' },
			{ path: 'root.items', kind: 'StartArray' },
			{ path: 'root.items[0]', kind: 'StartObject' },
			{ path: 'root.items[0].qty', kind: 'CompleteNumber', textValue: '2' },
			{ path: 'root.items[0]', kind: 'EndObject' },
			{ path: 'root.items[1]', kind: 'StartObject' },
			{ path: 'root.items[1].name', kind: 'CompleteString', textValue: '"apple"' },
			{ path: 'root.items[1]', kind: 'EndObject' },
			{ path: 'root.items', kind: 'EndArray' },
			{ path: 'root', kind: 'EndObject' }
		];
		r.apply(updates);
		expect(r.getValue()).toEqual({ items: [{ qty: 2 }, { name: 'apple' }] });
	});

	it('accumulates PartialString and finalizes with CompleteString', () => {
		const r = new JsonFragmentRebuilder();
		r.apply([{ path: 'root', kind: 'StartObject' }]);
		r.apply([{ path: 'root.msg', kind: 'PartialString', textValue: 'He' }]);
		expect(r.getValue()).toEqual({ msg: 'He' });
		r.apply([{ path: 'root.msg', kind: 'PartialString', textValue: 'llo' }]);
		expect(r.getValue()).toEqual({ msg: 'Hello' });
		r.apply([{ path: 'root.msg', kind: 'CompleteString', textValue: '"Hello"' }]);
		expect(r.getValue()).toEqual({ msg: 'Hello' });
	});

	it('creates containers on demand based on path', () => {
		const r = new JsonFragmentRebuilder();
		r.apply([
			{ path: 'root.user.name', kind: 'CompleteString', textValue: '"Ada"' },
			{ path: 'root.user.age', kind: 'CompleteNumber', textValue: '30' }
		]);
		expect(r.getValue()).toEqual({ user: { name: 'Ada', age: 30 } });
	});

	describe('locking mechanism', () => {
		it('should prevent updates after lockForResult() is called', () => {
			const r = new JsonFragmentRebuilder();

			r.apply([
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.value', kind: 'CompleteNumber', textValue: '1' }
			]);

			// Lock the rebuilder
			r.lockForResult();
			expect(r.isComplete()).toBe(true);

			// Try to apply more updates - should be silently ignored
			r.apply([{ path: 'root.value', kind: 'CompleteNumber', textValue: '2' }]);

			// Value should not change
			expect(r.getValue()).toEqual({ value: 1 });
		});

		it('should prevent updates after JsonComplete', () => {
			const r = new JsonFragmentRebuilder();

			r.apply([
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.value', kind: 'CompleteNumber', textValue: '1' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			expect(r.isComplete()).toBe(true);

			// Try to apply more updates - should be silently ignored
			r.apply([{ path: 'root.value', kind: 'CompleteNumber', textValue: '2' }]);

			// Value should not change
			expect(r.getValue()).toEqual({ value: 1 });
		});

		it('should set both complete and locked flags on JsonComplete', () => {
			const r = new JsonFragmentRebuilder();

			r.apply([{ path: 'root', kind: 'JsonComplete' }]);

			expect(r.isComplete()).toBe(true);

			// After locking separately, should still be complete
			r.lockForResult();
			expect(r.isComplete()).toBe(true);
		});
	});
});
