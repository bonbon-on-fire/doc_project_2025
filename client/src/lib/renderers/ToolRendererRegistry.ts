import type { ToolRenderer, ToolRendererRegistration } from '../types/toolRenderer.js';

/**
 * Registry system that manages tool-specific renderers, allowing dynamic registration
 * and retrieval of renderers based on tool names with pattern matching support.
 */
export class ToolRendererRegistry {
	private renderers: ToolRendererRegistration[] = [];
	private defaultRenderer: ToolRendererRegistration | null = null;
	private idCounter = 0;

	/**
	 * Registers a renderer for specific tool names.
	 *
	 * @param pattern - Pattern to match tool names (string, array, regex, or '*')
	 * @param renderer - The renderer interface implementation
	 * @param component - The Svelte component or async loader
	 * @returns Registration ID for later removal
	 */
	register(
		pattern: string | string[] | RegExp | '*',
		renderer: ToolRenderer,
		component: any | (() => Promise<any>)
	): string {
		const id = `tool-renderer-${++this.idCounter}`;
		const priority = renderer.priority ?? 0;

		const registration: ToolRendererRegistration = {
			pattern,
			renderer,
			component,
			priority,
			id
		};

		// Handle wildcard pattern as default renderer
		if (pattern === '*') {
			this.defaultRenderer = registration;
			return id;
		}

		// Insert in priority order (higher priority first)
		const insertIndex = this.renderers.findIndex((r) => r.priority < priority);
		if (insertIndex === -1) {
			this.renderers.push(registration);
		} else {
			this.renderers.splice(insertIndex, 0, registration);
		}

		return id;
	}

	/**
	 * Finds the best matching renderer for a tool name.
	 *
	 * @param toolName - The name of the tool to find a renderer for
	 * @returns The matching renderer registration or default if no match
	 */
	getRenderer(toolName: string): ToolRendererRegistration | null {
		// Try to find a matching renderer
		for (const registration of this.renderers) {
			if (this.matchesPattern(toolName, registration.pattern)) {
				return registration;
			}
		}

		// Fall back to default renderer
		return this.defaultRenderer;
	}

	/**
	 * Checks if a tool name matches a pattern.
	 */
	private matchesPattern(toolName: string, pattern: string | string[] | RegExp | '*'): boolean {
		if (pattern === '*') {
			return true;
		}

		if (typeof pattern === 'string') {
			return toolName.toLowerCase() === pattern.toLowerCase();
		}

		if (Array.isArray(pattern)) {
			return pattern.some((p) => toolName.toLowerCase() === p.toLowerCase());
		}

		if (pattern instanceof RegExp) {
			return pattern.test(toolName);
		}

		return false;
	}

	/**
	 * Gets the component for a specific tool renderer.
	 *
	 * @param toolName - The tool name to get component for
	 * @returns Promise that resolves to the Svelte component
	 */
	async getRendererComponent(toolName: string): Promise<any> {
		const registration = this.getRenderer(toolName);
		if (!registration) {
			return null;
		}

		// Handle async component loaders
		if (typeof registration.component === 'function') {
			return await registration.component();
		}

		return registration.component;
	}

	/**
	 * Unregisters a renderer by its ID.
	 *
	 * @param id - The registration ID returned from register()
	 * @returns true if a renderer was removed
	 */
	unregister(id: string): boolean {
		// Check if it's the default renderer
		if (this.defaultRenderer?.id === id) {
			this.defaultRenderer = null;
			return true;
		}

		// Remove from regular renderers
		const index = this.renderers.findIndex((r) => r.id === id);
		if (index !== -1) {
			this.renderers.splice(index, 1);
			return true;
		}

		return false;
	}

	/**
	 * Lists all registered tool patterns.
	 *
	 * @returns Array of pattern descriptions
	 */
	listRenderers(): string[] {
		const patterns: string[] = [];

		for (const reg of this.renderers) {
			if (typeof reg.pattern === 'string') {
				patterns.push(reg.pattern);
			} else if (Array.isArray(reg.pattern)) {
				patterns.push(`[${reg.pattern.join(', ')}]`);
			} else if (reg.pattern instanceof RegExp) {
				patterns.push(reg.pattern.toString());
			}
		}

		if (this.defaultRenderer) {
			patterns.push('* (default)');
		}

		return patterns;
	}

	/**
	 * Checks if a renderer is registered for the specified tool name.
	 *
	 * @param toolName - The tool name to check
	 * @returns true if a renderer is registered
	 */
	hasRenderer(toolName: string): boolean {
		return this.getRenderer(toolName) !== null;
	}

	/**
	 * Gets the total number of registered renderers.
	 *
	 * @returns The count of registered renderers (including default)
	 */
	getRendererCount(): number {
		return this.renderers.length + (this.defaultRenderer ? 1 : 0);
	}

	/**
	 * Clears all registered renderers.
	 */
	clear(): void {
		this.renderers = [];
		this.defaultRenderer = null;
		this.idCounter = 0;
	}

	/**
	 * Sets or updates the default renderer.
	 *
	 * @param renderer - The renderer interface
	 * @param component - The Svelte component or async loader
	 */
	setDefaultRenderer(renderer: ToolRenderer, component: any | (() => Promise<any>)): void {
		this.register('*', renderer, component);
	}
}

// Singleton instance for global use
export const toolRendererRegistry = new ToolRendererRegistry();

/**
 * Convenience function to register a tool renderer to the global registry.
 *
 * @param pattern - Pattern to match tool names
 * @param renderer - The renderer interface
 * @param component - The Svelte component or async loader
 * @returns Registration ID
 */
export function registerToolRenderer(
	pattern: string | string[] | RegExp | '*',
	renderer: ToolRenderer,
	component: any | (() => Promise<any>)
): string {
	return toolRendererRegistry.register(pattern, renderer, component);
}

/**
 * Convenience function to get a tool renderer from the global registry.
 *
 * @param toolName - The tool name to find a renderer for
 * @returns The renderer registration or null
 */
export function getToolRenderer(toolName: string): ToolRendererRegistration | null {
	return toolRendererRegistry.getRenderer(toolName);
}

/**
 * Convenience function to get a tool renderer component from the global registry.
 *
 * @param toolName - The tool name to get component for
 * @returns Promise that resolves to the Svelte component
 */
export async function getToolRendererComponent(toolName: string): Promise<any> {
	return toolRendererRegistry.getRendererComponent(toolName);
}
