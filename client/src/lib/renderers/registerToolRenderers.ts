import { registerToolRenderer } from './ToolRendererRegistry.js';
import type { ToolRenderer } from '../types/toolRenderer.js';

/**
 * Registers all built-in tool renderers with the global registry.
 * This function should be called during application initialization.
 */
export function registerBuiltInToolRenderers(): void {
	// Register Default Tool Renderer (lowest priority, catches all)
	const defaultRenderer: ToolRenderer = {
		toolNamePattern: '*',
		priority: -100,
		supportsStreaming: true
	};

	registerToolRenderer(
		'*',
		defaultRenderer,
		async () => (await import('../components/DefaultToolRenderer.svelte')).default
	);

	// Register Calculator Tool Renderer
	const calculatorRenderer: ToolRenderer = {
		toolNamePattern: /^(calculate|calculator|math|calc|compute|evaluate)/i,
		priority: 10,
		supportsStreaming: true
	};

	registerToolRenderer(
		calculatorRenderer.toolNamePattern,
		calculatorRenderer,
		async () => (await import('../components/CalculatorToolRenderer.svelte')).default
	);

	// Register Code Execution Tool Renderer (example for future)
	const codeExecutionRenderer: ToolRenderer = {
		toolNamePattern: ['execute_code', 'run_code', 'python', 'javascript', 'shell'],
		priority: 10,
		supportsStreaming: true
	};

	// For now, use default renderer for code execution
	// In future, create CodeExecutionToolRenderer.svelte with syntax highlighting
	// registerToolRenderer(
	//   codeExecutionRenderer.toolNamePattern,
	//   codeExecutionRenderer,
	//   async () => (await import('../components/CodeExecutionToolRenderer.svelte')).default
	// );

	// Register Database Query Tool Renderer (example for future)
	const databaseRenderer: ToolRenderer = {
		toolNamePattern: /^(query|sql|database|db_)/i,
		priority: 10,
		supportsStreaming: true
	};

	// For now, use default renderer for database queries
	// In future, create DatabaseQueryToolRenderer.svelte with table formatting
	// registerToolRenderer(
	//   databaseRenderer.toolNamePattern,
	//   databaseRenderer,
	//   async () => (await import('../components/DatabaseQueryToolRenderer.svelte')).default
	// );

	// Register File Operation Tool Renderer (example for future)
	const fileOperationRenderer: ToolRenderer = {
		toolNamePattern: ['read_file', 'write_file', 'list_files', 'create_file', 'delete_file'],
		priority: 10,
		supportsStreaming: false
	};

	// For now, use default renderer for file operations
	// In future, create FileOperationToolRenderer.svelte with file icons and paths
	// registerToolRenderer(
	//   fileOperationRenderer.toolNamePattern,
	//   fileOperationRenderer,
	//   async () => (await import('../components/FileOperationToolRenderer.svelte')).default
	// );

	// Register Web/API Tool Renderer (example for future)
	const webApiRenderer: ToolRenderer = {
		toolNamePattern: /^(fetch|api|http|get|post|put|delete|web_)/i,
		priority: 5,
		supportsStreaming: true
	};

	// For now, use default renderer for web/API calls
	// In future, create WebApiToolRenderer.svelte with status codes and headers
	// registerToolRenderer(
	//   webApiRenderer.toolNamePattern,
	//   webApiRenderer,
	//   async () => (await import('../components/WebApiToolRenderer.svelte')).default
	// );

	console.info('Built-in tool renderers registered successfully');
}

/**
 * Registers custom tool renderers from application configuration.
 * This allows users to add their own tool-specific renderers.
 *
 * @param customRenderers - Array of custom renderer configurations
 */
export function registerCustomToolRenderers(
	customRenderers?: Array<{
		pattern: string | string[] | RegExp;
		renderer: ToolRenderer;
		component: any | (() => Promise<any>);
	}>
): void {
	if (!customRenderers || customRenderers.length === 0) {
		return;
	}

	for (const config of customRenderers) {
		try {
			registerToolRenderer(config.pattern, config.renderer, config.component);
			console.info(`Registered custom tool renderer for pattern: ${config.pattern}`);
		} catch (error) {
			console.error(`Failed to register custom tool renderer:`, error);
		}
	}
}
