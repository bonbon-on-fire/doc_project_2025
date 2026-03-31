/**
 * Message Handler Registry Implementation
 *
 * Provides centralized registration and access to message type handlers.
 * Acts as a service locator for the message handling system.
 */

import type { MessageTypeHandler, MessageHandlerRegistry } from './messageHandlers';

/**
 * Implementation of the message handler registry
 */
export class MessageHandlerRegistryImpl implements MessageHandlerRegistry {
	private handlers = new Map<string, MessageTypeHandler>();

	/**
	 * Register a new message type handler
	 */
	register(handler: MessageTypeHandler): void {
		const messageType = handler.getMessageType();

		if (this.handlers.has(messageType)) {
			console.warn(`Handler for message type '${messageType}' is being replaced`);
		}

		this.handlers.set(messageType, handler);
		console.log(`Registered handler for message type: ${messageType}`);
	}

	/**
	 * Get handler for a specific message type
	 */
	getHandler(messageType: string): MessageTypeHandler | null {
		return this.handlers.get(messageType) || null;
	}

	/**
	 * Get all registered handlers
	 */
	getAllHandlers(): MessageTypeHandler[] {
		return Array.from(this.handlers.values());
	}

	/**
	 * Check if a message type is supported
	 */
	isSupported(messageType: string): boolean {
		return this.handlers.has(messageType);
	}

	/**
	 * Get all supported message types
	 */
	getSupportedTypes(): string[] {
		return Array.from(this.handlers.keys());
	}

	/**
	 * Clear all handlers (useful for testing)
	 */
	clear(): void {
		this.handlers.clear();
	}

	/**
	 * Get handler count
	 */
	size(): number {
		return this.handlers.size;
	}
}

/**
 * Factory function for creating handler registry
 */
export function createMessageHandlerRegistry(): MessageHandlerRegistry {
	return new MessageHandlerRegistryImpl();
}
