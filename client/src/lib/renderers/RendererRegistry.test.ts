import { describe, test, expect, vi, beforeEach } from 'vitest';
import {
	RendererRegistry,
	rendererRegistry,
	getRenderer,
	registerRenderer
} from './RendererRegistry.js';
import type { MessageRenderer, MessageDto } from '../types/index.js';

describe('RendererRegistry', () => {
	let registry: RendererRegistry;

	beforeEach(() => {
		registry = new RendererRegistry();
	});

	describe('Constructor', () => {
		test('should initialize with fallback renderer', () => {
			const fallback = registry.getFallbackRenderer();

			expect(fallback).toBeDefined();
			expect(fallback.messageType).toBe('fallback');
		});

		test('should start with empty registry', () => {
			expect(registry.getRendererCount()).toBe(0);
			expect(registry.listRenderers()).toEqual([]);
		});
	});

	describe('register()', () => {
		test('should register renderer correctly', () => {
			const mockRenderer: MessageRenderer<MessageDto> = {
				messageType: 'text'
			};

			registry.register('text', mockRenderer);
			const retrieved = registry.getRenderer('text');

			expect(retrieved).toBe(mockRenderer);
			expect(registry.hasRenderer('text')).toBe(true);
			expect(registry.getRendererCount()).toBe(1);
		});

		test('should register renderer with onExpand method', () => {
			const onExpandSpy = vi.fn();
			const rendererWithExpand: MessageRenderer<MessageDto> = {
				messageType: 'reasoning',
				onExpand: onExpandSpy
			};

			registry.register('reasoning', rendererWithExpand);
			const retrieved = registry.getRenderer('reasoning');

			expect(retrieved).toBe(rendererWithExpand);
			expect(retrieved.onExpand).toBeDefined();

			// Test that the method can be called
			retrieved.onExpand?.();
			expect(onExpandSpy).toHaveBeenCalledOnce();
		});

		test('should throw error for empty message type', () => {
			const mockRenderer: MessageRenderer<MessageDto> = {
				messageType: 'text'
			};

			expect(() => registry.register('', mockRenderer)).toThrow('Message type cannot be empty');
			expect(() => registry.register('   ', mockRenderer)).toThrow('Message type cannot be empty');
		});

		test('should throw error for invalid renderer', () => {
			expect(() => registry.register('text', null as any)).toThrow(
				'Renderer must be a valid MessageRenderer implementation'
			);
			expect(() => registry.register('text', {} as any)).toThrow(
				'Renderer must be a valid MessageRenderer implementation'
			);
		});

		test('should throw error for duplicate registration without force', () => {
			const mockRenderer1: MessageRenderer<MessageDto> = { messageType: 'text' };
			const mockRenderer2: MessageRenderer<MessageDto> = { messageType: 'text' };

			registry.register('text', mockRenderer1);

			expect(() => registry.register('text', mockRenderer2)).toThrow(
				"Renderer for message type 'text' is already registered. Use force=true to override."
			);
		});

		test('should allow override with force flag', () => {
			const mockRenderer1: MessageRenderer<MessageDto> = { messageType: 'text' };
			const mockRenderer2: MessageRenderer<MessageDto> = { messageType: 'text' };

			registry.register('text', mockRenderer1);
			registry.register('text', mockRenderer2, true);

			const retrieved = registry.getRenderer('text');
			expect(retrieved).toBe(mockRenderer2);
		});

		test('should warn when renderer messageType differs from registration key', () => {
			const consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };

			registry.register('different-type', mockRenderer);

			expect(consoleWarnSpy).toHaveBeenCalledWith(
				"Renderer messageType 'text' does not match registration key 'different-type'"
			);

			consoleWarnSpy.mockRestore();
		});
	});

	describe('getRenderer()', () => {
		test('should return registered renderer for known type', () => {
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };
			registry.register('text', mockRenderer);

			const retrieved = registry.getRenderer('text');
			expect(retrieved).toBe(mockRenderer);
		});

		test('should return fallback renderer for unknown type', () => {
			const consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

			const fallback = registry.getRenderer('unknown');

			expect(fallback).toBe(registry.getFallbackRenderer());
			expect(fallback.messageType).toBe('fallback');
			expect(consoleWarnSpy).toHaveBeenCalledWith(
				"No renderer found for message type 'unknown', using fallback renderer"
			);

			consoleWarnSpy.mockRestore();
		});

		test('should never return null or undefined', () => {
			// Test various edge cases
			expect(registry.getRenderer('unknown')).toBeDefined();
			expect(registry.getRenderer('')).toBeDefined();
			expect(registry.getRenderer('  ')).toBeDefined();
		});
	});

	describe('unregister()', () => {
		test('should remove registered renderer', () => {
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };
			registry.register('text', mockRenderer);

			expect(registry.hasRenderer('text')).toBe(true);

			const result = registry.unregister('text');

			expect(result).toBe(true);
			expect(registry.hasRenderer('text')).toBe(false);
			expect(registry.getRendererCount()).toBe(0);
		});

		test('should return false for non-existent renderer', () => {
			const result = registry.unregister('non-existent');
			expect(result).toBe(false);
		});
	});

	describe('listRenderers()', () => {
		test('should return empty array when no renderers registered', () => {
			expect(registry.listRenderers()).toEqual([]);
		});

		test('should return array of registered message types', () => {
			const textRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };
			const reasoningRenderer: MessageRenderer<MessageDto> = { messageType: 'reasoning' };

			registry.register('text', textRenderer);
			registry.register('reasoning', reasoningRenderer);

			const types = registry.listRenderers();
			expect(types).toHaveLength(2);
			expect(types).toContain('text');
			expect(types).toContain('reasoning');
		});
	});

	describe('hasRenderer()', () => {
		test('should return true for registered renderer', () => {
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };
			registry.register('text', mockRenderer);

			expect(registry.hasRenderer('text')).toBe(true);
		});

		test('should return false for unregistered renderer', () => {
			expect(registry.hasRenderer('unknown')).toBe(false);
		});
	});

	describe('getRendererCount()', () => {
		test('should return correct count', () => {
			expect(registry.getRendererCount()).toBe(0);

			const renderer1: MessageRenderer<MessageDto> = { messageType: 'text' };
			const renderer2: MessageRenderer<MessageDto> = { messageType: 'reasoning' };

			registry.register('text', renderer1);
			expect(registry.getRendererCount()).toBe(1);

			registry.register('reasoning', renderer2);
			expect(registry.getRendererCount()).toBe(2);

			registry.unregister('text');
			expect(registry.getRendererCount()).toBe(1);
		});
	});

	describe('clear()', () => {
		test('should remove all registered renderers', () => {
			const renderer1: MessageRenderer<MessageDto> = { messageType: 'text' };
			const renderer2: MessageRenderer<MessageDto> = { messageType: 'reasoning' };

			registry.register('text', renderer1);
			registry.register('reasoning', renderer2);
			expect(registry.getRendererCount()).toBe(2);

			registry.clear();
			expect(registry.getRendererCount()).toBe(0);
			expect(registry.listRenderers()).toEqual([]);
		});

		test('should preserve fallback renderer after clear', () => {
			const fallbackBefore = registry.getFallbackRenderer();

			registry.clear();

			const fallbackAfter = registry.getFallbackRenderer();
			expect(fallbackAfter).toBe(fallbackBefore);
		});
	});

	describe('setFallbackRenderer()', () => {
		test('should update fallback renderer', () => {
			const customFallback: MessageRenderer<MessageDto> = {
				messageType: 'custom-fallback'
			};

			registry.setFallbackRenderer(customFallback);

			expect(registry.getFallbackRenderer()).toBe(customFallback);
			expect(registry.getRenderer('unknown')).toBe(customFallback);
		});

		test('should throw error for invalid fallback renderer', () => {
			expect(() => registry.setFallbackRenderer(null as any)).toThrow(
				'Fallback renderer must be a valid MessageRenderer implementation'
			);
			expect(() => registry.setFallbackRenderer({} as any)).toThrow(
				'Fallback renderer must be a valid MessageRenderer implementation'
			);
		});
	});

	describe('Generic type support', () => {
		test('should support typed message renderers', () => {
			interface CustomMessageDto extends MessageDto {
				customField: string;
			}

			const customRenderer: MessageRenderer<CustomMessageDto> = {
				messageType: 'custom',
				onExpand: () => {
					// Custom logic here
				}
			};

			registry.register('custom', customRenderer);
			const retrieved = registry.getRenderer('custom');

			expect(retrieved).toBe(customRenderer);
			expect(retrieved.messageType).toBe('custom');
		});
	});
});

