import type { ComponentType } from 'svelte';

/**
 * Registry for dynamically loading Svelte components for message renderers.
 * This replaces the hardcoded switch statement with a registration-based approach.
 */
export class ComponentRegistry {
	private componentLoaders = new Map<string, () => Promise<ComponentType>>();
	private loadedComponents = new Map<string, ComponentType>();

	/**
	 * Registers a component loader for a specific message type.
	 * The loader is a function that returns a promise resolving to the component.
	 *
	 * @param messageType - The message type this component renders
	 * @param loader - A function that imports and returns the component
	 * @param force - If true, allows overriding existing registration
	 */
	registerComponentLoader(
		messageType: string,
		loader: () => Promise<ComponentType>,
		force = false
	): void {
		if (!messageType || messageType.trim() === '') {
			throw new Error('Message type cannot be empty');
		}

		if (!loader || typeof loader !== 'function') {
			throw new Error('Loader must be a function that returns a Promise<ComponentType>');
		}

		if (this.componentLoaders.has(messageType) && !force) {
			throw new Error(
				`Component loader for message type '${messageType}' is already registered. Use force=true to override.`
			);
		}

		this.componentLoaders.set(messageType, loader);
	}

	/**
	 * Registers a component directly (for already loaded components).
	 *
	 * @param messageType - The message type this component renders
	 * @param component - The Svelte component
	 * @param force - If true, allows overriding existing registration
	 */
	registerComponent(messageType: string, component: ComponentType, force = false): void {
		if (!messageType || messageType.trim() === '') {
			throw new Error('Message type cannot be empty');
		}

		if (!component) {
			throw new Error('Component cannot be null or undefined');
		}

		// Store as both a loaded component and create a loader for consistency
		if (this.loadedComponents.has(messageType) && !force) {
			throw new Error(
				`Component for message type '${messageType}' is already registered. Use force=true to override.`
			);
		}

		this.loadedComponents.set(messageType, component);
		// Also register a loader that returns the already loaded component
		this.componentLoaders.set(messageType, async () => component);
	}

	/**
	 * Gets the component for a specific message type.
	 * Will load the component if not already loaded.
	 *
	 * @param messageType - The message type to get component for
	 * @returns Promise that resolves to the component, or null if not found
	 */
	async getComponent(messageType: string): Promise<ComponentType | null> {
		// Check if already loaded
		const loaded = this.loadedComponents.get(messageType);
		if (loaded) {
			return loaded;
		}

		// Check if we have a loader for this type
		const loader = this.componentLoaders.get(messageType);
		if (!loader) {
			console.warn(`No component loader found for message type '${messageType}'`);
			return null;
		}

		try {
			// Load the component
			const component = await loader();
			// Cache the loaded component
			this.loadedComponents.set(messageType, component);
			return component;
		} catch (error) {
			console.error(`Failed to load component for message type '${messageType}':`, error);
			return null;
		}
	}

	/**
	 * Checks if a component loader is registered for the specified message type.
	 *
	 * @param messageType - The message type to check
	 * @returns true if a loader is registered, false otherwise
	 */
	hasComponentLoader(messageType: string): boolean {
		return this.componentLoaders.has(messageType);
	}

	/**
	 * Lists all registered message types with component loaders.
	 *
	 * @returns Array of registered message type strings
	 */
	listComponentTypes(): string[] {
		return Array.from(this.componentLoaders.keys());
	}

	/**
	 * Clears all registered component loaders and loaded components.
	 * Useful for testing or dynamic reconfiguration.
	 */
	clear(): void {
		this.componentLoaders.clear();
		this.loadedComponents.clear();
	}

	/**
	 * Preloads components for the specified message types.
	 * This can improve performance by loading components before they're needed.
	 *
	 * @param messageTypes - Array of message types to preload
	 * @returns Promise that resolves when all components are loaded
	 */
	async preloadComponents(messageTypes: string[]): Promise<void> {
		const loadPromises = messageTypes.map((type) => this.getComponent(type));
		await Promise.all(loadPromises);
	}
}

// Singleton instance for global use
export const componentRegistry = new ComponentRegistry();

/**
 * Convenience function to get a component from the global registry.
 *
 * @param messageType - The message type to get component for
 * @returns Promise that resolves to the component, or null if not found
 */
export async function getRendererComponent(messageType: string): Promise<ComponentType | null> {
	return componentRegistry.getComponent(messageType);
}

/**
 * Convenience function to register a component loader to the global registry.
 *
 * @param messageType - The message type this component renders
 * @param loader - A function that imports and returns the component
 * @param force - If true, allows overriding existing registration
 */
export function registerComponentLoader(
	messageType: string,
	loader: () => Promise<ComponentType>,
	force = false
): void {
	componentRegistry.registerComponentLoader(messageType, loader, force);
}

/**
 * Convenience function to register a component directly to the global registry.
 *
 * @param messageType - The message type this component renders
 * @param component - The Svelte component
 * @param force - If true, allows overriding existing registration
 */
export function registerComponent(
	messageType: string,
	component: ComponentType,
	force = false
): void {
	componentRegistry.registerComponent(messageType, component, force);
}
