import type { MessageRenderer, MessageDto } from '../types/index.js';

/**
 * Registry system that manages message renderers, allowing dynamic registration
 * and retrieval of renderers based on message type.
 *
 * Provides a centralized registry for all message renderers with fallback
 * behavior for unknown message types.
 */
export class RendererRegistry {
	private renderers = new Map<string, MessageRenderer<any>>();
	private fallbackRenderer: MessageRenderer<MessageDto>;

	constructor() {
		// Initialize with fallback renderer
		this.fallbackRenderer = {
			messageType: 'fallback'
		};
	}

	/**
	 * Registers a renderer for a specific message type.
	 *
	 * @param messageType - The message type this renderer handles
	 * @param renderer - The renderer implementation
	 * @param force - If true, allows overriding existing registration
	 * @throws Error if messageType is already registered and force is false
	 */
	register<T extends MessageDto>(
		messageType: string,
		renderer: MessageRenderer<T>,
		force = false
	): void {
		if (!messageType || messageType.trim() === '') {
			throw new Error('Message type cannot be empty');
		}

		if (!renderer || !renderer.messageType) {
			throw new Error('Renderer must be a valid MessageRenderer implementation');
		}

		if (this.renderers.has(messageType) && !force) {
			throw new Error(
				`Renderer for message type '${messageType}' is already registered. Use force=true to override.`
			);
		}

		// Validate that the renderer's messageType matches the registration key
		if (renderer.messageType !== messageType) {
			console.warn(
				`Renderer messageType '${renderer.messageType}' does not match registration key '${messageType}'`
			);
		}

		this.renderers.set(messageType, renderer);
	}

	/**
	 * Retrieves a renderer for the specified message type.
	 * Returns fallback renderer if no specific renderer is found.
	 *
	 * @param messageType - The message type to find a renderer for
	 * @returns The renderer for the message type, never null/undefined
	 */
	getRenderer(messageType: string): MessageRenderer<any> {
		const renderer = this.renderers.get(messageType);

		if (!renderer) {
			console.warn(`No renderer found for message type '${messageType}', using fallback renderer`);
			return this.fallbackRenderer;
		}

		return renderer;
	}

	/**
	 * Unregisters a renderer for the specified message type.
	 *
	 * @param messageType - The message type to unregister
	 * @returns true if a renderer was removed, false if none was registered
	 */
	unregister(messageType: string): boolean {
		return this.renderers.delete(messageType);
	}

	/**
	 * Lists all registered message types.
	 *
	 * @returns Array of registered message type strings
	 */
	listRenderers(): string[] {
		return Array.from(this.renderers.keys());
	}

	/**
	 * Checks if a renderer is registered for the specified message type.
	 *
	 * @param messageType - The message type to check
	 * @returns true if a renderer is registered, false otherwise
	 */
	hasRenderer(messageType: string): boolean {
		return this.renderers.has(messageType);
	}

	/**
	 * Gets the total number of registered renderers.
	 *
	 * @returns The count of registered renderers
	 */
	getRendererCount(): number {
		return this.renderers.size;
	}

	/**
	 * Clears all registered renderers (except fallback).
	 * Useful for testing or dynamic reconfiguration.
	 */
	clear(): void {
		this.renderers.clear();
	}

	/**
	 * Sets a custom fallback renderer.
	 *
	 * @param fallbackRenderer - The new fallback renderer
	 */
	setFallbackRenderer(fallbackRenderer: MessageRenderer<MessageDto>): void {
		if (!fallbackRenderer || !fallbackRenderer.messageType) {
			throw new Error('Fallback renderer must be a valid MessageRenderer implementation');
		}

		this.fallbackRenderer = fallbackRenderer;
	}

	/**
	 * Gets the current fallback renderer.
	 *
	 * @returns The current fallback renderer
	 */
	getFallbackRenderer(): MessageRenderer<MessageDto> {
		return this.fallbackRenderer;
	}
}

// Singleton instance for global use
export const rendererRegistry = new RendererRegistry();

/**
 * Convenience function to get a renderer from the global registry.
 *
 * @param messageType - The message type to find a renderer for
 * @returns The renderer for the message type
 */
export function getRenderer(messageType: string): MessageRenderer<any> {
	return rendererRegistry.getRenderer(messageType);
}

/**
 * Convenience function to register a renderer to the global registry.
 *
 * @param messageType - The message type this renderer handles
 * @param renderer - The renderer implementation
 * @param force - If true, allows overriding existing registration
 */
export function registerRenderer<T extends MessageDto>(
	messageType: string,
	renderer: MessageRenderer<T>,
	force = false
): void {
	rendererRegistry.register(messageType, renderer, force);
}
