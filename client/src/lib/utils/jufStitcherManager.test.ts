import { describe, it, expect, beforeEach, vi } from 'vitest';
import { JufStitcherManager } from './jufStitcherManager';
import type { JsonFragmentUpdate } from '../chat/sseEventTypes';

describe('JufStitcherManager', () => {
	let manager: JufStitcherManager;

	beforeEach(() => {
		manager = new JufStitcherManager();
	});

	describe('get()', () => {
		it('should create a new rebuilder for unknown document ID', () => {
			const rebuilder = manager.get('doc1');
			expect(rebuilder).toBeDefined();
			expect(rebuilder.getValue()).toBeUndefined();
			expect(rebuilder.isComplete()).toBe(false);
		});

		it('should return the same rebuilder for the same document ID', () => {
			const rebuilder1 = manager.get('doc1');
			const rebuilder2 = manager.get('doc1');
			expect(rebuilder1).toBe(rebuilder2);
		});

		it('should throw error when trying to get a completed document', () => {
			manager.apply('doc1', [{ path: 'root', kind: 'JsonComplete' }]);
			expect(() => manager.get('doc1')).toThrow('Document doc1 is already complete');
		});
	});

	describe('apply()', () => {
		it('should apply updates and return current value', () => {
			const updates: JsonFragmentUpdate[] = [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.name', kind: 'CompleteString', textValue: '"Alice"' },
				{ path: 'root.age', kind: 'CompleteNumber', textValue: '30' },
				{ path: 'root', kind: 'EndObject' }
			];

			const value = manager.apply('doc1', updates);
			expect(value).toEqual({ name: 'Alice', age: 30 });
		});

		it('should mark document as complete on JsonComplete', () => {
			const updates: JsonFragmentUpdate[] = [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.done', kind: 'CompleteBoolean', textValue: 'true' },
				{ path: 'root', kind: 'JsonComplete' }
			];

			manager.apply('doc1', updates);
			expect(() => manager.get('doc1')).toThrow('Document doc1 is already complete');
		});

		it('should silently ignore updates for completed documents', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.value', kind: 'CompleteNumber', textValue: '1' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			const value = manager.apply('doc1', [
				{ path: 'root.value', kind: 'CompleteNumber', textValue: '2' }
			]);

			expect(value).toEqual({ value: 1 }); // Should return original value
		});
	});

	describe('complete()', () => {
		it('should mark document as complete and lock rebuilder', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.status', kind: 'CompleteString', textValue: '"active"' }
			]);

			manager.complete('doc1');

			// Should not be able to get the document
			expect(() => manager.get('doc1')).toThrow('Document doc1 is already complete');

			// Should silently ignore further updates
			const value = manager.apply('doc1', [
				{ path: 'root.status', kind: 'CompleteString', textValue: '"inactive"' }
			]);
			// Value should remain unchanged
			expect(value).toEqual({ status: 'active' });
		});

		it('should handle completing non-existent document gracefully', () => {
			// Should not throw
			expect(() => manager.complete('non-existent')).not.toThrow();
		});
	});

	describe('delete()', () => {
		it('should remove a rebuilder and its completion state', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			manager.delete('doc1');

			// Should be able to create new rebuilder after deletion
			const rebuilder = manager.get('doc1');
			expect(rebuilder).toBeDefined();
			expect(rebuilder.getValue()).toBeUndefined();
		});

		it('should handle deleting non-existent document gracefully', () => {
			expect(() => manager.delete('non-existent')).not.toThrow();
		});
	});

	describe('clear()', () => {
		it('should remove all rebuilders and completion states', () => {
			manager.apply('doc1', [{ path: 'root', kind: 'StartObject' }]);
			manager.apply('doc2', [{ path: 'root', kind: 'StartArray' }]);
			manager.apply('doc3', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			manager.clear();

			// All documents should be cleared
			const rebuilder1 = manager.get('doc1');
			const rebuilder2 = manager.get('doc2');
			const rebuilder3 = manager.get('doc3');

			expect(rebuilder1.getValue()).toBeUndefined();
			expect(rebuilder2.getValue()).toBeUndefined();
			expect(rebuilder3.getValue()).toBeUndefined();
		});
	});

	describe('multi-document isolation', () => {
		it('should maintain isolation between concurrent documents', () => {
			const updates1: JsonFragmentUpdate[] = [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.doc', kind: 'CompleteString', textValue: '"first"' }
			];

			const updates2: JsonFragmentUpdate[] = [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.doc', kind: 'CompleteString', textValue: '"second"' }
			];

			const value1 = manager.apply('doc1', updates1);
			const value2 = manager.apply('doc2', updates2);

			expect(value1).toEqual({ doc: 'first' });
			expect(value2).toEqual({ doc: 'second' });
		});

		it('should handle interleaved updates to multiple documents', () => {
			// Start doc1
			manager.apply('doc1', [{ path: 'root', kind: 'StartObject' }]);

			// Start doc2 - test with object containing array instead
			manager.apply('doc2', [{ path: 'root', kind: 'StartObject' }]);
			manager.apply('doc2', [{ path: 'root.items', kind: 'StartArray' }]);

			// Update doc1
			manager.apply('doc1', [{ path: 'root.name', kind: 'CompleteString', textValue: '"Alice"' }]);

			// Update doc2 array
			manager.apply('doc2', [
				{ path: 'root.items[0]', kind: 'CompleteNumber', textValue: '1' },
				{ path: 'root.items[1]', kind: 'CompleteNumber', textValue: '2' }
			]);

			// Complete doc1
			manager.apply('doc1', [{ path: 'root', kind: 'JsonComplete' }]);

			// Use getValue() instead of get() for completed document
			const value1 = manager.getValue('doc1');
			const value2 = manager.getValue('doc2');

			expect(value1).toEqual({ name: 'Alice' });
			expect(value2).toEqual({ items: [1, 2] });
		});
	});

	describe('edge cases', () => {
		it('should handle empty document ID', () => {
			const rebuilder = manager.get('');
			expect(rebuilder).toBeDefined();
		});

		it('should handle very long document IDs', () => {
			const longId = 'a'.repeat(1000);
			const rebuilder = manager.get(longId);
			expect(rebuilder).toBeDefined();
		});

		it('should handle special characters in document ID', () => {
			const specialId = 'doc:123:tool_call:456';
			manager.apply(specialId, [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.valid', kind: 'CompleteBoolean', textValue: 'true' }
			]);

			const value = manager.apply(specialId, []);
			expect(value).toEqual({ valid: true });
		});
	});

	describe('getValue()', () => {
		it('should return undefined for non-existent document', () => {
			const value = manager.getValue('non-existent');
			expect(value).toBeUndefined();
		});

		it('should return current value for existing document', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.test', kind: 'CompleteString', textValue: '"value"' }
			]);

			const value = manager.getValue('doc1');
			expect(value).toEqual({ test: 'value' });
		});

		it('should return value for completed document', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.test', kind: 'CompleteString', textValue: '"value"' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			const value = manager.getValue('doc1');
			expect(value).toEqual({ test: 'value' });
		});
	});

	describe('has()', () => {
		it('should return false for non-existent document', () => {
			expect(manager.has('doc1')).toBe(false);
		});

		it('should return true for existing document', () => {
			manager.get('doc1');
			expect(manager.has('doc1')).toBe(true);
		});

		it('should return true for completed document', () => {
			manager.apply('doc1', [{ path: 'root', kind: 'JsonComplete' }]);
			expect(manager.has('doc1')).toBe(true);
		});
	});

	describe('isComplete()', () => {
		it('should return false for non-existent document', () => {
			expect(manager.isComplete('doc1')).toBe(false);
		});

		it('should return false for incomplete document', () => {
			manager.apply('doc1', [{ path: 'root', kind: 'StartObject' }]);
			expect(manager.isComplete('doc1')).toBe(false);
		});

		it('should return true for document with JsonComplete', () => {
			manager.apply('doc1', [{ path: 'root', kind: 'JsonComplete' }]);
			expect(manager.isComplete('doc1')).toBe(true);
		});

		it('should return true for manually completed document', () => {
			manager.get('doc1');
			manager.complete('doc1');
			expect(manager.isComplete('doc1')).toBe(true);
		});
	});

	describe('tryGet()', () => {
		it('should return undefined for non-existent document', () => {
			const rebuilder = manager.tryGet('doc1');
			expect(rebuilder).toBeUndefined();
		});

		it('should return existing rebuilder', () => {
			const original = manager.get('doc1');
			const retrieved = manager.tryGet('doc1');
			expect(retrieved).toBe(original);
		});

		it('should return rebuilder even for completed document', () => {
			manager.apply('doc1', [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			const rebuilder = manager.tryGet('doc1');
			expect(rebuilder).toBeDefined();
			expect(rebuilder?.getValue()).toEqual({});
		});
	});

	describe('ensure()', () => {
		it('should work as alias for get()', () => {
			const rebuilder1 = manager.ensure('doc1');
			const rebuilder2 = manager.get('doc1');
			expect(rebuilder1).toBe(rebuilder2);
		});

		it('should throw for completed documents like get()', () => {
			manager.complete('doc1');
			expect(() => manager.ensure('doc1')).toThrow('Document doc1 is already complete');
		});
	});

	describe('finalizeMessage()', () => {
		it('should clean up all documents for a message with specific tool call IDs', () => {
			const messageId = 'msg123';
			const toolCallIds = ['tool1', 'tool2', 'tool3'];

			// Create documents for the tool calls
			toolCallIds.forEach((toolCallId) => {
				const docId = `${messageId}:${toolCallId}`;
				manager.apply(docId, [
					{ path: 'root', kind: 'StartObject' },
					{ path: 'root.data', kind: 'CompleteString', textValue: '"test"' }
				]);
			});

			// Verify documents exist
			toolCallIds.forEach((toolCallId) => {
				const docId = `${messageId}:${toolCallId}`;
				expect(manager.has(docId)).toBe(true);
			});

			// Finalize the message
			manager.finalizeMessage(messageId, toolCallIds);

			// Verify documents are cleaned up
			toolCallIds.forEach((toolCallId) => {
				const docId = `${messageId}:${toolCallId}`;
				expect(manager.has(docId)).toBe(false);
			});
		});

		it('should clean up all documents with messageId prefix when no tool call IDs provided', () => {
			const messageId = 'msg456';

			// Create multiple documents with the message prefix
			manager.apply(`${messageId}:tool1`, [{ path: 'root', kind: 'StartObject' }]);
			manager.apply(`${messageId}:tool2`, [{ path: 'root', kind: 'StartArray' }]);
			manager.apply(`${messageId}:tool3:nested`, [{ path: 'root', kind: 'CompleteNull' }]);

			// Create a document that shouldn't be cleaned up
			manager.apply('other:doc', [{ path: 'root', kind: 'StartObject' }]);

			// Finalize without specifying tool call IDs
			manager.finalizeMessage(messageId);

			// Verify message documents are cleaned up
			expect(manager.has(`${messageId}:tool1`)).toBe(false);
			expect(manager.has(`${messageId}:tool2`)).toBe(false);
			expect(manager.has(`${messageId}:tool3:nested`)).toBe(false);

			// Verify unrelated document is not cleaned up
			expect(manager.has('other:doc')).toBe(true);
		});

		it('should handle finalizing message with no existing documents', () => {
			// Should not throw when finalizing non-existent message
			expect(() => manager.finalizeMessage('nonexistent', ['tool1', 'tool2'])).not.toThrow();
		});
	});

	describe('fragments after completion', () => {
		it('should ignore fragments arriving after tool result completion', () => {
			const docId = 'msg1:tool1';

			// Apply initial fragments
			manager.apply(docId, [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.status', kind: 'CompleteString', textValue: '"processing"' }
			]);

			// Complete the document (simulating tool result received)
			manager.complete(docId);

			// Try to apply more fragments
			const value = manager.apply(docId, [
				{ path: 'root.status', kind: 'CompleteString', textValue: '"completed"' },
				{ path: 'root.extra', kind: 'CompleteNumber', textValue: '42' }
			]);

			// Value should remain unchanged
			expect(value).toEqual({ status: 'processing' });
		});

		it('should ignore fragments after JsonComplete', () => {
			const docId = 'msg2:tool2';

			// Apply fragments including JsonComplete
			manager.apply(docId, [
				{ path: 'root', kind: 'StartObject' },
				{ path: 'root.done', kind: 'CompleteBoolean', textValue: 'true' },
				{ path: 'root', kind: 'JsonComplete' }
			]);

			// Try to apply more fragments
			const value = manager.apply(docId, [
				{ path: 'root.shouldIgnore', kind: 'CompleteString', textValue: '"ignored"' }
			]);

			// Value should not include the ignored update
			expect(value).toEqual({ done: true });
			expect(value).not.toHaveProperty('shouldIgnore');
		});
	});
});
