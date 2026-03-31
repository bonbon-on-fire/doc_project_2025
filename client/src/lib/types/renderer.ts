import type { MessageDto } from './chat.js';

/**
 * Core interface that all message renderers must implement.
 * Provides the foundation for the Rich Message Rendering system.
 *
 * @template T - The specific message type this renderer handles, extends MessageDto
 */
export interface MessageRenderer<T extends MessageDto> {
	/**
	 * Unique identifier for the message type this renderer handles.
	 * Used by the RendererRegistry to route messages to appropriate renderers.
	 *
	 * @example 'text', 'reasoning', 'tool_call', 'tool_result', 'usage'
	 */
	readonly messageType: string;

	/**
	 * Optional callback triggered when the rendered message is expanded.
	 * Allows renderers to perform lazy loading or additional setup when content becomes visible.
	 *
	 * @returns void or Promise<void> for async operations
	 */
	onExpand?(): void;
}

/**
 * Interface for custom renderers that provide specialized rendering for specific data types.
 * Enables extensible plugin-based rendering for tool results and custom content types.
 *
 * @template T - The data type this custom renderer handles
 */
export interface CustomRenderer<T = any> {
	/**
	 * Unique name identifying this custom renderer.
	 * Used for renderer registration and resolution.
	 *
	 * @example 'image-viewer', 'code-editor', 'chart-display'
	 */
	readonly rendererName: string;

	/**
	 * Optional method to generate preview content when the full renderer is collapsed.
	 * Should return a concise string representation of the data.
	 *
	 * @param data - The data to generate preview content for
	 * @returns A string representation suitable for preview display
	 */
	getPreviewContent?(data: T): string;
}
