import type { ToolCallPair } from './chat.js';

/**
 * Core interface that all tool renderers must implement.
 * Provides the foundation for the Tool Call Rendering system.
 */
export interface ToolRenderer {
	/**
	 * Pattern to match tool names. Can be:
	 * - string: Exact match (e.g., 'calculator')
	 * - string[]: Multiple exact matches (e.g., ['math', 'calc'])
	 * - RegExp: Pattern matching (e.g., /^(math|calc|compute)/)
	 * - '*': Wildcard to match any tool (used for default renderer)
	 */
	readonly toolNamePattern: string | string[] | RegExp | '*';

	/**
	 * Priority for this renderer when multiple patterns match.
	 * Higher values take precedence. Default is 0.
	 * Negative values can be used for fallback renderers.
	 */
	readonly priority?: number;

	/**
	 * Optional callback triggered when the tool result becomes available.
	 * Useful for renderers that need to update their display when results arrive.
	 */
	onResultAvailable?(result: string): void;

	/**
	 * Optional method to generate preview content for collapsed state.
	 * Should return a concise string representation of the tool call.
	 */
	getPreviewContent?(toolCall: ToolCallPair): string;

	/**
	 * Whether this renderer supports streaming display of arguments.
	 * If true, the renderer will receive partial argument updates during streaming.
	 */
	readonly supportsStreaming?: boolean;
}

/**
 * Registration entry for a tool renderer with resolved metadata.
 * Used internally by the ToolRendererRegistry.
 */
export interface ToolRendererRegistration {
	/**
	 * The pattern used to match tool names.
	 */
	pattern: string | string[] | RegExp | '*';

	/**
	 * The renderer interface definition.
	 */
	renderer: ToolRenderer;

	/**
	 * The Svelte component constructor or async loader.
	 */
	component: any | (() => Promise<any>);

	/**
	 * Resolved priority (defaults to 0 if not specified).
	 */
	priority: number;

	/**
	 * Unique identifier for this registration.
	 */
	id: string;
}

/**
 * Props passed to tool renderer components.
 */
export interface ToolRendererProps {
	/**
	 * The tool call and result pair to render.
	 */
	toolCallPair: ToolCallPair;

	/**
	 * Whether this tool call is currently streaming.
	 */
	isStreaming?: boolean;

	/**
	 * The current render phase.
	 */
	renderPhase?: 'initial' | 'streaming' | 'complete';

	/**
	 * Index of this tool call in the parent message.
	 */
	index?: number;

	/**
	 * Whether to show in expanded state.
	 */
	expanded?: boolean;
}