describe('Global Registry Functions', () => {
	beforeEach(() => {
		// Clear global registry before each test
		rendererRegistry.clear();
	});

	describe('getRenderer()', () => {
		test('should use global registry instance', () => {
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };
			rendererRegistry.register('text', mockRenderer);

			const retrieved = getRenderer('text');
			expect(retrieved).toBe(mockRenderer);
		});

		test('should return fallback for unknown types', () => {
			const consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});

			const fallback = getRenderer('unknown');

			expect(fallback.messageType).toBe('fallback');
			expect(consoleWarnSpy).toHaveBeenCalled();

			consoleWarnSpy.mockRestore();
		});
	});

	describe('registerRenderer()', () => {
		test('should register to global registry', () => {
			const mockRenderer: MessageRenderer<MessageDto> = { messageType: 'text' };

			registerRenderer('text', mockRenderer);

			expect(rendererRegistry.hasRenderer('text')).toBe(true);
			expect(getRenderer('text')).toBe(mockRenderer);
		});

		test('should support force parameter', () => {
			const renderer1: MessageRenderer<MessageDto> = { messageType: 'text' };
			const renderer2: MessageRenderer<MessageDto> = { messageType: 'text' };

			registerRenderer('text', renderer1);
			registerRenderer('text', renderer2, true);

			expect(getRenderer('text')).toBe(renderer2);
		});
	});
});

