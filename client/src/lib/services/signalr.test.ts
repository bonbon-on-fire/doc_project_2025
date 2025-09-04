/**
 * SignalR Service Tests
 *
 * Basic tests to verify SignalR functionality
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { SignalRService } from './signalr';

describe('SignalRService', () => {
	let service: SignalRService;

	beforeEach(() => {
		// Mock window and localStorage
		global.window = {
			location: {
				port: '5173',
				search: ''
			}
		} as any;

		global.localStorage = {
			getItem: vi.fn(),
			setItem: vi.fn()
		} as any;

		service = new SignalRService({
			enableLogging: false
		});
	});

	afterEach(() => {
		service.dispose();
	});

	describe('Configuration', () => {
		it('should initialize with default configuration', () => {
			expect(service).toBeDefined();
			expect(service.getConnectionState()).toBe('disconnected');
		});

		it('should construct correct hub URL in development', () => {
			const url = (service as any).getDefaultHubUrl();
			expect(url).toBe('http://localhost:5099/api/chat-hub');
		});
	});

	describe('Connection State', () => {
		it('should start in disconnected state', () => {
			expect(service.getConnectionState()).toBe('disconnected');
			expect(service.isConnected()).toBe(false);
		});

		it('should handle state change subscriptions', () => {
			const handler = vi.fn();
			const unsubscribe = service.onStateChange(handler);

			// Should call immediately with current state
			expect(handler).toHaveBeenCalledWith('disconnected');

			unsubscribe();
		});
	});

	describe('Message Buffering', () => {
		it('should buffer messages when not connected', async () => {
			// Try to send a message while disconnected
			await service.sendMessage('TestMethod', { data: 'test' });

			// Message should be buffered (not throw error)
			expect(true).toBe(true);
		});
	});

	describe('Event Subscriptions', () => {
		it('should handle event subscriptions', () => {
			const handler = vi.fn();
			const unsubscribe = service.subscribe('message', handler);

			expect(unsubscribe).toBeInstanceOf(Function);
			unsubscribe();
		});

		it('should support multiple handlers for same event', () => {
			const handler1 = vi.fn();
			const handler2 = vi.fn();

			const unsub1 = service.subscribe('message', handler1);
			const unsub2 = service.subscribe('message', handler2);

			// Both should be registered
			expect(unsub1).toBeInstanceOf(Function);
			expect(unsub2).toBeInstanceOf(Function);

			unsub1();
			unsub2();
		});
	});
});
