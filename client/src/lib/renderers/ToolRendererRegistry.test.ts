import { describe, it, expect, beforeEach } from 'vitest';
import { ToolRendererRegistry } from './ToolRendererRegistry';
import type { ToolRenderer } from '../types/toolRenderer';

describe('ToolRendererRegistry', () => {
	let registry: ToolRendererRegistry;

	beforeEach(() => {
		registry = new ToolRendererRegistry();
	});

	describe('register', () => {
		it('should register a tool renderer with string pattern', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: 'calculator',
				priority: 10
			};

			const id = registry.register('calculator', renderer, null);
			expect(id).toMatch(/^tool-renderer-\d+$/);
			expect(registry.hasRenderer('calculator')).toBe(true);
		});

		it('should register a tool renderer with array pattern', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: ['math', 'calc'],
				priority: 10
			};

			registry.register(['math', 'calc'], renderer, null);
			expect(registry.hasRenderer('math')).toBe(true);
			expect(registry.hasRenderer('calc')).toBe(true);
			expect(registry.hasRenderer('other')).toBe(false);
		});

		it('should register a tool renderer with regex pattern', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: /^(math|calc)/i,
				priority: 10
			};

			registry.register(/^(math|calc)/i, renderer, null);
			expect(registry.hasRenderer('math_tool')).toBe(true);
			expect(registry.hasRenderer('calculator')).toBe(true);
			expect(registry.hasRenderer('other')).toBe(false);
		});

		it('should register a default renderer with wildcard pattern', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: '*',
				priority: -100
			};

			registry.register('*', renderer, null);
			expect(registry.hasRenderer('anything')).toBe(true);
			expect(registry.hasRenderer('random_tool')).toBe(true);
		});

		it('should respect priority order', () => {
			const lowPriority: ToolRenderer = {
				toolNamePattern: 'calculator',
				priority: 5
			};
			const highPriority: ToolRenderer = {
				toolNamePattern: /calc/,
				priority: 15
			};

			registry.register('calculator', lowPriority, 'low');
			registry.register(/calc/, highPriority, 'high');

			const result = registry.getRenderer('calculator');
			expect(result?.component).toBe('high');
		});
	});

	describe('getRenderer', () => {
		it('should return matching renderer for exact string match', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: 'test_tool',
				priority: 10
			};

			registry.register('test_tool', renderer, 'component');
			const result = registry.getRenderer('test_tool');

			expect(result).toBeTruthy();
			expect(result?.renderer).toBe(renderer);
			expect(result?.component).toBe('component');
		});

		it('should return default renderer when no match found', () => {
			const defaultRenderer: ToolRenderer = {
				toolNamePattern: '*',
				priority: -100
			};

			registry.register('*', defaultRenderer, 'default');
			const result = registry.getRenderer('unknown_tool');

			expect(result).toBeTruthy();
			expect(result?.renderer).toBe(defaultRenderer);
		});

		it('should return null when no renderer and no default', () => {
			const result = registry.getRenderer('unknown_tool');
			expect(result).toBeNull();
		});

		it('should be case-insensitive for string patterns', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: 'Calculator',
				priority: 10
			};

			registry.register('Calculator', renderer, 'component');

			expect(registry.hasRenderer('calculator')).toBe(true);
			expect(registry.hasRenderer('CALCULATOR')).toBe(true);
			expect(registry.hasRenderer('CaLcUlAtOr')).toBe(true);
		});
	});

	describe('unregister', () => {
		it('should remove a registered renderer', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: 'test',
				priority: 10
			};

			const id = registry.register('test', renderer, null);
			expect(registry.hasRenderer('test')).toBe(true);

			const removed = registry.unregister(id);
			expect(removed).toBe(true);
			expect(registry.hasRenderer('test')).toBe(false);
		});

		it('should remove default renderer', () => {
			const renderer: ToolRenderer = {
				toolNamePattern: '*',
				priority: -100
			};

			const id = registry.register('*', renderer, null);
			expect(registry.hasRenderer('anything')).toBe(true);

			const removed = registry.unregister(id);
			expect(removed).toBe(true);
			expect(registry.hasRenderer('anything')).toBe(false);
		});
	});

	describe('listRenderers', () => {
		it('should list all registered patterns', () => {
			registry.register('tool1', { toolNamePattern: 'tool1' }, null);
			registry.register(['tool2', 'tool3'], { toolNamePattern: ['tool2', 'tool3'] }, null);
			registry.register(/^tool\d+/, { toolNamePattern: /^tool\d+/ }, null);
			registry.register('*', { toolNamePattern: '*' }, null);

			const list = registry.listRenderers();

			expect(list).toContain('tool1');
			expect(list).toContain('[tool2, tool3]');
			expect(list).toContain('/^tool\\d+/');
			expect(list).toContain('* (default)');
		});
	});

	describe('clear', () => {
		it('should remove all renderers', () => {
			registry.register('tool1', { toolNamePattern: 'tool1' }, null);
			registry.register('tool2', { toolNamePattern: 'tool2' }, null);
			registry.register('*', { toolNamePattern: '*' }, null);

			expect(registry.getRendererCount()).toBe(3);

			registry.clear();

			expect(registry.getRendererCount()).toBe(0);
			expect(registry.hasRenderer('tool1')).toBe(false);
			expect(registry.hasRenderer('tool2')).toBe(false);
			expect(registry.hasRenderer('anything')).toBe(false);
		});
	});
});
