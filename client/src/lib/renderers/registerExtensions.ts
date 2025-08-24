import { registerRenderer, registerComponentLoader } from './index.js';
import type { MessageRenderer, MessageDto } from '../types/index.js';
import type { ComponentType } from 'svelte';

/**
 * Interface for renderer extensions that can be registered dynamically
 */
export interface RendererExtension {
	/**
	 * The message type this extension handles
	 */
	messageType: string;

	/**
	 * The renderer configuration
	 */
	renderer: MessageRenderer<MessageDto>;

	/**
	 * Function that loads the component for this renderer
	 */
	componentLoader: () => Promise<ComponentType>;
}

/**
 * Registry for extension renderers that can be added at runtime
 */
class ExtensionRegistry {
	private extensions = new Map<string, RendererExtension>();

	/**
	 * Registers a new renderer extension
	 *
	 * @param extension - The extension to register
	 * @param force - If true, allows overriding existing extensions
	 */
	registerExtension(extension: RendererExtension, force = false): void {
		const { messageType, renderer, componentLoader } = extension;

		if (this.extensions.has(messageType) && !force) {
			throw new Error(
				`Extension for message type '${messageType}' is already registered. Use force=true to override.`
			);
		}

		// Register the renderer
		registerRenderer(messageType, renderer, force);

		// Register the component loader
		registerComponentLoader(messageType, componentLoader, force);

		// Store the extension
		this.extensions.set(messageType, extension);

		console.info(`Registered extension renderer for message type '${messageType}'`);
	}

	/**
	 * Unregisters an extension
	 *
	 * @param messageType - The message type to unregister
	 * @returns true if an extension was removed, false otherwise
	 */
	unregisterExtension(messageType: string): boolean {
		return this.extensions.delete(messageType);
	}

	/**
	 * Lists all registered extensions
	 *
	 * @returns Array of registered message types
	 */
	listExtensions(): string[] {
		return Array.from(this.extensions.keys());
	}

	/**
	 * Gets an extension by message type
	 *
	 * @param messageType - The message type to get
	 * @returns The extension or undefined if not found
	 */
	getExtension(messageType: string): RendererExtension | undefined {
		return this.extensions.get(messageType);
	}
}

// Global extension registry
export const extensionRegistry = new ExtensionRegistry();

/**
 * Convenience function to register a renderer extension
 *
 * @param extension - The extension to register
 * @param force - If true, allows overriding existing extensions
 */
export function registerRendererExtension(extension: RendererExtension, force = false): void {
	extensionRegistry.registerExtension(extension, force);
}

/**
 * Loads and registers renderer extensions from a configuration
 * This can be called at runtime to dynamically add new renderer types
 *
 * @param extensions - Array of extensions to register
 */
export function loadRendererExtensions(extensions: RendererExtension[]): void {
	for (const extension of extensions) {
		try {
			registerRendererExtension(extension);
		} catch (error) {
			console.error(
				`Failed to register extension for message type '${extension.messageType}':`,
				error
			);
		}
	}
}

/**
 * Example of how to create and register a custom renderer extension
 *
 * @example
 * ```typescript
 * const customExtension: RendererExtension = {
 *   messageType: 'custom_chart',
 *   renderer: {
 *     messageType: 'custom_chart',
 *     // Additional renderer configuration
 *   },
 *   componentLoader: () => import('./CustomChartRenderer.svelte').then(m => m.default)
 * };
 *
 * registerRendererExtension(customExtension);
 * ```
 */
