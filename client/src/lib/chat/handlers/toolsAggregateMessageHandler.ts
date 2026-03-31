/**
 * Tools Aggregate Message Handler
 *
 * Handles messages that combine tool calls with their results.
 * Maintains a paired structure where each tool call is paired with its result.
 * Creates aggregate message immediately upon first tool call.
 */

import type {
	MessageTypeHandler,
	MessageRenderer,
	MessageSnapshot,
	HandlerEventListener
} from '../messageHandlers';
import type {
	StreamChunkEventEnvelope,
	MessageCompleteEventEnvelope,
	ToolCallUpdateStreamChunkPayload,
	ToolResultStreamChunkPayload,
	ToolCallUpdate
} from '../sseEventTypes';
import { StreamChunkPayloadGuards, MessageCompletePayloadGuards } from '../sseEventTypes';
import type {
	MessageDto,
	ToolCall,
	ToolCallResult,
	ToolsCallAggregateMessageDto,
	ClientToolsCallAggregateMessageDto,
	ToolCallPair
} from '$lib/types/chat';
import { BaseMessageHandler } from '../messageHandlers';
import ToolsCallAggregateRenderer from '$lib/components/ToolsCallAggregateRenderer.svelte';
import { JufStitcherManager } from '$lib/utils/jufStitcherManager';
import { snapshot } from '$lib/utils/snapshotHelper';
import { taskManager } from '$lib/stores/taskManager';
import type { TaskItem, TaskOperation } from '$shared/types/tasks';

/**
 * Renderer for tools aggregate messages
 */
export class ToolsAggregateMessageRenderer implements MessageRenderer {
	getStreamingComponent() {
		return ToolsCallAggregateRenderer as any;
	}

	getCompleteComponent() {
		return ToolsCallAggregateRenderer as any;
	}

	getStreamingProps(snapshot: MessageSnapshot): Record<string, any> {
		return {
			message: {
				...snapshot,
				toolCallPairs: snapshot.toolCallPairs || [],
				messageType: 'tools_aggregate'
			} as ClientToolsCallAggregateMessageDto,
			isStreaming: snapshot.isStreaming
		};
	}

	getCompleteProps(dto: MessageDto): Record<string, any> {
		const aggregateDto = dto as ClientToolsCallAggregateMessageDto;
		return {
			message: aggregateDto,
			isStreaming: false
		};
	}
}

/**
 * Handler for tools aggregate messages
 */
export class ToolsAggregateMessageHandler extends BaseMessageHandler {
	private renderer = new ToolsAggregateMessageRenderer();
	// Map of messageId -> Map of toolCallId -> ToolCallPair
	private toolCallPairsMap = new Map<string, Map<string, ToolCallPair>>();
	// Manager for JSON fragment rebuilders
	private fragmentManager = new JufStitcherManager();
	// Store current chatId for task manager updates
	private currentChatId: string | null = null;

	getMessageType(): string {
		return 'tools_aggregate';
	}

	canHandle(messageType: string): boolean {
		// Handle aggregate messages and tool results
		return (
			messageType === 'tools_aggregate' ||
			messageType === 'tools_call_update' ||
			messageType === 'tool_result'
		);
	}

	getRenderer(): MessageRenderer {
		return this.renderer;
	}

	/**
	 * Set the current chat ID for task manager updates
	 */
	setChatId(chatId: string) {
		this.currentChatId = chatId;
	}

	/**
	 * Process streaming chunk (tool call updates or results)
	 */
	processChunk(messageId: string, envelope: StreamChunkEventEnvelope): MessageSnapshot {
		let snapshot = this.getSnapshot(messageId);

		// Create snapshot if it doesn't exist (first tool call creates the aggregate)
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);

			// Initialize with empty pairs array
			snapshot = this.updateSnapshot(messageId, {
				toolCallPairs: [],
				messageType: 'tools_aggregate'
			});

