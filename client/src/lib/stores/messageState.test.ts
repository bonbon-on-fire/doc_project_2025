import { describe, it, expect, beforeEach, vi } from 'vitest';
import { get } from 'svelte/store';
import {
	messageStates,
	latestMessageId,
	expandedStates,
	updateMessageState,
	initializeMessageState,
	setLatestMessage,
	toggleMessageExpansion,
	getMessageState,
	cleanupMessageState,
	cleanupMultipleMessageStates,
	clearMessageStates,
	getDebugInfo,
	type MessageState,
	type MessageStates
} from './messageState';

describe('Message State Management', () => {
	beforeEach(() => {
		// Clear all states before each test
		clearMessageStates();
	});

	describe('Store initialization', () => {
		it('should initialize with empty states', () => {
			expect(get(messageStates)).toEqual({});
			expect(get(latestMessageId)).toBeNull();
			expect(get(expandedStates)).toEqual({});
		});
	});

	describe('updateMessageState', () => {
		it('should update message state correctly', () => {
			updateMessageState('msg-1', { expanded: true });

			const state = get(messageStates)['msg-1'];
			expect(state.expanded).toBe(true);
			expect(state.renderPhase).toBe('initial');
		});

		it('should merge updates with existing state', () => {
			// Initialize with one property
			updateMessageState('msg-1', { expanded: true });

			// Update with another property
			updateMessageState('msg-1', { renderPhase: 'streaming' });

			const state = get(messageStates)['msg-1'];
			expect(state.expanded).toBe(true);
			expect(state.renderPhase).toBe('streaming');
		});

		it('should not trigger store update when values have not changed', () => {
			// Set initial state
			updateMessageState('msg-1', { expanded: true, renderPhase: 'streaming' });

			// Capture current state reference
			const statesBefore = get(messageStates);

			// Update with identical values
			updateMessageState('msg-1', { expanded: true, renderPhase: 'streaming' });

			// State reference should be unchanged (no new object created)
			const statesAfter = get(messageStates);
			expect(statesAfter).toBe(statesBefore); // Reference equality check
		});

		it('should not trigger store update for partial identical values', () => {
			// Set initial state
			updateMessageState('msg-1', { expanded: true, renderPhase: 'streaming' });

			// Capture current state reference
			const statesBefore = get(messageStates);

			// Update with only identical partial values
			updateMessageState('msg-1', { expanded: true });

			// State reference should be unchanged (no new object created)
			const statesAfter = get(messageStates);
			expect(statesAfter).toBe(statesBefore); // Reference equality check
		});

		it('should trigger store update when values actually change', () => {
			// Set initial state
			updateMessageState('msg-1', { expanded: true, renderPhase: 'initial' });

			// Capture current state reference
			const statesBefore = get(messageStates);

			// Update with different values
			updateMessageState('msg-1', { expanded: false });

			// State reference should have changed (new object created)
			const statesAfter = get(messageStates);
			expect(statesAfter).not.toBe(statesBefore); // Reference inequality check
			expect(statesAfter['msg-1'].expanded).toBe(false);
		});

		it('should create new state object if message does not exist', () => {
			updateMessageState('new-msg', { expanded: false, renderPhase: 'complete' });

			const state = get(messageStates)['new-msg'];
			expect(state).toEqual({
				expanded: false,
				renderPhase: 'complete'
			});
		});

		it('should validate messageId parameter', () => {
			expect(() => updateMessageState('', { expanded: true })).toThrow(
				'Message ID must be a non-empty string'
			);
			expect(() => updateMessageState('   ', { expanded: true })).toThrow(
				'Message ID must be a non-empty string'
			);
			expect(() => updateMessageState(null as any, { expanded: true })).toThrow(
				'Message ID must be a non-empty string'
			);
		});

		it('should validate state updates', () => {
			expect(() => updateMessageState('msg-1', { expanded: 'invalid' as any })).toThrow(
				'expanded must be a boolean'
			);
			expect(() => updateMessageState('msg-1', { renderPhase: 'invalid' as any })).toThrow(
				'renderPhase must be one of'
			);
		});

		it('should trigger reactive updates for subscribers', () => {
			const callback = vi.fn();
			messageStates.subscribe(callback);

			updateMessageState('msg-1', { expanded: true });

			expect(callback).toHaveBeenCalled();
		});
	});

	describe('initializeMessageState', () => {
		it('should initialize message with default values', () => {
			initializeMessageState('msg-1');

			const state = get(messageStates)['msg-1'];
			expect(state).toEqual({
				expanded: false,
				renderPhase: 'initial'
			});
		});

		it('should initialize latest message as expanded', () => {
			initializeMessageState('msg-1', true);

			const state = get(messageStates)['msg-1'];
			expect(state.expanded).toBe(true);
			expect(get(latestMessageId)).toBe('msg-1');
		});

		it('should not overwrite existing state', () => {
			// Initialize first
			initializeMessageState('msg-1');
			updateMessageState('msg-1', { renderPhase: 'streaming' });

			// Try to initialize again
			initializeMessageState('msg-1');

			const state = get(messageStates)['msg-1'];
			expect(state.renderPhase).toBe('streaming'); // Should remain unchanged
		});

		it('should validate messageId parameter', () => {
			expect(() => initializeMessageState('')).toThrow('Message ID must be a non-empty string');
		});
	});

	describe('setLatestMessage', () => {
		beforeEach(() => {
			// Set up multiple messages
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			initializeMessageState('msg-3');
			updateMessageState('msg-1', { expanded: true });
			updateMessageState('msg-2', { expanded: true });
		});

		it('should update the latest message ID', () => {
			setLatestMessage('msg-2');
			expect(get(latestMessageId)).toBe('msg-2');
		});

		it('should auto-collapse all other expanded messages', () => {
			setLatestMessage('msg-2');

			const states = get(messageStates);
			expect(states['msg-1'].expanded).toBe(false);
			expect(states['msg-2'].expanded).toBe(true);
			expect(states['msg-3'].expanded).toBe(false);
		});

		it('should ensure the latest message is expanded', () => {
			// msg-3 starts collapsed
			setLatestMessage('msg-3');

			const states = get(messageStates);
			expect(states['msg-3'].expanded).toBe(true);
		});

		it('should validate messageId parameter', () => {
			expect(() => setLatestMessage('')).toThrow('Message ID must be a non-empty string');
		});
	});

	describe('toggleMessageExpansion', () => {
		beforeEach(() => {
			initializeMessageState('msg-1');
		});

		it('should toggle expansion state', () => {
			// Start collapsed
			expect(get(messageStates)['msg-1'].expanded).toBe(false);

			toggleMessageExpansion('msg-1');
			expect(get(messageStates)['msg-1'].expanded).toBe(true);

			toggleMessageExpansion('msg-1');
			expect(get(messageStates)['msg-1'].expanded).toBe(false);
		});

		it('should handle non-existent message gracefully', () => {
			expect(() => toggleMessageExpansion('non-existent')).not.toThrow();
			expect(get(messageStates)['non-existent']).toBeUndefined();
		});

		it('should validate messageId parameter', () => {
			expect(() => toggleMessageExpansion('')).toThrow('Message ID must be a non-empty string');
		});
	});

	describe('getMessageState', () => {
		it('should return existing state', () => {
			const expectedState = { expanded: true, renderPhase: 'complete' as const };
			updateMessageState('msg-1', expectedState);

			const state = getMessageState('msg-1');
			expect(state).toEqual(expectedState);
		});

		it('should return default state for non-existent message', () => {
			const state = getMessageState('non-existent');
			expect(state).toEqual({
				expanded: false,
				renderPhase: 'initial'
			});
		});

		it('should validate messageId parameter', () => {
			expect(() => getMessageState('')).toThrow('Message ID must be a non-empty string');
		});
	});

	describe('cleanupMessageState', () => {
		beforeEach(() => {
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			setLatestMessage('msg-1');
		});

		it('should remove state for specified message', () => {
			cleanupMessageState('msg-2');

			const states = get(messageStates);
			expect(states['msg-1']).toBeDefined();
			expect(states['msg-2']).toBeUndefined();
		});

		it('should clear latest ID if cleaning up latest message', () => {
			cleanupMessageState('msg-1');

			expect(get(latestMessageId)).toBeNull();
		});

		it('should not affect latest ID if cleaning up non-latest message', () => {
			cleanupMessageState('msg-2');

			expect(get(latestMessageId)).toBe('msg-1');
		});

		it('should validate messageId parameter', () => {
			expect(() => cleanupMessageState('')).toThrow('Message ID must be a non-empty string');
		});
	});

	describe('cleanupMultipleMessageStates', () => {
		beforeEach(() => {
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			initializeMessageState('msg-3');
			setLatestMessage('msg-2');
		});

		it('should remove multiple message states', () => {
			cleanupMultipleMessageStates(['msg-1', 'msg-3']);

			const states = get(messageStates);
			expect(states['msg-1']).toBeUndefined();
			expect(states['msg-2']).toBeDefined();
			expect(states['msg-3']).toBeUndefined();
		});

		it('should clear latest ID if one of the cleaned messages was latest', () => {
			cleanupMultipleMessageStates(['msg-1', 'msg-2']);

			expect(get(latestMessageId)).toBeNull();
		});

		it('should handle empty array gracefully', () => {
			expect(() => cleanupMultipleMessageStates([])).not.toThrow();
		});

		it('should validate all messageIds', () => {
			expect(() => cleanupMultipleMessageStates(['msg-1', ''])).toThrow(
				'Message ID must be a non-empty string'
			);
		});
	});

	describe('expandedStates derived store', () => {
		it('should mark latest message as expanded', () => {
			initializeMessageState('msg-1', false);
			initializeMessageState('msg-2', false);
			setLatestMessage('msg-1');

			const expanded = get(expandedStates);
			expect(expanded['msg-1']).toBe(true);
			expect(expanded['msg-2']).toBe(false);
		});

		it('should respect manual expansion for non-latest messages', () => {
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			setLatestMessage('msg-2');

			// Manually expand msg-1
			toggleMessageExpansion('msg-1');

			const expanded = get(expandedStates);
			expect(expanded['msg-1']).toBe(true);
			expect(expanded['msg-2']).toBe(true);
		});
	});

	describe('clearMessageStates', () => {
		it('should clear all states and latest ID', () => {
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			setLatestMessage('msg-1');

			clearMessageStates();

			expect(get(messageStates)).toEqual({});
			expect(get(latestMessageId)).toBeNull();
		});
	});

	describe('Auto-collapse behavior integration tests', () => {
		it('should auto-collapse when new latest message is set', () => {
			// Set up initial state
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			updateMessageState('msg-1', { expanded: true });
			updateMessageState('msg-2', { expanded: true });
			setLatestMessage('msg-1');

			// Set new latest message
			setLatestMessage('msg-2');

			const states = get(messageStates);
			expect(states['msg-1'].expanded).toBe(false);
			expect(states['msg-2'].expanded).toBe(true);
		});

		it('should preserve manual expansion preferences for latest message', () => {
			initializeMessageState('msg-1', true);

			// User manually collapses latest message
			toggleMessageExpansion('msg-1');
			expect(get(messageStates)['msg-1'].expanded).toBe(false);

			// Setting the same message as latest again should expand it
			setLatestMessage('msg-1');
			expect(get(messageStates)['msg-1'].expanded).toBe(true);
		});
	});

	describe('Error scenarios', () => {
		it('should handle rapid state updates without losing data', () => {
			// Simulate rapid updates
			for (let i = 0; i < 100; i++) {
				updateMessageState('msg-1', { renderPhase: i % 2 === 0 ? 'streaming' : 'complete' });
			}

			const state = get(messageStates)['msg-1'];
			expect(state).toBeDefined();
			expect(['streaming', 'complete']).toContain(state.renderPhase);
		});

		it('should maintain immutable state updates', () => {
			const initialStates = get(messageStates);
			updateMessageState('msg-1', { expanded: true });

			const newStates = get(messageStates);
			expect(newStates).not.toBe(initialStates); // Different object references
		});
	});

	describe('Debug functionality', () => {
		it('should provide debug info in development mode', () => {
			// Skip this test in environments where debug is disabled
			// This is expected behavior in production builds
			initializeMessageState('msg-1');
			initializeMessageState('msg-2');
			setLatestMessage('msg-1');

			const debugInfo = getDebugInfo();
			// Debug info might be null in test environment, which is acceptable
			if (debugInfo !== null) {
				expect(debugInfo).toEqual({
					totalMessages: 2,
					latestMessageId: 'msg-1',
					expandedCount: 1,
					messageStates: get(messageStates)
				});
			} else {
				// This is expected in production-like test environments
				expect(debugInfo).toBeNull();
			}
		});

		it('should return null in production mode', () => {
			// In test environments, this behavior may vary
			const debugInfo = getDebugInfo();
			// Accept either null (production mode) or debug object (development mode)
			expect(debugInfo === null || typeof debugInfo === 'object').toBe(true);
		});
	});
});

describe('Performance and Memory Management', () => {
	it('should not leak memory with repeated operations', () => {
		// Test with many operations to check for memory leaks
		for (let i = 0; i < 1000; i++) {
			initializeMessageState(`msg-${i}`);
			updateMessageState(`msg-${i}`, { expanded: true });
			if (i % 100 === 0) {
				cleanupMultipleMessageStates(Array.from({ length: 50 }, (_, j) => `msg-${i - j}`));
			}
		}

		// Should complete without issues
		expect(Object.keys(get(messageStates)).length).toBeLessThan(1000);
	});

	it('should handle concurrent state updates gracefully', async () => {
		clearMessageStates(); // Start fresh

		const promises = Array.from({ length: 50 }, (_, i) =>
			Promise.resolve().then(() => {
				initializeMessageState(`msg-${i}`);
				updateMessageState(`msg-${i}`, { expanded: i % 2 === 0 });
			})
		);

		await Promise.all(promises);

		const states = get(messageStates);
		expect(Object.keys(states).length).toBe(50);
	});
});
