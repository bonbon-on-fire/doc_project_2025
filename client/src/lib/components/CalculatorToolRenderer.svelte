<script lang="ts">
	import type { ToolRendererProps } from '$lib/types/toolRenderer';
	import type { ToolRenderer } from '$lib/types/toolRenderer';
	import { onMount } from 'svelte';

	// Accept standard tool renderer props
	export let toolCallPair: ToolRendererProps['toolCallPair'];
	export let isStreaming: boolean = false;
	export let renderPhase: ToolRendererProps['renderPhase'] = 'initial';
	export let index: number = 0;
	export let expanded: boolean = true;

	onMount(() => {
		console.log('[CalculatorToolRenderer] Mounted with tool:', toolCallPair.toolCall);
	});

	// Renderer interface implementation
	const rendererInterface: ToolRenderer = {
		toolNamePattern: /^(calculate|calculator|math|calc|compute|evaluate)/i,
		priority: 10,
		supportsStreaming: true,
		getPreviewContent: (pair) => {
			const expr = getExpression(pair);
			const result = pair.toolResult?.result;
			if (result && !hasError(pair)) {
				return `${expr} = ${formatResult(result)}`;
			}
			return expr;
		}
	};

	// Cache previous expression to prevent flickering during incremental updates
	// This maintains the previous state when JsonFragmentRebuilder returns undefined between keys
	let expression_cache: string | undefined = undefined;
	let expression_cache_toolCallId: string | undefined = undefined;

	// Reset cache when tool call changes (new tool call or different ID)
	$: if (toolCallPair?.toolCall?.tool_call_id || toolCallPair?.toolCall?.id) {
		// Check if this is a different tool call than what we cached
		const currentId = toolCallPair.toolCall.tool_call_id || toolCallPair.toolCall.id;
		if (expression_cache_toolCallId !== currentId) {
			expression_cache = undefined;
			expression_cache_toolCallId = currentId;
		}
	}

	// Extract expression from arguments
	function getExpression(pair: typeof toolCallPair): string {
		// Prefer stitched args object from JUF over raw function_args string
		const rawArgs = (pair.toolCall as any).args ?? pair.toolCall.function_args;
		let parsed: any = rawArgs;

		if (parsed == null) {
			// Return cached expression if available, otherwise show loading indicator
			return expression_cache ?? 'Loading...';
		}

		if (typeof rawArgs === 'string') {
			try {
				parsed = JSON.parse(rawArgs);
			} catch {
				// If not valid JSON yet, render the raw string (streaming partial)
				return rawArgs;
			}
		}

		// Common patterns for calculator/math tools
		if (parsed.expression) {
			expression_cache = parsed.expression;
			return parsed.expression;
		}
		if (parsed.formula) {
			expression_cache = parsed.formula;
			return parsed.formula;
		}
		if (parsed.equation) {
			expression_cache = parsed.equation;
			return parsed.equation;
		}
		if (parsed.query) {
			expression_cache = parsed.query;
			return parsed.query;
		}

		// Handle operation-based formats
		if (parsed.operation && parsed.operands) {
			const ops = Array.isArray(parsed.operands)
				? parsed.operands
				: [parsed.a || parsed.x, parsed.b || parsed.y];
			const op = parsed.operation.toLowerCase();
			let expr: string;

			switch (op) {
				case 'add':
				case 'addition':
				case '+':
					expr = ops.join(' + ');
					break;
				case 'subtract':
				case 'subtraction':
				case '-':
					expr = ops.join(' - ');
					break;
				case 'multiply':
				case 'multiplication':
				case '*':
				case '×':
					expr = ops.join(' × ');
					break;
				case 'divide':
				case 'division':
				case '/':
				case '÷':
					expr = ops.join(' ÷ ');
					break;
				case 'power':
				case 'pow':
				case '^':
					expr = `${ops[0]}^${ops[1]}`;
					break;
				case 'sqrt':
				case 'square_root':
					expr = `√${ops[0]}`;
					break;
				case 'mod':
				case 'modulo':
				case '%':
					expr = `${ops[0]} mod ${ops[1]}`;
					break;
				default:
					expr = `${op}(${ops.join(', ')})`;
			}

			expression_cache = expr;
			return expr;
		}

		// Handle simple two-operand format
		if (parsed.a !== undefined && parsed.b !== undefined) {
			let expr: string;
			if (parsed.operator) {
				expr = `${parsed.a} ${parsed.operator} ${parsed.b}`;
			} else {
				// Try to infer operation from tool name
				const toolName = (pair.toolCall.function_name || pair.toolCall.name || '').toLowerCase();
				if (toolName.includes('add')) expr = `${parsed.a} + ${parsed.b}`;
				else if (toolName.includes('subtract')) expr = `${parsed.a} - ${parsed.b}`;
				else if (toolName.includes('multiply')) expr = `${parsed.a} × ${parsed.b}`;
				else if (toolName.includes('divide')) expr = `${parsed.a} ÷ ${parsed.b}`;
				else expr = `${parsed.a}, ${parsed.b}`;
			}
			expression_cache = expr;
			return expr;
		}

		// Handle single value
		if (parsed.value !== undefined) {
			const expr = String(parsed.value);
			expression_cache = expr;
			return expr;
		}

		// Fallback to JSON representation for unknown shapes
		let result: string;
		try {
			result = JSON.stringify(parsed);
		} catch {
			result = String(parsed);
		}

		// Cache the successfully computed expression
		expression_cache = result;
		return result;
	}

	// Format the result value
	function formatResult(result: string): string {
		// Try to parse as number
		const num = parseFloat(result);
		if (!isNaN(num)) {
			// Format with appropriate precision
			if (Number.isInteger(num)) {
				return num.toLocaleString();
			} else {
				// Remove trailing zeros after decimal
				return num.toFixed(10).replace(/\.?0+$/, '');
			}
		}

		// Try to extract number from string like "Result: 42"
		const match = result.match(/[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?/);
		if (match) {
			return formatResult(match[0]);
		}

		return result;
	}

	// Check if result is an error
	function hasError(pair: typeof toolCallPair): boolean {
		return (
			!!pair.toolResult &&
			(pair.toolResult.result.toLowerCase().includes('error') ||
				pair.toolResult.result.toLowerCase().includes('invalid') ||
				pair.toolResult.result.toLowerCase().includes('failed'))
		);
	}

	// Get tool name
	function getToolName(pair: typeof toolCallPair): string {
		return pair.toolCall.function_name || pair.toolCall.name || 'Calculator';
	}

	$: expression = getExpression(toolCallPair);
	$: isError = hasError(toolCallPair);
	$: toolName = getToolName(toolCallPair);
</script>

<!-- Calculator tool renderer -->
<div
	class="calculator-renderer rounded-lg border p-6"
	data-testid="calculator-tool-renderer"
	data-tool-name={toolName}
>
	<!-- Tool header -->
	<div class="mb-4 flex items-center justify-between">
		<div class="flex items-center space-x-2">
			<svg class="calc-icon h-6 w-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
				<path
					stroke-linecap="round"
					stroke-linejoin="round"
					stroke-width="2"
					d="M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 14h.01M12 14h.01M15 11h.01M12 11h.01M9 11h.01M7 21h10a2 2 0 002-2V5a2 2 0 00-2-2H7a2 2 0 00-2 2v14a2 2 0 002 2z"
				/>
			</svg>
			<button class="calc-title text-lg font-semibold" role="button"> Math Calculation </button>
		</div>
		{#if toolCallPair.toolCall.id || toolCallPair.toolCall.tool_call_id}
			<span class="calc-id text-xs">
				#{toolCallPair.toolCall.id || toolCallPair.toolCall.tool_call_id}
			</span>
		{/if}
	</div>

	<!-- Calculator display -->
	<div class="calculator-display rounded-lg p-6">
		<div class="text-center">
			<!-- Expression -->
			<div class="calc-expression mb-3 font-mono text-3xl" data-testid="calc-expression">
				{expression}
			</div>

			<!-- Result or loading state -->
			{#if toolCallPair.toolResult}
				{#if !isError}
					<div class="calc-result-container pt-3">
						<div class="calc-result text-4xl font-bold" data-testid="calc-result">
							= {formatResult(toolCallPair.toolResult.result)}
						</div>
					</div>
				{:else}
					<div class="calc-error-container pt-3">
						<div class="calc-error text-sm" data-testid="calc-error">
							<svg
								class="mr-1 inline h-5 w-5"
								fill="none"
								stroke="currentColor"
								viewBox="0 0 24 24"
							>
								<path
									stroke-linecap="round"
									stroke-linejoin="round"
									stroke-width="2"
									d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
								/>
							</svg>
							{toolCallPair.toolResult.result}
						</div>
					</div>
				{/if}
			{:else if isStreaming}
				<div class="calc-loading-container pt-3">
					<div class="calc-loading flex items-center justify-center space-x-2">
						<div class="flex space-x-1">
							<div
								class="calc-loading-dot h-2 w-2 animate-bounce rounded-full"
								style="animation-delay: 0ms"
							></div>
							<div
								class="calc-loading-dot h-2 w-2 animate-bounce rounded-full"
								style="animation-delay: 150ms"
							></div>
							<div
								class="calc-loading-dot h-2 w-2 animate-bounce rounded-full"
								style="animation-delay: 300ms"
							></div>
						</div>
						<span class="text-sm">Calculating</span>
					</div>
				</div>
			{/if}
		</div>
	</div>

	<!-- Show raw arguments in details (collapsed by default) -->
	{#if expanded && (toolCallPair.toolCall.function_args || toolCallPair.toolCall.args)}
		<details class="mt-4">
			<summary class="text-xs"> View raw parameters </summary>
			<div class="mt-2 rounded p-2 font-mono text-xs">
				<pre class="whitespace-pre-wrap">{JSON.stringify(
						toolCallPair.toolCall.function_args || toolCallPair.toolCall.args,
						null,
						2
					)}</pre>
			</div>
		</details>
	{/if}
</div>

<style>
	.calculator-renderer {
		background: linear-gradient(135deg, rgba(17, 24, 39, 0.95), rgba(11, 15, 25, 0.95));
		border: 1px solid rgba(59, 130, 246, 0.15);
		backdrop-filter: blur(10px);
		transition: all 0.3s ease;
	}

	.calc-icon {
		color: #60a5fa;
	}

	.calc-title {
		color: #e2e8f0;
		font-weight: 500;
		background: none;
		border: none;
		cursor: default;
		padding: 0;
	}

	.calc-id {
		color: rgba(148, 163, 184, 0.5);
		font-size: 0.7rem;
	}

	.calculator-display {
		background: rgba(10, 14, 23, 0.9);
		box-shadow:
			inset 0 2px 8px rgba(0, 0, 0, 0.8),
			inset 0 -1px 0 rgba(59, 130, 246, 0.1);
		border: 1px solid rgba(30, 41, 59, 0.5);
		backdrop-filter: blur(5px);
	}

	.calc-expression {
		color: #cbd5e1;
		letter-spacing: 0.05em;
		font-size: 1.75rem;
	}

	.calc-result-container {
		border-top: 2px solid rgba(59, 130, 246, 0.3);
	}

	.calc-result {
		color: #60a5fa;
		text-shadow: 0 0 30px rgba(96, 165, 250, 0.6);
		font-size: 2rem;
	}

	.calc-error-container {
		border-top: 2px solid var(--color-error, #ef4444);
	}

	.calc-error {
		color: var(--color-error, #ef4444);
	}

	.calc-loading-container {
		border-top: 2px solid var(--color-border, #3a4556);
	}

	.calc-loading {
		color: var(--color-text-secondary, #9ca3af);
	}

	.calc-loading-dot {
		background: var(--color-primary, #60a5fa);
	}

	@keyframes bounce {
		0%,
		100% {
			transform: translateY(0);
		}
		50% {
			transform: translateY(-0.5rem);
		}
	}

	/* Details element styling */
	details {
		margin-top: 0.75rem;
	}

	details summary {
		color: rgba(100, 116, 139, 0.8);
		cursor: pointer;
		transition: color 0.2s;
		font-size: 0.7rem;
		user-select: none;
	}

	details summary:hover {
		color: rgba(96, 165, 250, 0.8);
	}

	details summary::marker {
		color: rgba(59, 130, 246, 0.4);
	}

	details[open] summary {
		color: rgba(96, 165, 250, 0.8);
		margin-bottom: 0.5rem;
	}

	details pre {
		background: rgba(10, 14, 23, 0.6);
		border: 1px solid rgba(30, 41, 59, 0.4);
		color: #64748b;
		padding: 0.75rem;
		border-radius: 0.375rem;
		font-size: 0.7rem;
		line-height: 1.4;
	}
</style>