describe('Integration Tests', () => {
	test('should work with complex message renderer implementations', () => {
		const registry = new RendererRegistry();

		// Mock complex renderer with all optional methods
		const complexRenderer: MessageRenderer<MessageDto> = {
			messageType: 'complex',
			onExpand: vi.fn()
		};

		registry.register('complex', complexRenderer);

		const retrieved = registry.getRenderer('complex');
		expect(retrieved).toBe(complexRenderer);

		// Test that optional methods work
		retrieved.onExpand?.();
		expect(complexRenderer.onExpand).toHaveBeenCalledOnce();
	});

	test('should handle registration and retrieval of multiple renderer types', () => {
		const registry = new RendererRegistry();

		const renderers = [
			{ messageType: 'text' },
			{ messageType: 'reasoning' },
			{ messageType: 'tool_call' },
			{ messageType: 'tool_result' },
			{ messageType: 'usage' }
		];

		// Register all renderers
		renderers.forEach((renderer) => {
			registry.register(renderer.messageType, renderer);
		});

		// Verify all are registered correctly
		expect(registry.getRendererCount()).toBe(5);

		renderers.forEach((renderer) => {
			const retrieved = registry.getRenderer(renderer.messageType);
			expect(retrieved).toBe(renderer);
			expect(registry.hasRenderer(renderer.messageType)).toBe(true);
		});

		const registeredTypes = registry.listRenderers();
		expect(registeredTypes).toHaveLength(5);
		renderers.forEach((renderer) => {
			expect(registeredTypes).toContain(renderer.messageType);
		});
	});
});
