import { describe, test, expect, beforeEach } from 'vitest';
import { get } from 'svelte/store';
import {
	messageStates,
	clearMessageStates,
	initializeMessageState,
	setLatestMessage
} from '$lib/stores/messageState';
import { registerBuiltInRenderers, getRenderer, getRendererComponent } from '$lib/renderers';
import type { RichMessageDto, TextMessageDto } from '$lib/types';

// Test scenarios for E2E integration
describe('RMR-P1-007: End-to-End Integration Tests', () => {
	beforeEach(() => {
		clearMessageStates();
		// Register built-in renderers for testing
		registerBuiltInRenderers();
	});

	describe('Complete Message Rendering Pipeline', () => {
		test('should handle complete text message workflow', async () => {
			// 1. Create a text message
			const textMessage: RichMessageDto & TextMessageDto = {
				id: 'text-msg-1',
				chatId: 'test-chat',
				role: 'assistant',
				text: 'Hello, this is a test message with **bold** text.',
				timestamp: new Date(),
				sequenceNumber: 1,
				messageType: 'text'
			};

			// 2. Initialize message state
			initializeMessageState(textMessage.id, true);

			// 3. Verify renderer resolution
			const renderer = getRenderer('text');
			expect(renderer).toBeDefined();
			expect(renderer.messageType).toBe('text');

			// 4. Verify component loading
			const RendererComponent = await getRendererComponent('text');
			expect(RendererComponent).toBeDefined();

			// 5. Verify state management
			const messageState = get(messageStates)[textMessage.id];
			expect(messageState).toBeDefined();
			expect(messageState.expanded).toBe(true);
			expect(messageState.renderPhase).toBe('initial');
		});

		test('should handle multiple message scenarios', async () => {
			// Create multiple messages
			const messages: Array<RichMessageDto & TextMessageDto> = [
				{
					id: 'msg-1',
					chatId: 'test-chat',
					role: 'user',
					text: 'First message',
					timestamp: new Date(Date.now() - 2000),
					sequenceNumber: 1,
					messageType: 'text'
				},
				{
					id: 'msg-2',
					chatId: 'test-chat',
					role: 'assistant',
					text: 'Second message',
					timestamp: new Date(Date.now() - 1000),
					sequenceNumber: 2,
					messageType: 'text'
				},
				{
					id: 'msg-3',
					chatId: 'test-chat',
					role: 'assistant',
					text: 'Latest message',
					timestamp: new Date(),
					sequenceNumber: 3,
					messageType: 'text'
				}
			];

			// Initialize all messages
			messages.forEach((msg) => initializeMessageState(msg.id, false));

			// Set the latest message
			setLatestMessage('msg-3');

			// Verify auto-collapse behavior
			const states = get(messageStates);
			expect(states['msg-1'].expanded).toBe(false); // Auto-collapsed
			expect(states['msg-2'].expanded).toBe(false); // Auto-collapsed
			expect(states['msg-3'].expanded).toBe(true); // Latest is expanded
		});
	});

	describe('Error Handling and Edge Cases', () => {
		test('should handle unknown message types gracefully', async () => {
			// Test with unknown message type string directly
			const unknownType = 'unknown-type' as 'text' | 'reasoning'; // Test fallback for unexpected server response

			// Should fall back to default renderer
			const renderer = getRenderer(unknownType);
			expect(renderer).toBeDefined();
			expect(renderer.messageType).toBe('fallback');

			// Component should return null for unknown types
			const RendererComponent = await getRendererComponent(unknownType);
			expect(RendererComponent).toBeNull();
		});

		test('should handle missing message types', async () => {
			// Test with empty string message type
			const emptyType = '' as 'text' | 'reasoning';

			// Should use fallback renderer as default
			const renderer = getRenderer(emptyType);
			expect(renderer).toBeDefined();
			expect(renderer.messageType).toBe('fallback');
		});
	});

	describe('Performance Validation', () => {
		test('should maintain performance with multiple messages', () => {
			const startTime = performance.now();

			// Create 50 messages (meeting acceptance criteria requirement)
			for (let i = 1; i <= 50; i++) {
				initializeMessageState(`perf-msg-${i}`, i === 50);
			}

			const endTime = performance.now();
			const duration = endTime - startTime;

			// Should complete within reasonable time (< 100ms for initialization)
			expect(duration).toBeLessThan(100);

			// Verify all states are properly managed
			const states = get(messageStates);
			expect(Object.keys(states)).toHaveLength(50);

			// Verify only latest is expanded
			const expandedCount = Object.values(states).filter((s) => s.expanded).length;
			expect(expandedCount).toBe(1);
		});
	});

	describe('User Interaction Flows', () => {
		test('should handle expand/collapse interactions', () => {
			// Create messages
			initializeMessageState('interact-msg-1', false);
			initializeMessageState('interact-msg-2', true);

			const initialStates = get(messageStates);
			expect(initialStates['interact-msg-1'].expanded).toBe(false);
			expect(initialStates['interact-msg-2'].expanded).toBe(true);

			// Simulate setting new latest message
			setLatestMessage('interact-msg-1');

			const updatedStates = get(messageStates);
			expect(updatedStates['interact-msg-1'].expanded).toBe(true); // Now expanded
			expect(updatedStates['interact-msg-2'].expanded).toBe(false); // Auto-collapsed
		});
	});

	describe('Acceptance Criteria Validation', () => {
		test('AC1: System Integration - Routes text messages correctly', async () => {
			const renderer = getRenderer('text');
			expect(renderer.messageType).toBe('text');

			const component = await getRendererComponent('text');
			expect(component).toBeDefined();
		});

		test('AC1: System Integration - Handles errors gracefully', async () => {
			const fallbackRenderer = getRenderer('invalid-type');
			expect(fallbackRenderer.messageType).toBe('fallback');

			const fallbackComponent = await getRendererComponent('invalid-type');
			expect(fallbackComponent).toBeNull(); // Graceful fallback
		});

		test('AC3: Multiple Messages - Auto-collapses and preserves preferences', () => {
			// Set up multiple messages
			initializeMessageState('msg-a', true);
			initializeMessageState('msg-b', false);
			initializeMessageState('msg-c', false);

			// Verify initial state
			let states = get(messageStates);
			expect(states['msg-a'].expanded).toBe(true);

			// Change latest message
			setLatestMessage('msg-b');

			// Verify auto-collapse behavior
			states = get(messageStates);
			expect(states['msg-a'].expanded).toBe(false); // Auto-collapsed
			expect(states['msg-b'].expanded).toBe(true); // New latest expanded
			expect(states['msg-c'].expanded).toBe(false); // Remained collapsed
		});
	});
});
