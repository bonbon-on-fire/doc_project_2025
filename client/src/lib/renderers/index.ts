/**
 * Barrel export file for the renderers module.
 * Provides convenient imports for the Rich Message Rendering system.
 */

// Import what we need internally
import { registerRenderer } from './RendererRegistry.js';
import { registerComponentLoader, componentRegistry } from './ComponentRegistry.js';
import { logger } from '$lib/utils/logger';

// Export the main RendererRegistry class and utilities
export {
	RendererRegistry,
	rendererRegistry,
	getRenderer,
	registerRenderer
} from './RendererRegistry.js';

// Export the ComponentRegistry class and utilities
export {
	ComponentRegistry,
	componentRegistry,
	getRendererComponent as getComponentFromRegistry,
	registerComponentLoader,
	registerComponent
} from './ComponentRegistry.js';

// Export Tool Renderer Registry and utilities
export {
	ToolRendererRegistry,
	toolRendererRegistry,
	registerToolRenderer,
	getToolRenderer,
	getToolRendererComponent
} from './ToolRendererRegistry.js';

// Export tool renderer registration functions
export {
	registerBuiltInToolRenderers,
	registerCustomToolRenderers
} from './registerToolRenderers.js';

// Re-export types for convenience
export type { MessageRenderer, CustomRenderer } from '../types/index.js';

// Import and register built-in renderers
import type { MessageDto } from '../types/index.js';
import type { MessageRenderer } from '../types/renderer.js';

/**
 * Registers all built-in message renderers with the global registry.
 * This function should be called during application initialization.
 */
export function registerBuiltInRenderers(): void {
	// Register TextRenderer for text message types
	const textRenderer: MessageRenderer<MessageDto> = {
		messageType: 'text'
	};

	// Register ReasoningRenderer for reasoning message types
	const reasoningRenderer: MessageRenderer<MessageDto> = {
		messageType: 'reasoning'
	};

	// Register ToolCallRenderer for tool call message types
	const toolCallRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tool_call'
	};

	// Register ToolResultRenderer for tool result message types
	const toolResultRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tool_result'
	};

	// Register ToolsCallAggregateRenderer for aggregate tool messages
	const toolsAggregateRenderer: MessageRenderer<MessageDto> = {
		messageType: 'tools_aggregate'
	};

	// Register TaskManagerToolCallRenderer for task manager tool calls
	const taskManagerRenderer: MessageRenderer<MessageDto> = {
		messageType: 'task_manager_tool_call'
	};

	// Register UsageRenderer for usage message types (doesn't display anything)
	const usageRenderer: MessageRenderer<MessageDto> = {
		messageType: 'usage'
	};

	registerRenderer('text', textRenderer, true);
	registerRenderer('reasoning', reasoningRenderer, true);
	registerRenderer('tool_call', toolCallRenderer, true);
	registerRenderer('tool_result', toolResultRenderer, true);
	registerRenderer('tools_aggregate', toolsAggregateRenderer, true);
	registerRenderer('task_manager_tool_call', taskManagerRenderer, true);
	registerRenderer('usage', usageRenderer, true);

	// Also register the component loaders
	registerBuiltInComponentLoaders();

	logger.info('Built-in message renderers and components registered successfully');
}

/**
 * Registers all built-in component loaders with the global component registry.
 * This uses dynamic imports for lazy loading of components.
 */
function registerBuiltInComponentLoaders(): void {
	// Register component loaders for each message type
	registerComponentLoader(
		'text',
		() => import('../components/TextRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'reasoning',
		() => import('../components/ReasoningRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'tool_call',
		() => import('../components/ToolCallRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'tool_result',
		() => import('../components/ToolResultRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'tools_aggregate',
		() => import('../components/ToolsCallAggregateRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'task_manager_tool_call',
		() => import('./TaskManagerToolCallRenderer.svelte').then((m) => m.default),
		true
	);

	registerComponentLoader(
		'usage',
		() => import('../components/UsageRenderer.svelte').then((m) => m.default),
		true
	);
}

/**
 * Gets the Svelte component for a specific renderer type.
 * This is used by MessageRouter to dynamically load renderer components.
 * Now uses the ComponentRegistry instead of a hardcoded switch statement.
 *
 * @param messageType - The message type to get component for
 * @returns Promise that resolves to the Svelte component
 */
export async function getRendererComponent(messageType: string): Promise<any> {
	const component = await componentRegistry.getComponent(messageType);
	if (!component) {
		logger.warn({ messageType }, 'No component found for message type, using fallback');
	}
	return component;
}