			// Initialize map for this message
			this.toolCallPairsMap.set(messageId, new Map());
		}

		// Handle tool call updates
		if (envelope.kind === 'tools_call_update') {
			if (!StreamChunkPayloadGuards.isToolCallUpdateStreamChunk(envelope.payload)) {
				console.error('[ToolsAggregateMessageHandler] Invalid tool call update payload');
				return snapshot;
			}

			const payload = envelope.payload as ToolCallUpdateStreamChunkPayload;
			if (payload.toolCallUpdate) {
				this.processToolCallUpdate(messageId, payload.toolCallUpdate);
			}
		}

		// Handle tool result updates
		else if (envelope.kind === 'tool_result') {
			if (!StreamChunkPayloadGuards.isToolResultStreamChunk(envelope.payload)) {
				console.error('[ToolsAggregateMessageHandler] Invalid tool result payload');
				return snapshot;
			}

			const payload = envelope.payload as ToolResultStreamChunkPayload;
			this.processToolResult(messageId, payload);
		}

		// Update snapshot with current paired state
		const pairsMap = this.toolCallPairsMap.get(messageId);
		const toolCallPairs = pairsMap ? Array.from(pairsMap.values()) : [];

		return this.updateSnapshot(messageId, {
			toolCallPairs,
			isStreaming: true
		});
	}

	private processToolCallUpdate(messageId: string, update: ToolCallUpdate) {
		const pairsMap = this.toolCallPairsMap.get(messageId);
		if (!pairsMap) return;

		const toolCallId = update.tool_call_id || `${messageId}_tool_${update.index || 0}`;

		// Get or create pair for this tool call
		let pair = pairsMap.get(toolCallId);
		if (!pair) {
			pair = {
				toolCall: {
					id: toolCallId,
					tool_call_id: toolCallId,
					index: update.index
				} as ToolCall,
				toolResult: undefined
			};
			pairsMap.set(toolCallId, pair);
		}

		// Apply JsonFragments if present using the manager
		// Note: We only process if there are actual fragments to avoid unnecessary updates
		if (update.json_update_fragments && update.json_update_fragments.length > 0) {
			// Generate document ID for this tool call
			const docId = `${messageId}:${toolCallId}`;

			// Apply fragments via manager and get current value
			const args = this.fragmentManager.apply(docId, update.json_update_fragments);

			// Only update args if we got a value (including empty object {})
			// undefined means the rebuilder hasn't started yet
			if (args !== undefined) {
				// Use snapshot to ensure Svelte reactivity
				pair.toolCall.args = snapshot(args);
			}
		} else if (update.json_update_fragments && update.json_update_fragments.length === 0) {
			// Empty fragments array - this happens between keys
			// Don't update args to avoid flickering
		}

		// Update tool call data
		if (update.function_name) {
			pair.toolCall.function_name = update.function_name;
			pair.toolCall.name = update.function_name;
		}

		// Accumulate function arguments (fallback + persistence parity)
		if (update.function_args) {
			const currentArgs = (pair.toolCall.function_args || '') + update.function_args;
			pair.toolCall.function_args = currentArgs;

			// If no fragments were applied, try to parse accumulated JSON as a best-effort
			if (!(update.json_update_fragments && update.json_update_fragments.length > 0)) {
				try {
					pair.toolCall.args = JSON.parse(currentArgs);
				} catch {
					// Keep args undefined on parse failure, rely on function_args for raw display
					pair.toolCall.args = undefined;
				}
			}
		}
	}

	private processToolResult(messageId: string, result: ToolResultStreamChunkPayload) {
		const pairsMap = this.toolCallPairsMap.get(messageId);
		if (!pairsMap) return;

		// Find the pair with matching toolCallId
		let pair = pairsMap.get(result.toolCallId);

		// If pair doesn't exist yet (shouldn't happen normally), create placeholder
		if (!pair) {
			// Received result for unknown tool call - create placeholder
			pair = {
				toolCall: {
					id: result.toolCallId,
					tool_call_id: result.toolCallId,
					function_name: 'unknown',
					name: 'unknown'
				} as ToolCall,
				toolResult: undefined
			};
			pairsMap.set(result.toolCallId, pair);
		}

		// Update the result in the pair
		pair.toolResult = {
			toolCallId: result.toolCallId,
			result: result.result
		};

		// Mark the document as complete since tool result received
		const docId = `${messageId}:${result.toolCallId}`;
		this.fragmentManager.complete(docId);

		// Check if this is a task manager tool call and update store
		this.checkAndUpdateTaskManager(pair);
	}

	/**
	 * Check if a tool call is a task manager operation and update the store
	 */
	private checkAndUpdateTaskManager(pair: ToolCallPair) {
		if (!pair.toolCall || !pair.toolResult) return;

		const functionName = (pair.toolCall.function_name || pair.toolCall.name || '').toLowerCase();

		// Check if this is a task manager function
		const taskManagerFunctions = [
			'add_task',
			'addtask',
			'add-task',
			'update_task',
			'updatetask',
			'update-task',
			'delete_task',
			'deletetask',
			'delete-task',
			'remove_task',
			'removetask',
			'list_tasks',
			'listtasks',
			'list-tasks',
			'get_tasks',
			'gettasks',
			'clear_tasks',
			'cleartasks',
			'clear-tasks',
			'complete_task',
			'completetask',
			'complete-task',
			'start_task',
			'starttask',
			'start-task',
			'add_subtask',
			'addsubtask',
			'add-subtask',
			'add_note',
			'addnote',
			'add-note',
			'task_manager',
			'taskmanager'
		];

		if (!taskManagerFunctions.some((fn) => functionName.includes(fn))) {
			return;
		}

		// Parse the result to get task state
		try {
			const result = pair.toolResult.result;
			let tasks: TaskItem[] = [];
			let chatId: string | undefined;

			// Try to parse the result as JSON
			if (typeof result === 'string') {
				try {
					const parsed = JSON.parse(result);
					if (parsed.tasks) {
						tasks = parsed.tasks;
						chatId = parsed.chatId;
					} else if (Array.isArray(parsed)) {
						tasks = parsed;
					}
				} catch {
					// Result might be plain text or markdown, skip task update
					console.log('Task manager result is not JSON:', result);
					return;
				}
			} else if (typeof result === 'object' && result !== null) {
				if (result.tasks) {
					tasks = result.tasks;
					chatId = result.chatId;
				} else if (Array.isArray(result)) {
					tasks = result;
				}
			}

			// Determine operation type from function name
			let operation: TaskOperation = { type: 'list' };
			if (functionName.includes('add')) {
				operation.type = 'add';
			} else if (
				functionName.includes('update') ||
				functionName.includes('complete') ||
				functionName.includes('start')
			) {
				operation.type = 'update';
			} else if (functionName.includes('delete') || functionName.includes('remove')) {
				operation.type = 'delete';
			} else if (functionName.includes('clear')) {
				operation.type = 'clear';
			}

			// Extract operation details from arguments if available
			if (pair.toolCall.args) {
				operation = { ...operation, ...pair.toolCall.args };
			}

			// Update the task store if we have tasks or a clear operation
			if ((tasks.length > 0 || operation.type === 'clear') && this.currentChatId) {
				console.log('Task manager operation detected:', operation, 'Tasks:', tasks);

				// Update the task manager store with the new task state
				taskManager.updateFromToolCall(this.currentChatId, operation, tasks);
			}
		} catch (error) {
			console.error('Error processing task manager result:', error);
		}
	}

	/**
	 * Complete the aggregate message
	 */
	completeMessage(messageId: string, envelope: MessageCompleteEventEnvelope): MessageDto {
		let snapshot = this.getSnapshot(messageId);
		let toolCallPairs: ToolCallPair[] = [];

		// If we have a snapshot from streaming, use its pairs
		if (snapshot) {
			const pairsMap = this.toolCallPairsMap.get(messageId);
			if (pairsMap) {
				toolCallPairs = Array.from(pairsMap.values());
			}
		}

		// If server sends a complete aggregate, transform it to paired format
		if (envelope.kind === 'tools_aggregate' || envelope.kind === 'tools_call') {
			const payload = envelope.payload as any;

			if (payload?.toolCalls || payload?.toolResults) {
				// Transform server's array format to paired format
				const serverToolCalls = payload.toolCalls || [];
				const serverToolResults = payload.toolResults || [];

				// Create a map of results by toolCallId for easy lookup
				const resultsMap = new Map<string, ToolCallResult>();
				for (const result of serverToolResults) {
					// Handle both toolCallId and tool_call_id formats from server
					const toolCallId = result.toolCallId || result.tool_call_id;
					if (toolCallId) {
						resultsMap.set(toolCallId, result);
					}
				}

				// Build pairs from server data
				const serverPairs: ToolCallPair[] = serverToolCalls.map((toolCall: ToolCall) => {
					const toolCallId = toolCall.id || toolCall.tool_call_id || `tool_${toolCall.index}`;
					const result = resultsMap.get(toolCallId);

					return {
						toolCall,
						toolResult: result
					};
				});

				// If we don't have streaming pairs or server has more complete data, use server pairs
				if (toolCallPairs.length === 0 || serverPairs.length > toolCallPairs.length) {
					toolCallPairs = serverPairs;
				}
			}
		}

		// Create or update snapshot with final pairs
		if (!snapshot) {
			snapshot = this.initializeMessage(
				messageId,
				envelope.chatId,
				new Date(envelope.ts),
				envelope.sequenceId
			);
		}

		snapshot = this.updateSnapshot(messageId, {
			toolCallPairs,
			messageType: 'tools_aggregate',
			isStreaming: false,
			isComplete: true
		});

		// Convert to DTO
		const dto = this.snapshotToDto(snapshot);
		this.emitEvent('message_completed', messageId, envelope.chatId, dto);

		// Clean up all documents for this message from fragment manager
		const pairsMapForCleanup = this.toolCallPairsMap.get(messageId);
		if (pairsMapForCleanup) {
			const toolCallIds = Array.from(pairsMapForCleanup.keys());
			this.fragmentManager.finalizeMessage(messageId, toolCallIds);
		}

		// Clean up the pairs map itself
		this.toolCallPairsMap.delete(messageId);

		return dto;
	}

	/**
	 * Convert snapshot to DTO
	 */
	protected snapshotToDto(snapshot: MessageSnapshot): ClientToolsCallAggregateMessageDto {
		return {
			id: snapshot.id,
			chatId: snapshot.chatId,
			role: snapshot.role,
			timestamp: snapshot.timestamp,
			sequenceNumber: snapshot.sequenceNumber,
			messageType: 'tools_aggregate',
			toolCallPairs: snapshot.toolCallPairs || []
		};
	}
}

/**
 * Factory function for creating tools aggregate message handler
 */
export function createToolsAggregateMessageHandler(
	eventListener?: HandlerEventListener
): ToolsAggregateMessageHandler {
	return new ToolsAggregateMessageHandler(eventListener);
}
